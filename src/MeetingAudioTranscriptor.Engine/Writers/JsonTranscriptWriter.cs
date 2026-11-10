// Writes a transcript as the JSON file, through the contract's one serialiser.
using System;
using System.IO;
using MeetingAudioTranscriptor.Contracts.Transcripts;

namespace MeetingAudioTranscriptor.Engine.Writers;

/// <summary>Writes a <see cref="TranscriptDocument"/> as the JSON transcript.</summary>
/// <remarks>
/// The JSON file always carries the cleaned and the raw text of every segment, so it has no
/// variant: a reader who wants either picks the field.
/// </remarks>
public static class JsonTranscriptWriter
{
    /// <summary>Writes the document to the stream as UTF-8 without a byte order mark.</summary>
    /// <param name="document">The transcript to write.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    public static void Write(TranscriptDocument document, Stream destination)
    {
        TranscriptJson.Serialize(document, destination);
    }
}
