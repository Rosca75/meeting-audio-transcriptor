// The Linux desktop host: it will own the Linux window; for now it reports that it is not built yet.
using System;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>The Linux implementation of <see cref="IDesktopHost"/>.</summary>
internal sealed class LinuxDesktopHost : IDesktopHost
{
    /// <summary>The exit code for a host that is not implemented yet.</summary>
    private const int NotImplementedExitCode = 4;

    /// <inheritdoc />
    public int Run(string[] args)
    {
        Console.Error.WriteLine("the linux desktop window is not implemented yet; use meeting-audio-transcriptor-cli in the meantime");
        return NotImplementedExitCode;
    }
}
