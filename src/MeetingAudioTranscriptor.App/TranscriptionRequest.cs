// What a front end asks the transcription service for: the recording, the language, the files to write and how.
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Writers;

namespace MeetingAudioTranscriptor.App;

/// <summary>What happens when a transcript file already exists.</summary>
public enum OverwritePolicy
{
    /// <summary>The new file replaces it, once the new file is complete.</summary>
    Overwrite,

    /// <summary>The run refuses to start and names the file.</summary>
    Refuse,
}

/// <summary>One transcription the user asked for.</summary>
public sealed record TranscriptionRequest
{
    /// <summary>The recording: a .wav, .m4a or .mp4 file.</summary>
    public required string InputPath { get; init; }

    /// <summary>The language spoken: <c>en</c>, <c>fr</c> or <c>de</c>.</summary>
    public required string Language { get; init; }

    /// <summary>The cleaned transcript formats to write, at least one; the JSON always carries both texts.</summary>
    public IReadOnlyList<TranscriptFormat> Formats { get; init; } = [TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Txt];

    /// <summary>The folder the transcripts go to; null for the recording's own folder.</summary>
    public string? OutputFolder { get; init; }

    /// <summary>Whether speakers are told apart; null for "when a separator is installed".</summary>
    public bool? SpeakerSeparation { get; init; }

    /// <summary>The number of speakers, when the user knows it.</summary>
    public int? SpeakerCountHint { get; init; }

    /// <summary>
    /// The names the user gave the speakers, by order of first appearance; a blank entry and a speaker
    /// past the end keep the default label. Null for none; ignored by a run that does not separate speakers.
    /// </summary>
    public IReadOnlyList<string>? SpeakerNames { get; init; }

    /// <summary>The recognition workers; null for the default (the logical cores, at most four).</summary>
    public int? Threads { get; init; }

    /// <summary>Whether <c>.raw.vtt</c> and <c>.raw.txt</c> files are written beside the cleaned ones, for the text formats chosen.</summary>
    public bool RawFiles { get; init; }

    /// <summary>What happens to an existing file of the same name.</summary>
    public OverwritePolicy Overwrite { get; init; } = OverwritePolicy.Overwrite;
}
