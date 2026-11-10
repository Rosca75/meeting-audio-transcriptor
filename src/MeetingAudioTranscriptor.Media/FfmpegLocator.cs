// Finds the ffmpeg executable: an explicit override, then the copy bundled beside the application, then the PATH.
using System;
using System.Collections.Generic;
using System.IO;

namespace MeetingAudioTranscriptor.Media;

/// <summary>Where a located ffmpeg came from, for diagnostics and the transcript metadata.</summary>
public enum FfmpegOrigin
{
    /// <summary>The path named by the <c>MAT_FFMPEG</c> environment variable.</summary>
    EnvironmentVariable,

    /// <summary>The copy shipped in the <c>ffmpeg</c> folder beside the application.</summary>
    Bundled,

    /// <summary>The first ffmpeg found on the PATH.</summary>
    SearchPath,
}

/// <summary>A located ffmpeg executable and how it was found.</summary>
public sealed record FfmpegLocation(string ExecutablePath, FfmpegOrigin Origin)
{
    /// <summary>A short description such as <c>ffmpeg (bundled) at /opt/app/ffmpeg/ffmpeg</c>.</summary>
    public override string ToString()
    {
        string origin = Origin switch
        {
            FfmpegOrigin.EnvironmentVariable => FfmpegLocator.EnvironmentVariable,
            FfmpegOrigin.Bundled => "bundled",
            _ => "PATH",
        };
        return $"ffmpeg ({origin}) at {ExecutablePath}";
    }
}

/// <summary>Finds the ffmpeg executable the decoder starts.</summary>
/// <remarks>
/// The order is the order of intent: an explicit override is a developer or an administrator saying
/// which binary to use; the bundled copy is the one the release was tested with; a PATH copy is the
/// fallback for a source build. The environment, the base directory and the file system are injected,
/// so every branch is tested without touching the machine's own PATH.
/// </remarks>
public sealed class FfmpegLocator
{
    /// <summary>The environment variable that names an ffmpeg executable explicitly.</summary>
    public const string EnvironmentVariable = "MAT_FFMPEG";

    /// <summary>The folder beside the application that holds the bundled ffmpeg.</summary>
    public const string BundledFolder = "ffmpeg";

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _baseDirectory;
    private readonly bool _isWindows;
    private readonly Func<string, bool> _fileExists;

    /// <summary>Creates a locator over this process's environment and application folder.</summary>
    public FfmpegLocator()
        : this(Environment.GetEnvironmentVariable, AppContext.BaseDirectory, OperatingSystem.IsWindows(), File.Exists)
    {
    }

    /// <summary>Creates a locator over an injected environment, application folder and file system.</summary>
    internal FfmpegLocator(Func<string, string?> getEnvironmentVariable, string baseDirectory, bool isWindows, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);
        _getEnvironmentVariable = getEnvironmentVariable;
        _baseDirectory = baseDirectory;
        _isWindows = isWindows;
        _fileExists = fileExists;
    }

    /// <summary>The executable's file name on this platform.</summary>
    public string ExecutableName => _isWindows ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>The path the bundled copy is expected at.</summary>
    public string BundledPath => Path.Combine(_baseDirectory, BundledFolder, ExecutableName);

    /// <summary>Returns the first ffmpeg found, or null when there is none.</summary>
    public FfmpegLocation? Locate()
    {
        string? explicitPath = _getEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath) && _fileExists(explicitPath.Trim()))
        {
            return new FfmpegLocation(explicitPath.Trim(), FfmpegOrigin.EnvironmentVariable);
        }

        if (_fileExists(BundledPath))
        {
            return new FfmpegLocation(BundledPath, FfmpegOrigin.Bundled);
        }

        foreach (string directory in SearchPathDirectories())
        {
            string candidate = Path.Combine(directory, ExecutableName);
            if (_fileExists(candidate))
            {
                return new FfmpegLocation(candidate, FfmpegOrigin.SearchPath);
            }
        }

        return null;
    }

    /// <summary>Says where the search looked, for an error message when nothing was found.</summary>
    public string DescribeSearch()
    {
        List<string> places = [];
        string? explicitPath = _getEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            places.Add($"{EnvironmentVariable}={explicitPath.Trim()} (no such file)");
        }

        places.Add(BundledPath);
        places.Add($"every folder on the PATH for {ExecutableName}");
        return string.Join("; ", places);
    }

    /// <summary>The folders of the PATH variable, in order, skipping empty entries.</summary>
    private IEnumerable<string> SearchPathDirectories()
    {
        string? path = _getEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        char separator = _isWindows ? ';' : ':';
        foreach (string entry in path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Windows allows a PATH entry in quotes, which is not part of the folder name.
            string directory = entry.Trim('"');
            if (directory.Length > 0)
            {
                yield return directory;
            }
        }
    }
}
