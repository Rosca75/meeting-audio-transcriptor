// Tests of the JSON transcript: round trip, property order, null omission, rounding and encoding.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;
using static MeetingAudioTranscriptor.Engine.Tests.Writers.SampleTranscripts;

namespace MeetingAudioTranscriptor.Engine.Tests.Writers;

[Trait("Category", "writers")]
public sealed class JsonTranscriptTests
{
    private static string Json(TranscriptDocument document) =>
        Text(TranscriptFormat.Json, TranscriptVariant.Cleaned, document);

    [Fact]
    public void RoundTripReproducesTheDocument()
    {
        TranscriptDocument original = Document(
        [
            Segment(1, 0.25, 1.5, "S1", "Gruß Gott, café."),
            Segment(2, 1.5, 2.5, "S2", "", raw: "uh", removed: "filler") with
            {
                Words = [new TranscriptWord { Text = "uh", Start = 1.5, End = 1.75, Confidence = 0.5, Removed = true }],
            },
        ]);

        using MemoryStream stream = new(Bytes(TranscriptFormat.Json, TranscriptVariant.Cleaned, original));
        TranscriptDocument back = TranscriptJson.Deserialize(stream);

        // Records hold lists by reference, so the check compares the written form of both.
        Assert.Equal(Json(original), Json(back));
        Assert.Equal("S2", back.Segments[1].Speaker);
        Assert.True(back.Segments[1].Words![0].Removed);
        Assert.Equal("filler", back.Segments[1].Removed);
    }

    [Fact]
    public void PropertiesAreCamelCaseInDeclarationOrder()
    {
        using JsonDocument parsed = JsonDocument.Parse(Json(Document([Segment(1, 0, 1, "S1", "Hi.")])));

        Assert.Equal(["schemaVersion", "metadata", "speakers", "segments"], Names(parsed.RootElement));
        JsonElement segment = parsed.RootElement.GetProperty("segments")[0];
        Assert.Equal(["id", "start", "end", "speaker", "text", "rawText", "confidence"], Names(segment));
    }

    [Fact]
    public void AGivenNameIsTheLabelAndTheIdStays()
    {
        string json = Json(Named([Segment(1, 0, 1, "S1", "Hi.")]));

        Assert.Contains(
            "  \"speakers\": [\n    {\n      \"id\": \"S1\",\n      \"label\": \"Anne-Sophie Müller\",\n      \"speakingSeconds\": 10\n    },\n    {\n      \"id\": \"S2\",\n      \"label\": \"R&D\",\n      \"speakingSeconds\": 5\n    }\n  ],",
            json);
        Assert.Contains("\"speaker\": \"S1\"", json);
    }

    [Fact]
    public void NullsAreOmitted()
    {
        string json = Json(Document([Segment(1, 0, 1, null, "Hi.")], speakers: false));

        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("\"speaker\"", json);
        Assert.DoesNotContain("\"removed\"", json);
        Assert.DoesNotContain("\"words\"", json);
        Assert.DoesNotContain("speakerCountHint", json);
    }

    [Fact]
    public void TimesAndConfidencesAreRoundedToThreeDecimals()
    {
        TranscriptSegment segment = Segment(1, 1.23456, 2.99999, "S1", "Hi.") with { Confidence = 0.87654 };

        string json = Json(Document([segment]));

        Assert.Contains("\"start\": 1.235,", json);
        Assert.Contains("\"end\": 3,", json);
        Assert.Contains("\"confidence\": 0.877", json);
    }

    [Fact]
    public void OutputIsIndentedWithTwoSpacesAndLfOnly()
    {
        string json = Json(Document([]));

        Assert.StartsWith("{\n  \"schemaVersion\": \"1\",\n  \"metadata\": {\n    \"application\"", json);
        Assert.DoesNotContain("\r", json);
        Assert.EndsWith("}\n", json);
    }

    [Fact]
    public void NonAsciiCharactersAreWrittenLiterallyWithoutBom()
    {
        byte[] bytes = Bytes(TranscriptFormat.Json, TranscriptVariant.Cleaned, Document([Segment(1, 0, 1, "S1", "Grüße, Zoë, très bien <ok> & \"quoted\"")]));

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "got a UTF-8 BOM, wanted none");
        string json = new UTF8Encoding(false).GetString(bytes);
        Assert.Contains("Grüße, Zoë, très bien", json);
        Assert.Contains("réunion.wav", json);
    }

    [Fact]
    public void JsonCarriesCleanedAndRawTextRegardlessOfVariant()
    {
        TranscriptDocument document = Document([Segment(1, 0, 1, "S1", "Hello.", raw: "uh hello")]);

        string raw = Text(TranscriptFormat.Json, TranscriptVariant.Raw, document);

        Assert.Equal(Json(document), raw);
        Assert.Contains("\"text\": \"Hello.\"", raw);
        Assert.Contains("\"rawText\": \"uh hello\"", raw);
    }

    [Fact]
    public void NonFiniteNumberIsRefusedWithTheFieldNamed()
    {
        TranscriptDocument document = Document([Segment(1, double.NaN, 1, "S1", "Hi.")]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => Json(document));

        Assert.Contains("segment.start", error.Message);
    }

    [Fact]
    public void DeserialisingGarbageThrowsJsonException()
    {
        using MemoryStream stream = new("null"u8.ToArray());

        Assert.Throws<JsonException>(() => TranscriptJson.Deserialize(stream));
    }

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();
}
