// The desktop entry point: it picks the host compiled for this platform, reads the test hook and
// runs the window with the bridge the composition root builds.
using System;

namespace MeetingAudioTranscriptor.Shell;

internal static class Program
{
    /// <summary>
    /// The entry point. The single-threaded apartment is what the Windows web view and the system
    /// dialogs require of the thread that owns the window; elsewhere the attribute has no effect.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        _ = args;

#if SHELL_WINDOWS
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("this build of meeting-audio-transcriptor is the Windows one; install the Linux build on this machine");
            return ShellExitCodes.UnsupportedPlatform;
        }
#elif SHELL_LINUX
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("this build of meeting-audio-transcriptor is the Linux one; install the build made for this operating system");
            return ShellExitCodes.UnsupportedPlatform;
        }
#endif

        AutomationOptions automation;
        try
        {
            automation = AutomationOptions.FromEnvironment(Environment.GetEnvironmentVariable);
        }
        catch (InvalidOperationException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return ShellExitCodes.StartupFailed;
        }

        return CreateHost().Run(ShellComposition.CreateBridge, automation);
    }

    /// <summary>The host for the platform this build targets; the project defines exactly one constant.</summary>
    private static IDesktopHost CreateHost()
    {
#if SHELL_WINDOWS
        return new Windows.WindowsDesktopHost();
#elif SHELL_LINUX
        return new Linux.LinuxDesktopHost();
#else
#error The shell project must define SHELL_WINDOWS or SHELL_LINUX through its ShellHost property.
#endif
    }
}
