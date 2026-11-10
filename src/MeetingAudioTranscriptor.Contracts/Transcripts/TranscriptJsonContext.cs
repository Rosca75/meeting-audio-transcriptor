// The source-generated serialisation metadata for the JSON transcript, so the file is written
// and read without reflection and the trimmed build keeps working.
using System.Text.Json.Serialization;

namespace MeetingAudioTranscriptor.Contracts.Transcripts;

/// <summary>The generated metadata for <see cref="TranscriptDocument"/> and the records under it.</summary>
/// <remarks>
/// The naming, null and indentation options are set on the options object `TranscriptJson` hands to
/// this context, because the encoder cannot be named in an attribute and every option must live in one
/// place for the output to be byte-stable.
/// </remarks>
[JsonSerializable(typeof(TranscriptDocument))]
internal sealed partial class TranscriptJsonContext : JsonSerializerContext
{
}
