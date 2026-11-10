// Tests that hold for every writer: encoding, empty input, format names and the dispatcher.
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;
using static MeetingAudioTranscriptor.Engine.Tests.Writers.SampleTranscripts;

namespace MeetingAudioTranscriptor.Engine.Tests.Writers;

[Trait("Category", "writers")]
public sealed class WriterCommonTests
{
    [Theory]
    [InlineData(TranscriptFormat.Vtt)]
    [InlineData(TranscriptFormat.Txt)]
    [InlineData(TranscriptFormat.Json)]
    public void OutputIsUtf8WithoutBomWithLiteralAccentsAndLfOnly(TranscriptFormat format)
    {
        TranscriptDocument document = Document([Segment(1, 0, 1, "S1", "Très bien, Grüße à tous.")]);

        byte[] bytes = Bytes(format, TranscriptVariant.Cleaned, document);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"got a BOM in {format}, wanted none");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.Contains("Très bien, Grüße à tous.", text);
        Assert.DoesNotContain("\r", text);
    }

    [Theory]
    [InlineData(TranscriptFormat.Vtt)]
    [InlineData(TranscriptFormat.Txt)]
    [InlineData(TranscriptFormat.Json)]
    public void EmptyTranscriptWritesAValidFile(TranscriptFormat format)
    {
        string text = Text(format, TranscriptVariant.Cleaned, Empty());

        Assert.False(string.IsNullOrWhiteSpace(text), $"got an empty {format} file, wanted a valid one");
        if (format == TranscriptFormat.Vtt)
        {
            Assert.Equal("WEBVTT\n", text);
        }
        else if (format == TranscriptFormat.Json)
        {
            using JsonDocument parsed = JsonDocument.Parse(text);
            Assert.Equal(0, parsed.RootElement.GetProperty("segments").GetArrayLength());
        }
        else
        {
            Assert.StartsWith("Transcript of ", text);
        }
    }

    [Fact]
    public void WritersLeaveTheStreamOpen()
    {
        using MemoryStream stream = new();

        VttWriter.Write(Empty(), TranscriptVariant.Cleaned, stream);

        Assert.True(stream.CanWrite, "got a closed stream, wanted the writer to leave it open");
    }

    [Theory]
    [InlineData("vtt", TranscriptFormat.Vtt)]
    [InlineData("JSON", TranscriptFormat.Json)]
    [InlineData(" txt ", TranscriptFormat.Txt)]
    public void FormatNamesParse(string text, TranscriptFormat expected)
    {
        Assert.True(TranscriptFormats.TryParse(text, out TranscriptFormat format));
        Assert.Equal(expected, format);
    }

    [Fact]
    public void UnknownFormatIsRefusedWithTheAcceptedNames()
    {
        Assert.False(TranscriptFormats.TryParse("srt", out _));
        FormatException error = Assert.Throws<FormatException>(() => TranscriptFormats.Parse("srt"));
        Assert.Contains("vtt, json or txt", error.Message);
    }

    [Theory]
    [InlineData(TranscriptFormat.Vtt, "vtt", ".vtt")]
    [InlineData(TranscriptFormat.Json, "json", ".json")]
    [InlineData(TranscriptFormat.Txt, "txt", ".txt")]
    public void NamesAndExtensionsMatch(TranscriptFormat format, string name, string extension)
    {
        Assert.Equal(name, TranscriptFormats.ToWireName(format));
        Assert.Equal(extension, TranscriptWriters.FileExtension(format));
    }

    [Fact]
    public void DispatcherRoutesToTheMatchingWriter()
    {
        TranscriptDocument document = Document([Segment(1, 0, 1, "S1", "Hi.")]);

        Assert.StartsWith("WEBVTT", Text(TranscriptFormat.Vtt, TranscriptVariant.Cleaned, document));
        Assert.StartsWith("{", Text(TranscriptFormat.Json, TranscriptVariant.Cleaned, document));
        Assert.StartsWith("Transcript of", Text(TranscriptFormat.Txt, TranscriptVariant.Cleaned, document));
    }
}
