// Tests each bridge method on its own: what it resolves to and what it asks of the host.
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Contracts.Bridge;
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests.Bridge;

[Trait("Category", "config")]
public sealed class MethodTests
{
    [Fact]
    public async Task AppInfoReportsVersionExtensionsAndModels()
    {
        using BridgeHarness h = new();

        JsonElement result = (await h.CallAsync(BridgeMethods.AppInfo)).GetProperty("result");

        Assert.Equal("1.2.3", result.GetProperty("version").GetString());
        Assert.Equal([".m4a", ".mp4", ".wav"], result.GetProperty("extensions").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.True(result.GetProperty("models").GetProperty("ready").GetBoolean());
        Assert.False(result.GetProperty("models").TryGetProperty("message", out _), "got a message on a ready result, wanted it omitted");
    }

    [Fact]
    public async Task AppInfoCarriesTheMissingModelsMessage()
    {
        using BridgeHarness h = new();
        h.Transcriber.Models = new ModelCheckResult { Ready = false, Message = "Models are missing. Put them in the models folder." };

        JsonElement models = (await h.CallAsync(BridgeMethods.AppInfo)).GetProperty("result").GetProperty("models");

        Assert.False(models.GetProperty("ready").GetBoolean());
        Assert.Equal("Models are missing. Put them in the models folder.", models.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AppInfoSurvivesAThrowingModelsCheck()
    {
        using BridgeHarness h = new();
        h.Transcriber.CheckFailure = new IOException("disk gone");

        JsonElement models = (await h.CallAsync(BridgeMethods.AppInfo)).GetProperty("result").GetProperty("models");

        Assert.False(models.GetProperty("ready").GetBoolean());
        Assert.Contains("disk gone", models.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FilePickResolvesToPathAndName()
    {
        using BridgeHarness h = new();
        string path = h.File("standup.m4a");
        h.Host.NextFile = path;

        JsonElement result = (await h.CallAsync(BridgeMethods.FilePick)).GetProperty("result");

        Assert.Equal(path, result.GetProperty("path").GetString());
        Assert.Equal("standup.m4a", result.GetProperty("name").GetString());
    }

    [Fact]
    public async Task FilePickResolvesToNullWhenCancelled()
    {
        using BridgeHarness h = new();
        h.Host.NextFile = null;

        JsonElement reply = await h.CallAsync(BridgeMethods.FilePick);

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task FilePickRefusesAnUnsupportedFileWithAnEventAndNull()
    {
        using BridgeHarness h = new();
        h.Host.NextFile = h.File("notes.pdf");

        JsonElement reply = await h.CallAsync(BridgeMethods.FilePick);

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
        string reason = Assert.Single(h.Host.Events(BridgeEvents.FileRefused)).GetProperty("reason").GetString()!;
        Assert.Contains("\"notes.pdf\"", reason);
        Assert.Contains(".m4a, .mp4 and .wav", reason);
    }

    [Fact]
    public async Task FolderPickPassesAnExistingInitialFolderAndResolvesThePath()
    {
        using BridgeHarness h = new();
        h.Host.NextFolder = h.Folder;

        JsonElement reply = await h.CallAsync(BridgeMethods.FolderPick, h.Folder);

        Assert.Equal(h.Folder, reply.GetProperty("result").GetString());
        Assert.Equal(h.Folder, h.Host.LastInitialFolder);
    }

    [Fact]
    public async Task FolderPickIgnoresAMissingInitialFolderAndResolvesNullWhenCancelled()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.FolderPick, Path.Combine(h.Folder, "gone"));

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
        Assert.Null(h.Host.LastInitialFolder);
    }

    [Fact]
    public async Task FolderOpenAsksTheHostToShowAnExistingFolder()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.FolderOpen, h.Folder);

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
        Assert.Equal(h.Folder, h.Host.OpenedFolder);
    }

    [Fact]
    public async Task FolderOpenRejectsAMissingFolderWithAWayOut()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.FolderOpen, Path.Combine(h.Folder, "gone"));

        string error = reply.GetProperty("error").GetString()!;
        Assert.Contains("does not exist", error);
        Assert.Null(h.Host.OpenedFolder);
    }

    [Fact]
    public async Task RunCancelWithNoRunResolvesNull()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.RunCancel);

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
    }
}
