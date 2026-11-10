// The few vector operations the embeddings and the clustering need, written once on hardware vectors.
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>Dot products, scaling and normalisation of float vectors.</summary>
internal static class VectorMath
{
    /// <summary>The dot product of two vectors of equal length.</summary>
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException($"the vectors have lengths {a.Length} and {b.Length}; a dot product needs equal lengths", nameof(b));
        }

        int width = Vector<float>.Count;
        int whole = a.Length - (a.Length % width);
        ReadOnlySpan<Vector<float>> va = MemoryMarshal.Cast<float, Vector<float>>(a[..whole]);
        ReadOnlySpan<Vector<float>> vb = MemoryMarshal.Cast<float, Vector<float>>(b[..whole]);
        Vector<float> sum = Vector<float>.Zero;
        for (int i = 0; i < va.Length; i++)
        {
            sum += va[i] * vb[i];
        }

        float total = Vector.Sum(sum);
        for (int i = whole; i < a.Length; i++)
        {
            total += a[i] * b[i];
        }

        return total;
    }

    /// <summary>Scales a vector to unit length in place; a zero vector stays zero.</summary>
    public static void Normalise(Span<float> vector)
    {
        float norm = MathF.Sqrt(Dot(vector, vector));
        if (norm > 0)
        {
            Scale(vector, 1f / norm);
        }
    }

    /// <summary>Multiplies every element by a factor in place.</summary>
    public static void Scale(Span<float> vector, float factor)
    {
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] *= factor;
        }
    }

    /// <summary>Adds <paramref name="source"/> to <paramref name="target"/> element by element.</summary>
    public static void Add(Span<float> target, ReadOnlySpan<float> source)
    {
        for (int i = 0; i < target.Length; i++)
        {
            target[i] += source[i];
        }
    }
}
