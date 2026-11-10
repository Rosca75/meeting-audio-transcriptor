// Tests that page messages reach the bridge untouched, and that automation messages are the host's
// own only while a test drives the window.
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class PageMessageRouterTests
{
    [Theory]
    [InlineData("{\"id\":1,\"method\":\"app.info\",\"args\":[]}")]
    [InlineData("not json")]
    [InlineData("{\"automation\":{\"exit\":0}}")]
    public void WithoutAutomationEveryMessageGoesToTheBridge(string message)
    {
        Recorder recorder = new();
        PageMessageRouter router = new(recorder.Bridge.Add, recorder, automationEnabled: false);

        bool answered = router.Route(message);

        Assert.True(!answered && recorder.Bridge.Count == 1 && recorder.Bridge[0] == message, $"got answered={answered} and bridge ({string.Join(" | ", recorder.Bridge)}), wanted the message passed on unchanged");
        Assert.True(recorder.Exits.Count == 0, $"got {recorder.Exits.Count} exit(s), wanted none");
    }

    [Fact]
    public void AnOrdinaryMessageGoesToTheBridgeWithAutomationOn()
    {
        Recorder recorder = new();
        PageMessageRouter router = new(recorder.Bridge.Add, recorder, automationEnabled: true);

        router.Route("{\"id\":2,\"method\":\"run.start\",\"args\":[{\"inputPath\":\"automation\"}]}");

        Assert.True(recorder.Bridge.Count == 1, $"got {recorder.Bridge.Count} bridge message(s), wanted 1");
    }

    [Fact]
    public void AutomationMessagesAreAnsweredByTheHost()
    {
        Recorder recorder = new();
        PageMessageRouter router = new(recorder.Bridge.Add, recorder, automationEnabled: true);

        router.Route("{\"automation\":{\"log\":\"hello\"}}");
        router.Route("{\"automation\":{\"echo\":\"q\\\" \\u2028 \\u00e9\"}}");
        router.Route("{\"automation\":{\"dropUris\":[\"file:///a.wav\",3]}}");
        router.Route("{\"automation\":{\"exit\":7}}");

        Assert.True(recorder.Bridge.Count == 0, $"got {recorder.Bridge.Count} bridge message(s), wanted none");
        Assert.True(recorder.Logs.Count == 1 && recorder.Logs[0] == "automation: log hello", $"got logs ({string.Join(" | ", recorder.Logs)}), wanted the one line");
        Assert.True(recorder.Emits.Count == 1, $"got {recorder.Emits.Count} emitted envelope(s), wanted 1");
        using (JsonDocument echo = JsonDocument.Parse(recorder.Emits[0]))
        {
            string? text = echo.RootElement.GetProperty("automation").GetProperty("echo").GetString();
            Assert.True(text == "q\" \u2028 \u00e9", $"got echo '{text}', wanted the text sent");
        }

        Assert.True(recorder.Drops.Count == 1 && recorder.Drops[0].Count == 1 && recorder.Drops[0][0] == "file:///a.wav", "got a drop other than the one string URI, wanted that URI alone");
        Assert.True(recorder.Exits.Count == 1 && recorder.Exits[0] == 7, $"got exits ({string.Join(", ", recorder.Exits)}), wanted 7");
    }

    [Theory]
    [InlineData("{\"automation\":\"exit\"}")]
    [InlineData("[\"automation\"]")]
    [InlineData("{\"automation\": {\"exit\": 0}")]
    public void AMalformedAutomationMessageGoesToTheBridge(string message)
    {
        Recorder recorder = new();
        PageMessageRouter router = new(recorder.Bridge.Add, recorder, automationEnabled: true);

        router.Route(message);

        Assert.True(recorder.Bridge.Count == 1 && recorder.Exits.Count == 0, $"got {recorder.Bridge.Count} bridge message(s) and {recorder.Exits.Count} exit(s) for {message}, wanted it passed to the bridge");
    }

    private sealed class Recorder : IAutomationActions
    {
        public List<string> Bridge { get; } = [];

        public List<int> Exits { get; } = [];

        public List<string> Logs { get; } = [];

        public List<string> Emits { get; } = [];

        public List<IReadOnlyList<string>> Drops { get; } = [];

        public void Exit(int code) => Exits.Add(code);

        public void Log(string line) => Logs.Add(line);

        public void Emit(string json) => Emits.Add(json);

        public void Drop(IReadOnlyList<string> uris) => Drops.Add(uris);
    }
}
