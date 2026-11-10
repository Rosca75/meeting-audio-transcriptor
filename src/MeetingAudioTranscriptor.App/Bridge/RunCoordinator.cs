// Owns the one transcription that may run at a time: its start, its cancellation and the single
// terminal event it always emits.
using System;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Runs transcriptions one at a time on a worker and reports each ending exactly once.</summary>
/// <remarks>
/// One lock guards the cancellation slot, and nothing is awaited or called out to while it is held.
/// Cancelling happens outside the lock because a token's callbacks run synchronously on the thread
/// that cancels. The terminal event is posted from a <c>finally</c>, so a run that throws, is
/// cancelled or returns nonsense still ends the page's progress display.
/// </remarks>
internal sealed class RunCoordinator
{
    private readonly ITranscriber _transcriber;
    private readonly BridgeEmitter _emitter;
    private readonly TimeProvider _clock;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _active;

    public RunCoordinator(ITranscriber transcriber, BridgeEmitter emitter, TimeProvider clock)
    {
        _transcriber = transcriber;
        _emitter = emitter;
        _clock = clock;
    }

    /// <summary>Validates the request and starts the run on a worker; returns once it is accepted.</summary>
    /// <exception cref="ArgumentException">The request is invalid.</exception>
    /// <exception cref="InvalidOperationException">A run is already active.</exception>
    public void Start(TranscribeRequest request)
    {
        TranscribeRequest validated = RequestValidator.Validate(request);
        CancellationTokenSource source = new();
        lock (_lock)
        {
            if (_active is not null)
            {
                source.Dispose();
                throw new InvalidOperationException(
                    "A transcription is already running. Wait for it to finish, or cancel it, before starting another.");
            }

            _active = source;
        }

        _ = Task.Run(() => ExecuteAsync(validated, source));
    }

    /// <summary>Cancels the active run, if any; outside the lock, because the token's callbacks run inline.</summary>
    public void Cancel()
    {
        CancellationTokenSource? source;
        lock (_lock)
        {
            source = _active;
        }

        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended between reading the slot and cancelling; there is nothing left to cancel.
        }
    }

    private async Task ExecuteAsync(TranscribeRequest request, CancellationTokenSource source)
    {
        ProgressForwarder progress = new(_emitter, _clock);
        TranscribeOutcome outcome = Failure(request, "The transcription stopped unexpectedly. Try again, and if it keeps failing restart the application.");
        try
        {
            TranscribeOutcome result = await _transcriber.RunAsync(request, progress, source.Token).ConfigureAwait(false);
            outcome = Normalise(result, request);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            outcome = new TranscribeOutcome
            {
                Kind = TranscribeOutcomeKinds.Cancelled,
                Message = "The transcription was cancelled. No transcript was written.",
                OutputFolder = request.OutputFolder,
            };
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            outcome = Failure(request, Faults.Sentence("The transcription", failure));
        }
        finally
        {
            // Free the slot first so the page can start again as soon as it sees the terminal event.
            lock (_lock)
            {
                _active = null;
            }

            progress.Close();
            _emitter.Event(BridgeEvents.RunFinished, outcome, BridgeJson.Context.TranscribeOutcome);
            source.Dispose();
        }
    }

    // A transcriber that returns an unknown kind or no message must not leave the page without a
    // sentence to show.
    private static TranscribeOutcome Normalise(TranscribeOutcome result, TranscribeRequest request)
    {
        if (result is null || !TranscribeOutcomeKinds.IsKnown(result.Kind))
        {
            return Failure(request, "The transcription ended in a state the application does not recognise. Try again, and if it keeps failing restart the application.");
        }

        string message = !string.IsNullOrWhiteSpace(result.Message) ? result.Message : result.Kind switch
        {
            TranscribeOutcomeKinds.Completed => "The transcript was written.",
            TranscribeOutcomeKinds.NoSpeech => "No speech was found in this recording, so the transcript files were written empty. Check that the right file is chosen and that it has sound.",
            TranscribeOutcomeKinds.Cancelled => "The transcription was cancelled. No transcript was written.",
            _ => "The transcription failed. Try again, and if it keeps failing restart the application.",
        };
        return result with { Message = message, OutputFiles = result.OutputFiles ?? [] };
    }

    private static TranscribeOutcome Failure(TranscribeRequest request, string message)
    {
        return new TranscribeOutcome
        {
            Kind = TranscribeOutcomeKinds.Failed,
            Message = message,
            OutputFolder = request.OutputFolder,
        };
    }

}
