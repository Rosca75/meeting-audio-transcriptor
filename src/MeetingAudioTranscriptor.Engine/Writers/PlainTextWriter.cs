// Writes a transcript as readable plain text: a header, then speaker turns split into paragraphs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MeetingAudioTranscriptor.Contracts.Transcripts;

namespace MeetingAudioTranscriptor.Engine.Writers;

/// <summary>Writes a <see cref="TranscriptDocument"/> as a plain text file for people to read.</summary>
/// <remarks>
/// Consecutive segments of one speaker form a turn, and a turn is cut into paragraphs where the
/// speaker paused for more than two seconds or where a paragraph has grown past about 700
/// characters and the last segment ended a sentence. A segment is a sentence or a clause, so the
/// segment boundary is the sentence boundary the length rule needs. Segments without a speaker are
/// printed as paragraphs that each start with their own timestamp.
/// </remarks>
public static class PlainTextWriter
{
    private const double ParagraphGapSeconds = 2.0;
    private const int ParagraphLength = 700;

    /// <summary>Writes the document to the stream as UTF-8 without a byte order mark.</summary>
    /// <param name="document">The transcript to write.</param>
    /// <param name="variant">Cleaned text without the removed segments, or the raw text of all of them.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    public static void Write(TranscriptDocument document, TranscriptVariant variant, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        Dictionary<string, string> labels = TranscriptText.LabelsById(document.Speakers);
        List<string> blocks = [Header(document)];

        TranscriptSegment? previous = null;
        string? turnSpeaker = null;
        StringBuilder? paragraph = null;
        List<string> turnBlocks = [];
        long turnStart = 0;
        long paragraphStart = 0;

        void CloseParagraph()
        {
            if (paragraph is null)
            {
                return;
            }

            string text = paragraph.ToString();
            turnBlocks.Add(turnSpeaker is null ? $"[{Stamp(paragraphStart)}] {text}" : text);
            paragraph = null;
        }

        void CloseTurn()
        {
            CloseParagraph();
            if (turnBlocks.Count == 0)
            {
                return;
            }

            if (turnSpeaker is null)
            {
                blocks.AddRange(turnBlocks);
            }
            else
            {
                string label = labels.TryGetValue(turnSpeaker, out string? known) ? known : turnSpeaker;
                turnBlocks[0] = $"[{Stamp(turnStart)}] {label}:\n{turnBlocks[0]}";
                blocks.AddRange(turnBlocks);
            }

            turnBlocks = [];
        }

        foreach (TranscriptSegment segment in document.Segments)
        {
            string text = TranscriptText.TextOf(segment, variant);
            if (text.Length == 0)
            {
                continue;
            }

            // A change of speaker closes the turn; a run of segments with no speaker is one turn that
            // prints as timestamped paragraphs.
            bool newTurn = previous is null || !string.Equals(segment.Speaker, turnSpeaker, StringComparison.Ordinal);
            if (newTurn)
            {
                CloseTurn();
                turnSpeaker = segment.Speaker;
                turnStart = Seconds(segment.Start);
            }

            bool newParagraph = paragraph is null
                || segment.Start - previous!.End > ParagraphGapSeconds
                || (paragraph.Length > ParagraphLength && EndsSentence(paragraph));
            if (newParagraph)
            {
                CloseParagraph();
                paragraph = new StringBuilder();
                paragraphStart = Seconds(segment.Start);
            }
            else
            {
                paragraph!.Append(' ');
            }

            paragraph.Append(text);
            previous = segment;
        }

        CloseTurn();
        TranscriptText.WriteTo(destination, string.Join("\n\n", blocks) + "\n");
    }

    private static string Header(TranscriptDocument document)
    {
        TranscriptMetadata metadata = document.Metadata;
        string speakers = document.Speakers.Count > 0
            ? document.Speakers.Count.ToString(CultureInfo.InvariantCulture)
            : "not separated";

        return string.Join(
            "\n",
            $"Transcript of {metadata.SourceFile}",
            $"Duration: {TranscriptText.Clock(Seconds(metadata.DurationSeconds), 1)}",
            $"Language: {metadata.Language}",
            $"Date: {DateOf(metadata.CreatedUtc)}",
            $"Speakers: {speakers}");
    }

    // The calendar date of the run, or the stored text when it is not an ISO timestamp.
    private static string DateOf(string createdUtc)
    {
        return DateTimeOffset.TryParse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : createdUtc;
    }

    private static bool EndsSentence(StringBuilder paragraph)
    {
        char last = paragraph[^1];
        return last is '.' or '!' or '?' or '…';
    }

    // Whole seconds, rounded down, so a timestamp never points after the moment it names.
    private static long Seconds(double seconds)
    {
        return double.IsFinite(seconds) && seconds > 0 ? (long)Math.Floor(seconds) : 0;
    }

    private static string Stamp(long seconds)
    {
        return TranscriptText.Clock(seconds, 2);
    }
}
