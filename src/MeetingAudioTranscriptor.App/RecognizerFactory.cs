// The one place the speech recogniser is constructed: which files it needs, how its session is opened and what it is called.
using System;
using System.Collections.Generic;
using System.IO;
using MeetingAudioTranscriptor.Engine.Recognition;
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
/// which measures fastest and keeps one copy of the weights in memory.
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

    /// <summary>Opens the recogniser.</summary>
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

        throw new ModelLoadException(
            $"this build carries no speech recogniser for {ModelName}; build the application from a revision that includes the recogniser");
    }
}

/// <summary>Builds the speaker separator of a run; none is installed in this build, so every run goes without.</summary>
internal static class SpeakerSeparatorFactory
{
    /// <summary>The files speaker separation needs, relative to the models folder.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } = [];

    /// <summary>Whether speaker separation can run with the given models folder.</summary>
    public static bool IsAvailable(string modelsRoot) => false;

    /// <summary>Creates the per-run separator factory; null runs without speakers.</summary>
    public static Func<Engine.Pipeline.ISpeakerSeparator?>? Create(string modelsRoot) => null;
}
