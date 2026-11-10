// Writes replies and events as JSON envelopes and hands them to the host, never throwing: a failed
// post has nobody left to report to.
using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>The one place the bridge serialises an outgoing message.</summary>
internal sealed class BridgeEmitter
{
    /// <summary>A JSON null, the result of a method with nothing to return.</summary>
    public static readonly JsonElement Null = JsonDocument.Parse("null").RootElement.Clone();

    private readonly IHostServices _host;

    public BridgeEmitter(IHostServices host)
    {
        _host = host;
    }

    /// <summary>Posts a successful reply.</summary>
    public void Reply(long id, JsonElement result)
    {
        Post(JsonSerializer.Serialize(new BridgeReply { Id = id, Result = result }, BridgeJson.Context.BridgeReply));
    }

    /// <summary>Posts a rejection carrying a plain sentence.</summary>
    public void Reject(long id, string message)
    {
        Post(JsonSerializer.Serialize(new BridgeReply { Id = id, Error = message }, BridgeJson.Context.BridgeReply));
    }

    /// <summary>Posts an event with a typed payload.</summary>
    public void Event<T>(string name, T payload, JsonTypeInfo<T> info)
    {
        JsonElement element = JsonSerializer.SerializeToElement(payload, info);
        Post(JsonSerializer.Serialize(new BridgeEvent { Event = name, Payload = element }, BridgeJson.Context.BridgeEvent));
    }

    /// <summary>Posts the host:error event, which carries a code and nothing else.</summary>
    public void HostError(string code)
    {
        Event(BridgeEvents.HostError, new HostErrorPayload { Code = code }, BridgeJson.Context.HostErrorPayload);
    }

    private void Post(string json)
    {
        try
        {
            _host.Emit(json);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // The host could not deliver. Nothing can be reported through the same channel, so the
            // message is dropped; the page's pending call stays open until the user retries.
            System.Diagnostics.Debug.WriteLine("the host could not post a message: " + failure.Message);
        }
    }
}
