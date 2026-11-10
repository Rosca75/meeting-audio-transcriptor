// The seam between the shell's entry point and the platform's window: one interface, one
// implementation per platform.
namespace MeetingAudioTranscriptor.Shell;

/// <summary>A desktop host owns the window for one platform and runs until it closes.</summary>
internal interface IDesktopHost
{
    /// <summary>Runs the window and returns the process exit code.</summary>
    /// <param name="args">The command-line arguments the process started with.</param>
    int Run(string[] args);
}
