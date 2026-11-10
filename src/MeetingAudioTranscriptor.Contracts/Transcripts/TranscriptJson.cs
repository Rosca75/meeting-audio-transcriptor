// The one way a TranscriptDocument becomes JSON bytes and back: fixed options, rounded numbers,
// UTF-8 without a byte order mark.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingAudioTranscriptor.Contracts.Transcripts;

/// <summary>Reads and writes the JSON transcript.</summary>
/// <remarks>
/// The output is deterministic: camelCase names in record declaration order, two-space indentation,
/// `\n` line endings, nulls omitted, and non-ASCII characters written literally so French and German
/// text stays readable. Times, durations and confidences are rounded to three decimals before
/// serialising rather than through a custom converter, so the serialiser stays the stock generated one.
/// </remarks>
public static class TranscriptJson
{
    private const int Decimals = 3;

    private static readonly TranscriptJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>Writes the document as UTF-8 JSON, without a byte order mark, ending in a newline.</summary>
    /// <param name="document">The transcript to write.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <exception cref="ArgumentException">A time or confidence is not a finite number.</exception>
    public static void Serialize(TranscriptDocument document, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        JsonSerializer.Serialize(destination, Rounded(document), Context.TranscriptDocument);
        destination.WriteByte((byte)'\n');
    }

    /// <summary>Reads a document from UTF-8 JSON.</summary>
    /// <param name="source">The stream to read from; it is left open.</param>
    /// <exception cref="JsonException">The content is not a transcript; the message names the position.</exception>
    public static TranscriptDocument Deserialize(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        TranscriptDocument? document = JsonSerializer.Deserialize(source, Context.TranscriptDocument);
        return document ?? throw new JsonException(
            "the transcript file holds a JSON null where a transcript object was expected; write the file again with the application");
    }

    private static TranscriptDocument Rounded(TranscriptDocument document)
    {
        return document with
        {
            Metadata = document.Metadata with
            {
                DurationSeconds = Round(document.Metadata.DurationSeconds, "metadata.durationSeconds"),
                SpeechSeconds = Round(document.Metadata.SpeechSeconds, "metadata.speechSeconds"),
                ProcessingSeconds = Round(document.Metadata.ProcessingSeconds, "metadata.processingSeconds"),
            },
            Speakers = document.Speakers
                .Select(speaker => speaker with { SpeakingSeconds = Round(speaker.SpeakingSeconds, "speaker.speakingSeconds") })
                .ToList(),
            Segments = document.Segments.Select(RoundedSegment).ToList(),
        };
    }

    private static TranscriptSegment RoundedSegment(TranscriptSegment segment)
    {
        IReadOnlyList<TranscriptWord>? words = segment.Words?
            .Select(word => word with
            {
                Start = Round(word.Start, "word.start"),
                End = Round(word.End, "word.end"),
                Confidence = Round(word.Confidence, "word.confidence"),
            })
            .ToList();

        return segment with
        {
            Start = Round(segment.Start, "segment.start"),
            End = Round(segment.End, "segment.end"),
            Confidence = Round(segment.Confidence, "segment.confidence"),
            Words = words,
        };
    }

    // Half away from zero, so 59.9995 reads 60 the way a person rounds it; NaN and infinity have no
    // JSON form, so they are refused with the field named.
    private static double Round(double value, string field)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException(
                $"{field} is {value}, which JSON cannot hold; the transcript needs a finite number there, so fix the stage that produced it");
        }

        return Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
    }
}
