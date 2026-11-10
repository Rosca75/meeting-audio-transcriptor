// Helpers the text writers share: encoding, speaker labels, the text of a variant and millisecond rounding.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MeetingAudioTranscriptor.Contracts.Transcripts;

namespace MeetingAudioTranscriptor.Engine.Writers;

/// <summary>The small pieces VTT and TXT writing have in common.</summary>
/// <remarks>
/// Output is UTF-8 without a byte order mark and `\n` line endings, because a transcript is read by
/// tools on both operating systems and a BOM or CRLF breaks line-oriented ones.
/// </remarks>
internal static class TranscriptText
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the text to the stream as UTF-8 and leaves the stream open.</summary>
    public static void WriteTo(Stream destination, string text)
    {
        ArgumentNullException.ThrowIfNull(destination);
        byte[] bytes = Utf8NoBom.GetBytes(text);
        destination.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// The segment's text in the chosen variant with every whitespace run made one space, or an
    /// empty string when the variant has nothing to print for it.
    /// </summary>
    /// <remarks>
    /// A segment whose text is blank is left out in both variants: an empty cue or an empty
    /// paragraph carries nothing, and a cleaned segment is empty exactly when cleaning removed it.
    /// </remarks>
    public static string TextOf(TranscriptSegment segment, TranscriptVariant variant)
    {
        return Collapse(variant == TranscriptVariant.Raw ? segment.RawText : segment.Text);
    }

    /// <summary>Collapses every run of whitespace, line breaks included, into one space and trims the ends.</summary>
    public static string Collapse(string text)
    {
        StringBuilder builder = new(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>The label of every speaker by id; a segment naming an unlisted id falls back to the id itself.</summary>
    public static Dictionary<string, string> LabelsById(IReadOnlyList<TranscriptSpeaker> speakers)
    {
        Dictionary<string, string> labels = new(StringComparer.Ordinal);
        foreach (TranscriptSpeaker speaker in speakers)
        {
            labels[speaker.Id] = speaker.Label;
        }

        return labels;
    }

    /// <summary>Whole milliseconds of a time in seconds, rounded half up; negative and non-finite times read as 0.</summary>
    /// <remarks>
    /// The rounding goes through decimal so 59.9995 seconds is 60000 ms and not 59999 ms by the
    /// binary representation of the double.
    /// </remarks>
    public static long Milliseconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return 0;
        }

        // Clamped far below the decimal range; no recording is a century long.
        double bounded = Math.Min(seconds, 3_153_600_000d);
        return (long)Math.Round((decimal)bounded * 1000m, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Formats whole seconds as hours, minutes and seconds with hours of at least the given width.</summary>
    public static string Clock(long totalSeconds, int hourDigits)
    {
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;
        return $"{hours.ToString("D" + hourDigits, System.Globalization.CultureInfo.InvariantCulture)}:{minutes:D2}:{seconds:D2}";
    }
}
