// The shapes a transcription reports back: progress while it runs, the outcome when it ends and the
// models check that decides whether Start is enabled.
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Contracts.Bridge;

/// <summary>One progress report from a running transcription.</summary>
public sealed record TranscribeProgress
{
    /// <summary>The name of the stage now running, in words the page shows as they are.</summary>
    public string Stage { get; init; } = "";

    /// <summary>How much of the whole run is done, from 0 to 1; the bridge never lets it decrease.</summary>
    public double Fraction { get; init; }
}

/// <summary>How a transcription ended, as the page shows it.</summary>
public sealed record TranscribeOutcome
{
    /// <summary>One of <see cref="TranscribeOutcomeKinds"/>.</summary>
    public string Kind { get; init; } = "";

    /// <summary>A sentence for the user; the bridge fills in a default when absent.</summary>
    public string? Message { get; init; }

    /// <summary>The folder the files were written to.</summary>
    public string? OutputFolder { get; init; }

    /// <summary>The full paths of the files written; empty when none.</summary>
    public IReadOnlyList<string> OutputFiles { get; init; } = [];

    /// <summary>The length of the recording in seconds.</summary>
    public double DurationSeconds { get; init; }

    /// <summary>How long the run took in seconds.</summary>
    public double ProcessingSeconds { get; init; }

    /// <summary>Recording seconds processed per second of work.</summary>
    public double SpeedFactor { get; init; }

    /// <summary>The number of speakers found.</summary>
    public int Speakers { get; init; }

    /// <summary>The number of words in the transcript.</summary>
    public int Words { get; init; }
}

/// <summary>The values <see cref="TranscribeOutcome.Kind"/> may take.</summary>
public static class TranscribeOutcomeKinds
{
    /// <summary>The run finished and wrote its files.</summary>
    public const string Completed = "completed";

    /// <summary>The run finished and found no speech to transcribe.</summary>
    public const string NoSpeech = "noSpeech";

    /// <summary>The user cancelled the run.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The run failed; the message says why and what to do.</summary>
    public const string Failed = "failed";

    /// <summary>Whether the value is one of the four kinds.</summary>
    public static bool IsKnown(string? kind)
    {
        return kind is Completed or NoSpeech or Cancelled or Failed;
    }
}

/// <summary>Whether the models a run needs are installed.</summary>
public sealed record ModelCheckResult
{
    /// <summary>True when a run can start.</summary>
    public bool Ready { get; init; }

    /// <summary>When not ready, a sentence saying what is missing and where to put it.</summary>
    public string? Message { get; init; }
}
