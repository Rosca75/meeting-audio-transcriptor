// The bridge's transcriber over the real transcription service: maps the page's request, progress and outcome to the service's and back.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Bridge;
using MeetingAudioTranscriptor.Engine.Writers;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Runs the page's transcriptions on one <see cref="TranscriptionService"/>, which keeps its models loaded between runs.</summary>
/// <remarks>
/// <para>
/// The bridge hands over a validated request whose output folder is set. Progress is reported in
/// the page's stage words (<see cref="BridgeStages"/>) with the service's overall fraction. Every way
/// a run ends becomes one of the four page kinds: a written transcript is <c>completed</c>, an empty
/// one <c>noSpeech</c>, a cancellation <c>cancelled</c>, and every failure <c>failed</c> with the
/// service's actionable sentence.
/// </para>
/// <para>
/// The service never throws for a run that started, so a cancellation arrives as an outcome rather
/// than an exception; the bridge accepts either.
/// </para>
/// </remarks>
public sealed class ServiceTranscriber : ITranscriber, IDisposable
{
    private readonly TranscriptionService _service;

    /// <summary>Creates the transcriber over a models folder.</summary>
    /// <param name="modelsRoot">The models folder; null for the catalog's default (<c>MAT_MODELS_DIR</c>, else <c>models</c> beside the executable).</param>
    public ServiceTranscriber(string? modelsRoot = null)
        : this(new TranscriptionService(modelsRoot))
    {
    }

    /// <summary>Creates the transcriber over a service; the tests pass one with fakes.</summary>
    internal ServiceTranscriber(TranscriptionService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>The models folder the service reads.</summary>
    public string ModelsRoot => _service.ModelsRoot;

    /// <inheritdoc />
    public ModelCheckResult CheckModels()
    {
        ModelStatus status = _service.Check();
        return status.Ready
            ? new ModelCheckResult { Ready = true }
            : new ModelCheckResult { Ready = false, Message = Sentence(status.Message) };
    }

    /// <inheritdoc />
    public async Task<TranscribeOutcome> RunAsync(TranscribeRequest request, IProgress<TranscribeProgress> progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        TranscriptionRequest serviceRequest = ToServiceRequest(request);
        StageProgress forward = new(progress);
        TranscriptionOutcome outcome = await _service.RunAsync(serviceRequest, forward, cancellation).ConfigureAwait(false);
        return ToPageOutcome(outcome, request);
    }

    /// <summary>Releases the loaded models.</summary>
    public void Dispose() => _service.Dispose();

    /// <summary>The service's request for the page's: same file, language, formats, speakers, speaker names and raw files; overwriting as the service does by default.</summary>
    internal static TranscriptionRequest ToServiceRequest(TranscribeRequest request) => new()
    {
        InputPath = request.InputPath,
        Language = request.Language,
        Formats = request.Formats.Select(TranscriptFormats.Parse).Distinct().ToArray(),
        OutputFolder = request.OutputFolder,
        SpeakerSeparation = request.SpeakerSeparation,
        SpeakerCountHint = request.SpeakerSeparation ? request.SpeakerCount : null,
        SpeakerNames = request.SpeakerSeparation ? request.SpeakerNames : null,
        RawFiles = request.RawFiles,
    };

    /// <summary>The page's outcome for the service's.</summary>
    internal static TranscribeOutcome ToPageOutcome(TranscriptionOutcome outcome, TranscribeRequest request)
    {
        string kind = outcome.Kind switch
        {
            TranscriptionOutcomeKind.Succeeded => TranscribeOutcomeKinds.Completed,
            TranscriptionOutcomeKind.NoSpeech => TranscribeOutcomeKinds.NoSpeech,
            TranscriptionOutcomeKind.Cancelled => TranscribeOutcomeKinds.Cancelled,
            _ => TranscribeOutcomeKinds.Failed,
        };

        // A notice (speakers not told apart) follows the success sentence, so the page shows both.
        string message = string.Join(" ", new[] { outcome.Message }.Concat(outcome.Notices).Where(text => !string.IsNullOrWhiteSpace(text)).Select(Sentence));
        double speed = outcome.AudioSeconds > 0 && outcome.ProcessingSeconds > 0 ? outcome.AudioSeconds / outcome.ProcessingSeconds : 0;
        return new TranscribeOutcome
        {
            Kind = kind,
            Message = message.Length > 0 ? message : null,
            OutputFolder = request.OutputFolder,
            OutputFiles = outcome.OutputFiles.ToArray(),
            DurationSeconds = outcome.AudioSeconds,
            ProcessingSeconds = outcome.ProcessingSeconds,
            SpeedFactor = speed,
            Speakers = outcome.SpeakerCount,
            Words = outcome.CleanedWordCount,
        };
    }

    /// <summary>The service's lower-case clause as a sentence the page shows: a capital first letter and a full stop.</summary>
    internal static string Sentence(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        string capital = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        return capital[^1] is '.' or '!' or '?' ? capital : capital + ".";
    }

    /// <summary>Forwards the service's progress in the page's words.</summary>
    private sealed class StageProgress : IProgress<TranscriptionProgress>
    {
        private readonly IProgress<TranscribeProgress> _page;

        public StageProgress(IProgress<TranscribeProgress> page)
        {
            _page = page;
        }

        public void Report(TranscriptionProgress value) =>
            _page.Report(new TranscribeProgress { Stage = BridgeStages.Of(value.Stage), Fraction = value.Fraction });
    }
}
