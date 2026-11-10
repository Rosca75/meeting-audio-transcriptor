// Tests that the published JSON Schema file is well formed and names exactly the properties the records write.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;
using static MeetingAudioTranscriptor.Engine.Tests.Writers.SampleTranscripts;

namespace MeetingAudioTranscriptor.Engine.Tests.Writers;

[Trait("Category", "transcript")]
public sealed class TranscriptSchemaTests
{
    [Fact]
    public void SchemaIsWellFormedJsonWithTheRootProperties()
    {
        using JsonDocument schema = LoadSchema();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
        Assert.True(schema.RootElement.TryGetProperty("$defs", out _), "got no $defs, wanted the record definitions");
    }

    [Fact]
    public void SchemaListsExactlyThePropertyNamesTheRecordsWrite()
    {
        TranscriptSegment full = Segment(1, 0, 1, "S1", "Hi.", removed: "x") with
        {
            Words = [new TranscriptWord { Text = "Hi", Start = 0, End = 1, Confidence = 1, Removed = true }],
        };
        TranscriptDocument document = Document([full]);
        document = document with
        {
            Metadata = document.Metadata with
            {
                Settings = document.Metadata.Settings with { SpeakerCountHint = 2 },
            },
        };

        using JsonDocument written = JsonDocument.Parse(Text(TranscriptFormat.Json, TranscriptVariant.Cleaned, document));
        HashSet<string> fromRecords = [];
        Collect(written.RootElement, fromRecords);

        using JsonDocument schema = LoadSchema();
        HashSet<string> fromSchema = [];
        foreach (JsonProperty definition in schema.RootElement.GetProperty("$defs").EnumerateObject())
        {
            foreach (JsonProperty property in definition.Value.GetProperty("properties").EnumerateObject())
            {
                fromSchema.Add(property.Name);
            }
        }

        foreach (JsonProperty property in schema.RootElement.GetProperty("properties").EnumerateObject())
        {
            fromSchema.Add(property.Name);
        }

        Assert.Equal(fromRecords.Order().ToArray(), fromSchema.Order().ToArray());
    }

    private static void Collect(JsonElement element, HashSet<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                names.Add(property.Name);
                Collect(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                Collect(item, names);
            }
        }
    }

    private static JsonDocument LoadSchema()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "MeetingAudioTranscriptor.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.True(directory is not null, "got no repository root above the test binaries, wanted the folder holding the solution file");
        string path = Path.Combine(directory!, "docs", "transcript.schema.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
