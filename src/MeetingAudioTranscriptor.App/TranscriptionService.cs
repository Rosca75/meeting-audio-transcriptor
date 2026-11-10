// The application's transcription service: opens the recording and the models, runs the pipeline on a worker and writes the transcripts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Writers;
using MeetingAudioTranscriptor.Inference;
using MeetingAudioTranscriptor.Media;

namespace MeetingAudioTranscriptor.App;

/// <summary>The collaborators of the service that touch files, models and clocks, replaced by fakes in the tests.</summary>
/// <param name="OpenAudio">Opens a recording and names the decoder that read it.</param>
/// <param name="LoadModels">Loads every model from a models folder.</param>
/// <param name="MissingFiles">Lists the required files a models folder lacks.</param>
/// <param name="SeparationAvailable">Says whether speaker separation can run with a models folder.</param>
/// <param name="Time">The clock of the timings.</param>
/// <param name="PeakWorkingSet">Reads the process's peak working set in bytes.</param>
/// <param name="Options">Adjusts the pipeline options of a run; the tests shorten intervals with it.</param>
internal sealed record ServiceSeams(
    Func<string, (IAudioSource Source, string Decoder)> OpenAudio,
    Func<string, LoadedModels> LoadModels,
    Func<string, IReadOnlyList<string>> MissingFiles,
    Func<string, bool> SeparationAvailable,
    TimeProvider Time,
    Func<long> PeakWorkingSet,
    Func<PipelineOptions, PipelineOptions>? Options = null)
{
    /// <summary>The real collaborators of the shipped application.</summary>
    public static ServiceSeams Default { get; } = new(
        OpenWithMedia,
        LoadedModels.Load,
        root => LoadedModels.RequiredFiles().Where(file => !File.Exists(Path.Combine(root, file))).ToArray(),
        SpeakerSeparatorFactory.IsAvailable,
        TimeProvider.System,
        ReadPeakWorkingSet);

    private static (IAudioSource Source, string Decoder) OpenWithMedia(string path)
    {
        // The engine's WAV reader reads plain WAV; a WAV encoding it refuses goes to the platform decoder.
        AudioFileOpener opener = new(stream => new WavAudioSource(stream), error => error is AudioFormatException);
        IAudioSource source = opener.Open(path);
        return (source, opener.DecoderName);
    }

    private static long ReadPeakWorkingSet()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PeakWorkingSet64;
    }
}

/// <summary>Transcribes recordings to transcript files; the one surface both front ends call.</summary>
/// <remarks>
/// <para>
/// The models load on the first run and stay loaded for later ones, because loading the recogniser
/// takes seconds and its weights are shared by every worker. A failed load is not remembered, so a
/// run after the models were fetched succeeds. One run goes at a time: a second run while one is in
/// flight ends at once with <see cref="TranscriptionOutcomeKind.AlreadyRunning"/>, because two runs
/// would only share the same cores.
/// </para>
/// <para>
/// <see cref="RunAsync"/> never throws for a run that started: every way a run ends is one
/// <see cref="TranscriptionOutcome"/>, with an actionable message. It runs the work on a dedicated
/// thread, so the caller's thread stays free. The service is the only place that opens the recording,
/// the models and the output folder; the engine below it sees samples and bytes.
/// </para>
/// </remarks>
public sealed class TranscriptionService : IDisposable
{
    /// <summary>The share of the overall bar the pipeline takes; writing takes the rest.</summary>
    private const double PipelineShare = 0.98;

    private readonly ServiceSeams _seams;
    private readonly Lock _lock = new();
    private LoadedModels? _models;
    private bool _running;
    private bool _disposed;

    /// <summary>Creates the service over a models folder.</summary>
    /// <param name="modelsRoot">The models folder; null for <see cref="ModelCatalog.ResolveRoot"/> (MAT_MODELS_DIR, beside the executable, or the repository's).</param>
    public TranscriptionService(string? modelsRoot = null)
        : this(modelsRoot ?? ModelCatalog.ResolveRoot(), ServiceSeams.Default)
    {
    }

    /// <summary>Creates the service over injected collaborators.</summary>
    internal TranscriptionService(string modelsRoot, ServiceSeams seams)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsRoot);
        ArgumentNullException.ThrowIfNull(seams);
        ModelsRoot = Path.GetFullPath(modelsRoot);
        _seams = seams;
    }

    /// <summary>The models folder the service reads.</summary>
    public string ModelsRoot { get; }

    /// <summary>The version of the native ONNX Runtime, or the reason it cannot load.</summary>
    public static string RuntimeVersion
    {
        get
        {
            try
            {
                return OrtRunnerFactory.RuntimeVersion;
            }
            catch (ModelLoadException error)
            {
                return "unavailable: " + error.Message;
            }
        }
    }

    /// <summary>Says which model files are missing, with the command that fetches them.</summary>
    public ModelStatus Check()
    {
        IReadOnlyList<string> missing = _seams.MissingFiles(ModelsRoot);
        string message = missing.Count == 0
            ? ""
            : $"the models folder {ModelsRoot} lacks {string.Join(", ", missing)}; fetch them with: node build/fetch-models.mjs --set all --into {ModelsRoot} (or point {ModelCatalog.ModelsDirectoryVariable} at a folder that holds them)";
        return new ModelStatus(ModelsRoot, LoadedModels.RequiredFiles(), missing, message, _seams.SeparationAvailable(ModelsRoot));
    }

    /// <summary>Compares every required file with its size and SHA-256 in the models lock; slow on the large files.</summary>
    /// <param name="lockPath">The lock to read; null to look beside the models, beside the executable, then in the repository.</param>
    /// <returns>One sentence per problem; empty when every file matches.</returns>
    /// <exception cref="FileNotFoundException">No lock was found.</exception>
    public IReadOnlyList<string> Verify(string? lockPath = null) =>
        ModelVerification.Verify(ModelsRoot, LoadedModels.RequiredFiles(), lockPath ?? ModelVerification.FindLock(ModelsRoot));

    /// <summary>Transcribes one recording and writes its transcript files.</summary>
    /// <param name="request">What to transcribe and where to write.</param>
    /// <param name="progress">Receives the stage and the overall fraction from the worker thread; null for none.</param>
    /// <param name="cancellation">Stops the run; it then ends as <see cref="TranscriptionOutcomeKind.Cancelled"/> with no file written.</param>
    /// <returns>How the run ended.</returns>
    /// <exception cref="ArgumentException">The request names no input, no format, or an unknown language.</exception>
    public Task<TranscriptionOutcome> RunAsync(TranscriptionRequest request, IProgress<TranscriptionProgress>? progress, CancellationToken cancellation)
    {
        Validate(request);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running)
            {
                return Task.FromResult(new TranscriptionOutcome
                {
                    Kind = TranscriptionOutcomeKind.AlreadyRunning,
                    Message = "a transcription is already running; wait for it to finish or cancel it, then start this one",
                });
            }

            _running = true;
        }

        return Task.Factory.StartNew(
            () =>
            {
                try
                {
                    return Run(request, progress, cancellation);
                }
                finally
                {
                    lock (_lock)
                    {
                        _running = false;
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>Releases the loaded models.</summary>
    public void Dispose()
    {
        LoadedModels? models;
        lock (_lock)
        {
            _disposed = true;
            models = _models;
            _models = null;
        }

        models?.Dispose();
    }

    private static void Validate(TranscriptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputPath);
        if (!CleaningLanguages.TryParse(request.Language, out _))
        {
            throw new ArgumentException($"the language '{request.Language}' is not supported; use en, fr or de", nameof(request));
        }

        if (request.Formats is null || request.Formats.Count == 0)
        {
            throw new ArgumentException("the request names no transcript format; ask for at least one of vtt, json and txt", nameof(request));
        }

        if (request.Threads is < 1 || request.SpeakerCountHint is < 1)
        {
            throw new ArgumentException("the thread count and the speaker count must be at least 1 when given", nameof(request));
        }
    }

    private TranscriptionOutcome Run(TranscriptionRequest request, IProgress<TranscriptionProgress>? progress, CancellationToken cancellation)
    {
        TimeProvider time = _seams.Time;
        long started = time.GetTimestamp();
        string inputPath = Path.GetFullPath(request.InputPath);
        string outputFolder = Path.GetFullPath(request.OutputFolder ?? Path.GetDirectoryName(inputPath) ?? ".");
        OverallProgress overall = new(progress);
        RunPhase phase = RunPhase.Input;
        PipelineResult? result = null;
        string? decoder = null;
        double loadSeconds = 0;
        IAudioSource? source = null;
        try
        {
            if (!File.Exists(inputPath))
            {
                throw new AudioDecodeException(AudioDecodeFailure.FileNotFound, $"'{inputPath}' does not exist; check the path and the file name");
            }

            // The output is checked before the long run, so a two-hour recording does not fail at its last step.
            phase = RunPhase.Output;
            List<PlannedFile> files = TranscriptFiles.Plan(inputPath, outputFolder, request.Formats.Distinct().ToArray(), request.RawFiles);
            TranscriptFiles.ProbeFolder(outputFolder);
            if (request.Overwrite == OverwritePolicy.Refuse && files.FirstOrDefault(file => File.Exists(file.Path)) is PlannedFile existing)
            {
                return Failed(TranscriptionOutcomeKind.OutputNotWritable, $"{existing.Path} already exists and overwriting was refused; delete it, choose another output folder, or allow overwriting", started, null, null);
            }

            if (files.FirstOrDefault(file => Directory.Exists(file.Path)) is PlannedFile blocked)
            {
                return Failed(TranscriptionOutcomeKind.OutputNotWritable, $"{blocked.Path} is a folder, so the transcript cannot be written there; rename that folder or choose another output folder", started, null, null);
            }

            phase = RunPhase.Models;
            ModelStatus status = Check();
            if (!status.Ready)
            {
                return Failed(TranscriptionOutcomeKind.ModelsMissing, status.Message, started, null, null);
            }

            cancellation.ThrowIfCancellationRequested();
            overall.Report(TranscriptionStage.LoadingModels, 0, 0);
            long loading = time.GetTimestamp();
            LoadedModels models = Models();
            loadSeconds = time.GetElapsedTime(loading).TotalSeconds;
            cancellation.ThrowIfCancellationRequested();

            phase = RunPhase.Input;
            (source, decoder) = _seams.OpenAudio(inputPath);

            phase = RunPhase.Pipeline;
            bool separation = (request.SpeakerSeparation ?? true) && models.Separators is not null;
            PipelineOptions options = new() { Threads = request.Threads ?? PipelineOptions.DefaultThreads };
            options = _seams.Options?.Invoke(options) ?? options;
            TranscriptionPipeline pipeline = new(models.Vad, models.Recognizer, separation ? models.Separators : null, options, time);
            PipelineRequest pipelineRequest = new()
            {
                Language = CleaningLanguages.Parse(request.Language),
                SourceFileName = Path.GetFileName(inputPath),
                ApplicationVersion = AppVersion.Current,
                Models = models.Models,
                Settings = new TranscriptSettings
                {
                    SpeakerSeparation = separation,
                    SpeakerCountHint = separation ? request.SpeakerCountHint : null,
                    Threads = options.Threads,
                    Formats = files.Where(file => file.Variant == TranscriptVariant.Cleaned).Select(file => TranscriptFormats.ToWireName(file.Format)).ToArray(),
                    RawFiles = request.RawFiles,
                },
                SecondsBeforeRun = time.GetElapsedTime(started).TotalSeconds,
            };
            InlineProgress pipelineProgress = new(report => overall.Report(report.Stage, report.StageFraction, report.Fraction * PipelineShare));
            result = pipeline.Run(source, pipelineRequest, pipelineProgress, cancellation);
            source.Dispose();
            source = null;

            phase = RunPhase.Output;
            overall.Report(TranscriptionStage.Writing, 0, PipelineShare);
            long writing = time.GetTimestamp();
            TranscriptFiles.Write(files, result.Document, fraction => overall.Report(TranscriptionStage.Writing, fraction, PipelineShare + (fraction * (1 - PipelineShare))), cancellation);
            double writeSeconds = time.GetElapsedTime(writing).TotalSeconds;
            overall.Report(TranscriptionStage.Writing, 1, 1);

            TranscriptDocument document = result.Document;
            bool noSpeech = document.Segments.Count == 0;
            return Outcome(started, result, decoder, loadSeconds) with
            {
                Kind = noSpeech ? TranscriptionOutcomeKind.NoSpeech : TranscriptionOutcomeKind.Succeeded,
                Message = noSpeech
                    ? $"no speech was found in '{inputPath}'; an empty transcript was written"
                    : $"the transcript of '{Path.GetFileName(inputPath)}' was written to {outputFolder}",
                OutputFiles = files.Select(file => file.Path).ToArray(),
                WriteSeconds = writeSeconds,
            };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            (TranscriptionOutcomeKind kind, string message) = FailureMapping.Map(error, phase, inputPath, outputFolder);
            if (cancellation.IsCancellationRequested && kind == TranscriptionOutcomeKind.Unexpected)
            {
                // A failure caused by the cancellation itself (a decoder killed mid-read) is a cancellation.
                (kind, message) = FailureMapping.Map(new OperationCanceledException(cancellation), phase, inputPath, outputFolder);
            }

            return Failed(kind, message, started, result, decoder) with { ModelLoadSeconds = loadSeconds };
        }
        finally
        {
            source?.Dispose();
        }
    }

    /// <summary>Returns the loaded models, loading them on the first call.</summary>
    private LoadedModels Models()
    {
        // Loading happens under the lock: a run is the only caller, and a second run is refused at its start.
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _models ??= _seams.LoadModels(ModelsRoot);
            return _models;
        }
    }

    private TranscriptionOutcome Failed(TranscriptionOutcomeKind kind, string message, long started, PipelineResult? result, string? decoder) =>
        Outcome(started, result, decoder, 0) with { Kind = kind, Message = message };

    private TranscriptionOutcome Outcome(long started, PipelineResult? result, string? decoder, double loadSeconds)
    {
        TranscriptDocument? document = result?.Document;
        return new TranscriptionOutcome
        {
            Kind = TranscriptionOutcomeKind.Unexpected,
            Message = "",
            AudioSeconds = result?.AudioSeconds ?? 0,
            ProcessingSeconds = _seams.Time.GetElapsedTime(started).TotalSeconds,
            SpeechSeconds = result?.SpeechSeconds ?? 0,
            SpeakerCount = document?.Speakers.Count ?? 0,
            SegmentCount = document?.Segments.Count ?? 0,
            WordCount = result?.RecognizedWords ?? 0,
            CleanedWordCount = document?.Segments.Sum(segment => segment.Words?.Count(word => word.Removed != true) ?? 0) ?? 0,
            PeakWorkingSetBytes = _seams.PeakWorkingSet(),
            DecoderName = decoder,
            ModelLoadSeconds = loadSeconds,
            Pipeline = result,
        };
    }

    /// <summary>Forwards pipeline reports on the reporting thread.</summary>
    private sealed class InlineProgress : IProgress<PipelineProgress>
    {
        private readonly Action<PipelineProgress> _callback;

        public InlineProgress(Action<PipelineProgress> callback)
        {
            _callback = callback;
        }

        public void Report(PipelineProgress value) => _callback(value);
    }

    /// <summary>The one place the run's overall fraction is held, so it never decreases across the pipeline and the writing.</summary>
    private sealed class OverallProgress
    {
        private readonly IProgress<TranscriptionProgress>? _sink;
        private readonly Lock _lock = new();
        private double _last;

        public OverallProgress(IProgress<TranscriptionProgress>? sink)
        {
            _sink = sink;
        }

        public void Report(TranscriptionStage stage, double stageFraction, double fraction)
        {
            if (_sink is null)
            {
                return;
            }

            lock (_lock)
            {
                _last = Math.Max(_last, Math.Clamp(fraction, 0, 1));
                _sink.Report(new TranscriptionProgress(stage, stageFraction, _last));
            }
        }
    }
}
