// Groups the embedding windows of a whole recording into speakers: agglomerative clustering on cosine
// distance, in two stages when the recording is long, with the speaker-count, minimum-size and cap rules.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>Agglomerative hierarchical clustering of unit-length speaker embeddings.</summary>
/// <remarks>
/// <para>
/// <b>Algorithm.</b> The clusters' cosine distances live in one square matrix updated by the
/// Lance-Williams rule of the chosen linkage, and merges are found by the nearest-neighbour-chain
/// algorithm, which needs O(n^2) time and the matrix as its only large memory. Average and complete
/// linkage are both reducible, so the merges sorted by height form the exact dendrogram, and one
/// dendrogram answers every cut: at the distance threshold, or at exactly K clusters.
/// </para>
/// <para>
/// <b>Two stages for long recordings.</b> A two-hour meeting yields about 10,000 windows, whose
/// matrix would take 400 MB. Above <see cref="DiarisationOptions.MaxDirectWindows"/> (M) windows, the
/// windows are split in time order into blocks of M, each block is clustered on its own and cut at
/// half the threshold (a merge that cautious joins windows of one voice only), and at most M / blocks
/// clusters are kept per block, so the second stage sees at most M units. The second stage clusters
/// those units, each carrying the sum of its windows' embeddings and their count. For unit-length
/// vectors the average-linkage similarity of two groups is the dot product of their sums divided by
/// the product of their counts, so average linkage across units is exact for the windows they hold;
/// complete linkage starts the second stage from the same average and is an approximation there.
/// Memory stays at one M by M matrix (16 MB for M = 2000) whatever the recording's length.
/// </para>
/// <para>
/// <b>The rules after the cut.</b> A cluster holding less speech than the minimum size is folded
/// into the large cluster it is closest to, because a few seconds of a voice print is more often a
/// cough, a laugh or two people at once than a speaker. A full window counts for one hop of speech
/// and a shorter one for its own length: voice activity cuts real speech into many short regions,
/// each embedded as one short window whose voice print is unsteady, and counted as a full hop a few
/// of them would pass for a speaker. More clusters than the cap are merged closest pair first.
/// With a speaker count K, the threshold cut is taken first. When it holds more than K clusters,
/// the smallest is folded into the one most similar to it until K remain, because a surplus
/// cluster is usually a part of one voice (a cough, a change of microphone) while two real speakers
/// are both large. When it holds fewer, the dendrogram is cut deeper, at the smallest number of
/// clusters that leaves exactly K large ones, and the small ones are folded in; when no cut does,
/// it is cut at K.
/// Refinement passes then move every window to the centroid nearest it, which repairs windows a
/// merge order placed badly. Every loop breaks ties by the lower index, so the result is deterministic.
/// </para>
/// </remarks>
internal static class SpeakerClusterer
{
    /// <summary>Clusters the embeddings and returns one cluster number per window, numbered from 0.</summary>
    /// <param name="embeddings">Unit-length embeddings in time order.</param>
    /// <param name="options">The linkage, the threshold, the hint and the size rules.</param>
    /// <param name="cancellation">Stops between blocks and between passes.</param>
    /// <param name="windowSeconds">The length of each window in seconds; null when every window is a full one.</param>
    public static int[] Cluster(IReadOnlyList<float[]> embeddings, DiarisationOptions options, CancellationToken cancellation, IReadOnlyList<double>? windowSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        int n = embeddings.Count;
        if (n == 0)
        {
            return [];
        }

        if (n == 1)
        {
            return [0];
        }

        int dimension = embeddings[0].Length;
        if (windowSeconds is not null && windowSeconds.Count != n)
        {
            throw new ArgumentException($"there are {n} embeddings and {windowSeconds.Count} window lengths; pass one length per window", nameof(windowSeconds));
        }

        // The speech a window stands for: one hop for a full window (windows overlap by the rest), its
        // own length for a short one, so a cluster of short windows is not counted as long speech.
        // With a speaker count every window counts one hop: the count is the user's, the sizes only
        // order the folds, and on the meeting clips that order attributes better.
        double[] speechOfWindow = new double[n];
        for (int i = 0; i < n; i++)
        {
            speechOfWindow[i] = options.SpeakerCountHint is null ? Math.Min(options.HopSeconds, windowSeconds?[i] ?? options.HopSeconds) : options.HopSeconds;
        }

        // Stage one: windows become units, one per window, or one per block cluster when the recording is long.
        int[] unitOfWindow;
        List<float[]> unitSums;
        List<int> unitCounts;
        if (n <= options.MaxDirectWindows)
        {
            unitOfWindow = new int[n];
            unitSums = new List<float[]>(n);
            unitCounts = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                unitOfWindow[i] = i;
                unitSums.Add(embeddings[i]);
                unitCounts.Add(1);
            }
        }
        else
        {
            (unitOfWindow, unitSums, unitCounts) = PreCluster(embeddings, options, dimension, cancellation);
        }

        // Stage two: the dendrogram of the units and the cut the options ask for.
        int units = unitSums.Count;
        int[] counts = unitCounts.ToArray();
        double[] speech = new double[units];
        for (int i = 0; i < n; i++)
        {
            speech[unitOfWindow[i]] += speechOfWindow[i];
        }

        Merge[] merges = Dendrogram(unitSums, counts, options.Linkage, cancellation);
        int[] unitLabels = ChooseCut(merges, units, counts, speech, unitSums, options);

        int[] labels = new int[n];
        for (int i = 0; i < n; i++)
        {
            labels[i] = unitLabels[unitOfWindow[i]];
        }

        if (options.SpeakerCountHint is null)
        {
            labels = MergeToCap(labels, embeddings, dimension, options.MaxSpeakers);
        }

        for (int pass = 0; pass < options.RefinementPasses; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            labels = Refine(labels, embeddings, dimension);
        }

        return Compact(labels);
    }

    /// <summary>One merge of the dendrogram: the two clusters, by the lowest unit index each holds, and the distance.</summary>
    internal readonly record struct Merge(int A, int B, float Height, int Order);

    /// <summary>Builds the dendrogram over units with the nearest-neighbour chain; returns the merges sorted by height.</summary>
    internal static Merge[] Dendrogram(IReadOnlyList<float[]> sums, int[] counts, SpeakerLinkage linkage, CancellationToken cancellation)
    {
        int u = sums.Count;
        float[] distance = new float[u * u];
        for (int i = 0; i < u; i++)
        {
            if ((i & 63) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            for (int j = i + 1; j < u; j++)
            {
                float similarity = VectorMath.Dot(sums[i], sums[j]) / ((float)counts[i] * counts[j]);
                float d = 1f - similarity;
                distance[(i * u) + j] = d;
                distance[(j * u) + i] = d;
            }
        }

        int[] size = (int[])counts.Clone();
        bool[] active = new bool[u];
        Array.Fill(active, true);
        List<int> chain = new(u);
        List<Merge> merges = new(u - 1);
        int remaining = u;
        int nextStart = 0;
        while (remaining > 1)
        {
            if (chain.Count == 0)
            {
                while (!active[nextStart])
                {
                    nextStart++;
                }

                chain.Add(nextStart);
            }

            int x;
            int y;
            float height;
            while (true)
            {
                x = chain[^1];
                int previous = chain.Count >= 2 ? chain[^2] : -1;
                int best = previous;
                float bestDistance = previous >= 0 ? distance[(x * u) + previous] : float.PositiveInfinity;
                int row = x * u;
                for (int k = 0; k < u; k++)
                {
                    if (k != x && active[k] && distance[row + k] < bestDistance)
                    {
                        bestDistance = distance[row + k];
                        best = k;
                    }
                }

                if (best == previous)
                {
                    y = previous;
                    height = bestDistance;
                    break;
                }

                chain.Add(best);
            }

            chain.RemoveAt(chain.Count - 1);
            chain.RemoveAt(chain.Count - 1);

            // The merged cluster keeps the lower index; the Lance-Williams rule gives its distance to every other.
            int keep = Math.Min(x, y);
            int drop = Math.Max(x, y);
            int sizeKeep = size[keep];
            int sizeDrop = size[drop];
            for (int k = 0; k < u; k++)
            {
                if (!active[k] || k == keep || k == drop)
                {
                    continue;
                }

                float dk = distance[(keep * u) + k];
                float dd = distance[(drop * u) + k];
                float merged = linkage == SpeakerLinkage.Complete
                    ? Math.Max(dk, dd)
                    : ((sizeKeep * dk) + (sizeDrop * dd)) / (sizeKeep + sizeDrop);
                distance[(keep * u) + k] = merged;
                distance[(k * u) + keep] = merged;
            }

            size[keep] = sizeKeep + sizeDrop;
            active[drop] = false;
            merges.Add(new Merge(keep, drop, height, merges.Count));
            remaining--;
        }

        Merge[] sorted = merges.ToArray();
        Array.Sort(sorted, static (a, b) => a.Height != b.Height ? a.Height.CompareTo(b.Height) : a.Order.CompareTo(b.Order));
        return sorted;
    }

    /// <summary>The cluster of every unit after applying the first <paramref name="mergeCount"/> merges, numbered by first unit.</summary>
    internal static int[] Cut(Merge[] merges, int units, int mergeCount)
    {
        int[] parent = new int[units];
        for (int i = 0; i < units; i++)
        {
            parent[i] = i;
        }

        for (int m = 0; m < mergeCount; m++)
        {
            int a = Find(parent, merges[m].A);
            int b = Find(parent, merges[m].B);
            if (a != b)
            {
                parent[Math.Max(a, b)] = Math.Min(a, b);
            }
        }

        int[] labels = new int[units];
        for (int i = 0; i < units; i++)
        {
            labels[i] = Find(parent, i);
        }

        return Compact(labels);
    }

    /// <summary>How many merges lie at or below the distance threshold.</summary>
    internal static int MergesWithin(Merge[] merges, double threshold)
    {
        int count = 0;
        while (count < merges.Length && merges[count].Height <= threshold)
        {
            count++;
        }

        return count;
    }

    // Applies the threshold or the speaker count to the dendrogram and folds small clusters in.
    private static int[] ChooseCut(Merge[] merges, int units, int[] counts, double[] speech, IReadOnlyList<float[]> sums, DiarisationOptions options)
    {
        double minimumSize = options.MinimumSpeakerSeconds;
        int[] byThreshold = FoldSmall(Cut(merges, units, MergesWithin(merges, options.Threshold)), counts, speech, sums, minimumSize);
        if (options.SpeakerCountHint is int hint)
        {
            int wanted = Math.Min(hint, units);
            int found = byThreshold.Max() + 1;
            if (found == wanted)
            {
                return byThreshold;
            }

            if (found > wanted)
            {
                return MergeSmallest(byThreshold, counts, speech, sums, wanted);
            }

            for (int clusters = wanted; clusters <= units; clusters++)
            {
                int[] labels = Cut(merges, units, units - clusters);
                int large = LargeClusterCount(labels, speech, minimumSize);
                if (large == wanted)
                {
                    return FoldSmall(labels, counts, speech, sums, minimumSize);
                }

                if (large > wanted)
                {
                    break;
                }
            }

            return Cut(merges, units, units - wanted);
        }

        return byThreshold;
    }

    // Folds the smallest cluster into the one of highest average similarity until the wanted count
    // remains; ties go to the lower cluster number.
    private static int[] MergeSmallest(int[] labels, int[] counts, double[] speech, IReadOnlyList<float[]> sums, int wanted)
    {
        int[] current = labels;
        while (true)
        {
            int[] sizes = ClusterSizes(current, counts);
            double[] seconds = ClusterSpeech(current, speech);
            int clusters = sizes.Length;
            if (clusters <= wanted)
            {
                return current;
            }

            int smallest = 0;
            for (int c = 1; c < clusters; c++)
            {
                if (seconds[c] < seconds[smallest])
                {
                    smallest = c;
                }
            }

            float[][] clusterSums = SumsByLabel(current, sums, clusters, sums[0].Length);
            int target = -1;
            float best = float.NegativeInfinity;
            for (int c = 0; c < clusters; c++)
            {
                if (c == smallest)
                {
                    continue;
                }

                float similarity = VectorMath.Dot(clusterSums[smallest], clusterSums[c]) / ((float)sizes[smallest] * sizes[c]);
                if (similarity > best)
                {
                    best = similarity;
                    target = c;
                }
            }

            int[] merged = new int[current.Length];
            for (int i = 0; i < current.Length; i++)
            {
                merged[i] = current[i] == smallest ? target : current[i];
            }

            current = Compact(merged);
        }
    }

    private static int LargeClusterCount(int[] labels, double[] speech, double minimumSize)
    {
        double[] sizes = ClusterSpeech(labels, speech);
        int large = 0;
        foreach (double s in sizes)
        {
            if (s >= minimumSize)
            {
                large++;
            }
        }

        return large;
    }

    private static int[] ClusterSizes(int[] labels, int[] counts)
    {
        int clusters = 0;
        foreach (int label in labels)
        {
            clusters = Math.Max(clusters, label + 1);
        }

        int[] sizes = new int[clusters];
        for (int i = 0; i < labels.Length; i++)
        {
            sizes[labels[i]] += counts[i];
        }

        return sizes;
    }

    // The seconds of speech each cluster stands for.
    private static double[] ClusterSpeech(int[] labels, double[] speech)
    {
        int clusters = 0;
        foreach (int label in labels)
        {
            clusters = Math.Max(clusters, label + 1);
        }

        double[] seconds = new double[clusters];
        for (int i = 0; i < labels.Length; i++)
        {
            seconds[labels[i]] += speech[i];
        }

        return seconds;
    }

    // Moves every unit of a cluster holding less speech than the minimum to the large cluster with the
    // highest average similarity; when no cluster is large, the biggest one is treated as large.
    private static int[] FoldSmall(int[] labels, int[] counts, double[] speech, IReadOnlyList<float[]> sums, double minimumSize)
    {
        int[] sizes = ClusterSizes(labels, counts);
        double[] seconds = ClusterSpeech(labels, speech);
        int clusters = sizes.Length;
        bool[] large = new bool[clusters];
        int biggest = 0;
        bool any = false;
        for (int c = 0; c < clusters; c++)
        {
            large[c] = seconds[c] >= minimumSize;
            any |= large[c];
            if (seconds[c] > seconds[biggest])
            {
                biggest = c;
            }
        }

        if (!any)
        {
            large[biggest] = true;
        }

        int dimension = sums[0].Length;
        float[][] clusterSums = SumsByLabel(labels, sums, clusters, dimension);
        int[] target = new int[clusters];
        for (int c = 0; c < clusters; c++)
        {
            target[c] = c;
            if (large[c])
            {
                continue;
            }

            float best = float.NegativeInfinity;
            for (int d = 0; d < clusters; d++)
            {
                if (!large[d])
                {
                    continue;
                }

                float similarity = VectorMath.Dot(clusterSums[c], clusterSums[d]) / ((float)sizes[c] * sizes[d]);
                if (similarity > best)
                {
                    best = similarity;
                    target[c] = d;
                }
            }
        }

        int[] folded = new int[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            folded[i] = target[labels[i]];
        }

        return Compact(folded);
    }

    // Merges the two clusters of highest average similarity until no more than the cap remain.
    private static int[] MergeToCap(int[] labels, IReadOnlyList<float[]> embeddings, int dimension, int cap)
    {
        int[] current = Compact(labels);
        while (true)
        {
            int[] ones = new int[current.Length];
            Array.Fill(ones, 1);
            int[] sizes = ClusterSizes(current, ones);
            int clusters = sizes.Length;
            if (clusters <= cap)
            {
                return current;
            }

            float[][] sums = SumsByLabel(current, embeddings, clusters, dimension);
            int bestA = 0;
            int bestB = 1;
            float best = float.NegativeInfinity;
            for (int a = 0; a < clusters; a++)
            {
                for (int b = a + 1; b < clusters; b++)
                {
                    float similarity = VectorMath.Dot(sums[a], sums[b]) / ((float)sizes[a] * sizes[b]);
                    if (similarity > best)
                    {
                        best = similarity;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] == bestB)
                {
                    current[i] = bestA;
                }
            }

            current = Compact(current);
        }
    }

    // Moves every window to the cluster whose normalised centroid is most similar to it.
    private static int[] Refine(int[] labels, IReadOnlyList<float[]> embeddings, int dimension)
    {
        int clusters = 0;
        foreach (int label in labels)
        {
            clusters = Math.Max(clusters, label + 1);
        }

        float[][] centroids = SumsByLabel(labels, embeddings, clusters, dimension);
        foreach (float[] centroid in centroids)
        {
            VectorMath.Normalise(centroid);
        }

        int[] refined = new int[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int best = labels[i];
            float bestSimilarity = VectorMath.Dot(embeddings[i], centroids[best]);
            for (int c = 0; c < clusters; c++)
            {
                float similarity = VectorMath.Dot(embeddings[i], centroids[c]);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    best = c;
                }
            }

            refined[i] = best;
        }

        return Compact(refined);
    }

    // The first stage for long recordings: blocks of windows in time order, each cut cautiously, at most a share of the budget each.
    private static (int[] UnitOfWindow, List<float[]> Sums, List<int> Counts) PreCluster(
        IReadOnlyList<float[]> embeddings, DiarisationOptions options, int dimension, CancellationToken cancellation)
    {
        int n = embeddings.Count;
        int blockSize = options.MaxDirectWindows;
        int blocks = (n + blockSize - 1) / blockSize;
        int perBlock = Math.Max(1, options.MaxDirectWindows / blocks);
        int[] unitOfWindow = new int[n];
        List<float[]> sums = [];
        List<int> counts = [];
        for (int start = 0; start < n; start += blockSize)
        {
            cancellation.ThrowIfCancellationRequested();
            int length = Math.Min(blockSize, n - start);
            List<float[]> block = new(length);
            int[] ones = new int[length];
            for (int i = 0; i < length; i++)
            {
                block.Add(embeddings[start + i]);
                ones[i] = 1;
            }

            Merge[] merges = Dendrogram(block, ones, options.Linkage, cancellation);
            int mergeCount = Math.Max(MergesWithin(merges, options.Threshold * 0.5), length - perBlock);
            mergeCount = Math.Min(mergeCount, length - 1);
            int[] labels = length == 1 ? [0] : Cut(merges, length, mergeCount);
            int clusters = 0;
            foreach (int label in labels)
            {
                clusters = Math.Max(clusters, label + 1);
            }

            int firstUnit = sums.Count;
            for (int c = 0; c < clusters; c++)
            {
                sums.Add(new float[dimension]);
                counts.Add(0);
            }

            for (int i = 0; i < length; i++)
            {
                int unit = firstUnit + labels[i];
                unitOfWindow[start + i] = unit;
                VectorMath.Add(sums[unit], block[i]);
                counts[unit]++;
            }
        }

        return (unitOfWindow, sums, counts);
    }

    private static float[][] SumsByLabel(int[] labels, IReadOnlyList<float[]> vectors, int clusters, int dimension)
    {
        float[][] sums = new float[clusters][];
        for (int c = 0; c < clusters; c++)
        {
            sums[c] = new float[dimension];
        }

        for (int i = 0; i < labels.Length; i++)
        {
            VectorMath.Add(sums[labels[i]], vectors[i]);
        }

        return sums;
    }

    // Renumbers labels 0, 1, 2 ... in order of first appearance, so equal partitions give equal arrays.
    internal static int[] Compact(int[] labels)
    {
        Dictionary<int, int> map = [];
        int[] compact = new int[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            if (!map.TryGetValue(labels[i], out int number))
            {
                number = map.Count;
                map[labels[i]] = number;
            }

            compact[i] = number;
        }

        return compact;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }
}
