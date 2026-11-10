// A placeholder of the bridge contract, so the desktop hosts compile and can be driven before the
// real bridge exists: the signatures only, with an echoing router. The real bridge replaces this file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("meeting-audio-transcriptor")]

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>The window as the bridge sees it.</summary>
public interface IHostServices
{
    /// <summary>Posts one JSON envelope to the page; callable from any thread.</summary>
    void Emit(string json);

    /// <summary>The native open dialog filtered to .m4a, .mp4 and .wav; null when cancelled.</summary>
    Task<string?> PickAudioFileAsync();

    /// <summary>The native folder dialog; null when cancelled.</summary>
    Task<string?> PickFolderAsync(string? initialFolder);

    /// <summary>Shows the folder in the system file manager.</summary>
    void OpenFolder(string folderPath);
}

/// <summary>The adapter over the transcription service.</summary>
public interface ITranscriber
{
}

/// <summary>Simulated runs, selected when MAT_UI_DEMO=1.</summary>
internal sealed class DemoTranscriber : ITranscriber
{
}

/// <summary>The router from page messages to the application; this placeholder echoes.</summary>
public sealed class Bridge
{
    private readonly IHostServices _host;
    private readonly string _version;

    public Bridge(IHostServices host, ITranscriber transcriber, string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(transcriber);
        _host = host;
        _version = applicationVersion;
    }

    /// <summary>One message from the page; never throws; replies through the host.</summary>
    public void Receive(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("drop", out JsonElement drop))
            {
                _host.Emit(Write(writer =>
                {
                    writer.WriteString("event", "drop.received");
                    writer.WritePropertyName("payload");
                    drop.WriteTo(writer);
                }));
                return;
            }

            long id = root.GetProperty("id").GetInt64();
            string method = root.GetProperty("method").GetString() ?? "";
            _host.Emit(Write(writer =>
            {
                writer.WriteNumber("id", id);
                if (method == "app.info")
                {
                    writer.WriteStartObject("result");
                    writer.WriteString("name", "Meeting Audio Transcriptor");
                    writer.WriteString("version", _version);
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteString("error", "unknown method " + method);
                }
            }));
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _host.Emit("{\"event\":\"host.error\",\"payload\":{\"kind\":\"message_unreadable\"}}");
        }
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
