// The model boundary: the engine hands named tensors to a model and receives named tensors back.
using System;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Models;

/// <summary>Runs one model graph on the CPU.</summary>
/// <remarks>
/// Every algorithm around a model (features, decoding, clustering) lives in the engine and reaches
/// the model through this interface alone, so the engine references no model runtime and each
/// algorithm is tested against a fake. An implementation is safe to call from several threads at
/// once, because recognition workers share one loaded model rather than loading one each.
/// </remarks>
public interface ITensorRunner : IDisposable
{
    /// <summary>Runs the graph once.</summary>
    /// <param name="inputs">One tensor per graph input, in any order.</param>
    /// <param name="outputNames">The graph outputs to return, in the order wanted.</param>
    /// <param name="cancellation">Stops a run in flight when the user cancels.</param>
    /// <returns>One tensor per requested output, in the order of <paramref name="outputNames"/>.</returns>
    TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation);
}
