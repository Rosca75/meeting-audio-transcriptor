// A named block of numbers passed to or returned by a model, the only data shape the model boundary knows.
using System;

namespace MeetingAudioTranscriptor.Engine.Models;

/// <summary>One named tensor: a shape and the numbers in row-major order.</summary>
/// <remarks>
/// Exactly one of the three arrays is set, matching the element type the model declares. Plain
/// arrays keep the engine free of any model runtime's types, so a test builds a tensor by hand.
/// </remarks>
public sealed class TensorValue
{
    private TensorValue(string name, int[] shape, float[]? floats, long[]? int64s, int[]? int32s)
    {
        Name = name;
        Shape = shape;
        Floats = floats;
        Int64s = int64s;
        Int32s = int32s;
    }

    /// <summary>The input or output name the model's graph declares.</summary>
    public string Name { get; }

    /// <summary>The size of each dimension.</summary>
    public int[] Shape { get; }

    /// <summary>The values when the element type is 32-bit floating point.</summary>
    public float[]? Floats { get; }

    /// <summary>The values when the element type is a 64-bit integer.</summary>
    public long[]? Int64s { get; }

    /// <summary>The values when the element type is a 32-bit integer.</summary>
    public int[]? Int32s { get; }

    /// <summary>Wraps 32-bit floating point values; the array is used as it is, without a copy.</summary>
    public static TensorValue OfFloats(string name, int[] shape, float[] values)
    {
        CheckLength(name, shape, values.Length);
        return new TensorValue(name, shape, values, null, null);
    }

    /// <summary>Wraps 64-bit integer values; the array is used as it is, without a copy.</summary>
    public static TensorValue OfInt64s(string name, int[] shape, long[] values)
    {
        CheckLength(name, shape, values.Length);
        return new TensorValue(name, shape, null, values, null);
    }

    /// <summary>Wraps 32-bit integer values; the array is used as it is, without a copy.</summary>
    public static TensorValue OfInt32s(string name, int[] shape, int[] values)
    {
        CheckLength(name, shape, values.Length);
        return new TensorValue(name, shape, null, null, values);
    }

    /// <summary>The number of elements the shape describes.</summary>
    public static long ElementCount(ReadOnlySpan<int> shape)
    {
        long count = 1;
        foreach (int dimension in shape)
        {
            count *= dimension;
        }

        return count;
    }

    private static void CheckLength(string name, int[] shape, int length)
    {
        long expected = ElementCount(shape);
        if (expected != length)
        {
            throw new ArgumentException(
                $"Tensor '{name}' has {length} values but its shape [{string.Join(", ", shape)}] describes {expected}. "
                + "Pass an array whose length is the product of the dimensions.");
        }
    }
}
