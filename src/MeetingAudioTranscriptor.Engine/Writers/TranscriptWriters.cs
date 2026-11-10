// The one entry point that writes a transcript in a chosen format, and the extension each format uses.
using System;
using System.IO;
using MeetingAudioTranscriptor.Contracts.Transcripts;

namespace MeetingAudioTranscriptor.Engine.Writers;

/// <summary>Dispatches to the writer of a <see cref="TranscriptFormat"/>.</summary>
/// <remarks>
/// The front ends write the same set of files, so the choice of writer lives here once. The JSON
/// format ignores the variant because it holds both texts.
/// </remarks>
public static class TranscriptWriters
{
    /// <summary>Writes the document in the format and variant to the stream.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one of the three.</exception>
    public static void Write(TranscriptFormat format, TranscriptVariant variant, TranscriptDocument document, Stream destination)
    {
        switch (format)
        {
            case TranscriptFormat.Vtt:
                VttWriter.Write(document, variant, destination);
                break;
            case TranscriptFormat.Txt:
                PlainTextWriter.Write(document, variant, destination);
                break;
            case TranscriptFormat.Json:
                JsonTranscriptWriter.Write(document, destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "the transcript format is not one of vtt, json and txt");
        }
    }

    /// <summary>The file extension of a format, with its leading dot: `.vtt`, `.json` or `.txt`.</summary>
    public static string FileExtension(TranscriptFormat format)
    {
        return "." + TranscriptFormats.ToWireName(format);
    }
}
