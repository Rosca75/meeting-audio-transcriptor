// The models a service keeps loaded between runs: the VAD, the recogniser and the separator factory, with the sessions to dispose.
using System;
using System.Collections.Generic;
using System.IO;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Vad;
using MeetingAudioTranscriptor.Inference;

namespace MeetingAudioTranscriptor.App;

/// <summary>Everything a pipeline needs from the models folder, loaded once.</summary>
internal sealed class LoadedModels : IDisposable
{
    /// <summary>The VAD model's name in the models lock.</summary>
    public const string VadModelName = "silero-vad";

    /// <summary>The VAD graph, relative to the models folder.</summary>
    public const string VadFile = VadModelName + "/silero_vad.onnx";

    /// <summary>The metadata role of the recognition model.</summary>
    public const string RecognitionRole = "recognition";

    /// <summary>The metadata role of the voice-activity model.</summary>
    public const string VoiceActivityRole = "voice-activity";

    /// <summary>The metadata role of the speaker-embedding model.</summary>
    public const string SpeakerEmbeddingRole = "speaker-embedding";

    private readonly IReadOnlyList<IDisposable> _owned;

    public LoadedModels(
        SileroVadModel vad,
        ISpeechRecognizer recognizer,
        Func<ISpeakerSeparator?>? separators,
        IReadOnlyList<TranscriptModel> models,
        IReadOnlyList<IDisposable> owned)
    {
        Vad = vad;
        Recognizer = recognizer;
        Separators = separators;
        Models = models;
        _owned = owned;
    }

    /// <summary>The VAD, shared by every run.</summary>
    public SileroVadModel Vad { get; }

    /// <summary>The recogniser, shared by every worker of every run.</summary>
    public ISpeechRecognizer Recognizer { get; }

    /// <summary>Creates one run's separator; null when separation is unavailable.</summary>
    public Func<ISpeakerSeparator?>? Separators { get; }

    /// <summary>The models every run uses, as the transcript metadata lists them; the speaker-embedding model is added by a run that separates speakers.</summary>
    public IReadOnlyList<TranscriptModel> Models { get; }

    /// <summary>The files every run needs, relative to the models folder.</summary>
    public static IReadOnlyList<string> RequiredFiles()
    {
        List<string> files = [VadFile];
        files.AddRange(RecognizerFactory.RequiredFiles);
        return files;
    }

    /// <summary>Opens every model in the folder; disposes what it opened when one fails.</summary>
    /// <exception cref="ModelLoadException">A model is missing or does not load.</exception>
    public static LoadedModels Load(string modelsRoot)
    {
        List<IDisposable> owned = [];
        try
        {
            // The VAD is light; one intra-op thread keeps it from competing with the recognition workers.
            ITensorRunner vadRunner = OrtRunnerFactory.Open(Path.Combine(modelsRoot, VadFile), new RunnerOptions(IntraOpThreads: 1, AllowSpinning: false));
            owned.Add(vadRunner);
            LoadedRecognizer recognizer = RecognizerFactory.Create(modelsRoot);
            owned.AddRange(recognizer.Owned);
            Func<ISpeakerSeparator?>? separators = SpeakerSeparatorFactory.Create(modelsRoot, owned);

            // The speaker-embedding model is named by the service, in the runs that use it.
            TranscriptModel[] models =
            [
                new() { Role = RecognitionRole, Name = RecognizerFactory.ModelName },
                new() { Role = VoiceActivityRole, Name = VadModelName },
            ];
            return new LoadedModels(new SileroVadModel(vadRunner), recognizer.Recognizer, separators, models, owned);
        }
        catch
        {
            foreach (IDisposable disposable in owned)
            {
                disposable.Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        foreach (IDisposable disposable in _owned)
        {
            disposable.Dispose();
        }
    }
}
