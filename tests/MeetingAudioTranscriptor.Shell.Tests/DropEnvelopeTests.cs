// Tests that dropped file URIs become local paths and that the drop message has the bridge's shape.
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class DropEnvelopeTests
{
    [Theory]
    [InlineData("file:///home/ana/meeting.m4a", "/home/ana/meeting.m4a")]
    [InlineData("file://localhost/home/ana/meeting.m4a", "/home/ana/meeting.m4a")]
    [InlineData("FILE:///tmp/a.wav", "/tmp/a.wav")]
    [InlineData("file:///home/ana/r%C3%A9union%20one.m4a", "/home/ana/r\u00e9union one.m4a")]
    [InlineData("file:///tmp/%E4%BC%9A%E8%AE%AE.wav", "/tmp/\u4f1a\u8bae.wav")]
    [InlineData("file:///tmp/a%23b%25c.wav", "/tmp/a#b%c.wav")]
    [InlineData("file:///tmp/already-\u00e9.wav", "/tmp/already-\u00e9.wav")]
    public void AFileUriBecomesItsPath(string uri, string wanted)
    {
        string? got = DropEnvelope.LocalPathOf(uri);

        Assert.True(got == wanted, $"got '{got}' for {uri}, wanted '{wanted}'");
    }

    [Theory]
    [InlineData("https://example.com/a.wav")]
    [InlineData("file://server/share/a.wav")]
    [InlineData("file://")]
    [InlineData("file:///tmp/a%2.wav")]
    [InlineData("file:///tmp/a%00.wav")]
    [InlineData("/tmp/a.wav")]
    public void AnythingElseIsSkipped(string uri)
    {
        string? got = DropEnvelope.LocalPathOf(uri);

        Assert.True(got is null, $"got '{got}' for {uri}, wanted it skipped");
    }

    [Fact]
    public void ACarriedListKeepsOnlyLocalFilesInOrder()
    {
        IReadOnlyList<string> got = DropEnvelope.PathsFromUris(["https://a/b", " file:///x/1.wav\r", "file:///x/2.m4a"]);

        Assert.True(got.Count == 2 && got[0] == "/x/1.wav" && got[1] == "/x/2.m4a", $"got ({string.Join(", ", got)}), wanted (/x/1.wav, /x/2.m4a)");
    }

    [Fact]
    public void TheEnvelopeIsTheBridgesDropShape()
    {
        string json = DropEnvelope.FromPaths(["/tmp/r\u00e9union \"one\".wav", "C:\\a\\b.m4a"]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement paths = document.RootElement.GetProperty("drop").GetProperty("paths");
        Assert.True(paths.GetArrayLength() == 2, $"got {paths.GetArrayLength()} paths in {json}, wanted 2");
        Assert.True(paths[0].GetString() == "/tmp/r\u00e9union \"one\".wav", $"got '{paths[0].GetString()}', wanted the first path unchanged");
        Assert.True(paths[1].GetString() == "C:\\a\\b.m4a", $"got '{paths[1].GetString()}', wanted the second path unchanged");
    }
}
