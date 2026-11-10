// The request the page sends to start a transcription, and the language and format codes it may hold.
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Contracts.Bridge;

/// <summary>What the user asked for on the page when pressing Start.</summary>
/// <remarks>
/// Property order is the order of the keys on the wire. The bridge validates the request before a
/// run starts, and hands the transcriber a copy whose <see cref="OutputFolder"/> is always set.
/// </remarks>
public sealed record TranscribeRequest
{
    /// <summary>The full path of the recording to transcribe.</summary>
    public string InputPath { get; init; } = "";

    /// <summary>The spoken language: "en", "fr" or "de".</summary>
    public string Language { get; init; } = "";

    /// <summary>The transcript formats to write: any of "vtt", "json" and "txt"; at least one.</summary>
    public IReadOnlyList<string> Formats { get; init; } = [];

    /// <summary>Whether to tell the speakers apart.</summary>
    public bool SpeakerSeparation { get; init; }

    /// <summary>The number of speakers when the user knows it (1 to 10); absent means automatic.</summary>
    public int? SpeakerCount { get; init; }

    /// <summary>Whether to also write the raw, uncleaned transcript files.</summary>
    public bool RawFiles { get; init; }

    /// <summary>The folder to write into; absent means the input file's folder.</summary>
    public string? OutputFolder { get; init; }
}

/// <summary>The language codes a request may carry.</summary>
public static class TranscribeLanguages
{
    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>French.</summary>
    public const string French = "fr";

    /// <summary>German.</summary>
    public const string German = "de";

    /// <summary>Every supported code.</summary>
    public static readonly IReadOnlyList<string> All = [English, French, German];
}

/// <summary>The transcript format codes a request may carry.</summary>
public static class TranscribeFormats
{
    /// <summary>WebVTT subtitles.</summary>
    public const string Vtt = "vtt";

    /// <summary>The JSON transcript.</summary>
    public const string Json = "json";

    /// <summary>The plain-text transcript.</summary>
    public const string Txt = "txt";

    /// <summary>Every supported code.</summary>
    public static readonly IReadOnlyList<string> All = [Vtt, Json, Txt];
}
