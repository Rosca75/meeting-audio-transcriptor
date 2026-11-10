// The three transcript file formats and their names on the command line and in the JSON settings.
using System;

namespace MeetingAudioTranscriptor.Engine.Writers;

/// <summary>A transcript file format.</summary>
public enum TranscriptFormat
{
    /// <summary>WebVTT subtitles.</summary>
    Vtt,

    /// <summary>The JSON transcript.</summary>
    Json,

    /// <summary>Readable plain text.</summary>
    Txt,
}

/// <summary>Converts <see cref="TranscriptFormat"/> to and from its lower-case name.</summary>
public static class TranscriptFormats
{
    /// <summary>The name of a format: `vtt`, `json` or `txt`.</summary>
    public static string ToWireName(TranscriptFormat format)
    {
        return format switch
        {
            TranscriptFormat.Vtt => "vtt",
            TranscriptFormat.Json => "json",
            TranscriptFormat.Txt => "txt",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "the transcript format is not one of vtt, json and txt"),
        };
    }

    /// <summary>Reads a name, ignoring case and surrounding spaces; false when it is none of the three.</summary>
    public static bool TryParse(string? text, out TranscriptFormat format)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "vtt":
                format = TranscriptFormat.Vtt;
                return true;
            case "json":
                format = TranscriptFormat.Json;
                return true;
            case "txt":
                format = TranscriptFormat.Txt;
                return true;
            default:
                format = default;
                return false;
        }
    }

    /// <summary>Reads a name or throws a message that lists the accepted ones.</summary>
    /// <exception cref="FormatException">The text is none of `vtt`, `json` and `txt`.</exception>
    public static TranscriptFormat Parse(string? text)
    {
        return TryParse(text, out TranscriptFormat format)
            ? format
            : throw new FormatException($"the transcript format '{text}' is unknown; use one of vtt, json or txt");
    }
}
