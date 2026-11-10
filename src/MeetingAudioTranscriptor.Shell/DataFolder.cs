// The one folder the desktop application writes to on its own: the web view's profile and cache
// live under it, per user, and nothing else is written outside the files the user saves.
using System;
using System.IO;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>Resolves the per-user data folder on each operating system.</summary>
internal static class DataFolder
{
    /// <summary>The folder name under the platform's per-user data location.</summary>
    public const string ApplicationFolderName = "meeting-audio-transcriptor";

    /// <summary>The data folder of this process's user on this operating system.</summary>
    public static string Current() =>
        Resolve(
            OperatingSystem.IsWindows(),
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// The data folder for a platform: <c>%LOCALAPPDATA%\meeting-audio-transcriptor</c> on Windows,
    /// and <c>$XDG_DATA_HOME/meeting-audio-transcriptor</c> on Linux, falling back to
    /// <c>~/.local/share</c> when the variable is unset, empty or relative, as the XDG base directory
    /// rules require.
    /// </summary>
    /// <param name="windows">True for the Windows layout.</param>
    /// <param name="environment">Reads one environment variable; null when it is unset.</param>
    /// <param name="home">The user's home folder.</param>
    public static string Resolve(bool windows, Func<string, string?> environment, string home)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(home);

        if (windows)
        {
            string? local = environment("LOCALAPPDATA");
            if (string.IsNullOrEmpty(local))
            {
                local = Path.Combine(home, "AppData", "Local");
            }

            return Path.Combine(local, ApplicationFolderName);
        }

        string? dataHome = environment("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) || !Path.IsPathRooted(dataHome))
        {
            dataHome = Path.Combine(home, ".local", "share");
        }

        return Path.Combine(dataHome, ApplicationFolderName);
    }

    /// <summary>The web view's profile folder under a data folder; the one sub-folder the shell creates.</summary>
    public static string WebViewProfile(string dataFolder) => Path.Combine(dataFolder, "webview");
}
