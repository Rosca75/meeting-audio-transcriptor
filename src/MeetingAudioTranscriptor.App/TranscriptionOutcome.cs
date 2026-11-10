// How a transcription ended, in a closed set of kinds a front end maps to a message and an exit code, with the run's figures.
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Pipeline;

namespace MeetingAudioTranscriptor.App;

/// <summary>The ways a run ends; each needs a different action from the user.</summary>
public enum TranscriptionOutcomeKind
{
    /// <summary>The transcripts were written.</summary>
    Succeeded,

    /// <summary>The recording holds no speech; an empty transcript was written.</summary>
    NoSpeech,

    /// <summary>The recording does not exist.</summary>
    InputNotFound,

    /// <summary>The recording exists and cannot be read (permissions).</summary>
    InputUnreadable,

    /// <summary>The recording is not a supported type or encoding, or no decoder for it is installed.</summary>
    InputUnsupported,

    /// <summary>The recording is empty, damaged or holds no audio stream.</summary>
    InputCorrupt,

    /// <summary>A model file is missing; the message names the fetch command.</summary>
    ModelsMissing,

    /// <summary>The output folder cannot be created or written, or a file exists and overwriting was refused.</summary>
    OutputNotWritable,

    /// <summary>The disk filled up while the transcripts were written.</summary>
    DiskFull,

    /// <summary>The user cancelled; nothing was written.</summary>
    Cancelled,

    /// <summary>Another run of the same service is still going.</summary>
    AlreadyRunning,

    /// <summary>Anything else; the message carries the exception's own.</summary>
    Unexpected,
}

/// <summary>The end of one run.</summary>
public sealed record TranscriptionOutcome
{
    /// <summary>How it ended.</summary>
    public required TranscriptionOutcomeKind Kind { get; init; }

    /// <summary>One actionable sentence for the user.</summary>
    public required string Message { get; init; }

    /// <summary>True when transcript files were written (also when no speech was found).</summary>
    public bool Succeeded => Kind is TranscriptionOutcomeKind.Succeeded or TranscriptionOutcomeKind.NoSpeech;

    /// <summary>The files written, in the order they were named; empty on failure.</summary>
    public IReadOnlyList<string> OutputFiles { get; init; } = [];

    /// <summary>The recording's length in seconds.</summary>
    public double AudioSeconds { get; init; }

    /// <summary>The wall time of the run in seconds, from the request to the last file moved into place.</summary>
    public double ProcessingSeconds { get; init; }

    /// <summary>Processing seconds per audio second: 0.1 means ten times faster than real time; 0 without audio.</summary>
    public double RealTimeFactor => AudioSeconds > 0 ? ProcessingSeconds / AudioSeconds : 0;

    /// <summary>The seconds in which speech was detected.</summary>
    public double SpeechSeconds { get; init; }

    /// <summary>The speakers told apart; 0 without separation.</summary>
    public int SpeakerCount { get; init; }

    /// <summary>The segments of the transcript, removed ones included.</summary>
    public int SegmentCount { get; init; }

    /// <summary>The words the recogniser returned.</summary>
    public int WordCount { get; init; }

    /// <summary>The words left in the cleaned text.</summary>
    public int CleanedWordCount { get; init; }

    /// <summary>The process's peak working set in bytes when the run ended.</summary>
    public long PeakWorkingSetBytes { get; init; }

    /// <summary>The decoder that read the recording, such as <c>wav</c> or <c>ffmpeg (bundled) at ...</c>.</summary>
    public string? DecoderName { get; init; }

    /// <summary>The seconds spent loading models in this run; 0 when they were already loaded.</summary>
    public double ModelLoadSeconds { get; init; }

    /// <summary>The seconds spent writing the files.</summary>
    public double WriteSeconds { get; init; }

    /// <summary>The pipeline's result, transcript and timings; null when the run did not get that far.</summary>
    public PipelineResult? Pipeline { get; init; }
}

/// <summary>One progress report of a run.</summary>
/// <param name="Stage">The stage running.</param>
/// <param name="StageFraction">How far the stage has come, 0 to 1.</param>
/// <param name="Fraction">How far the run has come, 0 to 1, never decreasing.</param>
public readonly record struct TranscriptionProgress(TranscriptionStage Stage, double StageFraction, double Fraction);

/// <summary>Whether the models a run needs are installed.</summary>
/// <param name="ModelsRoot">The models folder looked in.</param>
/// <param name="MissingFiles">The missing files, relative to the folder.</param>
/// <param name="Message">What to do about them; empty when nothing is missing.</param>
/// <param name="SpeakerSeparationAvailable">Whether speaker separation can run.</param>
public sealed record ModelStatus(string ModelsRoot, IReadOnlyList<string> MissingFiles, string Message, bool SpeakerSeparationAvailable)
{
    /// <summary>True when every required file is present.</summary>
    public bool Ready => MissingFiles.Count == 0;
}
