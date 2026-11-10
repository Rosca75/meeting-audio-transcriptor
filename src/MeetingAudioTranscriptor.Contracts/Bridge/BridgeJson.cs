// The one source-generated serialisation context for every shape the bridge sends or reads, so the
// wire format is declared once and the trimmed build needs no reflection.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingAudioTranscriptor.Contracts.Bridge;

/// <summary>The generated metadata for every bridge shape.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(BridgeCall))]
[JsonSerializable(typeof(BridgeReply))]
[JsonSerializable(typeof(BridgeEvent))]
[JsonSerializable(typeof(TranscribeRequest))]
[JsonSerializable(typeof(TranscribeProgress))]
[JsonSerializable(typeof(TranscribeOutcome))]
[JsonSerializable(typeof(ModelCheckResult))]
[JsonSerializable(typeof(AppInfoResult))]
[JsonSerializable(typeof(FileSelection))]
[JsonSerializable(typeof(FileRefusal))]
[JsonSerializable(typeof(HostErrorPayload))]
[JsonSerializable(typeof(DropMessage))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
public sealed partial class BridgeJsonContext : JsonSerializerContext
{
}

/// <summary>Holds the one configured instance of <see cref="BridgeJsonContext"/>.</summary>
/// <remarks>
/// The relaxed encoder writes non-ASCII text literally, so a French or German file name stays readable
/// on the wire; the encoder cannot be named in an attribute, so the options are built here.
/// </remarks>
public static class BridgeJson
{
    /// <summary>The context every bridge read and write goes through.</summary>
    public static BridgeJsonContext Context { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
