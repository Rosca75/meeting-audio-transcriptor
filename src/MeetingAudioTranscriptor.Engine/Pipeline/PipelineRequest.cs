// What one transcription run is asked for: the language and the facts the transcript's metadata records.
using System.Collections.Generic;
using MeetingAudioTranscriptor.Contracts;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Cleaning;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>The inputs of one run besides the audio.</summary>
/// <remarks>
/// The engine names no path, so the caller passes the recording's file name alone, and the facts
/// only the composition knows (the version, the models) arrive here as plain values.
/// </remarks>
public sealed record PipelineRequest
{
    /// <summary>The language the user chose, which picks the cleaning lexicon.</summary>
    public required CleaningLanguage Language { get; init; }

    /// <summary>The recording's file name without its folder.</summary>
    public required string SourceFileName { get; init; }

    /// <summary>The application's name for the metadata.</summary>
    public string ApplicationName { get; init; } = AppInfo.Name;

    /// <summary>The application's version for the metadata.</summary>
    public required string ApplicationVersion { get; init; }

    /// <summary>The models the run uses, for the metadata.</summary>
    public IReadOnlyList<TranscriptModel> Models { get; init; } = [];

    /// <summary>
    /// The settings the run was started with. The pipeline overwrites the thread count and the
    /// speaker separation flag with what it actually used.
    /// </summary>
    public required TranscriptSettings Settings { get; init; }

    /// <summary>Seconds the caller spent before the pipeline started (opening the file), added to the recorded processing time.</summary>
    public double SecondsBeforeRun { get; init; }
}
