// The one place the speech recogniser and the speaker separator are constructed: which files they need, how their sessions are opened and what they are called.
using System;
using System.Collections.Generic;
using System.IO;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;
using MeetingAudioTranscriptor.Inference;

namespace MeetingAudioTranscriptor.App;

/// <summary>A recogniser and the native sessions it holds, which the owner disposes.</summary>
/// <param name="Recognizer">The recogniser every worker shares.</param>
/// <param name="Owned">The sessions to dispose with it.</param>
internal sealed record LoadedRecognizer(ISpeechRecognizer Recognizer, IReadOnlyList<IDisposable> Owned);

/// <summary>Builds the shipped recogniser over the models folder.</summary>
/// <remarks>
/// The session runs with one intra-op thread and no spinning: the recognition model scales poorly
/// with intra-op threads, so the pipeline's workers share this one session, each call using one core,
/// which measures fastest and keeps one copy of the weights in memory. The paths are the ones
/// <c>build/models.lock</c> pins for the model; a unit test holds the two in step.
/// </remarks>
internal static class RecognizerFactory
{
    /// <summary>The recognition model's name in the models lock and the transcript metadata.</summary>
    public const string ModelName = "granite-speech-5.0-470m-turboctc";

    /// <summary>The graph file, relative to the models folder.</summary>
    public const string ModelFile = ModelName + "/model.int8.scores.onnx";

    /// <summary>The tokenizer file, relative to the models folder.</summary>
    public const string TokenizerFile = ModelName + "/tokenizer.json";

    /// <summary>The files the recogniser needs, relative to the models folder.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } = [ModelFile, TokenizerFile];

    /// <summary>The session settings of the shared recognition session.</summary>
    public static RunnerOptions SessionOptions { get; } = new(IntraOpThreads: 1, AllowSpinning: false);

    /// <summary>Opens the recogniser: one session shared by every worker, and the vocabulary.</summary>
    /// <param name="modelsRoot">The models folder.</param>
    /// <exception cref="ModelLoadException">A file is missing or does not load.</exception>
    public static LoadedRecognizer Create(string modelsRoot)
    {
        string model = Path.Combine(modelsRoot, ModelFile);
        string tokenizer = Path.Combine(modelsRoot, TokenizerFile);
        if (!File.Exists(model) || !File.Exists(tokenizer))
        {
            throw new ModelLoadException(
                $"the recognition model is not complete in {modelsRoot}: {ModelFile} and {TokenizerFile} are needed; fetch them with node build/fetch-models.mjs --model {ModelName} --into {modelsRoot}");
        }

        BpeVocabulary vocabulary;
        try
        {
            using FileStream stream = File.OpenRead(tokenizer);
            vocabulary = BpeVocabulary.Load(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelLoadException(
                $"the recogniser's vocabulary {tokenizer} could not be read ({error.Message}); fetch it again with node build/fetch-models.mjs --model {ModelName} --into {modelsRoot}",
                error);
        }

        ITensorRunner runner = OrtRunnerFactory.Open(model, SessionOptions);
        try
        {
            // The low-yield retry stays off: the level normalisation alone recovers the quiet speech it was meant for.
            return new LoadedRecognizer(new GraniteCtcRecognizer(runner, vocabulary), [runner]);
        }
        catch
        {
            runner.Dispose();
            throw;
        }
    }
}

/// <summary>Builds the speaker separator of each run over one shared embedding session.</summary>
/// <remarks>
/// Speaker separation is optional: without the embedding model a run goes on without speakers and
/// its outcome says so. The embedding session is opened once per service, with one intra-op thread
/// and no spinning like the recogniser's, because the recognition workers call it from their own
/// threads; each run gets a fresh separator, which holds that recording's windows.
/// </remarks>
internal static class SpeakerSeparatorFactory
{
    /// <summary>The embedding model's name in the models lock and the transcript metadata.</summary>
    public const string ModelName = "wespeaker-campplus-lm";

    /// <summary>The embedding graph, relative to the models folder.</summary>
    public const string ModelFile = ModelName + "/wespeaker_en_voxceleb_campplus_lm.onnx";

    /// <summary>The files speaker separation needs, relative to the models folder.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } = [ModelFile];

    /// <summary>The session settings of the shared embedding session.</summary>
    public static RunnerOptions SessionOptions { get; } = new(IntraOpThreads: 1, AllowSpinning: false);

    /// <summary>Whether speaker separation can run with the given models folder.</summary>
    public static bool IsAvailable(string modelsRoot) => File.Exists(Path.Combine(modelsRoot, ModelFile));

    /// <summary>Opens the embedding session and returns the per-run separator factory; null when the model is not installed.</summary>
    /// <param name="modelsRoot">The models folder.</param>
    /// <param name="owned">Receives the session to dispose with the other models.</param>
    /// <exception cref="ModelLoadException">The file exists and does not load.</exception>
    public static Func<ISpeakerSeparator?>? Create(string modelsRoot, ICollection<IDisposable> owned)
    {
        ArgumentNullException.ThrowIfNull(owned);
        if (!IsAvailable(modelsRoot))
        {
            return null;
        }

        ITensorRunner runner = OrtRunnerFactory.Open(Path.Combine(modelsRoot, ModelFile), SessionOptions);
        owned.Add(runner);
        DiarisationOptions options = new();
        SpeakerEmbedder embedder = new(runner, options.BatchSize);
        return () => new DiarisationSpeakerSeparator(embedder, options);
    }
}
