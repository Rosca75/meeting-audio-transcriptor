// Runs one ONNX model graph through an ONNX Runtime session and moves arrays in and out of it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MeetingAudioTranscriptor.Inference;

/// <summary>The engine's model boundary implemented over one ONNX Runtime <c>InferenceSession</c>.</summary>
/// <remarks>
/// The class moves arrays and nothing else: no algorithm of any model lives here. A session is safe
/// to call from several threads, so <see cref="Run"/> keeps no state between calls and needs no
/// lock; recognition workers share one runner and the weights are loaded once. Input arrays are
/// handed to the runtime without a copy (the runtime pins them for the duration of the call), so the
/// caller must not change an input array while a call that uses it is running. Outputs are copied
/// into new managed arrays, because the runtime frees its buffers when the call returns.
/// </remarks>
public sealed class OrtTensorRunner : ITensorRunner
{
    private readonly InferenceSession _session;
    private readonly string _modelName;
    private readonly string[] _inputNames;
    private readonly string[] _outputNames;

    internal OrtTensorRunner(InferenceSession session, string modelName)
    {
        _session = session;
        _modelName = modelName;
        _inputNames = session.InputMetadata.Keys.ToArray();
        _outputNames = session.OutputMetadata.Keys.ToArray();
    }

    /// <summary>The input names the graph declares, for diagnostics and for callers that adapt to a graph.</summary>
    public IReadOnlyList<string> InputNames => _inputNames;

    /// <summary>The output names the graph declares.</summary>
    public IReadOnlyList<string> OutputNames => _outputNames;

    /// <inheritdoc />
    public TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ValidateInputs(inputs);
        ValidateOutputs(outputNames);

        string[] inputNameList = new string[inputs.Length];
        OrtValue[] inputValues = new OrtValue[inputs.Length];
        string[] outputNameList = outputNames.ToArray();
        try
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputNameList[i] = inputs[i].Name;
                inputValues[i] = ToOrtValue(inputs[i]);
            }

            using RunOptions runOptions = new();

            // Setting Terminate asks the runtime to abandon the call at its next operator boundary.
            // The registration is disposed before the options, so the callback never touches a
            // disposed object (Dispose waits for a callback that is already running).
            using CancellationTokenRegistration stop = cancellation.Register(() => runOptions.Terminate = true);
            try
            {
                using IDisposableReadOnlyCollection<OrtValue> results = _session.Run(runOptions, inputNameList, inputValues, outputNameList);
                TensorValue[] copied = new TensorValue[outputNameList.Length];
                for (int i = 0; i < copied.Length; i++)
                {
                    copied[i] = CopyOut(outputNameList[i], results[i]);
                }

                return copied;
            }
            catch (OnnxRuntimeException) when (cancellation.IsCancellationRequested)
            {
                // The runtime reports a terminated call as a failure; the caller asked for it.
                throw new OperationCanceledException(cancellation);
            }
            catch (OnnxRuntimeException e)
            {
                throw new InvalidOperationException(
                    $"the model '{_modelName}' failed while running: {e.Message}. Check that every input has the shape and the length the graph expects "
                    + $"(inputs: {string.Join(", ", _inputNames)}); if the shapes are right, fetch the model again with node build/fetch-models.mjs.",
                    e);
            }
        }
        finally
        {
            foreach (OrtValue value in inputValues)
            {
                value?.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();

    /// <summary>Checks every input against the session's metadata before anything is allocated.</summary>
    private void ValidateInputs(ReadOnlySpan<TensorValue> inputs)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (TensorValue input in inputs)
        {
            if (!_session.InputMetadata.TryGetValue(input.Name, out NodeMetadata? declared))
            {
                throw new ArgumentException(
                    $"the model '{_modelName}' has no input named '{input.Name}'; its inputs are: {string.Join(", ", _inputNames)}. "
                    + "Rename the tensor to one of them.");
            }

            if (!seen.Add(input.Name))
            {
                throw new ArgumentException($"the input '{input.Name}' was passed twice; pass each of the model's inputs ({string.Join(", ", _inputNames)}) exactly once.");
            }

            TensorElementType actual = input.Floats is not null ? TensorElementType.Float
                : input.Int64s is not null ? TensorElementType.Int64
                : TensorElementType.Int32;
            if (declared.IsTensor && declared.ElementDataType != actual)
            {
                throw new ArgumentException(
                    $"the input '{input.Name}' of model '{_modelName}' is {actual} but the graph declares {declared.ElementDataType}. "
                    + $"Build the tensor with TensorValue.{ElementFactoryFor(declared.ElementDataType)}.");
            }
        }

        string[] missing = _inputNames.Where(name => !seen.Contains(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"the model '{_modelName}' needs the input(s) {string.Join(", ", missing)} and they were not passed; pass one tensor per input ({string.Join(", ", _inputNames)}).");
        }
    }

    /// <summary>Checks the requested outputs exist and are a tensor type the boundary can return.</summary>
    private void ValidateOutputs(ReadOnlySpan<string> outputNames)
    {
        if (outputNames.IsEmpty)
        {
            throw new ArgumentException($"no output was requested from model '{_modelName}'; name at least one of: {string.Join(", ", _outputNames)}.");
        }

        foreach (string name in outputNames)
        {
            if (!_session.OutputMetadata.TryGetValue(name, out NodeMetadata? declared))
            {
                throw new ArgumentException($"the model '{_modelName}' has no output named '{name}'; its outputs are: {string.Join(", ", _outputNames)}.");
            }

            if (!declared.IsTensor || !IsSupported(declared.ElementDataType))
            {
                throw new NotSupportedException(UnsupportedOutputMessage(name, declared.IsTensor ? declared.ElementDataType.ToString() : "a non-tensor value"));
            }
        }
    }

    private static bool IsSupported(TensorElementType type) =>
        type is TensorElementType.Float or TensorElementType.Int64 or TensorElementType.Int32;

    private static string ElementFactoryFor(TensorElementType type) => type switch
    {
        TensorElementType.Float => "OfFloats",
        TensorElementType.Int64 => "OfInt64s",
        TensorElementType.Int32 => "OfInt32s",
        _ => "a factory for a type the boundary supports (float32, int64 or int32)",
    };

    private static string UnsupportedOutputMessage(string name, string type) =>
        $"the output '{name}' has type {type}, which the model boundary cannot return; it carries float32, int64 and int32 only. "
        + "Request a different output, or cast the value to float32 inside the graph.";

    /// <summary>Wraps a tensor's managed array as a runtime value over the same memory, without a copy.</summary>
    private static OrtValue ToOrtValue(TensorValue tensor)
    {
        long[] shape = Array.ConvertAll(tensor.Shape, dimension => (long)dimension);
        if (tensor.Floats is not null)
        {
            return OrtValue.CreateTensorValueFromMemory(tensor.Floats, shape);
        }

        if (tensor.Int64s is not null)
        {
            return OrtValue.CreateTensorValueFromMemory(tensor.Int64s, shape);
        }

        return OrtValue.CreateTensorValueFromMemory(tensor.Int32s!, shape);
    }

    /// <summary>Copies one result into a managed tensor of the matching element type.</summary>
    private static TensorValue CopyOut(string name, OrtValue value)
    {
        OrtTensorTypeAndShapeInfo info = value.GetTensorTypeAndShape();
        int[] shape = Array.ConvertAll(info.Shape, dimension => checked((int)dimension));
        return info.ElementDataType switch
        {
            TensorElementType.Float => TensorValue.OfFloats(name, shape, value.GetTensorDataAsSpan<float>().ToArray()),
            TensorElementType.Int64 => TensorValue.OfInt64s(name, shape, value.GetTensorDataAsSpan<long>().ToArray()),
            TensorElementType.Int32 => TensorValue.OfInt32s(name, shape, value.GetTensorDataAsSpan<int>().ToArray()),
            _ => throw new NotSupportedException(UnsupportedOutputMessage(name, info.ElementDataType.ToString())),
        };
    }
}
