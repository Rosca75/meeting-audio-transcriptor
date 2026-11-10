// The desktop executable's exit codes, so a launcher, a test or a support request can tell the
// outcomes apart without reading a message.
namespace MeetingAudioTranscriptor.Shell;

/// <summary>The process exit codes of the desktop executable.</summary>
internal static class ShellExitCodes
{
    /// <summary>The window was closed normally.</summary>
    public const int Ok = 0;

    /// <summary>The web view runtime, the windowing libraries or the display are missing.</summary>
    public const int RuntimeMissing = 2;

    /// <summary>The window or the web view could not be set up.</summary>
    public const int StartupFailed = 3;

    /// <summary>This build's host does not match the operating system it runs on.</summary>
    public const int UnsupportedPlatform = 4;
}
