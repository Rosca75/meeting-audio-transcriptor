// The adapter over the transcription service: the one thing the bridge needs to run a transcription.
using System;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Runs one transcription for the bridge.</summary>
/// <remarks>
/// An adapter rather than the pipeline itself, so the bridge is testable with a fake and does not
/// depend on the engine's stages. The request it receives is already validated and has its output
/// folder set.
/// </remarks>
public interface ITranscriber
{
    /// <summary>Checks that the models a run needs are installed.</summary>
    ModelCheckResult CheckModels();

    /// <summary>Runs one transcription; throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    Task<TranscribeOutcome> RunAsync(TranscribeRequest request, IProgress<TranscribeProgress> progress, CancellationToken cancellation);
}
