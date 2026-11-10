// The name a decoder gives itself, so the opener can report which one decoded a file.
using MeetingAudioTranscriptor.Engine.Audio;

namespace MeetingAudioTranscriptor.Media;

/// <summary>An audio source that says which decoder produced it.</summary>
/// <remarks>The name reaches the transcript metadata and the logs, which is where a decoding difference between two machines is first noticed.</remarks>
internal interface INamedAudioSource : IAudioSource
{
    /// <summary>A short name such as <c>media-foundation</c> or <c>ffmpeg (bundled) at /opt/app/ffmpeg/ffmpeg</c>.</summary>
    string DecoderName { get; }
}
