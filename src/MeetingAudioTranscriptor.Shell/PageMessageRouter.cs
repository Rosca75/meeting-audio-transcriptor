// The first stop of every message the page posts to the host: automation messages are answered by
// the host itself while a test drives it, and everything else goes to the bridge untouched.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>What a host does for each automation message; the router decides which one applies.</summary>
internal interface IAutomationActions
{
    /// <summary>Closes the window and ends the process with this code.</summary>
    void Exit(int code);

    /// <summary>Writes one line to standard output, where the test reads it.</summary>
    void Log(string line);

    /// <summary>Hands one envelope back to the page through the host's normal event path.</summary>
    void Emit(string json);

    /// <summary>Feeds URIs to the same handler a native drop on the window reaches.</summary>
    void Drop(IReadOnlyList<string> uris);
}

/// <summary>
/// Routes one page message. While automation is enabled, a message whose top-level object has an
/// <c>automation</c> member is the host's own:
/// <c>{"automation":{"exit":0}}</c> ends the process with that code,
/// <c>{"automation":{"log":"..."}}</c> prints the line,
/// <c>{"automation":{"echo":"..."}}</c> posts <c>{"automation":{"echo":"..."}}</c> back to the page, and
/// <c>{"automation":{"dropUris":["file:///..."]}}</c> runs the native drop handler on those URIs.
/// Without automation, and for every other message, the message goes to the bridge as it arrived.
/// </summary>
internal sealed class PageMessageRouter
{
    private const string AutomationMarker = "\"automation\"";

    private readonly Action<string> _bridge;
    private readonly IAutomationActions _actions;
    private readonly bool _automationEnabled;

    public PageMessageRouter(Action<string> bridge, IAutomationActions actions, bool automationEnabled)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(actions);
        _bridge = bridge;
        _actions = actions;
        _automationEnabled = automationEnabled;
    }

    /// <summary>Routes one message; returns true when the host answered it itself.</summary>
    public bool Route(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The cheap test first, so an ordinary bridge message is parsed by the bridge alone.
        if (_automationEnabled && message.Contains(AutomationMarker, StringComparison.Ordinal) && TryAutomation(message))
        {
            return true;
        }

        _bridge(message);
        return false;
    }

    private bool TryAutomation(string message)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("automation", out JsonElement automation)
                || automation.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (automation.TryGetProperty("log", out JsonElement line) && line.ValueKind == JsonValueKind.String)
            {
                _actions.Log("automation: log " + line.GetString());
            }

            if (automation.TryGetProperty("echo", out JsonElement echo) && echo.ValueKind == JsonValueKind.String)
            {
                _actions.Emit(EchoEnvelope(echo.GetString() ?? ""));
            }

            if (automation.TryGetProperty("dropUris", out JsonElement uris) && uris.ValueKind == JsonValueKind.Array)
            {
                List<string> list = [];
                foreach (JsonElement uri in uris.EnumerateArray())
                {
                    if (uri.ValueKind == JsonValueKind.String)
                    {
                        list.Add(uri.GetString() ?? "");
                    }
                }

                _actions.Drop(list);
            }

            if (automation.TryGetProperty("exit", out JsonElement exit) && exit.ValueKind == JsonValueKind.Number && exit.TryGetInt32(out int code))
            {
                _actions.Exit(code);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The envelope an echo answers with: the same text, re-encoded by the host.</summary>
    private static string EchoEnvelope(string text)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("automation");
            writer.WriteString("echo", text);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
