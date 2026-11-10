// Converts any audio source to the sample rate the models read, streaming, with a polyphase
// windowed-sinc filter whose quality is fixed by numbers rather than by a library default.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Audio;

/// <summary>Resamples another source to a target rate (16 kHz by default) as it is read.</summary>
/// <remarks>
/// <para>
/// Output sample n is the band-limited input evaluated at time n / targetRate, so the output starts
/// with no delay and lines up with the input to the sample: timestamps computed on the output are
/// times in the recording. The input is treated as zero before its first sample and after its last,
/// and the output holds exactly ceil(inputLength * target / source) samples.
/// </para>
/// <para>
/// The filter is a Kaiser-windowed sinc designed for 90 dB of stop-band attenuation (80 dB is the
/// requirement; the margin absorbs float rounding and the window estimate). Its pass band is flat to
/// within a few thousandths of a decibel up to 0.9 of the lower Nyquist frequency, and its stop band
/// starts at the lower Nyquist frequency, so nothing above what the output can represent folds back
/// into speech. The rate pair is reduced by its greatest common divisor to L/M and the filter is
/// tabulated for each of the L output phases. When L exceeds 4096 (an awkward pair such as
/// 47999 to 16000) the table holds 4096 phases and each output interpolates linearly between the two
/// nearest, which keeps the table small at a cost far below the stop-band level.
/// </para>
/// <para>
/// Each output is one dot product of its phase's taps with the input window, vectorised with
/// <see cref="Vector{T}"/>. Outputs n, n + L, ..., n + 7L share a phase, so they are computed
/// together: one load of the taps feeds eight windows, which is what makes the filter run near the
/// processor's multiply-add rate. The outputs are computed a frame of whole 8L groups at a time into
/// a staging buffer and copied out from there. Each output's arithmetic is the same whether it was
/// computed in a group or alone (at the tail), so the result is bit-identical for any read sizes.
/// </para>
/// </remarks>
public sealed class ResamplingAudioSource : IAudioSource
{
    /// <summary>The stop-band attenuation the filter is designed for, in decibels.</summary>
    public const double DesignAttenuationDecibels = 90.0;

    /// <summary>The pass band edge as a fraction of the lower Nyquist frequency.</summary>
    public const double PassBandFraction = 0.9;

    // The phase table holds at most this many phases; beyond it outputs interpolate between phases.
    private const int MaximumPhases = 4096;

    // Taps per phase are a multiple of this, so a vector of any width (4, 8 or 16 floats) divides them
    // and the filter itself does not change with the machine's vector width.
    private const int TapAlignment = 16;

    // How many outputs sharing a phase are computed in one pass over its taps.
    private const int GroupSize = 8;

    // The fewest outputs a frame holds, and the frame size when phases are interpolated.
    private const int MinimumFrame = 256;

    // Input samples read from the inner source per refill, at least.
    private const int InputChunk = 16384;

    // How many rate pairs keep their table between instances.
    private const int CachedPairs = 8;

    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<(int Source, int Target), Lazy<(float[] Table, int Taps)>> Cache = [];

    private readonly IAudioSource _inner;
    private readonly bool _passThrough;

    // The reduced ratio: output rate / input rate = _up / _down.
    private readonly long _up;
    private readonly long _down;

    // The phase table: _phaseCount (+1 when interpolating) rows of _taps floats each.
    private readonly float[] _table = [];
    private readonly int _taps;
    private readonly int _halfWidth;
    private readonly int _phaseCount;
    private readonly bool _interpolates;

    // The input history: _buffer[0] is input index _bufferStart; _count entries are valid.
    private readonly float[] _buffer = [];
    private long _bufferStart;
    private int _count;
    private bool _innerEnded;
    private long _inputLength;

    // Outputs are computed a frame at a time into _staged and handed out from _stagedPosition.
    private readonly float[] _staged = [];
    private readonly int _frameLength;
    private int _stagedCount;
    private int _stagedPosition;
    private long _nextFrame;
    private long _outputLength = long.MaxValue;

    private bool _disposed;

    /// <summary>Wraps a source and delivers its audio at the target rate.</summary>
    /// <param name="inner">The source to resample; disposed with this one.</param>
    /// <param name="targetSampleRate">The output rate in samples per second; at least 1.</param>
    public ResamplingAudioSource(IAudioSource inner, int targetSampleRate = AudioFormat.ModelSampleRate)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetSampleRate, 1);
        if (inner.SampleRate < 1)
        {
            throw new ArgumentException(
                $"the source to resample reports a sample rate of {inner.SampleRate} Hz where at least 1 is needed; the decoder that created it is faulty",
                nameof(inner));
        }

        _inner = inner;
        SampleRate = targetSampleRate;
        if (inner.SampleRate == targetSampleRate)
        {
            _passThrough = true;
            return;
        }

        long divisor = GreatestCommonDivisor(targetSampleRate, inner.SampleRate);
        _up = targetSampleRate / divisor;
        _down = inner.SampleRate / divisor;
        _interpolates = _up > MaximumPhases;
        _phaseCount = _interpolates ? MaximumPhases : (int)_up;

        _table = GetTable(inner.SampleRate, targetSampleRate, _phaseCount, _interpolates, out _taps);
        _halfWidth = _taps / 2;

        // A frame is whole groups of 8L outputs, each starting at phase 0, and at least
        // MinimumFrame outputs so the per-frame work is spread thin; the buffer holds one frame's
        // windows plus room to read ahead.
        int group = GroupSize * (int)_up;
        _frameLength = _interpolates ? MinimumFrame : group * (int)CeilingDivide(MinimumFrame, group);
        long frameSpan = CeilingDivide(_frameLength * _down, _up) + _taps + 1;
        _buffer = new float[frameSpan + InputChunk];
        _staged = new float[_frameLength];

        // The first window reaches _halfWidth - 1 samples before the input starts; they are zeros.
        _bufferStart = -(_halfWidth - 1);
        _count = _halfWidth - 1;
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public double? DurationSeconds => _inner.DurationSeconds;

    /// <summary>The number of filter taps per output sample, zero when the rates are equal.</summary>
    internal int TapsPerOutput => _passThrough ? 0 : _taps;

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_passThrough)
        {
            return _inner.Read(buffer);
        }

        int produced = 0;
        while (produced < buffer.Length)
        {
            if (_stagedPosition == _stagedCount && !StageNextFrame())
            {
                break;
            }

            int take = Math.Min(buffer.Length - produced, _stagedCount - _stagedPosition);
            _staged.AsSpan(_stagedPosition, take).CopyTo(buffer[produced..]);
            _stagedPosition += take;
            produced += take;
        }

        return produced;
    }

    /// <summary>Disposes the wrapped source.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inner.Dispose();
    }

    // Computes the next frame of outputs into the staging buffer; false when the output is complete.
    private bool StageNextFrame()
    {
        long first = _nextFrame;
        long last = first + _frameLength - 1;

        // The frame's windows run from the first output's window start to the last output's window end.
        EnsureInput(CentreOf(first) - _halfWidth + 1, CentreOf(last) + _halfWidth + 1);
        if (first >= _outputLength)
        {
            return false;
        }

        int count = (int)Math.Min(_frameLength, _outputLength - first);
        if (_interpolates)
        {
            StageInterpolated(first, count);
        }
        else if (count == _frameLength)
        {
            StageGrouped(first);
        }
        else
        {
            StageSingly(first, count);
        }

        _stagedCount = count;
        _stagedPosition = 0;
        _nextFrame += count;
        return true;
    }

    // A whole frame, in groups of 8L outputs: for each phase p of a group starting at g, the outputs
    // g + p + jL for j = 0..7 come from one pass over the taps.
    private void StageGrouped(long first)
    {
        int up = (int)_up;
        int down = (int)_down;
        int taps = _taps;
        int whole = down / up;
        int fraction = down % up;
        ReadOnlySpan<float> table = _table;
        ReadOnlySpan<float> history = _buffer.AsSpan(0, _count);

        for (int group = 0; group < _frameLength; group += GroupSize * up)
        {
            Span<float> staged = _staged.AsSpan(group, GroupSize * up);

            // A group starts at phase 0, so output g + p has its window offset by floor(p * M / L).
            int start = (int)(CentreOf(first + group) - _halfWidth + 1 - _bufferStart);
            int phase = 0;
            for (int p = 0; p < up; p++)
            {
                DotEight(table.Slice(phase * taps, taps), history, start, down, staged[p..], up);

                // Step to the next output: whole and fractional parts of M / L, the fraction carried.
                start += whole;
                phase += fraction;
                if (phase >= up)
                {
                    phase -= up;
                    start++;
                }
            }
        }
    }

    // The tail of the output, shorter than a frame: one output at a time, with the same arithmetic.
    private void StageSingly(long first, int count)
    {
        int taps = _taps;
        ReadOnlySpan<float> table = _table;
        ReadOnlySpan<float> history = _buffer.AsSpan(0, _count);
        for (int k = 0; k < count; k++)
        {
            long n = first + k;
            int phase = (int)(n * _down % _up);
            int start = (int)(CentreOf(n) - _halfWidth + 1 - _bufferStart);
            _staged[k] = DotOne(table.Slice(phase * taps, taps), history.Slice(start, taps));
        }
    }

    // Outputs whose exact phase falls between two tabulated rows: both rows, interpolated linearly.
    private void StageInterpolated(long first, int count)
    {
        int taps = _taps;
        ReadOnlySpan<float> table = _table;
        ReadOnlySpan<float> history = _buffer.AsSpan(0, _count);
        for (int k = 0; k < count; k++)
        {
            long n = first + k;
            long scaled = n * _down % _up * _phaseCount;
            int row = (int)(scaled / _up);
            float weight = (float)(scaled % _up / (double)_up);
            ReadOnlySpan<float> window = history.Slice((int)(CentreOf(n) - _halfWidth + 1 - _bufferStart), taps);
            float lower = DotOne(table.Slice(row * taps, taps), window);
            float upper = DotOne(table.Slice((row + 1) * taps, taps), window);
            _staged[k] = lower + (weight * (upper - lower));
        }
    }

    // The input index the window of output n is centred on: floor(n * M / L).
    private long CentreOf(long n)
    {
        return n * _down / _up;
    }

    // Makes input [from, to) available in the buffer, dropping what lies before it, reading the inner
    // source and padding with zeros past its end. Learns the output length when the source ends.
    private void EnsureInput(long from, long to)
    {
        // The history is moved down only when the frame would not fit, about once per InputChunk
        // samples, rather than on every frame.
        if (_bufferStart + _buffer.Length < to)
        {
            int drop = (int)Math.Clamp(from - _bufferStart, 0, _count);
            _buffer.AsSpan(drop, _count - drop).CopyTo(_buffer);
            _bufferStart += drop;
            _count -= drop;
        }

        while (_bufferStart + _count < to)
        {
            Span<float> free = _buffer.AsSpan(_count);
            if (_innerEnded)
            {
                // Past the end the input is zeros, as far as the frame's last window reaches.
                int zeros = (int)Math.Min(free.Length, to - (_bufferStart + _count));
                free[..zeros].Clear();
                _count += zeros;
                continue;
            }

            int read = _inner.Read(free);
            if (read == 0)
            {
                _innerEnded = true;
                _outputLength = CeilingDivide(_inputLength * _up, _down);
                continue;
            }

            _inputLength += read;
            _count += read;
        }
    }

    // Returns the phase table for a rate pair, building it on first use. A table is read-only once
    // built, and an awkward pair's table takes a noticeable fraction of a second to build, so every
    // file of a batch at the same rate reuses one, and two sources created at once wait for one build.
    // The cache holds a few pairs and starts over when full.
    private static float[] GetTable(int sourceRate, int targetRate, int phaseCount, bool interpolates, out int taps)
    {
        Lazy<(float[] Table, int Taps)>? entry;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue((sourceRate, targetRate), out entry))
            {
                if (Cache.Count >= CachedPairs)
                {
                    Cache.Clear();
                }

                entry = new Lazy<(float[] Table, int Taps)>(() => BuildTable(sourceRate, targetRate, phaseCount, interpolates));
                Cache[(sourceRate, targetRate)] = entry;
            }
        }

        (float[] table, taps) = entry.Value;
        return table;
    }

    // Designs the Kaiser-windowed sinc and tabulates it for every phase.
    private static (float[] Table, int Taps) BuildTable(int sourceRate, int targetRate, int phaseCount, bool interpolates)
    {
        // The cut-off and the transition band in cycles per input sample: the band from 0.9 to 1.0
        // of the lower Nyquist frequency carries the Kaiser transition.
        double lowerRatio = Math.Min(targetRate, sourceRate) / (double)sourceRate;
        double transition = 0.5 * (1.0 - PassBandFraction) * lowerRatio;
        double cutoff = 0.5 * (1.0 + PassBandFraction) * 0.5 * lowerRatio;
        double beta = 0.1102 * (DesignAttenuationDecibels - 8.7);
        double length = ((DesignAttenuationDecibels - 7.95) / (2.285 * 2.0 * Math.PI * transition)) + 1.0;
        int taps = (int)Math.Ceiling(length / TapAlignment) * TapAlignment;

        int rows = phaseCount + (interpolates ? 1 : 0);
        float[] table = new float[rows * taps];
        double[] values = new double[taps];
        for (int row = 0; row < rows; row++)
        {
            FillPhase(table.AsSpan(row * taps, taps), values, row / (double)phaseCount, cutoff, beta, taps / 2);
        }

        return (table, taps);
    }

    // Tabulates the kernel for outputs whose time lies frac input samples after the window's centre sample.
    private static void FillPhase(Span<float> taps, double[] values, double frac, double cutoff, double beta, int halfWidth)
    {
        // Tap k multiplies input centre - halfWidth + 1 + k, which lies tau = frac + halfWidth - 1 - k
        // samples before the output's time; the kernel is symmetric, so the sign of tau does not matter.
        double sum = 0;
        double denominator = BesselI0(beta);
        for (int k = 0; k < taps.Length; k++)
        {
            double tau = frac + halfWidth - 1 - k;
            double x = tau / halfWidth;
            double window = Math.Abs(x) >= 1.0 ? 0.0 : BesselI0(beta * Math.Sqrt(1.0 - (x * x))) / denominator;
            double argument = 2.0 * cutoff * tau;
            double sinc = Math.Abs(argument) < 1e-12 ? 1.0 : Math.Sin(Math.PI * argument) / (Math.PI * argument);
            values[k] = 2.0 * cutoff * sinc * window;
            sum += values[k];
        }

        // Each phase is scaled to a gain of exactly one at 0 Hz, so a constant input stays constant.
        for (int k = 0; k < taps.Length; k++)
        {
            taps[k] = (float)(values[k] / sum);
        }
    }

    // The zeroth-order modified Bessel function of the first kind, by its power series.
    private static double BesselI0(double x)
    {
        double sum = 1.0;
        double term = 1.0;
        double half = x / 2.0;
        for (int k = 1; k < 64; k++)
        {
            term *= half / k;
            double squared = term * term;
            sum += squared;
            if (squared < sum * 1e-17)
            {
                break;
            }
        }

        return sum;
    }

    // One output: the inner product of the taps and one window, accumulated in one vector, lane by
    // lane in tap order, then summed across the lanes. DotEight repeats exactly this per window.
    private static float DotOne(ReadOnlySpan<float> taps, ReadOnlySpan<float> window)
    {
        ReadOnlySpan<Vector<float>> a = MemoryMarshal.Cast<float, Vector<float>>(taps);
        ReadOnlySpan<Vector<float>> b = MemoryMarshal.Cast<float, Vector<float>>(window[..taps.Length]);
        Vector<float> sum = Vector<float>.Zero;
        for (int i = 0; i < a.Length; i++)
        {
            sum = Vector.MultiplyAddEstimate(a[i], b[i], sum);
        }

        return Vector.Sum(sum);
    }

    // Eight outputs sharing one phase, whose windows start step input samples apart. Each tap vector
    // is loaded once for the eight, and the eight independent sums keep the multiply-add units busy
    // instead of waiting on one chain. Output j is written to results[j * stride].
    private static void DotEight(
        ReadOnlySpan<float> taps,
        ReadOnlySpan<float> history,
        int start,
        int step,
        Span<float> results,
        int stride)
    {
        int length = taps.Length;
        ReadOnlySpan<Vector<float>> a = MemoryMarshal.Cast<float, Vector<float>>(taps);
        ReadOnlySpan<Vector<float>> w0 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start, length));
        ReadOnlySpan<Vector<float>> w1 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start + step, length));
        ReadOnlySpan<Vector<float>> w2 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start + (2 * step), length));
        ReadOnlySpan<Vector<float>> w3 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start + (3 * step), length));
        ReadOnlySpan<Vector<float>> w4 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start + (4 * step), length));
        ReadOnlySpan<Vector<float>> w5 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start + (5 * step), length));
        ReadOnlySpan<Vector<float>> w6 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start + (6 * step), length));
        ReadOnlySpan<Vector<float>> w7 = MemoryMarshal.Cast<float, Vector<float>>(history.Slice(start + (7 * step), length));
        Vector<float> s0 = Vector<float>.Zero;
        Vector<float> s1 = Vector<float>.Zero;
        Vector<float> s2 = Vector<float>.Zero;
        Vector<float> s3 = Vector<float>.Zero;
        Vector<float> s4 = Vector<float>.Zero;
        Vector<float> s5 = Vector<float>.Zero;
        Vector<float> s6 = Vector<float>.Zero;
        Vector<float> s7 = Vector<float>.Zero;
        for (int i = 0; i < a.Length; i++)
        {
            Vector<float> tap = a[i];
            s0 = Vector.MultiplyAddEstimate(tap, w0[i], s0);
            s1 = Vector.MultiplyAddEstimate(tap, w1[i], s1);
            s2 = Vector.MultiplyAddEstimate(tap, w2[i], s2);
            s3 = Vector.MultiplyAddEstimate(tap, w3[i], s3);
            s4 = Vector.MultiplyAddEstimate(tap, w4[i], s4);
            s5 = Vector.MultiplyAddEstimate(tap, w5[i], s5);
            s6 = Vector.MultiplyAddEstimate(tap, w6[i], s6);
            s7 = Vector.MultiplyAddEstimate(tap, w7[i], s7);
        }

        results[0] = Vector.Sum(s0);
        results[stride] = Vector.Sum(s1);
        results[2 * stride] = Vector.Sum(s2);
        results[3 * stride] = Vector.Sum(s3);
        results[4 * stride] = Vector.Sum(s4);
        results[5 * stride] = Vector.Sum(s5);
        results[6 * stride] = Vector.Sum(s6);
        results[7 * stride] = Vector.Sum(s7);
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    private static long CeilingDivide(long numerator, long denominator)
    {
        return (numerator + denominator - 1) / denominator;
    }
}
