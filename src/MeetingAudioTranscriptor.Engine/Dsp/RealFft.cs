// A real-input fast Fourier transform for the power-of-two frame sizes the feature extractors use,
// with every table built once per instance and nothing allocated per frame.
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace MeetingAudioTranscriptor.Engine.Dsp;

/// <summary>Transforms real frames of a fixed power-of-two size and computes their power spectrum.</summary>
/// <remarks>
/// <para>
/// A real frame of N samples is read as N/2 complex numbers (even samples real, odd samples
/// imaginary), transformed by an iterative radix-2 complex FFT and then split into the N/2 + 1 bins of
/// the real spectrum. This halves the work of a complex transform of the same size.
/// </para>
/// <para>
/// The complex transform keeps the real and imaginary parts in separate halves of the scratch buffer,
/// so the butterflies of every stage whose span reaches a vector's width run on whole
/// <see cref="Vector{T}"/> lanes with contiguous twiddles. Twiddles are computed in double precision
/// once per instance.
/// </para>
/// <para>
/// An instance holds only read-only tables after construction, so one instance serves any number of
/// threads at once provided each caller passes its own scratch buffer (see <see cref="CreateScratch"/>).
/// A frame may itself be the scratch buffer: the transform then runs in place.
/// </para>
/// </remarks>
public sealed class RealFft
{
    /// <summary>The smallest supported frame size.</summary>
    public const int MinimumSize = 16;

    /// <summary>The largest supported frame size.</summary>
    public const int MaximumSize = 65536;

    // The size of the complex transform: half the real frame.
    private readonly int _half;

    // For complex index m, the position it moves to before the butterflies.
    private readonly int[] _bitReverse;

    // Stage twiddles, stage after stage: the stage of span h stores cos and -sin of 2 pi j / (2 h) for j < h at offset h.
    private readonly float[] _stageCos;
    private readonly float[] _stageSin;

    // The split twiddles cos and -sin of 2 pi k / N for k = 0 .. N/2.
    private readonly float[] _splitCos;
    private readonly float[] _splitSin;

    /// <summary>Builds the tables for frames of the given size.</summary>
    /// <param name="size">The frame length: a power of two from 16 to 65536.</param>
    public RealFft(int size)
    {
        if (size < MinimumSize || size > MaximumSize || !BitOperations.IsPow2(size))
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                $"the FFT size is {size} where a power of two from {MinimumSize} to {MaximumSize} is needed; pick the next power of two above the frame length and zero-pad");
        }

        Size = size;
        _half = size / 2;

        int bits = BitOperations.Log2((uint)_half);
        _bitReverse = new int[_half];
        for (int m = 0; m < _half; m++)
        {
            _bitReverse[m] = (int)(ReverseBits((uint)m) >> (32 - bits));
        }

        _stageCos = new float[_half];
        _stageSin = new float[_half];
        for (int h = 1; h < _half; h *= 2)
        {
            for (int j = 0; j < h; j++)
            {
                double angle = Math.PI * j / h;
                _stageCos[h + j] = (float)Math.Cos(angle);
                _stageSin[h + j] = (float)-Math.Sin(angle);
            }
        }

        _splitCos = new float[_half + 1];
        _splitSin = new float[_half + 1];
        for (int k = 0; k <= _half; k++)
        {
            double angle = 2.0 * Math.PI * k / size;
            _splitCos[k] = (float)Math.Cos(angle);
            _splitSin[k] = (float)-Math.Sin(angle);
        }
    }

    /// <summary>The frame length this instance transforms.</summary>
    public int Size { get; }

    /// <summary>The number of spectrum bins: Size / 2 + 1, from 0 Hz to the Nyquist frequency.</summary>
    public int SpectrumLength => _half + 1;

    /// <summary>Allocates a scratch buffer of the length every call needs (Size floats).</summary>
    public float[] CreateScratch()
    {
        return new float[Size];
    }

    /// <summary>Computes |X[k]|^2 for k = 0 .. Size/2 of a real frame.</summary>
    /// <param name="frame">Size samples; may be the scratch buffer itself.</param>
    /// <param name="power">At least Size/2 + 1 floats; receives the power of each bin.</param>
    /// <param name="scratch">At least Size floats owned by the caller; overwritten.</param>
    public void PowerSpectrum(ReadOnlySpan<float> frame, Span<float> power, Span<float> scratch)
    {
        CheckLengths(frame, power, scratch, nameof(power));
        Transform(frame, scratch);

        ReadOnlySpan<float> re = scratch[.._half];
        ReadOnlySpan<float> im = scratch.Slice(_half, _half);
        power[0] = Square(re[0] + im[0]);
        power[_half] = Square(re[0] - im[0]);

        // Four bins at a time: bins k..k+3 pair with the mirrored bins n-k-3..n-k, read as a vector and
        // reversed. The lane arithmetic is the scalar formula of Split, operation for operation.
        ReadOnlySpan<float> cosines = _splitCos;
        ReadOnlySpan<float> sines = _splitSin;
        Vector128<float> half = Vector128.Create(0.5f);
        Vector128<int> reversed = Vector128.Create(3, 2, 1, 0);
        int k = 1;
        for (; k + 3 < _half; k += 4)
        {
            int mirror = _half - k - 3;
            Vector128<float> rk = Vector128.Create(re.Slice(k, 4));
            Vector128<float> ik = Vector128.Create(im.Slice(k, 4));
            Vector128<float> rm = Vector128.Shuffle(Vector128.Create(re.Slice(mirror, 4)), reversed);
            Vector128<float> imm = Vector128.Shuffle(Vector128.Create(im.Slice(mirror, 4)), reversed);
            Vector128<float> evenRe = half * (rk + rm);
            Vector128<float> evenIm = half * (ik - imm);
            Vector128<float> oddRe = half * (ik + imm);
            Vector128<float> oddIm = -half * (rk - rm);
            Vector128<float> c = Vector128.Create(cosines.Slice(k, 4));
            Vector128<float> s = Vector128.Create(sines.Slice(k, 4));
            Vector128<float> xr = evenRe + (c * oddRe) - (s * oddIm);
            Vector128<float> xi = evenIm + (c * oddIm) + (s * oddRe);
            ((xr * xr) + (xi * xi)).CopyTo(power.Slice(k, 4));
        }

        for (; k < _half; k++)
        {
            Split(re, im, k, out float xr, out float xi);
            power[k] = (xr * xr) + (xi * xi);
        }
    }

    /// <summary>Computes the complex spectrum X[k] for k = 0 .. Size/2 of a real frame.</summary>
    /// <param name="frame">Size samples; may be the scratch buffer itself.</param>
    /// <param name="real">At least Size/2 + 1 floats; receives the real parts.</param>
    /// <param name="imaginary">At least Size/2 + 1 floats; receives the imaginary parts.</param>
    /// <param name="scratch">At least Size floats owned by the caller; overwritten.</param>
    public void Forward(ReadOnlySpan<float> frame, Span<float> real, Span<float> imaginary, Span<float> scratch)
    {
        CheckLengths(frame, real, scratch, nameof(real));
        CheckLengths(frame, imaginary, scratch, nameof(imaginary));
        Transform(frame, scratch);

        Span<float> re = scratch[.._half];
        Span<float> im = scratch.Slice(_half, _half);
        real[0] = re[0] + im[0];
        imaginary[0] = 0;
        real[_half] = re[0] - im[0];
        imaginary[_half] = 0;
        for (int k = 1; k < _half; k++)
        {
            Split(re, im, k, out real[k], out imaginary[k]);
        }
    }

    // Recovers bin k of the real spectrum from the complex transform Z of the packed frame:
    // X[k] = (Z[k] + conj Z[n-k]) / 2 + W^k (Z[k] - conj Z[n-k]) / 2i, with W = exp(-2 pi i / N).
    private void Split(ReadOnlySpan<float> re, ReadOnlySpan<float> im, int k, out float xr, out float xi)
    {
        int mirror = _half - k;
        float evenRe = 0.5f * (re[k] + re[mirror]);
        float evenIm = 0.5f * (im[k] - im[mirror]);

        // (Z[k] - conj Z[n-k]) / 2i, whose real part is half the imaginary sum and imaginary part minus half the real difference.
        float oddRe = 0.5f * (im[k] + im[mirror]);
        float oddIm = -0.5f * (re[k] - re[mirror]);

        float c = _splitCos[k];
        float s = _splitSin[k];
        xr = evenRe + (c * oddRe) - (s * oddIm);
        xi = evenIm + (c * oddIm) + (s * oddRe);
    }

    // Packs the frame into the scratch buffer in bit-reversed order and runs the complex butterflies.
    private void Transform(ReadOnlySpan<float> frame, Span<float> scratch)
    {
        int n = _half;

        // An in-place call passes the scratch as the frame; the packing below reads the frame while
        // writing the scratch, so the frame is first moved to a place the packing does not overwrite.
        if (frame.Overlaps(scratch))
        {
            Span<float> copy = n <= 4096 ? stackalloc float[2 * n] : new float[2 * n];
            frame[..(2 * n)].CopyTo(copy);
            Pack(copy, scratch);
        }
        else
        {
            Pack(frame, scratch);
        }

        Span<float> re = scratch[..n];
        Span<float> im = scratch.Slice(n, n);
        int width = Vector<float>.Count;
        for (int h = 4; h < n; h *= 2)
        {
            if (h >= width)
            {
                VectorStage(re, im, h);
            }
            else if (h == 4)
            {
                Stage4(re, im);
            }
            else
            {
                ScalarStage(re, im, h);
            }
        }
    }

    // Splits even samples into the real half and odd samples into the imaginary half in bit-reversed
    // order, and runs the first two stages on the way: their twiddles are 1 and -i, so they need no
    // multiplication, and doing them while the values are in registers saves two passes over memory.
    private void Pack(ReadOnlySpan<float> frame, Span<float> scratch)
    {
        int n = _half;
        ReadOnlySpan<int> reverse = _bitReverse;
        Span<float> re = scratch[..n];
        Span<float> im = scratch.Slice(n, n);
        for (int g = 0; g < n; g += 4)
        {
            // Bit reversal is its own inverse: position p of the reordered sequence holds element reverse[p].
            int m0 = 2 * reverse[g];
            int m1 = 2 * reverse[g + 1];
            int m2 = 2 * reverse[g + 2];
            int m3 = 2 * reverse[g + 3];

            // Span 1: two butterflies with twiddle 1.
            float r0 = frame[m0] + frame[m1];
            float i0 = frame[m0 + 1] + frame[m1 + 1];
            float r1 = frame[m0] - frame[m1];
            float i1 = frame[m0 + 1] - frame[m1 + 1];
            float r2 = frame[m2] + frame[m3];
            float i2 = frame[m2 + 1] + frame[m3 + 1];
            float r3 = frame[m2] - frame[m3];
            float i3 = frame[m2 + 1] - frame[m3 + 1];

            // Span 2: twiddle 1 for the first pair and -i for the second, where (r3 + i i3)(-i) = i3 - i r3.
            re[g] = r0 + r2;
            im[g] = i0 + i2;
            re[g + 2] = r0 - r2;
            im[g + 2] = i0 - i2;
            re[g + 1] = r1 + i3;
            im[g + 1] = i1 - r3;
            re[g + 3] = r1 - i3;
            im[g + 3] = i1 + r3;
        }
    }

    // One radix-2 stage of span h, one butterfly at a time; used for spans narrower than a vector.
    private void ScalarStage(Span<float> re, Span<float> im, int h)
    {
        ReadOnlySpan<float> cos = _stageCos.AsSpan(h, h);
        ReadOnlySpan<float> sin = _stageSin.AsSpan(h, h);
        for (int start = 0; start < re.Length; start += 2 * h)
        {
            for (int j = 0; j < h; j++)
            {
                int a = start + j;
                int b = a + h;
                float tr = (re[b] * cos[j]) - (im[b] * sin[j]);
                float ti = (re[b] * sin[j]) + (im[b] * cos[j]);
                re[b] = re[a] - tr;
                im[b] = im[a] - ti;
                re[a] += tr;
                im[a] += ti;
            }
        }
    }

    // The stage of span 4 on 128-bit vectors, which every supported processor has, for machines whose
    // general vector is wider than four floats.
    private void Stage4(Span<float> re, Span<float> im)
    {
        Vector128<float> cos = Vector128.Create(_stageCos.AsSpan(4, 4));
        Vector128<float> sin = Vector128.Create(_stageSin.AsSpan(4, 4));
        for (int a = 0; a < re.Length; a += 8)
        {
            int b = a + 4;
            Vector128<float> ar = Vector128.Create(re.Slice(a, 4));
            Vector128<float> ai = Vector128.Create(im.Slice(a, 4));
            Vector128<float> br = Vector128.Create(re.Slice(b, 4));
            Vector128<float> bi = Vector128.Create(im.Slice(b, 4));
            Vector128<float> tr = (br * cos) - (bi * sin);
            Vector128<float> ti = (br * sin) + (bi * cos);
            (ar - tr).CopyTo(re.Slice(b, 4));
            (ai - ti).CopyTo(im.Slice(b, 4));
            (ar + tr).CopyTo(re.Slice(a, 4));
            (ai + ti).CopyTo(im.Slice(a, 4));
        }
    }

    // One radix-2 stage of span h, a whole vector of butterflies at a time; h is a multiple of the width.
    private void VectorStage(Span<float> re, Span<float> im, int h)
    {
        Span<Vector<float>> vr = MemoryMarshal.Cast<float, Vector<float>>(re);
        Span<Vector<float>> vi = MemoryMarshal.Cast<float, Vector<float>>(im);
        ReadOnlySpan<Vector<float>> cos = MemoryMarshal.Cast<float, Vector<float>>(_stageCos.AsSpan(h, h));
        ReadOnlySpan<Vector<float>> sin = MemoryMarshal.Cast<float, Vector<float>>(_stageSin.AsSpan(h, h));
        int span = h / Vector<float>.Count;
        for (int start = 0; start < vr.Length; start += 2 * span)
        {
            for (int j = 0; j < span; j++)
            {
                int a = start + j;
                int b = a + span;
                Vector<float> tr = (vr[b] * cos[j]) - (vi[b] * sin[j]);
                Vector<float> ti = (vr[b] * sin[j]) + (vi[b] * cos[j]);
                vr[b] = vr[a] - tr;
                vi[b] = vi[a] - ti;
                vr[a] += tr;
                vi[a] += ti;
            }
        }
    }

    private void CheckLengths(ReadOnlySpan<float> frame, Span<float> destination, Span<float> scratch, string destinationName)
    {
        if (frame.Length < Size)
        {
            throw new ArgumentException($"the frame holds {frame.Length} samples where the FFT needs {Size}; zero-pad the frame to the transform size", nameof(frame));
        }

        if (destination.Length < SpectrumLength)
        {
            throw new ArgumentException($"the destination holds {destination.Length} floats where {SpectrumLength} bins are written; size it with SpectrumLength", destinationName);
        }

        if (scratch.Length < Size)
        {
            throw new ArgumentException($"the scratch buffer holds {scratch.Length} floats where {Size} are needed; create it with CreateScratch", nameof(scratch));
        }
    }

    private static float Square(float value)
    {
        return value * value;
    }

    private static uint ReverseBits(uint value)
    {
        value = ((value >> 1) & 0x55555555u) | ((value & 0x55555555u) << 1);
        value = ((value >> 2) & 0x33333333u) | ((value & 0x33333333u) << 2);
        value = ((value >> 4) & 0x0F0F0F0Fu) | ((value & 0x0F0F0F0Fu) << 4);
        value = ((value >> 8) & 0x00FF00FFu) | ((value & 0x00FF00FFu) << 8);
        return (value >> 16) | (value << 16);
    }
}
