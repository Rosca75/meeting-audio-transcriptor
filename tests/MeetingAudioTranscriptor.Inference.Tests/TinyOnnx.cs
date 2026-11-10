// Writes the smallest ONNX graphs by hand, so the runner can be tested without any model file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MeetingAudioTranscriptor.Inference.Tests;

/// <summary>Builds tiny ONNX models as bytes: a protocol buffer written field by field.</summary>
/// <remarks>
/// An ONNX file is a protocol buffer; only the handful of fields these graphs use are written. The
/// field numbers are from the ONNX schema: ModelProto (ir_version 1, opset_import 8, graph 7),
/// GraphProto (node 1, name 2, input 11, output 12), NodeProto (input 1, output 2, op_type 4,
/// attribute 5), ValueInfoProto (name 1, type 2), TypeProto (tensor_type 1), TensorTypeProto
/// (elem_type 1, shape 2), TensorShapeProto (dim 1), Dimension (dim_value 1, dim_param 2).
/// </remarks>
internal static class TinyOnnx
{
    /// <summary>ONNX element type codes.</summary>
    public const int Float = 1;

    /// <summary>ONNX element type code of a 64-bit integer.</summary>
    public const int Int64 = 7;

    /// <summary>ONNX element type code of a 64-bit float, a type the model boundary does not carry.</summary>
    public const int Double = 11;

    /// <summary>A graph with inputs a and b (float, [2, 3]) and the output sum = a + b.</summary>
    public static byte[] AddFloats() =>
        Model(
            [Node("Add", ["a", "b"], ["sum"])],
            [Value("a", Float, 2, 3), Value("b", Float, 2, 3)],
            [Value("sum", Float, 2, 3)]);

    /// <summary>A graph with one int64 input x of dynamic length, and the outputs same (x) and wide (x cast to double).</summary>
    public static byte[] IntegersAndAnUnsupportedOutput() =>
        Model(
            [Node("Identity", ["x"], ["same"]), Node("Cast", ["x"], ["wide"], castTo: Double)],
            [Value("x", Int64, -1)],
            [Value("same", Int64, -1), Value("wide", Double, -1)]);

    private static byte[] Model(List<byte[]> nodes, List<byte[]> inputs, List<byte[]> outputs)
    {
        using MemoryStream graph = new();
        foreach (byte[] node in nodes)
        {
            Bytes(graph, 1, node);
        }

        Bytes(graph, 2, Encoding.UTF8.GetBytes("tiny"));
        foreach (byte[] input in inputs)
        {
            Bytes(graph, 11, input);
        }

        foreach (byte[] output in outputs)
        {
            Bytes(graph, 12, output);
        }

        using MemoryStream opset = new();
        Varint(opset, 2, 13);

        using MemoryStream model = new();
        Varint(model, 1, 8);
        Bytes(model, 8, opset.ToArray());
        Bytes(model, 7, graph.ToArray());
        return model.ToArray();
    }

    private static byte[] Node(string opType, string[] inputs, string[] outputs, int? castTo = null)
    {
        using MemoryStream node = new();
        foreach (string input in inputs)
        {
            Bytes(node, 1, Encoding.UTF8.GetBytes(input));
        }

        foreach (string output in outputs)
        {
            Bytes(node, 2, Encoding.UTF8.GetBytes(output));
        }

        Bytes(node, 4, Encoding.UTF8.GetBytes(opType));
        if (castTo is int target)
        {
            // AttributeProto: name 1, i 3, type 20 (2 = INT).
            using MemoryStream attribute = new();
            Bytes(attribute, 1, Encoding.UTF8.GetBytes("to"));
            Varint(attribute, 3, target);
            Varint(attribute, 20, 2);
            Bytes(node, 5, attribute.ToArray());
        }

        return node.ToArray();
    }

    /// <summary>A tensor value declaration; a dimension of -1 is symbolic (any length).</summary>
    private static byte[] Value(string name, int elementType, params int[] dimensions)
    {
        using MemoryStream shape = new();
        foreach (int dimension in dimensions)
        {
            using MemoryStream dim = new();
            if (dimension < 0)
            {
                Bytes(dim, 2, Encoding.UTF8.GetBytes("n"));
            }
            else
            {
                Varint(dim, 1, dimension);
            }

            Bytes(shape, 1, dim.ToArray());
        }

        using MemoryStream tensor = new();
        Varint(tensor, 1, elementType);
        Bytes(tensor, 2, shape.ToArray());
        using MemoryStream type = new();
        Bytes(type, 1, tensor.ToArray());
        using MemoryStream info = new();
        Bytes(info, 1, Encoding.UTF8.GetBytes(name));
        Bytes(info, 2, type.ToArray());
        return info.ToArray();
    }

    private static void Varint(Stream stream, int field, long value)
    {
        WriteVarint(stream, (ulong)(field << 3));
        WriteVarint(stream, (ulong)value);
    }

    private static void Bytes(Stream stream, int field, byte[] payload)
    {
        WriteVarint(stream, (ulong)((field << 3) | 2));
        WriteVarint(stream, (ulong)payload.Length);
        stream.Write(payload, 0, payload.Length);
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }
}
