// The seam between the shell's entry point and the platform's window: one interface, one
// implementation per platform.
using System;
using MeetingAudioTranscriptor.App.Bridge;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>A desktop host owns the window for one platform and runs until it closes.</summary>
internal interface IDesktopHost
{
    /// <summary>Runs the window and returns the process exit code (see <see cref="ShellExitCodes"/>).</summary>
    /// <param name="createBridge">Builds the bridge over the host's window services, once they exist.</param>
    /// <param name="automation">What a test asked the host to do; <see cref="AutomationOptions.None"/> when shipped.</param>
    int Run(Func<IHostServices, Bridge> createBridge, AutomationOptions automation);
}
