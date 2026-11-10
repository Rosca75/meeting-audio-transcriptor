// Hand-built transcript documents and a stream-to-text helper shared by the writer tests.
using System.Collections.Generic;
using System.IO;
using System.Text;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;

namespace MeetingAudioTranscriptor.Engine.Tests.Writers;

internal static class SampleTranscripts
{
    public static TranscriptSegment Segment(
        int id, double start, double end, string? speaker, string text, string? raw = null, string? removed = null) =>
        new()
        {
            Id = id,
            Start = start,
            End = end,
            Speaker = speaker,
            Text = text,
            RawText = raw ?? text,
            Removed = removed,
            Confidence = 0.9,
        };

    public static TranscriptDocument Document(
        IReadOnlyList<TranscriptSegment> segments, bool speakers = true, double duration = 754.4) =>
        new()
        {
            SchemaVersion = "1",
            Metadata = new TranscriptMetadata
            {
                Application = "meeting-audio-transcriptor",
                ApplicationVersion = "0.1.0",
                SourceFile = "réunion.wav",
                DurationSeconds = duration,
                SpeechSeconds = 500,
                Language = "fr",
                CreatedUtc = "2026-11-10T18:30:00Z",
                ProcessingSeconds = 42.5,
                Models = [new TranscriptModel { Role = "recognition", Name = "whisper-small" }],
                Settings = new TranscriptSettings
                {
                    SpeakerSeparation = speakers,
                    Threads = 4,
                    Formats = ["vtt", "json", "txt"],
                    RawFiles = true,
                },
            },
            Speakers = speakers
                ? [
                    new TranscriptSpeaker { Id = "S1", Label = "Speaker 1", SpeakingSeconds = 10 },
                    new TranscriptSpeaker { Id = "S2", Label = "Speaker 2", SpeakingSeconds = 5 },
                ]
                : [],
            Segments = segments,
        };

    public static TranscriptDocument Empty(bool speakers = false) => Document([], speakers, 0);

    public static byte[] Bytes(TranscriptFormat format, TranscriptVariant variant, TranscriptDocument document)
    {
        using MemoryStream stream = new();
        TranscriptWriters.Write(format, variant, document, stream);
        return stream.ToArray();
    }

    public static string Text(TranscriptFormat format, TranscriptVariant variant, TranscriptDocument document) =>
        new UTF8Encoding(false).GetString(Bytes(format, variant, document));

    public static string Vtt(TranscriptDocument document, TranscriptVariant variant = TranscriptVariant.Cleaned) =>
        Text(TranscriptFormat.Vtt, variant, document);

    public static string Txt(TranscriptDocument document, TranscriptVariant variant = TranscriptVariant.Cleaned) =>
        Text(TranscriptFormat.Txt, variant, document);
}
