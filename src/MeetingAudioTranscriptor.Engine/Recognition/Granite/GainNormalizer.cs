// Evens out the input level before the Granite model reads it: one of the gain schemes of GainMode, applied sample by sample and clipped to [-1, 1].
using System;
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Engine.Recognition.Granite;

/// <summary>Applies an input gain to a chunk.</summary>
/// <remarks>
/// Every scheme reduces to a gain per sample: a constant for the chunk-level schemes, and for the
/// others a piecewise-linear curve through anchors (a region's constant gain, or a window's gain
/// at its centre), constant before the first anchor and after the last. A curve rather than steps,
/// because a gain that jumps adds a click the model may read as a sound. A stretch whose RMS is
/// below the silence level keeps gain one, so digital silence and a dead line stay as they are.
/// The class holds no state and is safe to call from several threads.
/// </remarks>
internal static class GainNormalizer
{
    /// <summary>Writes the normalised samples of a chunk into a destination at least as long.</summary>
    /// <param name="source">The chunk's samples.</param>
    /// <param name="destination">Receives the normalised samples; its first <c>source.Length</c> values are written.</param>
    /// <param name="regions">The speech regions as [Start, End) offsets, in order; empty when unknown.</param>
    /// <param name="options">The scheme and its settings.</param>
    public static void Apply(ReadOnlySpan<float> source, Span<float> destination, IReadOnlyList<(int Start, int End)> regions, GainOptions options)
    {
        if (destination.Length < source.Length)
        {
            throw new ArgumentException($"the destination holds {destination.Length} samples but the source has {source.Length}; pass a destination at least as long", nameof(destination));
        }

        Span<float> output = destination[..source.Length];
        switch (options.Mode)
        {
            case GainMode.None:
                source.CopyTo(output);
                return;
            case GainMode.ChunkRms:
                Scale(source, output, GainFor(Rms(source), options));
                return;
            case GainMode.SpeechRms:
                Scale(source, output, GainFor(SpeechRms(source, regions), options));
                return;
            case GainMode.SlowAgc:
                ApplyCurve(source, output, WindowAnchors(source, options));
                return;
            case GainMode.RegionRms:
                ApplyCurve(source, output, RegionAnchors(source, regions, options));
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "the gain mode is not one of None, ChunkRms, SpeechRms, SlowAgc or RegionRms");
        }
    }

    /// <summary>The gain that brings an RMS to the target, capped, and one for silence.</summary>
    public static float GainFor(double rms, GainOptions options) =>
        rms < options.SilenceRms || rms <= 0 ? 1f : (float)Math.Min(options.MaxGain, options.TargetRms / rms);

    /// <summary>The root mean square of samples; zero for none.</summary>
    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        double sum = 0;
        foreach (float sample in samples)
        {
            sum += (double)sample * sample;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>The RMS over the speech regions together, or over the whole chunk when no region lies in it.</summary>
    public static double SpeechRms(ReadOnlySpan<float> source, IReadOnlyList<(int Start, int End)> regions)
    {
        double sum = 0;
        long count = 0;
        foreach ((int start, int end) in regions)
        {
            (int from, int to) = Clamp(start, end, source.Length);
            foreach (float sample in source[from..to])
            {
                sum += (double)sample * sample;
            }

            count += to - from;
        }

        return count == 0 ? Rms(source) : Math.Sqrt(sum / count);
    }

    /// <summary>Multiplies by one gain and clips.</summary>
    private static void Scale(ReadOnlySpan<float> source, Span<float> output, float gain)
    {
        for (int i = 0; i < source.Length; i++)
        {
            output[i] = Math.Clamp(source[i] * gain, -1f, 1f);
        }
    }

    /// <summary>
    /// The slow automatic gain: the RMS of each window, floored at a fraction of the median window
    /// RMS so a pause is not raised above the speech around it, turned into a gain anchored at the
    /// window's centre.
    /// </summary>
    private static List<Anchor> WindowAnchors(ReadOnlySpan<float> source, GainOptions options)
    {
        int window = Math.Max(1, (int)Math.Round(options.WindowSeconds * Audio.AudioFormat.ModelSampleRate));
        int count = (source.Length + window - 1) / window;
        double[] envelope = new double[count];
        for (int k = 0; k < count; k++)
        {
            int from = k * window;
            envelope[k] = Rms(source[from..Math.Min(source.Length, from + window)]);
        }

        double floor = options.EnvelopeFloor * Median(envelope);
        List<Anchor> anchors = new(count);
        for (int k = 0; k < count; k++)
        {
            int from = k * window;
            int centre = (from + Math.Min(source.Length, from + window)) / 2;
            anchors.Add(new Anchor(centre, centre, GainFor(Math.Max(envelope[k], floor), options)));
        }

        return anchors;
    }

    /// <summary>One gain per speech region, from that region's RMS; the chunk's gain when no region lies in it.</summary>
    private static List<Anchor> RegionAnchors(ReadOnlySpan<float> source, IReadOnlyList<(int Start, int End)> regions, GainOptions options)
    {
        List<Anchor> anchors = new(regions.Count);
        foreach ((int start, int end) in regions)
        {
            (int from, int to) = Clamp(start, end, source.Length);
            if (to > from && (anchors.Count == 0 || from >= anchors[^1].End))
            {
                anchors.Add(new Anchor(from, to, GainFor(Rms(source[from..to]), options)));
            }
        }

        if (anchors.Count == 0)
        {
            anchors.Add(new Anchor(0, source.Length, GainFor(Rms(source), options)));
        }

        return anchors;
    }

    /// <summary>
    /// Multiplies by the curve through the anchors (constant inside an anchor, linear between two,
    /// constant beyond the ends) and clips.
    /// </summary>
    private static void ApplyCurve(ReadOnlySpan<float> source, Span<float> output, List<Anchor> anchors)
    {
        int next = 0;
        for (int i = 0; i < source.Length; i++)
        {
            while (next < anchors.Count && i >= anchors[next].End)
            {
                next++;
            }

            float gain;
            if (next == anchors.Count)
            {
                gain = anchors[^1].Gain;
            }
            else if (i >= anchors[next].Start)
            {
                gain = anchors[next].Gain;
            }
            else if (next == 0)
            {
                gain = anchors[0].Gain;
            }
            else
            {
                Anchor before = anchors[next - 1];
                Anchor after = anchors[next];
                float t = (float)(i - before.End) / Math.Max(1, after.Start - before.End);
                gain = before.Gain + ((after.Gain - before.Gain) * t);
            }

            output[i] = Math.Clamp(source[i] * gain, -1f, 1f);
        }
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static (int From, int To) Clamp(int start, int end, int length)
    {
        int from = Math.Clamp(start, 0, length);
        return (from, Math.Clamp(end, from, length));
    }

    /// <summary>A stretch [Start, End) held at one gain; Start equals End for a point.</summary>
    private readonly record struct Anchor(int Start, int End, float Gain);
}
