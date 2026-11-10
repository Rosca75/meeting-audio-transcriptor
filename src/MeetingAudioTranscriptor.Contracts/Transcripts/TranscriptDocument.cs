// The JSON transcript: the one file that carries everything a run produced, raw and cleaned.
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Contracts.Transcripts;

/// <summary>The root of the JSON transcript file.</summary>
/// <remarks>
/// Property order is the order of the keys in the file. A property that may be absent is nullable
/// and omitted when null. `docs/transcript-schema.md` describes every field.
/// </remarks>
public sealed record TranscriptDocument
{
    /// <summary>The version of this file's shape, raised when a field changes meaning.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>What was transcribed, by what, and with which settings.</summary>
    public required TranscriptMetadata Metadata { get; init; }

    /// <summary>The speakers the run told apart; empty when speaker separation was off.</summary>
    public required IReadOnlyList<TranscriptSpeaker> Speakers { get; init; }

    /// <summary>The segments in time order, each with its cleaned and its raw text.</summary>
    public required IReadOnlyList<TranscriptSegment> Segments { get; init; }
}

/// <summary>The facts about one run.</summary>
public sealed record TranscriptMetadata
{
    /// <summary>The application's name.</summary>
    public required string Application { get; init; }

    /// <summary>The application's version.</summary>
    public required string ApplicationVersion { get; init; }

    /// <summary>The file name of the recording, without its folder.</summary>
    public required string SourceFile { get; init; }

    /// <summary>The length of the recording in seconds.</summary>
    public required double DurationSeconds { get; init; }

    /// <summary>The seconds of the recording in which speech was detected.</summary>
    public required double SpeechSeconds { get; init; }

    /// <summary>The language the user chose: `en`, `fr` or `de`.</summary>
    public required string Language { get; init; }

    /// <summary>When the run finished, in UTC, ISO 8601.</summary>
    public required string CreatedUtc { get; init; }

    /// <summary>How long the run took in seconds, decoding and writing included.</summary>
    public required double ProcessingSeconds { get; init; }

    /// <summary>The models the run used.</summary>
    public required IReadOnlyList<TranscriptModel> Models { get; init; }

    /// <summary>The settings the run was started with.</summary>
    public required TranscriptSettings Settings { get; init; }
}

/// <summary>One model a run used.</summary>
public sealed record TranscriptModel
{
    /// <summary>What the model did: `recognition`, `voice-activity` or `speaker-embedding`.</summary>
    public required string Role { get; init; }

    /// <summary>The model's name as the models lock lists it.</summary>
    public required string Name { get; init; }
}

/// <summary>The settings of one run.</summary>
public sealed record TranscriptSettings
{
    /// <summary>Whether speakers were told apart.</summary>
    public required bool SpeakerSeparation { get; init; }

    /// <summary>The number of speakers the user stated, when one was stated.</summary>
    public int? SpeakerCountHint { get; init; }

    /// <summary>The number of processor threads the run used.</summary>
    public required int Threads { get; init; }

    /// <summary>The transcript formats the run wrote, such as `vtt`, `json`, `txt`.</summary>
    public required IReadOnlyList<string> Formats { get; init; }

    /// <summary>Whether raw transcript files were written beside the cleaned ones.</summary>
    public required bool RawFiles { get; init; }
}

/// <summary>One anonymous speaker.</summary>
public sealed record TranscriptSpeaker
{
    /// <summary>The identifier segments refer to, such as `S1`.</summary>
    public required string Id { get; init; }

    /// <summary>The label shown in the text formats, such as `Speaker 1`.</summary>
    public required string Label { get; init; }

    /// <summary>The seconds this speaker spoke, summed over their segments.</summary>
    public required double SpeakingSeconds { get; init; }
}

/// <summary>One stretch of speech by one speaker, usually a sentence.</summary>
public sealed record TranscriptSegment
{
    /// <summary>The segment's number, from 1, in time order.</summary>
    public required int Id { get; init; }

    /// <summary>Where the segment starts, in seconds.</summary>
    public required double Start { get; init; }

    /// <summary>Where the segment ends, in seconds.</summary>
    public required double End { get; init; }

    /// <summary>The speaker's identifier; absent when speaker separation was off.</summary>
    public string? Speaker { get; init; }

    /// <summary>The cleaned text; empty when cleaning removed the whole segment.</summary>
    public required string Text { get; init; }

    /// <summary>The text as the recogniser wrote it.</summary>
    public required string RawText { get; init; }

    /// <summary>Why cleaning removed the whole segment, such as `backchannel`; absent when it kept text.</summary>
    public string? Removed { get; init; }

    /// <summary>The mean of the word confidences, between 0 and 1.</summary>
    public required double Confidence { get; init; }

    /// <summary>The raw words with their times; absent when the recogniser gives no word times.</summary>
    public IReadOnlyList<TranscriptWord>? Words { get; init; }
}

/// <summary>One recognised word.</summary>
public sealed record TranscriptWord
{
    /// <summary>The word as the recogniser wrote it.</summary>
    public required string Text { get; init; }

    /// <summary>Where the word starts, in seconds.</summary>
    public required double Start { get; init; }

    /// <summary>Where the word ends, in seconds.</summary>
    public required double End { get; init; }

    /// <summary>The recogniser's probability for the word, between 0 and 1.</summary>
    public required double Confidence { get; init; }

    /// <summary>True when cleaning removed this word from the cleaned text; absent when it kept it.</summary>
    public bool? Removed { get; init; }
}
