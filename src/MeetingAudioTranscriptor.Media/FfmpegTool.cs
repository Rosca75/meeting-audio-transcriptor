// The name of the external decoder the media project starts as a child process.
namespace MeetingAudioTranscriptor.Media;

/// <summary>Names the ffmpeg executable, so the lookup and the packaging agree on it.</summary>
internal static class FfmpegTool
{
    /// <summary>The executable's name without a platform extension.</summary>
    public const string ExecutableStem = "ffmpeg";
}
