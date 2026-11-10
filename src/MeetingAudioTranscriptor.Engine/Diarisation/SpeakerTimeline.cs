// Who spoke when: the speaker turns built from the clustered windows, the speaking time per speaker,
// and the speaker of every recognised word.
using System;
using System.Collections.Generic;
using System.Globalization;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>One stretch of speech by one speaker.</summary>
/// <param name="Start">Where the turn starts, in seconds from the start of the recording.</param>
/// <param name="End">Where the turn ends, in seconds.</param>
/// <param name="Speaker">The speaker's index, from 0, in order of first appearance.</param>
public readonly record struct SpeakerTurn(double Start, double End, int Speaker);

/// <summary>The speaker timeline of one recording.</summary>
/// <remarks>
/// <para>
/// <b>Voting.</b> Each speech region is cut into 10 ms frames. Every window votes for its cluster on
/// the frames it covers, with a weight that falls linearly from 1 at its centre to near 0 at its
/// edges, so where two overlapping windows disagree the frame goes to the window centred nearer to it.
/// Frames of a region no window covers (the region's last few frames, or a region too short for a
/// window) take the speaker of the nearest voted frame, in the same region when it has one and in
/// the nearest region otherwise.
/// </para>
/// <para>
/// <b>Smoothing.</b> Inside one region, a run of frames shorter than the minimum turn is given to a
/// neighbouring run: the neighbour when both sides agree, else the side whose speaker received more
/// votes over the short run. The shortest run goes first, so a short run between two long ones never
/// survives by being merged with another short one. A whole region shorter than the minimum keeps
/// its speaker: a short answer between pauses is a real turn.
/// </para>
/// <para>
/// <b>Numbering.</b> Speakers are numbered 0, 1, 2 ... in order of their first turn, and the
/// transcript names them Speaker 1, Speaker 2 ..., so the labels say nothing about who anyone is.
/// </para>
/// </remarks>
public sealed class SpeakerTimeline
{
    /// <summary>The length of one voting frame in seconds.</summary>
    internal const double FrameSeconds = 0.01;

    private readonly SpeakerTurn[] _turns;
    private readonly double _strongTurnSeconds;

    private SpeakerTimeline(SpeakerTurn[] turns, int[] clusterOfSpeaker, double strongTurnSeconds)
    {
        _turns = turns;
        SpeakerCount = clusterOfSpeaker.Length;
        ClusterOfSpeaker = clusterOfSpeaker;
        _strongTurnSeconds = strongTurnSeconds;
    }

    /// <summary>The turns in time order; turns never overlap.</summary>
    public IReadOnlyList<SpeakerTurn> Turns => _turns;

    /// <summary>The number of distinct speakers.</summary>
    public int SpeakerCount { get; }

    /// <summary>The cluster label each speaker index was built from.</summary>
    internal IReadOnlyList<int> ClusterOfSpeaker { get; }

    /// <summary>The anonymous label of a speaker index: Speaker 1 for index 0.</summary>
    public static string Label(int speaker)
    {
        return "Speaker " + (speaker + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The total speaking time of each speaker in seconds, indexed by speaker.</summary>
    public double[] SpeakingSeconds()
    {
        double[] seconds = new double[SpeakerCount];
        foreach (SpeakerTurn turn in _turns)
        {
            seconds[turn.Speaker] += turn.End - turn.Start;
        }

        return seconds;
    }

    /// <summary>Builds the timeline from windows, their cluster labels and the speech regions.</summary>
    /// <param name="windows">The windows in any order.</param>
    /// <param name="labels">The cluster of each window, from 0.</param>
    /// <param name="speechRegions">The speech regions in samples from the recording's start, in any order; must not overlap.</param>
    /// <param name="sampleRate">Samples per second.</param>
    /// <param name="minimumTurnSeconds">The shortest turn kept inside continuous speech.</param>
    /// <param name="strongTurnSeconds">A turn at least this long keeps a word inside it when the words around it belong to another speaker.</param>
    public static SpeakerTimeline Build(
        IReadOnlyList<SpeakerWindow> windows,
        IReadOnlyList<int> labels,
        IReadOnlyList<(long Start, long End)> speechRegions,
        int sampleRate,
        double minimumTurnSeconds = 0.5,
        double strongTurnSeconds = 1.0)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(speechRegions);
        if (windows.Count != labels.Count)
        {
            throw new ArgumentException($"there are {windows.Count} windows and {labels.Count} labels; pass one label per window", nameof(labels));
        }

        int clusters = 0;
        foreach (int label in labels)
        {
            clusters = Math.Max(clusters, label + 1);
        }

        double samplesPerFrame = sampleRate * FrameSeconds;
        int minimumFrames = (int)Math.Round(minimumTurnSeconds / FrameSeconds);

        (long Start, long End)[] regions = [.. speechRegions];
        Array.Sort(regions, static (a, b) => a.Start.CompareTo(b.Start));
        int[] order = new int[windows.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => windows[a].StartSample != windows[b].StartSample ? windows[a].StartSample.CompareTo(windows[b].StartSample) : a.CompareTo(b));

        // Per region: frame labels after voting and smoothing; -1 marks a region no window reached.
        List<int[]> regionLabels = new(regions.Length);
        int cursor = 0;
        foreach ((long start, long end) in regions)
        {
            long firstFrame = (long)Math.Round(start / samplesPerFrame);
            int frames = Math.Max(1, (int)(Math.Round(end / samplesPerFrame) - firstFrame));
            float[] votes = new float[frames * Math.Max(clusters, 1)];
            bool anyVote = false;

            while (cursor < order.Length && windows[order[cursor]].EndSample <= start)
            {
                cursor++;
            }

            for (int w = cursor; w < order.Length && windows[order[w]].StartSample < end; w++)
            {
                SpeakerWindow window = windows[order[w]];
                double center = (window.StartSample + window.EndSample) / 2.0 / samplesPerFrame;
                double half = Math.Max(1.0, (window.EndSample - window.StartSample) / 2.0 / samplesPerFrame);
                int from = (int)Math.Max(0, Math.Round(window.StartSample / samplesPerFrame) - firstFrame);
                int to = (int)Math.Min(frames, Math.Round(window.EndSample / samplesPerFrame) - firstFrame);
                int label = labels[order[w]];
                for (int f = from; f < to; f++)
                {
                    double distance = Math.Abs(firstFrame + f + 0.5 - center);
                    votes[(f * clusters) + label] += (float)(1.0 - (distance / (half + 1.0)));
                    anyVote = true;
                }
            }

            regionLabels.Add(anyVote ? VoteAndSmooth(votes, frames, clusters, minimumFrames) : new int[frames]);
            if (!anyVote)
            {
                Array.Fill(regionLabels[^1], -1);
            }
        }

        FillUnvotedRegions(regionLabels, regions);

        // Runs of equal labels become turns; speakers are renumbered by first appearance.
        List<SpeakerTurn> turns = [];
        Dictionary<int, int> numbering = [];
        for (int r = 0; r < regions.Length; r++)
        {
            int[] frameLabels = regionLabels[r];
            if (frameLabels.Length == 0 || frameLabels[0] < 0)
            {
                continue;
            }

            double regionStart = regions[r].Start / (double)sampleRate;
            double regionEnd = regions[r].End / (double)sampleRate;
            long firstFrame = (long)Math.Round(regions[r].Start / samplesPerFrame);
            int runStart = 0;
            for (int f = 1; f <= frameLabels.Length; f++)
            {
                if (f < frameLabels.Length && frameLabels[f] == frameLabels[runStart])
                {
                    continue;
                }

                if (!numbering.TryGetValue(frameLabels[runStart], out int speaker))
                {
                    speaker = numbering.Count;
                    numbering[frameLabels[runStart]] = speaker;
                }

                double turnStart = runStart == 0 ? regionStart : (firstFrame + runStart) * FrameSeconds;
                double turnEnd = f == frameLabels.Length ? regionEnd : (firstFrame + f) * FrameSeconds;
                if (turnEnd > turnStart)
                {
                    turns.Add(new SpeakerTurn(turnStart, turnEnd, speaker));
                }

                runStart = f;
            }
        }

        int[] clusterOfSpeaker = new int[numbering.Count];
        foreach ((int cluster, int speaker) in numbering)
        {
            clusterOfSpeaker[speaker] = cluster;
        }

        return new SpeakerTimeline([.. turns], clusterOfSpeaker, strongTurnSeconds);
    }

    /// <summary>The speaker of every word, by the word's mid-point, smoothed so a lone word inside a sentence follows its neighbours.</summary>
    /// <param name="words">The recognised words in time order, with times from the recording's start.</param>
    /// <returns>One speaker index per word, or -1 for every word when the timeline holds no turn.</returns>
    /// <remarks>
    /// A word whose mid-point lies in no turn takes the speaker of the turn nearest in time. Then a
    /// single word whose speaker differs from both its neighbours, which agree with each other, is given
    /// to the neighbours' speaker when all of these hold: it is within 0.5 s of both neighbours (one
    /// stretch of speech), neither it nor the word before it ends with sentence punctuation (a
    /// sentence boundary is where speakers change), and the turn holding its mid-point is shorter than
    /// the strong-turn length (a long turn is evidence the other speaker really spoke there). Smoothing
    /// reads the speakers before smoothing, so two adjacent lone words do not decide each other.
    /// </remarks>
    public int[] AssignWords(IReadOnlyList<RecognizedWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        int[] speakers = new int[words.Count];
        double[] turnLength = new double[words.Count];
        if (_turns.Length == 0)
        {
            Array.Fill(speakers, -1);
            return speakers;
        }

        for (int i = 0; i < words.Count; i++)
        {
            double middle = (words[i].Start + words[i].End) / 2.0;
            SpeakerTurn turn = _turns[NearestTurn(middle)];
            speakers[i] = turn.Speaker;
            turnLength[i] = turn.Start <= middle && middle < turn.End ? turn.End - turn.Start : 0;
        }

        const double sameStretchSeconds = 0.5;
        int[] smoothed = (int[])speakers.Clone();
        for (int i = 1; i + 1 < words.Count; i++)
        {
            bool lone = speakers[i - 1] == speakers[i + 1] && speakers[i] != speakers[i - 1];
            bool close = words[i].Start - words[i - 1].End < sameStretchSeconds && words[i + 1].Start - words[i].End < sameStretchSeconds;
            bool boundary = EndsSentence(words[i - 1].Text) || EndsSentence(words[i].Text);
            if (lone && close && !boundary && turnLength[i] < _strongTurnSeconds)
            {
                smoothed[i] = speakers[i - 1];
            }
        }

        return smoothed;
    }

    // The turn holding the time, or the nearest one; turns are sorted and disjoint.
    private int NearestTurn(double time)
    {
        int low = 0;
        int high = _turns.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_turns[mid].Start <= time)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        // low is the last turn starting at or before the time (or 0); the next one may be nearer.
        int best = low;
        double bestDistance = Distance(_turns[low], time);
        if (low + 1 < _turns.Length && Distance(_turns[low + 1], time) < bestDistance)
        {
            best = low + 1;
        }

        return best;
    }

    private static double Distance(SpeakerTurn turn, double time)
    {
        return time < turn.Start ? turn.Start - time : time >= turn.End ? time - turn.End : 0;
    }

    private static bool EndsSentence(string text)
    {
        string trimmed = text.TrimEnd('"', '\'', ')', '”', '»');
        return trimmed.Length > 0 && trimmed[^1] is '.' or '?' or '!' or '…';
    }

    // Takes the heaviest vote per frame, fills unvoted frames from the nearest voted one and smooths short runs.
    private static int[] VoteAndSmooth(float[] votes, int frames, int clusters, int minimumFrames)
    {
        int[] labels = new int[frames];
        for (int f = 0; f < frames; f++)
        {
            int best = -1;
            float bestVote = 0;
            for (int c = 0; c < clusters; c++)
            {
                if (votes[(f * clusters) + c] > bestVote)
                {
                    bestVote = votes[(f * clusters) + c];
                    best = c;
                }
            }

            labels[f] = best;
        }

        FillFromNearest(labels);

        // Repeatedly hand the shortest run below the minimum to a neighbour, until none is left.
        while (true)
        {
            List<(int Start, int Length)> runs = Runs(labels);
            if (runs.Count < 2)
            {
                return labels;
            }

            int shortest = -1;
            for (int r = 0; r < runs.Count; r++)
            {
                if (runs[r].Length < minimumFrames && (shortest < 0 || runs[r].Length < runs[shortest].Length))
                {
                    shortest = r;
                }
            }

            if (shortest < 0)
            {
                return labels;
            }

            (int start, int length) = runs[shortest];
            int left = shortest > 0 ? labels[runs[shortest - 1].Start] : -1;
            int right = shortest + 1 < runs.Count ? labels[runs[shortest + 1].Start] : -1;
            int target;
            if (left < 0 || right < 0 || left == right)
            {
                target = left >= 0 ? left : right;
            }
            else
            {
                float leftVotes = 0;
                float rightVotes = 0;
                for (int f = start; f < start + length; f++)
                {
                    leftVotes += votes[(f * clusters) + left];
                    rightVotes += votes[(f * clusters) + right];
                }

                target = leftVotes > rightVotes ? left
                    : rightVotes > leftVotes ? right
                    : runs[shortest - 1].Length >= runs[shortest + 1].Length ? left : right;
            }

            labels.AsSpan(start, length).Fill(target);
        }
    }

    private static List<(int Start, int Length)> Runs(int[] labels)
    {
        List<(int Start, int Length)> runs = [];
        int start = 0;
        for (int f = 1; f <= labels.Length; f++)
        {
            if (f == labels.Length || labels[f] != labels[start])
            {
                runs.Add((start, f - start));
                start = f;
            }
        }

        return runs;
    }

    // Gives each unlabelled frame (-1) the label of the nearest labelled frame, the earlier one on a tie.
    private static void FillFromNearest(int[] labels)
    {
        int previous = -1;
        int[] nearestBefore = new int[labels.Length];
        for (int f = 0; f < labels.Length; f++)
        {
            if (labels[f] >= 0)
            {
                previous = f;
            }

            nearestBefore[f] = previous;
        }

        int next = -1;
        for (int f = labels.Length - 1; f >= 0; f--)
        {
            if (labels[f] >= 0)
            {
                next = f;
                continue;
            }

            int before = nearestBefore[f];
            if (before >= 0 && (next < 0 || f - before <= next - f))
            {
                labels[f] = labels[before];
            }
            else if (next >= 0)
            {
                labels[f] = labels[next];
            }
        }
    }

    // A region no window reached takes the speaker at the nearer edge of the voted region nearest in
    // time, the earlier one on a tie; only regions voted by windows are read, so no guess spreads.
    private static void FillUnvotedRegions(List<int[]> regionLabels, (long Start, long End)[] regions)
    {
        int count = regionLabels.Count;
        bool[] voted = new bool[count];
        for (int r = 0; r < count; r++)
        {
            voted[r] = regionLabels[r][0] >= 0;
        }

        for (int r = 0; r < count; r++)
        {
            if (voted[r])
            {
                continue;
            }

            int before = r - 1;
            while (before >= 0 && !voted[before])
            {
                before--;
            }

            int after = r + 1;
            while (after < count && !voted[after])
            {
                after++;
            }

            int speaker = -1;
            long gapBefore = before >= 0 ? regions[r].Start - regions[before].End : long.MaxValue;
            long gapAfter = after < count ? regions[after].Start - regions[r].End : long.MaxValue;
            if (before >= 0 && gapBefore <= gapAfter)
            {
                speaker = regionLabels[before][^1];
            }
            else if (after < count)
            {
                speaker = regionLabels[after][0];
            }

            if (speaker >= 0)
            {
                Array.Fill(regionLabels[r], speaker);
            }
        }
    }
}
