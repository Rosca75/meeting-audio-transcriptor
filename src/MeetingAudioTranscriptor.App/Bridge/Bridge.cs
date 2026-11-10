// The router between the page and the bound surface: parses each message, dispatches it and
// replies through the host, never letting a fault escape to the host's message loop.
using System;
using System.Text.Json;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Receives the page's messages and answers them through <see cref="IHostServices"/>.</summary>
/// <remarks>
/// A message is a call (<c>{id, method, args}</c>) or a resolved drop (<c>{drop: {paths}}</c>).
/// <see cref="Receive"/> hands the work to the thread pool and returns at once, so the window's
/// interface thread never waits on a dialog or a run. A message that cannot be read is reported
/// through the <c>host:error</c> event, and a call with a readable id is always answered, so no
/// promise on the page waits forever.
/// </remarks>
public sealed class Bridge
{
    private readonly BridgeEmitter _emitter;
    private readonly BridgeSurface _surface;

    /// <summary>Creates the bridge over a host, a transcriber and the version the footer shows.</summary>
    public Bridge(IHostServices host, ITranscriber transcriber, string applicationVersion)
        : this(host, transcriber, applicationVersion, TimeProvider.System)
    {
    }

    internal Bridge(IHostServices host, ITranscriber transcriber, string applicationVersion, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(transcriber);
        ArgumentNullException.ThrowIfNull(applicationVersion);
        _emitter = new BridgeEmitter(host);
        _surface = new BridgeSurface(host, transcriber, applicationVersion, _emitter, clock);
    }

    /// <summary>One message from the page or from the host's drop handling; never throws.</summary>
    public void Receive(string json)
    {
        _ = Task.Run(() => ReceiveAsync(json));
    }

    /// <summary>The awaitable form of <see cref="Receive"/>, used by tests to wait for the reply.</summary>
    internal async Task ReceiveAsync(string json)
    {
        long? id = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json ?? "");
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _emitter.HostError(HostErrorCodes.MessageUnreadable);
                return;
            }

            if (root.TryGetProperty("method", out _))
            {
                if (root.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt64(out long parsed))
                {
                    id = parsed;
                }

                await AnswerCallAsync(root, id).ConfigureAwait(false);
            }
            else if (root.TryGetProperty("drop", out JsonElement drop))
            {
                // The page's own {drop:true} marker is for the host's native handling; only the
                // resolved form with paths concerns the bridge.
                if (drop.ValueKind == JsonValueKind.Object)
                {
                    DropMessage message = root.Deserialize(BridgeJson.Context.DropMessage) ?? new DropMessage();
                    _surface.HandleDrop(message);
                }
            }
            else
            {
                _emitter.HostError(HostErrorCodes.MessageUnreadable);
            }
        }
        catch (JsonException)
        {
            // A message that is not JSON has no id to answer; the host:error event is the only report.
            _emitter.HostError(HostErrorCodes.MessageUnreadable);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // A fault in the bridge itself: the user gets a generic sentence and a code, never the detail.
            _emitter.HostError(HostErrorCodes.InternalError);
            if (id is { } known)
            {
                _emitter.Reject(known, "The request failed unexpectedly (" + failure.GetType().Name + "). Try again, and if it keeps failing restart the application.");
            }
        }
    }

    private async Task AnswerCallAsync(JsonElement root, long? id)
    {
        BridgeCall? call = null;
        try
        {
            call = root.Deserialize(BridgeJson.Context.BridgeCall);
        }
        catch (JsonException)
        {
            // Falls through to the rejection below, which still answers a readable id.
        }

        if (call is null || id is null)
        {
            _emitter.HostError(HostErrorCodes.MessageUnreadable);
            if (id is { } known)
            {
                _emitter.Reject(known, "The request could not be read. Restart the application; if it keeps failing the page and the host are out of step.");
            }

            return;
        }

        BridgeReply reply = await Dispatcher.InvokeAsync(_surface, call).ConfigureAwait(false);
        if (reply.Error is not null)
        {
            _emitter.Reject(reply.Id, reply.Error);
        }
        else
        {
            _emitter.Reply(reply.Id, reply.Result ?? BridgeEmitter.Null);
        }
    }
}
