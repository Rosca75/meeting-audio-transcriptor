// Tests of the speaker clustering on synthetic embeddings: exact recovery, the dendrogram against a
// brute-force reference, the speaker-count hint, the cap, small clusters, determinism and the two stages.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Diarisation;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Diarisation;

[Trait("Category", "diarisation")]
public sealed class SpeakerClustererTests
{
    /// <summary>Unit vectors around <paramref name="sizes"/>.Length random centres; returns them in a shuffled time order with their true cluster.</summary>
    internal static (List<float[]> Points, int[] Truth) Gaussians(int[] sizes, int dimension = 64, double spread = 0.06, int seed = 42, bool shuffle = true)
    {
        Random random = new(seed);
        float[][] centres = new float[sizes.Length][];
        for (int c = 0; c < sizes.Length; c++)
        {
            centres[c] = Normal(random, dimension, 1.0);
            VectorMath.Normalise(centres[c]);
        }

        List<(float[] Point, int Cluster)> all = [];
        for (int c = 0; c < sizes.Length; c++)
        {
            for (int i = 0; i < sizes[c]; i++)
            {
                float[] noise = Normal(random, dimension, spread);
                float[] point = centres[c].Select((v, d) => v + noise[d]).ToArray();
                VectorMath.Normalise(point);
                all.Add((point, c));
            }
        }

        if (shuffle)
        {
            all = all.OrderBy(_ => random.Next()).ToList();
        }

        return (all.Select(a => a.Point).ToList(), all.Select(a => a.Cluster).ToArray());
    }

    private static float[] Normal(Random random, int dimension, double sigma)
    {
        float[] values = new float[dimension];
        for (int d = 0; d < dimension; d++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            values[d] = (float)(sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }

        return values;
    }

    // True when the two labelings are the same partition up to renaming.
    private static bool SamePartition(int[] got, int[] truth)
    {
        return SpeakerClusterer.Compact(got).SequenceEqual(SpeakerClusterer.Compact(truth));
    }

    private static int Count(int[] labels) => labels.Distinct().Count();

    [Theory]
    [InlineData(SpeakerLinkage.Average)]
    [InlineData(SpeakerLinkage.Complete)]
    public void WellSeparatedSpeakersAreRecoveredExactly(SpeakerLinkage linkage)
    {
        (List<float[]> points, int[] truth) = Gaussians([30, 20, 40, 12]);

        int[] got = SpeakerClusterer.Cluster(points, new DiarisationOptions { Linkage = linkage }, CancellationToken.None);

        Assert.True(SamePartition(got, truth), $"got {Count(got)} clusters [{string.Join(",", got)}], wanted the 4 true ones");
    }

    [Theory]
    [InlineData(SpeakerLinkage.Average)]
    [InlineData(SpeakerLinkage.Complete)]
    public void TheDendrogramMatchesBruteForceAgglomeration(SpeakerLinkage linkage)
    {
        (List<float[]> points, _) = Gaussians([6, 5, 7], dimension: 8, spread: 0.4, seed: 3);
        int n = points.Count;

        SpeakerClusterer.Merge[] merges = SpeakerClusterer.Dendrogram(points, Enumerable.Repeat(1, n).ToArray(), linkage, CancellationToken.None);

        // The reference merges the closest pair of clusters by recomputing the linkage from the members each time.
        List<List<int>> clusters = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList();
        List<float> heights = [];
        while (clusters.Count > 1)
        {
            double best = double.MaxValue;
            (int A, int B) pair = (0, 1);
            for (int a = 0; a < clusters.Count; a++)
            {
                for (int b = a + 1; b < clusters.Count; b++)
                {
                    IEnumerable<double> distances = clusters[a].SelectMany(i => clusters[b].Select(j => 1.0 - VectorMath.Dot(points[i], points[j])));
                    double d = linkage == SpeakerLinkage.Average ? distances.Average() : distances.Max();
                    if (d < best)
                    {
                        best = d;
                        pair = (a, b);
                    }
                }
            }

            heights.Add((float)best);
            clusters[pair.A].AddRange(clusters[pair.B]);
            clusters.RemoveAt(pair.B);
        }

        for (int m = 0; m < heights.Count; m++)
        {
            Assert.True(Math.Abs(merges[m].Height - heights[m]) < 1e-4, $"merge {m}: got height {merges[m].Height}, wanted {heights[m]} ({linkage})");
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    [InlineData(6)]
    public void TheSpeakerCountHintIsHonoured(int hint)
    {
        (List<float[]> points, _) = Gaussians([30, 20, 40, 25], spread: 0.08);

        int[] got = SpeakerClusterer.Cluster(points, new DiarisationOptions { SpeakerCountHint = hint, MinimumSpeakerSeconds = 0 }, CancellationToken.None);

        Assert.True(Count(got) == hint, $"got {Count(got)} clusters, wanted the hint {hint}");
    }

    [Fact]
    public void TheHintIgnoresTinyClustersWhenCountingSpeakers()
    {
        // Three speakers and two stray windows far from everyone: with the hint 3, the strays are folded in rather than counted.
        (List<float[]> points, int[] truth) = Gaussians([30, 30, 30, 1, 1], seed: 9, shuffle: false);

        int[] got = SpeakerClusterer.Cluster(points, new DiarisationOptions { SpeakerCountHint = 3, WindowSeconds = 1.5, HopSeconds = 0.75 }, CancellationToken.None);

        Assert.True(Count(got) == 3, $"got {Count(got)} clusters, wanted 3");
        Assert.True(SamePartition(got[..90], truth[..90]), "the three real speakers were not recovered exactly");
    }

    [Fact]
    public void TheCapLimitsTheNumberOfSpeakers()
    {
        (List<float[]> points, _) = Gaussians(Enumerable.Repeat(15, 12).ToArray());

        int[] got = SpeakerClusterer.Cluster(points, new DiarisationOptions { MaxSpeakers = 10 }, CancellationToken.None);
        int[] uncapped = SpeakerClusterer.Cluster(points, new DiarisationOptions { MaxSpeakers = 20 }, CancellationToken.None);

        Assert.True(Count(got) == 10, $"got {Count(got)} clusters with a cap of 10, wanted 10");
        Assert.True(Count(uncapped) == 12, $"got {Count(uncapped)} clusters with a cap of 20, wanted the 12 true ones");
    }

    [Fact]
    public void TinyClustersAreFoldedIntoTheNearestLargeOne()
    {
        // Three speakers of 40 windows and three windows of a stray voice, below the 6-window minimum.
        (List<float[]> points, int[] truth) = Gaussians([40, 40, 40, 3], seed: 5, shuffle: false);

        int[] folded = SpeakerClusterer.Cluster(points, new DiarisationOptions { MinimumSpeakerSeconds = 4.0, WindowSeconds = 1.5, HopSeconds = 0.75 }, CancellationToken.None);
        int[] kept = SpeakerClusterer.Cluster(points, new DiarisationOptions { MinimumSpeakerSeconds = 0 }, CancellationToken.None);

        Assert.True(Count(folded) == 3, $"got {Count(folded)} clusters with a 4 s minimum (6 windows), wanted 3");
        Assert.True(SamePartition(folded[..120], truth[..120]), "folding moved windows of the large clusters");
        Assert.True(Count(kept) == 4, $"got {Count(kept)} clusters with no minimum, wanted 4");
    }

    [Fact]
    public void ShortWindowsCountForTheirOwnLengthAgainstTheMinimum()
    {
        // Three speakers of 40 full windows and a stray voice of three windows. As full 3 s windows at a
        // 2 s hop the stray holds 6 s, above the 4 s minimum; as three 0.8 s windows, cut from short
        // speech regions, it holds 2.4 s and is folded.
        (List<float[]> points, _) = Gaussians([40, 40, 40, 3], seed: 5, shuffle: false);
        double[] full = Enumerable.Repeat(3.0, points.Count).ToArray();
        double[] shortStray = full.Select((length, i) => i >= 120 ? 0.8 : length).ToArray();

        int[] asFull = SpeakerClusterer.Cluster(points, new DiarisationOptions(), CancellationToken.None, full);
        int[] asShort = SpeakerClusterer.Cluster(points, new DiarisationOptions(), CancellationToken.None, shortStray);

        Assert.True(Count(asFull) == 4, $"got {Count(asFull)} clusters with the stray's windows full, wanted 4");
        Assert.True(Count(asShort) == 3, $"got {Count(asShort)} clusters with the stray's windows 0.8 s long, wanted 3");
    }

    [Fact]
    public void WindowLengthsMustMatchTheEmbeddings()
    {
        (List<float[]> points, _) = Gaussians([5, 5], seed: 3);

        ArgumentException error = Assert.Throws<ArgumentException>(() => SpeakerClusterer.Cluster(points, new DiarisationOptions(), CancellationToken.None, [3.0]));
        Assert.Contains("one length per window", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResultIsDeterministic()
    {
        (List<float[]> points, _) = Gaussians([25, 25, 25], spread: 0.15, seed: 11);
        DiarisationOptions options = new() { Threshold = 0.5 };

        int[] first = SpeakerClusterer.Cluster(points, options, CancellationToken.None);
        int[] second = SpeakerClusterer.Cluster(points, options, CancellationToken.None);

        Assert.True(first.SequenceEqual(second), "two runs over the same embeddings disagree");
    }

    [Fact]
    public void TheTwoStageSchemeRecoversTheSpeakers()
    {
        (List<float[]> points, int[] truth) = Gaussians([200, 150, 250, 100]);

        int[] got = SpeakerClusterer.Cluster(points, new DiarisationOptions { MaxDirectWindows = 100 }, CancellationToken.None);

        Assert.True(SamePartition(got, truth), $"got {Count(got)} clusters with blocks of 100, wanted the 4 true ones");
    }

    [Fact]
    public void NoWindowAndOneWindowNeedNoClustering()
    {
        Assert.Empty(SpeakerClusterer.Cluster([], new DiarisationOptions(), CancellationToken.None));
        Assert.Equal([0], SpeakerClusterer.Cluster([[1f, 0f]], new DiarisationOptions(), CancellationToken.None));
    }

    [Fact]
    [Trait("Tier", "deep")]
    public void TenThousandWindowsClusterInSecondsAndBoundedMemory()
    {
        // A two-hour meeting at a 0.75 s hop: about 9,600 windows of 512 values from six speakers.
        (List<float[]> points, int[] truth) = Gaussians([2500, 2000, 1800, 1500, 1200, 1000], dimension: 512, spread: 0.03);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch clock = Stopwatch.StartNew();

        int[] got = SpeakerClusterer.Cluster(points, new DiarisationOptions(), CancellationToken.None);

        clock.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.Current.TestOutputHelper?.WriteLine($"10,000 windows: {clock.Elapsed.TotalSeconds:F2} s, {allocated / 1e6:F0} MB allocated");
        Assert.True(SamePartition(got, truth), $"got {Count(got)} clusters, wanted the 6 true ones");
        Assert.True(clock.Elapsed.TotalSeconds < 30, $"got {clock.Elapsed.TotalSeconds:F1} s on one core, wanted under 30 s");
        Assert.True(allocated < 200_000_000, $"got {allocated / 1e6:F0} MB allocated, wanted under 200 MB (a full matrix would be 400 MB)");
    }
}
