// Tests the envelope paths of the bridge: replies, rejections and the host:error report for
// messages that cannot be read.
using System.Text.Json;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests.Bridge;

[Trait("Category", "errors")]
public sealed class EnvelopeTests
{
    [Fact]
    public async Task MalformedJsonReportsHostErrorAndNeverThrows()
    {
        using BridgeHarness h = new();

        await h.Bridge.ReceiveAsync("{not json");

        JsonElement payload = Assert.Single(h.Host.Events(BridgeEvents.HostError));
        Assert.Equal(HostErrorCodes.MessageUnreadable, payload.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"hello\":1}")]
    [InlineData("")]
    public async Task ShapesThatAreNeitherACallNorADropReportHostError(string message)
    {
        using BridgeHarness h = new();

        await h.Bridge.ReceiveAsync(message);

        Assert.Single(h.Host.Events(BridgeEvents.HostError));
    }

    [Fact]
    public async Task ReceiveItselfDoesNotThrowOnGarbage()
    {
        using BridgeHarness h = new();

        h.Bridge.Receive("\u0000garbage");

        JsonElement payload = await h.Host.WaitForEventAsync(BridgeEvents.HostError);
        Assert.Equal("message_unreadable", payload.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownMethodIsRejectedByName()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync("nope.nothing");

        Assert.Equal("unknown method nope.nothing", reply.GetProperty("error").GetString());
        Assert.False(reply.TryGetProperty("result", out _), "got a result on a rejection, wanted none");
    }

    [Fact]
    public async Task CallWithoutAnIdCannotBeAnsweredSoItReportsHostError()
    {
        using BridgeHarness h = new();

        await h.Bridge.ReceiveAsync("{\"method\":\"app.info\",\"args\":[]}");

        Assert.Single(h.Host.Events(BridgeEvents.HostError));
    }

    [Fact]
    public async Task WrongArgumentCountIsRejectedNamingBothCounts()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.FolderOpen);

        string error = reply.GetProperty("error").GetString()!;
        Assert.Contains("1 argument", error);
        Assert.Contains("carried 0", error);
    }

    [Fact]
    public async Task ArgumentOfTheWrongShapeIsRejectedWithASentence()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallRawAsync(BridgeMethods.RunStart, "[\"not an object\"]");

        Assert.Contains("argument 1 is not in the shape", reply.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ProgrammingFaultBecomesAGenericSentenceWithoutTheDetail()
    {
        using BridgeHarness h = new();
        h.Transcriber.CheckFailure = new System.Collections.Generic.KeyNotFoundException("secret detail");

        JsonElement reply = await h.CallAsync(BridgeMethods.AppInfo);

        // app.info turns a failing check into a not-ready result; the fault text must not leak into it.
        JsonElement models = reply.GetProperty("result").GetProperty("models");
        Assert.False(models.GetProperty("ready").GetBoolean());
        Assert.DoesNotContain("secret detail", models.GetProperty("message").GetString());
    }

    [Fact]
    public async Task HostDialogFailureIsRejectedWithAnActionableSentence()
    {
        using BridgeHarness h = new();
        h.Host.DialogFailure = new System.InvalidOperationException("no display");

        JsonElement reply = await h.CallAsync(BridgeMethods.FilePick);

        string error = reply.GetProperty("error").GetString()!;
        Assert.Contains("could not be opened", error);
        Assert.Contains("restart", error);
    }
}
