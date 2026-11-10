// The binding from a method name to a surface method: each positional argument is read as its own
// type through the generated context, and the result is serialised into the reply.
using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>A switch over <see cref="BridgeMethods"/>, not reflection, so the trimmed build keeps working.</summary>
/// <remarks>
/// Adding a method means a constant in BridgeMethods, a case here, a wrapper in `frontend/js/api.js`
/// and a section in `frontend/BRIDGE.md`.
/// </remarks>
internal static class Dispatcher
{
    /// <summary>The start of the sentence for an unknown method name.</summary>
    public const string UnknownMethodPrefix = "unknown method ";

    /// <summary>Runs one call and returns its reply; never throws except for an out-of-memory failure.</summary>
    public static async Task<BridgeReply> InvokeAsync(BridgeSurface surface, BridgeCall call)
    {
        try
        {
            JsonElement result = await DispatchAsync(surface, call).ConfigureAwait(false);
            return new BridgeReply { Id = call.Id, Result = result };
        }
        catch (Exception failure) when (Faults.IsProgrammingFault(failure))
        {
            return new BridgeReply { Id = call.Id, Error = Faults.Sentence(call.Method, failure) };
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return new BridgeReply { Id = call.Id, Error = failure.Message };
        }
    }

    private static async Task<JsonElement> DispatchAsync(BridgeSurface surface, BridgeCall call)
    {
        BridgeJsonContext json = BridgeJson.Context;
        switch (call.Method)
        {
            case BridgeMethods.AppInfo:
                Expect(call, 0);
                return Result(surface.AppInfo(), json.AppInfoResult);
            case BridgeMethods.FilePick:
                Expect(call, 0);
                FileSelection? picked = await surface.PickFileAsync().ConfigureAwait(false);
                return picked is null ? BridgeEmitter.Null : Result(picked, json.FileSelection);
            case BridgeMethods.FilesDropped:
                surface.FilesDropped(Arg(call, 0, json.StringArray) ?? []);
                return BridgeEmitter.Null;
            case BridgeMethods.FolderPick:
                string? folder = await surface.PickFolderAsync(Arg(call, 0, json.String)).ConfigureAwait(false);
                return folder is null ? BridgeEmitter.Null : Result(folder, json.String);
            case BridgeMethods.FolderOpen:
                surface.OpenFolder(Arg(call, 0, json.String) ?? "");
                return BridgeEmitter.Null;
            case BridgeMethods.RunStart:
                surface.StartRun(Arg(call, 0, json.TranscribeRequest) ?? new TranscribeRequest());
                return BridgeEmitter.Null;
            case BridgeMethods.RunCancel:
                Expect(call, 0);
                surface.CancelRun();
                return BridgeEmitter.Null;
            default:
                throw new ArgumentException(UnknownMethodPrefix + call.Method);
        }
    }

    private static JsonElement Result<T>(T value, JsonTypeInfo<T> info)
    {
        return JsonSerializer.SerializeToElement(value, info);
    }

    private static void Expect(BridgeCall call, int count)
    {
        if (call.Args.Count != count)
        {
            throw new ArgumentException($"{call.Method} takes {count} argument(s) but the call carried {call.Args.Count}; the page and the host are out of step, so restart the application");
        }
    }

    // Reads one positional argument as its own type; a JSON null reads as null.
    private static T? Arg<T>(BridgeCall call, int index, JsonTypeInfo<T> info)
    {
        Expect(call, index + 1);
        try
        {
            return call.Args[index].Deserialize(info);
        }
        catch (JsonException)
        {
            throw new ArgumentException($"{call.Method} argument {index + 1} is not in the shape the host expects; the page and the host are out of step, so restart the application");
        }
    }
}
