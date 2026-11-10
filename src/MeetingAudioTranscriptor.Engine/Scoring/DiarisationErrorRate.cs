// The diarisation error rate and the speaker attribution accuracy of a hypothesis against a reference
// speaker timeline, frame by frame with the best one-to-one speaker mapping.
using System;
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Engine.Scoring;

/// <summary>How a diarisation is scored.</summary>
/// <param name="CollarSeconds">Time on each side of every reference boundary that is not scored, because a human marking boundaries is not exact to the frame.</param>
/// <param name="ExcludeOverlap">When true, time where two or more reference speakers talk at once is not scored.</param>
public sealed record DiarisationScoringOptions(double CollarSeconds = 0.25, bool ExcludeOverlap = false);

/// <summary>The scored amounts, in seconds of speaker time, and the derived rates.</summary>
/// <param name="ReferenceSeconds">Reference speaker time scored (a frame with two reference speakers counts twice).</param>
/// <param name="MissedSeconds">Reference speaker time the hypothesis gave no speaker.</param>
/// <param name="FalseAlarmSeconds">Hypothesis speaker time beyond the reference speakers present.</param>
/// <param name="ConfusionSeconds">Speaker time given to a speaker who does not map to the one speaking.</param>
/// <param name="AttributionAccuracy">Of the reference time with exactly one speaker (no collar), the fraction the hypothesis gives to that speaker's mapped speaker.</param>
/// <param name="ReferenceSpeakers">The number of distinct reference speakers.</param>
/// <param name="HypothesisSpeakers">The number of distinct hypothesis speakers.</param>
/// <param name="Mapping">Each mapped reference speaker and the hypothesis speaker it maps to.</param>
public sealed record DiarisationScore(
    double ReferenceSeconds,
    double MissedSeconds,
    double FalseAlarmSeconds,
    double ConfusionSeconds,
    double AttributionAccuracy,
    int ReferenceSpeakers,
    int HypothesisSpeakers,
    IReadOnlyDictionary<string, string> Mapping)
{
    /// <summary>(missed + false alarm + confusion) / reference time; 0 when no reference time is scored.</summary>
    public double ErrorRate => ReferenceSeconds > 0 ? (MissedSeconds + FalseAlarmSeconds + ConfusionSeconds) / ReferenceSeconds : 0;
}

/// <summary>Computes the diarisation error rate (DER) and the speaker attribution accuracy.</summary>
/// <remarks>
/// <para>
/// Time is cut into 10 ms frames; a speaker is active in a frame when one of its segments covers the
/// frame's centre. The reference and hypothesis speakers are mapped one to one so that the total
/// time they share is largest (the Hungarian algorithm on the co-occurrence matrix of the scored
/// frames), as the NIST scoring tool does. Per frame, with R reference and H hypothesis speakers of
/// which C are correctly mapped: missed = max(0, R - H), false alarm = max(0, H - R), confusion =
/// min(R, H) - C.
/// </para>
/// <para>
/// The attribution accuracy answers the question a reader of the transcript asks: when one person
/// was speaking, was the right label shown? It reads every frame with exactly one reference speaker,
/// ignores the collar, and counts a frame correct only when the hypothesis holds that speaker's mapped
/// speaker, so missed speech counts against it as well.
/// </para>
/// </remarks>
public static class DiarisationErrorRate
{
    private const double FrameSeconds = 0.01;

    /// <summary>Scores a hypothesis against a reference.</summary>
    public static DiarisationScore Compute(IReadOnlyList<SpeakerSegment> reference, IReadOnlyList<SpeakerSegment> hypothesis, DiarisationScoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(hypothesis);
        options ??= new DiarisationScoringOptions();

        List<string> referenceNames = Names(reference);
        List<string> hypothesisNames = Names(hypothesis);
        double end = 0;
        foreach (SpeakerSegment segment in reference)
        {
            end = Math.Max(end, segment.End);
        }

        foreach (SpeakerSegment segment in hypothesis)
        {
            end = Math.Max(end, segment.End);
        }

        int frames = (int)Math.Ceiling(end / FrameSeconds) + 1;
        ulong[] referenceActive = Activity(reference, referenceNames, frames);
        ulong[] hypothesisActive = Activity(hypothesis, hypothesisNames, frames);

        // Frames within the collar of a reference boundary are not scored.
        bool[] scored = new bool[frames];
        Array.Fill(scored, true);
        if (options.CollarSeconds > 0)
        {
            foreach (SpeakerSegment segment in reference)
            {
                Unscore(scored, segment.Start - options.CollarSeconds, segment.Start + options.CollarSeconds);
                Unscore(scored, segment.End - options.CollarSeconds, segment.End + options.CollarSeconds);
            }
        }

        if (options.ExcludeOverlap)
        {
            for (int f = 0; f < frames; f++)
            {
                if (ulong.PopCount(referenceActive[f]) > 1)
                {
                    scored[f] = false;
                }
            }
        }

        // The co-occurrence of every reference and hypothesis speaker over the scored frames.
        double[,] shared = new double[referenceNames.Count, hypothesisNames.Count];
        for (int f = 0; f < frames; f++)
        {
            if (!scored[f] || referenceActive[f] == 0 || hypothesisActive[f] == 0)
            {
                continue;
            }

            for (int r = 0; r < referenceNames.Count; r++)
            {
                if ((referenceActive[f] & (1UL << r)) == 0)
                {
                    continue;
                }

                for (int h = 0; h < hypothesisNames.Count; h++)
                {
                    if ((hypothesisActive[f] & (1UL << h)) != 0)
                    {
                        shared[r, h] += 1;
                    }
                }
            }
        }

        int[] mapping = MaximumAssignment(shared);

        long referenceFrames = 0;
        long missed = 0;
        long falseAlarm = 0;
        long confusion = 0;
        long singleFrames = 0;
        long singleCorrect = 0;
        for (int f = 0; f < frames; f++)
        {
            int referenceCount = (int)ulong.PopCount(referenceActive[f]);
            int hypothesisCount = (int)ulong.PopCount(hypothesisActive[f]);
            int correct = 0;
            for (int r = 0; r < referenceNames.Count; r++)
            {
                if ((referenceActive[f] & (1UL << r)) != 0 && mapping[r] >= 0 && (hypothesisActive[f] & (1UL << mapping[r])) != 0)
                {
                    correct++;
                }
            }

            if (referenceCount == 1)
            {
                singleFrames++;
                singleCorrect += correct;
            }

            if (!scored[f])
            {
                continue;
            }

            referenceFrames += referenceCount;
            missed += Math.Max(0, referenceCount - hypothesisCount);
            falseAlarm += Math.Max(0, hypothesisCount - referenceCount);
            confusion += Math.Min(referenceCount, hypothesisCount) - correct;
        }

        Dictionary<string, string> mapped = new(StringComparer.Ordinal);
        for (int r = 0; r < referenceNames.Count; r++)
        {
            if (mapping[r] >= 0)
            {
                mapped[referenceNames[r]] = hypothesisNames[mapping[r]];
            }
        }

        return new DiarisationScore(
            referenceFrames * FrameSeconds,
            missed * FrameSeconds,
            falseAlarm * FrameSeconds,
            confusion * FrameSeconds,
            singleFrames > 0 ? (double)singleCorrect / singleFrames : 1.0,
            referenceNames.Count,
            hypothesisNames.Count,
            mapped);
    }

    /// <summary>The column assigned to each row so the sum of the chosen cells is largest; -1 for a row left without a column.</summary>
    /// <remarks>The Hungarian algorithm (shortest augmenting paths with potentials) on the square matrix padded with zeros, in O(n^3).</remarks>
    internal static int[] MaximumAssignment(double[,] weights)
    {
        int rows = weights.GetLength(0);
        int columns = weights.GetLength(1);
        int n = Math.Max(rows, columns);
        int[] result = new int[rows];
        Array.Fill(result, -1);
        if (n == 0)
        {
            return result;
        }

        double largest = 0;
        foreach (double w in weights)
        {
            largest = Math.Max(largest, w);
        }

        // Cost of cell (i, j), 1-based, as largest minus weight; padding cells weigh 0.
        double Cost(int i, int j) => i <= rows && j <= columns ? largest - weights[i - 1, j - 1] : largest;

        double[] u = new double[n + 1];
        double[] v = new double[n + 1];
        int[] matchOfColumn = new int[n + 1];
        int[] way = new int[n + 1];
        for (int i = 1; i <= n; i++)
        {
            matchOfColumn[0] = i;
            int j0 = 0;
            double[] minimum = new double[n + 1];
            bool[] used = new bool[n + 1];
            Array.Fill(minimum, double.PositiveInfinity);
            do
            {
                used[j0] = true;
                int i0 = matchOfColumn[j0];
                double delta = double.PositiveInfinity;
                int j1 = 0;
                for (int j = 1; j <= n; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    double current = Cost(i0, j) - u[i0] - v[j];
                    if (current < minimum[j])
                    {
                        minimum[j] = current;
                        way[j] = j0;
                    }

                    if (minimum[j] < delta)
                    {
                        delta = minimum[j];
                        j1 = j;
                    }
                }

                for (int j = 0; j <= n; j++)
                {
                    if (used[j])
                    {
                        u[matchOfColumn[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minimum[j] -= delta;
                    }
                }

                j0 = j1;
            }
            while (matchOfColumn[j0] != 0);

            do
            {
                int j1 = way[j0];
                matchOfColumn[j0] = matchOfColumn[j1];
                j0 = j1;
            }
            while (j0 != 0);
        }

        for (int j = 1; j <= n; j++)
        {
            int i = matchOfColumn[j];
            if (i >= 1 && i <= rows && j <= columns && weights[i - 1, j - 1] > 0)
            {
                result[i - 1] = j - 1;
            }
        }

        return result;
    }

    private static List<string> Names(IReadOnlyList<SpeakerSegment> segments)
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (SpeakerSegment segment in segments)
        {
            if (seen.Add(segment.Speaker))
            {
                names.Add(segment.Speaker);
            }
        }

        if (names.Count > 64)
        {
            throw new ArgumentException($"the segments name {names.Count} speakers where the scorer handles at most 64; split the recording");
        }

        return names;
    }

    // One bit per active speaker per frame; a speaker is active where a segment covers the frame's centre.
    private static ulong[] Activity(IReadOnlyList<SpeakerSegment> segments, List<string> names, int frames)
    {
        Dictionary<string, int> index = new(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
        {
            index[names[i]] = i;
        }

        ulong[] active = new ulong[frames];
        foreach (SpeakerSegment segment in segments)
        {
            ulong bit = 1UL << index[segment.Speaker];
            (int first, int last) = CentredFrames(segment.Start, segment.End, frames);
            for (int f = first; f < last; f++)
            {
                active[f] |= bit;
            }
        }

        return active;
    }

    private static void Unscore(bool[] scored, double start, double end)
    {
        (int first, int last) = CentredFrames(start, end, scored.Length);
        for (int f = first; f < last; f++)
        {
            scored[f] = false;
        }
    }

    // The frames whose centre (f + 0.5) * 10 ms lies in [start, end).
    private static (int First, int Last) CentredFrames(double start, double end, int frames)
    {
        int first = (int)Math.Max(0, Math.Ceiling((start / FrameSeconds) - 0.5 - 1e-9));
        int last = (int)Math.Min(frames, Math.Ceiling((end / FrameSeconds) - 0.5 - 1e-9));
        return (first, Math.Max(first, last));
    }
}
