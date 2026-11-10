// The guards' view of the repository: where its root is and which files each guard reads.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MeetingAudioTranscriptor.Guards.Tests;

/// <summary>Locates the repository root and lists its files, skipping build output and fetched data.</summary>
internal static class RepositoryFiles
{
    /// <summary>The folder names no guard reads: build output, fetched data and caches.</summary>
    private static readonly string[] SkippedFolders =
    [
        "bin", "obj", ".git", "node_modules", "models", "spike-venv", "artifacts", "publish",
    ];

    /// <summary>The repository root: the nearest folder above the test's output holding the solution file.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Every file under the repository whose name matches one of the patterns, minus skipped folders.</summary>
    /// <param name="relativeFolder">The folder to read, relative to the root; empty for the whole repository.</param>
    /// <param name="patterns">File name patterns such as "*.cs".</param>
    public static IReadOnlyList<string> Under(string relativeFolder, params string[] patterns)
    {
        string start = Path.Combine(Root, relativeFolder);
        if (!Directory.Exists(start))
        {
            return [];
        }

        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(start, pattern, SearchOption.AllDirectories))
            .Where(path => !IsSkipped(path))
            .Distinct()
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The path relative to the root with forward slashes, for readable failure messages.</summary>
    public static string Relative(string path)
    {
        return Path.GetRelativePath(Root, path).Replace('\\', '/');
    }

    private static bool IsSkipped(string path)
    {
        string[] parts = Path.GetRelativePath(Root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(part => SkippedFolders.Contains(part, StringComparer.Ordinal)))
        {
            return true;
        }

        // bench/data and bench/.cache hold downloaded corpora and caches, never source.
        return parts.Length > 2 && parts[0] == "bench" && (parts[1] == "data" || parts[1] == ".cache");
    }

    private static string FindRoot()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (folder.EnumerateFiles("*.slnx").Any())
            {
                return folder.FullName;
            }

            folder = folder.Parent;
        }

        throw new InvalidOperationException(
            "no .slnx file was found above " + AppContext.BaseDirectory + "; run the guards from a checkout of the repository");
    }
}
