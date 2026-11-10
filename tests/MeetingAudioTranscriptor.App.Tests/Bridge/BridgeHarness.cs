// The test harness for the bridge: a fake window host that records every message, a scriptable fake
// transcriber, and helpers that send a call and read its reply.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Tests.Bridge;

/// <summary>A host that records every posted envelope and answers dialogs from a script.</summary>
internal sealed class FakeHost : IHostServices
{
    private readonly object _gate = new();
    private readonly List<JsonElement> _messages = [];

    public string? NextFile { get; set; }

    public string? NextFolder { get; set; }

    public string? LastInitialFolder { get; private set; }

    public string? OpenedFolder { get; private set; }

    public Exception? DialogFailure { get; set; }

    public void Emit(string json)
    {
        lock (_gate)
        {
            _messages.Add(JsonDocument.Parse(json).RootElement.Clone());
        }
    }

    public Task<string?> PickAudioFileAsync()
    {
        return DialogFailure is null ? Task.FromResult(NextFile) : Task.FromException<string?>(DialogFailure);
    }

    public Task<string?> PickFolderAsync(string? initialFolder)
    {
        LastInitialFolder = initialFolder;
        return DialogFailure is null ? Task.FromResult(NextFolder) : Task.FromException<string?>(DialogFailure);
    }

    public void OpenFolder(string folderPath)
    {
        OpenedFolder = folderPath;
    }

    public List<JsonElement> Messages()
    {
        lock (_gate)
        {
            return [.. _messages];
        }
    }

    /// <summary>The payloads of every event with this name, in the order posted.</summary>
    public List<JsonElement> Events(string name)
    {
        return Messages()
            .Where(m => m.TryGetProperty("event", out JsonElement e) && e.GetString() == name)
            .Select(m => m.GetProperty("payload"))
            .ToList();
    }

    /// <summary>Waits until at least one event of this name was posted, and returns its payload.</summary>
    public async Task<JsonElement> WaitForEventAsync(string name)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            List<JsonElement> found = Events(name);
            if (found.Count > 0)
            {
                return found[0];
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"got no '{name}' event within 10 seconds, wanted one; messages: {string.Join(" | ", Messages().Select(m => m.GetRawText()))}");
    }

    public JsonElement ReplyTo(long id)
    {
        return Messages().Single(m => m.TryGetProperty("id", out JsonElement i) && i.TryGetInt64(out long v) && v == id);
    }
}

/// <summary>A transcriber whose run is a delegate each test supplies.</summary>
internal sealed class FakeTranscriber : ITranscriber
{
    public ModelCheckResult Models { get; set; } = new() { Ready = true };

    public Exception? CheckFailure { get; set; }

    public Func<TranscribeRequest, IProgress<TranscribeProgress>, CancellationToken, Task<TranscribeOutcome>> Run { get; set; } =
        (_, _, _) => Task.FromResult(new TranscribeOutcome { Kind = TranscribeOutcomeKinds.Completed });

    public TranscribeRequest? LastRequest { get; private set; }

    public ModelCheckResult CheckModels()
    {
        return CheckFailure is null ? Models : throw CheckFailure;
    }

    public Task<TranscribeOutcome> RunAsync(TranscribeRequest request, IProgress<TranscribeProgress> progress, CancellationToken cancellation)
    {
        LastRequest = request;
        return Run(request, progress, cancellation);
    }
}

/// <summary>A bridge over fakes, a scratch folder with sample recordings, and call helpers.</summary>
internal sealed class BridgeHarness : IDisposable
{
    private long _nextId = 1;

    public BridgeHarness(TimeProvider? clock = null)
    {
        Folder = Directory.CreateTempSubdirectory("mat-bridge-").FullName;
        Bridge = new MeetingAudioTranscriptor.App.Bridge.Bridge(Host, Transcriber, "1.2.3", clock ?? TimeProvider.System);
    }

    public FakeHost Host { get; } = new();

    public FakeTranscriber Transcriber { get; } = new();

    public MeetingAudioTranscriptor.App.Bridge.Bridge Bridge { get; }

    public string Folder { get; }

    /// <summary>Creates an empty file in the scratch folder and returns its full path.</summary>
    public string File(string name)
    {
        string path = Path.Combine(Folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    /// <summary>A valid request for a file made by <see cref="File"/>.</summary>
    public TranscribeRequest Request(string? path = null)
    {
        return new TranscribeRequest
        {
            InputPath = path ?? File("meeting.wav"),
            Language = "en",
            Formats = ["vtt", "json"],
            SpeakerSeparation = true,
        };
    }

    /// <summary>Sends a call with raw JSON arguments and returns the whole reply envelope.</summary>
    public async Task<JsonElement> CallRawAsync(string method, string argsJson)
    {
        long id = _nextId++;
        await Bridge.ReceiveAsync($"{{\"id\":{id},\"method\":\"{method}\",\"args\":{argsJson}}}");
        return Host.ReplyTo(id);
    }

    /// <summary>Sends a call with arguments serialised from objects.</summary>
    public Task<JsonElement> CallAsync(string method, params object?[] args)
    {
        string json = "[" + string.Join(",", args.Select(Serialise)) + "]";
        return CallRawAsync(method, json);
    }

    public void Dispose()
    {
        Directory.Delete(Folder, recursive: true);
    }

    private static string Serialise(object? value)
    {
        return value switch
        {
            null => "null",
            TranscribeRequest r => JsonSerializer.Serialize(r, BridgeJson.Context.TranscribeRequest),
            string[] a => JsonSerializer.Serialize(a, BridgeJson.Context.StringArray),
            string s => JsonSerializer.Serialize(s, BridgeJson.Context.String),
            _ => throw new ArgumentException("the harness cannot serialise " + value.GetType().Name),
        };
    }
}
