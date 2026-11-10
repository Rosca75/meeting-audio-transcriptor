// Writes a transcript as WebVTT: one cue per segment, the speaker as a voice tag.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MeetingAudioTranscriptor.Contracts.Transcripts;

namespace MeetingAudioTranscriptor.Engine.Writers;

/// <summary>Writes a <see cref="TranscriptDocument"/> as a WebVTT file.</summary>
/// <remarks>
/// Players reject a cue whose end is not after its start and expect starts in order, so the writer
/// repairs both rather than trusting the recogniser: a start is never earlier than the previous
/// cue's and every cue lasts at least ten milliseconds. The cue text escapes `&amp;`, `&lt;` and
/// `&gt;`, which also keeps `--&gt;` from appearing inside a cue.
/// </remarks>
public static class VttWriter
{
    private const long MinimumCueMilliseconds = 10;

    /// <summary>Writes the document to the stream as UTF-8 without a byte order mark.</summary>
    /// <param name="document">The transcript to write.</param>
    /// <param name="variant">Cleaned text without the removed segments, or the raw text of all of them.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    public static void Write(TranscriptDocument document, TranscriptVariant variant, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        Dictionary<string, string> labels = TranscriptText.LabelsById(document.Speakers);
        StringBuilder builder = new();
        builder.Append("WEBVTT\n");

        long previousStart = 0;
        foreach (TranscriptSegment segment in document.Segments)
        {
            string text = TranscriptText.TextOf(segment, variant);
            if (text.Length == 0)
            {
                continue;
            }

            long start = Math.Max(TranscriptText.Milliseconds(segment.Start), previousStart);
            long end = Math.Max(TranscriptText.Milliseconds(segment.End), start + MinimumCueMilliseconds);
            previousStart = start;

            builder.Append('\n')
                .Append(segment.Id.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(Timestamp(start)).Append(" --> ").Append(Timestamp(end)).Append('\n');

            string escaped = Escape(text);
            if (segment.Speaker is null)
            {
                builder.Append(escaped);
            }
            else
            {
                string label = labels.TryGetValue(segment.Speaker, out string? known) ? known : segment.Speaker;
                builder.Append("<v ").Append(Escape(label)).Append('>').Append(escaped).Append("</v>");
            }

            builder.Append('\n');
        }

        TranscriptText.WriteTo(destination, builder.ToString());
    }

    /// <summary>Formats milliseconds as `HH:MM:SS.mmm`; hours grow past two digits only after 99 hours.</summary>
    internal static string Timestamp(long milliseconds)
    {
        long wholeSeconds = milliseconds / 1000;
        return TranscriptText.Clock(wholeSeconds, 2) + "." + (milliseconds % 1000).ToString("D3", CultureInfo.InvariantCulture);
    }

    private static string Escape(string text)
    {
        return text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
