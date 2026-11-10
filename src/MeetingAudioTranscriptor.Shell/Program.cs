// The desktop entry point: it picks the host compiled for this platform and runs it.
namespace MeetingAudioTranscriptor.Shell;

internal static class Program
{
    private static int Main(string[] args)
    {
        return CreateHost().Run(args);
    }

    /// <summary>The host for the platform this build targets; the project defines exactly one constant.</summary>
    private static IDesktopHost CreateHost()
    {
#if SHELL_WINDOWS
        return new WindowsDesktopHost();
#elif SHELL_LINUX
        return new LinuxDesktopHost();
#else
#error The shell project must define SHELL_WINDOWS or SHELL_LINUX through its ShellHost property.
#endif
    }
}
