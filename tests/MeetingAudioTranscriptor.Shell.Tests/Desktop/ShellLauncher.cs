// Starts the built desktop executable under a virtual display with an automation script, and
// collects its exit code and its output for a test to read.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests.Desktop;

/// <summary>What one run of the window produced.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Output">Standard output, one entry per line.</param>
/// <param name="Error">Standard error, as one text.</param>
internal sealed record ShellRun(int ExitCode, IReadOnlyList<string> Output, string Error)
{
    /// <summary>The lines the page logged through <c>{"automation":{"log":...}}</c>, without their prefix.</summary>
    public IReadOnlyList<string> PageLog =>
        Output.Where(line => line.StartsWith("automation: log ", StringComparison.Ordinal)).Select(line => line["automation: log ".Length..]).ToList();

    /// <summary>Everything the run printed, for a failure message.</summary>
    public string Transcript => "stdout:\n" + string.Join("\n", Output) + "\nstderr:\n" + Error;
}

/// <summary>Finds the built shell and runs it, under xvfb-run unless asked otherwise.</summary>
internal static class ShellLauncher
{
    /// <summary>The variable that points the suite at a shell executable elsewhere (a trimmed publish, say).</summary>
    public const string ShellPathVariable = "MAT_SHELL_PATH";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    private static readonly string[] Configurations = ["Debug", "Release"];

    /// <summary>
    /// Skips the calling test unless the Linux window can run here: Linux, xvfb-run on the PATH,
    /// WebKitGTK 4.1 installed and the shell built.
    /// </summary>
    public static string RequireLinuxWindow()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "needs Linux: the GTK window runs on Linux only; run the Windows checklist on Windows");
        Assert.SkipUnless(OnPath("xvfb-run"), "needs xvfb-run: install it with sudo apt install xvfb");
        Assert.SkipUnless(
            NativeLibrary.TryLoad("libwebkit2gtk-4.1.so.0", out _) && NativeLibrary.TryLoad("libgtk-3.so.0", out _),
            "needs WebKitGTK 4.1 and GTK 3: install them with sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0");
        string? shell = FindShell();
        Assert.SkipUnless(shell is not null, "needs the built shell: run dotnet build src/MeetingAudioTranscriptor.Shell first, or set " + ShellPathVariable);
        return shell!;
    }

    /// <summary>Runs the shell with the script and waits for it to end.</summary>
    /// <param name="shell">The executable.</param>
    /// <param name="script">The automation script's text, or null for none.</param>
    /// <param name="virtualDisplay">False to run without xvfb-run and without a display.</param>
    /// <param name="environment">Variables set after the defaults, so a test can replace one (MAT_UI_DEMO=0 runs the real transcriber); null for none.</param>
    /// <param name="limit">How long the window may run before it is killed; null for two minutes.</param>
    public static async Task<ShellRun> RunAsync(string shell, string? script, bool virtualDisplay = true, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? limit = null)
    {
        string scratch = Directory.CreateTempSubdirectory("mat-shell-").FullName;
        try
        {
            ProcessStartInfo start = new(virtualDisplay ? "xvfb-run" : shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (virtualDisplay)
            {
                start.ArgumentList.Add("-a");
                start.ArgumentList.Add(shell);
            }
            else
            {
                start.Environment.Remove("DISPLAY");
                start.Environment.Remove("WAYLAND_DISPLAY");
            }

            // The app host finds the runtime this suite runs on; the web view's data goes to a scratch
            // folder, so a test never touches the developer's own profile.
            start.Environment["DOTNET_ROOT"] = RuntimeRoot();
            start.Environment["XDG_DATA_HOME"] = Path.Combine(scratch, "data");
            start.Environment["MAT_UI_DEMO"] = "1";
            start.Environment[AutomationOptions.ExitAfterVariable] = "60000";
            foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
            {
                start.Environment[name] = value;
            }

            if (script is not null)
            {
                string path = Path.Combine(scratch, "automation.js");
                await File.WriteAllTextAsync(path, script);
                start.Environment[AutomationOptions.ScriptVariable] = path;
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("the shell could not be started: " + shell);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task exited = process.WaitForExitAsync();
            if (await Task.WhenAny(exited, Task.Delay(limit ?? Limit)) != exited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            string[] lines = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return new ShellRun(process.ExitCode, lines, await error);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>The shell named by the variable, or the newest one the build left under the shell project.</summary>
    private static string? FindShell()
    {
        string? named = Environment.GetEnvironmentVariable(ShellPathVariable);
        if (!string.IsNullOrEmpty(named))
        {
            return File.Exists(named) ? named : null;
        }

        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "MeetingAudioTranscriptor.slnx")))
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            return null;
        }

        string bin = Path.Combine(folder.FullName, "src", "MeetingAudioTranscriptor.Shell", "bin");
        if (!Directory.Exists(bin))
        {
            return null;
        }

        return Configurations
            .Select(configuration => Path.Combine(bin, configuration, "net10.0", "meeting-audio-transcriptor"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>The .NET root this suite runs on: three folders above the shared framework folder.</summary>
    private static string RuntimeRoot() =>
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));

    private static bool OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, program)));
}
