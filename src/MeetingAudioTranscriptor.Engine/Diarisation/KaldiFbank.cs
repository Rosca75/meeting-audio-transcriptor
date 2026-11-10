// The Kaldi-compatible log mel filterbank the speaker-embedding model was trained on, computed frame by
// frame without allocating, and the per-window mean normalisation the model expects.
using System;
using MeetingAudioTranscriptor.Engine.Dsp;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>The analysis window applied to each frame before the transform.</summary>
internal enum FbankWindow
{
    /// <summary>The Hamming window, 0.54 - 0.46 cos(2 pi i / (N - 1)), which the WeSpeaker training recipe uses.</summary>
    Hamming,

    /// <summary>Kaldi's default window: a Hann window raised to the power 0.85.</summary>
    Povey,
}

/// <summary>Computes 80-bin log mel filterbank features exactly as Kaldi's <c>compute-fbank-feats</c> does.</summary>
/// <remarks>
/// <para>
/// The settings are those of the WeSpeaker embedding recipe: 16 kHz input scaled to the 16-bit integer
/// range, 25 ms frames every 10 ms with <c>snip_edges</c> (only whole frames, the first starting at
/// sample 0), no dither, the DC offset of each frame removed, pre-emphasis 0.97, a 512-point power
/// spectrum, 80 triangular filters equally spaced on Kaldi's mel scale (1127 ln(1 + f / 700)) from
/// 20 Hz to the Nyquist frequency, and the natural logarithm floored at the float epsilon. The steps
/// run in Kaldi's order, which matters: the DC offset is removed before pre-emphasis.
/// </para>
/// <para>
/// An instance holds read-only tables (the window and the sparse filter weights) and a shared
/// <see cref="RealFft"/>, so one instance serves any number of threads provided each passes its own
/// <see cref="Scratch"/>. Nothing is allocated per frame.
/// </para>
/// </remarks>
internal sealed class KaldiFbank
{
    /// <summary>The sample rate the features are defined for.</summary>
    public const int SampleRate = 16000;

    /// <summary>The number of mel bins per frame.</summary>
    public const int BinCount = 80;

    /// <summary>Samples per frame: 25 ms.</summary>
    public const int FrameLength = 400;

    /// <summary>Samples between frame starts: 10 ms.</summary>
    public const int FrameShift = 160;

    private const int FftSize = 512;
    private const float PreEmphasis = 0.97f;
    private const float LowFrequency = 20f;

    // Kaldi multiplies samples in [-1, 1] by 32768 because its tools read 16-bit integers as they are.
    private const float IntegerScale = 32768f;

    // Kaldi floors the filter energies at the float epsilon before the logarithm.
    private const float LogFloor = 1.19209290e-07f;

    private readonly RealFft _fft = new(FftSize);
    private readonly float[] _window = new float[FrameLength];

    // The filters are triangles over a few FFT bins each, stored sparsely: filter b covers bins
    // _firstBin[b] .. _firstBin[b] + _weightCount[b] - 1 with weights from _weights at _weightOffset[b].
    private readonly int[] _firstBin = new int[BinCount];
    private readonly int[] _weightCount = new int[BinCount];
    private readonly int[] _weightOffset = new int[BinCount];
    private readonly float[] _weights;

    /// <summary>Builds the window and the filter tables.</summary>
    public KaldiFbank(FbankWindow window = FbankWindow.Hamming)
    {
        Window = window;
        double denominator = FrameLength - 1;
        for (int i = 0; i < FrameLength; i++)
        {
            double cosine = Math.Cos(2.0 * Math.PI * i / denominator);
            _window[i] = window == FbankWindow.Hamming
                ? (float)(0.54 - (0.46 * cosine))
                : (float)Math.Pow(0.5 - (0.5 * cosine), 0.85);
        }

        _weights = BuildFilters(_firstBin, _weightCount, _weightOffset);
    }

    /// <summary>The analysis window this instance applies.</summary>
    public FbankWindow Window { get; }

    /// <summary>The number of whole frames <paramref name="sampleCount"/> samples hold (Kaldi's snip_edges rule).</summary>
    public static int FrameCount(int sampleCount)
    {
        return sampleCount < FrameLength ? 0 : 1 + ((sampleCount - FrameLength) / FrameShift);
    }

    /// <summary>The per-thread working buffers one caller passes to <see cref="Compute"/>.</summary>
    public sealed class Scratch
    {
        internal float[] Frame { get; } = new float[FftSize];

        internal float[] Transform { get; } = new float[FftSize];

        internal float[] Power { get; } = new float[(FftSize / 2) + 1];
    }

    /// <summary>Computes the features of every whole frame of the samples.</summary>
    /// <param name="samples">Mono samples in [-1, 1] at 16 kHz.</param>
    /// <param name="features">At least FrameCount(samples.Length) * 80 floats; frame after frame, bin after bin.</param>
    /// <param name="scratch">This caller's working buffers.</param>
    /// <returns>The number of frames written.</returns>
    public int Compute(ReadOnlySpan<float> samples, Span<float> features, Scratch scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        int frames = FrameCount(samples.Length);
        if (features.Length < frames * BinCount)
        {
            throw new ArgumentException(
                $"the feature buffer holds {features.Length} floats where {frames} frames of {BinCount} bins need {frames * BinCount}; size it with FrameCount",
                nameof(features));
        }

        for (int f = 0; f < frames; f++)
        {
            ComputeFrame(samples.Slice(f * FrameShift, FrameLength), features.Slice(f * BinCount, BinCount), scratch);
        }

        return frames;
    }

    /// <summary>Subtracts from every bin its mean over the frames (cepstral mean normalisation without variance scaling).</summary>
    /// <param name="features">Frame after frame, 80 bins each; normalised in place.</param>
    public static void SubtractMean(Span<float> features)
    {
        int frames = features.Length / BinCount;
        if (frames == 0)
        {
            return;
        }

        // The means are accumulated in double, so a long window does not lose precision.
        Span<double> sums = stackalloc double[BinCount];
        for (int f = 0; f < frames; f++)
        {
            ReadOnlySpan<float> row = features.Slice(f * BinCount, BinCount);
            for (int b = 0; b < BinCount; b++)
            {
                sums[b] += row[b];
            }
        }

        Span<float> means = stackalloc float[BinCount];
        for (int b = 0; b < BinCount; b++)
        {
            means[b] = (float)(sums[b] / frames);
        }

        for (int f = 0; f < frames; f++)
        {
            Span<float> row = features.Slice(f * BinCount, BinCount);
            for (int b = 0; b < BinCount; b++)
            {
                row[b] -= means[b];
            }
        }
    }

    private void ComputeFrame(ReadOnlySpan<float> samples, Span<float> output, Scratch scratch)
    {
        Span<float> frame = scratch.Frame;

        // Scale to the integer range and remove the frame's DC offset.
        double sum = 0;
        for (int i = 0; i < FrameLength; i++)
        {
            float value = samples[i] * IntegerScale;
            frame[i] = value;
            sum += value;
        }

        float mean = (float)(sum / FrameLength);
        for (int i = 0; i < FrameLength; i++)
        {
            frame[i] -= mean;
        }

        // Pre-emphasis from the end backwards, so each sample reads its unmodified predecessor; the
        // first sample is emphasised against itself, as Kaldi does.
        for (int i = FrameLength - 1; i > 0; i--)
        {
            frame[i] -= PreEmphasis * frame[i - 1];
        }

        frame[0] -= PreEmphasis * frame[0];

        // Window, then zero-pad to the transform size.
        for (int i = 0; i < FrameLength; i++)
        {
            frame[i] *= _window[i];
        }

        frame[FrameLength..].Clear();

        Span<float> power = scratch.Power;
        _fft.PowerSpectrum(frame, power, scratch.Transform);

        ReadOnlySpan<float> weights = _weights;
        for (int b = 0; b < BinCount; b++)
        {
            ReadOnlySpan<float> w = weights.Slice(_weightOffset[b], _weightCount[b]);
            ReadOnlySpan<float> p = power.Slice(_firstBin[b], _weightCount[b]);
            float energy = 0;
            for (int k = 0; k < w.Length; k++)
            {
                energy += w[k] * p[k];
            }

            output[b] = MathF.Log(MathF.Max(energy, LogFloor));
        }
    }

    // Kaldi's mel filter construction: the mel range from 20 Hz to Nyquist is cut into 81 equal steps,
    // filter b rises from step b to step b + 1 and falls to step b + 2, and an FFT bin (0 .. 255,
    // the Nyquist bin excluded as Kaldi excludes it) gets the triangle's value at its own mel position.
    private static float[] BuildFilters(int[] firstBin, int[] weightCount, int[] weightOffset)
    {
        const int fftBins = FftSize / 2;
        double binWidth = (double)SampleRate / FftSize;
        double melLow = Mel(LowFrequency);
        double melHigh = Mel(SampleRate / 2.0);
        double melStep = (melHigh - melLow) / (BinCount + 1);

        float[][] dense = new float[BinCount][];
        int total = 0;
        for (int b = 0; b < BinCount; b++)
        {
            double left = melLow + (b * melStep);
            double center = melLow + ((b + 1) * melStep);
            double right = melLow + ((b + 2) * melStep);
            int first = -1;
            int last = -1;
            float[] row = new float[fftBins];
            for (int i = 0; i < fftBins; i++)
            {
                double mel = Mel(binWidth * i);
                if (mel > left && mel < right)
                {
                    row[i] = (float)(mel <= center ? (mel - left) / (center - left) : (right - mel) / (right - center));
                    if (first < 0)
                    {
                        first = i;
                    }

                    last = i;
                }
            }

            firstBin[b] = Math.Max(first, 0);
            weightCount[b] = first < 0 ? 0 : last - first + 1;
            dense[b] = row;
            total += weightCount[b];
        }

        float[] weights = new float[total];
        int offset = 0;
        for (int b = 0; b < BinCount; b++)
        {
            weightOffset[b] = offset;
            Array.Copy(dense[b], firstBin[b], weights, offset, weightCount[b]);
            offset += weightCount[b];
        }

        return weights;
    }

    private static double Mel(double frequency)
    {
        return 1127.0 * Math.Log(1.0 + (frequency / 700.0));
    }
}
