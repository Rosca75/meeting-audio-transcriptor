// The envelopes that cross the bridge and the small payloads that ride in them. Property order is
// the wire order, and an absent optional property is omitted.
using System.Collections.Generic;
using System.Text.Json;

namespace MeetingAudioTranscriptor.Contracts.Bridge;

/// <summary>A call from the page: a request id, a method name and positional arguments.</summary>
public sealed record BridgeCall
{
    /// <summary>The page's request id, echoed in the reply.</summary>
    public long Id { get; init; }

    /// <summary>One of the names in the App's BridgeMethods.</summary>
    public string Method { get; init; } = "";

    /// <summary>The positional arguments, each bound to its own type.</summary>
    public IReadOnlyList<JsonElement> Args { get; init; } = [];
}

/// <summary>The reply to one call: a result or an error message.</summary>
public sealed record BridgeReply
{
    /// <summary>The id of the call this answers.</summary>
    public long Id { get; init; }

    /// <summary>The result; a JSON null for a method with nothing to return; absent on an error.</summary>
    public JsonElement? Result { get; init; }

    /// <summary>The actionable sentence of a rejected call; absent on success.</summary>
    public string? Error { get; init; }
}

/// <summary>An event the host pushes to the page.</summary>
public sealed record BridgeEvent
{
    /// <summary>One of the names in the App's BridgeEvents.</summary>
    public string Event { get; init; } = "";

    /// <summary>The event's payload.</summary>
    public JsonElement Payload { get; init; }
}

/// <summary>The resolved shape of the app.info method.</summary>
public sealed record AppInfoResult
{
    /// <summary>The application version shown in the footer.</summary>
    public string Version { get; init; } = "";

    /// <summary>The supported input extensions, lower case with the dot.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Whether the models are installed.</summary>
    public ModelCheckResult Models { get; init; } = new();
}

/// <summary>A chosen input file: the resolved shape of file.pick and the payload of file:selected.</summary>
public sealed record FileSelection
{
    /// <summary>The full path.</summary>
    public string Path { get; init; } = "";

    /// <summary>The file name without its folder.</summary>
    public string Name { get; init; } = "";

    /// <summary>When several files were dropped, a sentence saying which one was used; otherwise absent.</summary>
    public string? Note { get; init; }
}

/// <summary>The payload of file:refused.</summary>
public sealed record FileRefusal
{
    /// <summary>A sentence naming what was refused and what is supported.</summary>
    public string Reason { get; init; } = "";
}

/// <summary>The payload of host:error: a code only, because no content rides on a host failure.</summary>
public sealed record HostErrorPayload
{
    /// <summary>One of the codes in the App's HostErrorCodes.</summary>
    public string Code { get; init; } = "";
}

/// <summary>The message the host sends when a drop resolved to paths: <c>{"drop":{"paths":[...]}}</c>.</summary>
public sealed record DropMessage
{
    /// <summary>The resolved drop.</summary>
    public DropPaths? Drop { get; init; }
}

/// <summary>The paths a drop resolved to.</summary>
public sealed record DropPaths
{
    /// <summary>Full paths, in the order dropped.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}
