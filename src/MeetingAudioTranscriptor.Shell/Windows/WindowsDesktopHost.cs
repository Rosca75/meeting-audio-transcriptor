// The Windows desktop host: it will own the WebView2 window; for now it reports that it is not built yet.
using System;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>The Windows implementation of <see cref="IDesktopHost"/>.</summary>
internal sealed class WindowsDesktopHost : IDesktopHost
{
    /// <summary>The exit code for a host that is not implemented yet.</summary>
    private const int NotImplementedExitCode = 4;

    /// <inheritdoc />
    public int Run(string[] args)
    {
        Console.Error.WriteLine("the windows desktop window is not implemented yet; use meeting-audio-transcriptor-cli in the meantime");
        return NotImplementedExitCode;
    }
}
