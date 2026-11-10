// Where the suite finds the repository, the lock and the installed models, and how it writes temporary files.
using System;
using System.IO;

namespace MeetingAudioTranscriptor.Inference.Tests;

/// <summary>Locations the tests share.</summary>
internal static class TestPaths
{
    /// <summary>The repository root: the nearest folder above the test's output holding the solution file.</summary>
    public static string RepositoryRoot { get; } = FindRoot();

    /// <summary>The model lock the fetch tool and the catalog both read.</summary>
    public static string ModelLock => Path.Combine(RepositoryRoot, "build", "models.lock");

    /// <summary>The models folder the models-tier tests read: MAT_MODELS_DIR, else the repository's models folder.</summary>
    public static string ModelsDirectory => ModelCatalog.ResolveRoot();

    /// <summary>The catalog over <see cref="ModelsDirectory"/> and the repository's lock.</summary>
    public static ModelCatalog Catalog() => ModelCatalog.FromLockFile(ModelsDirectory, ModelLock);

    /// <summary>Writes bytes to a new file in a fresh temporary folder and returns its path.</summary>
    public static string WriteTemporary(string fileName, byte[] bytes)
    {
        string folder = Path.Combine(Path.GetTempPath(), "mat-inference-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MeetingAudioTranscriptor.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("the repository root (the folder holding MeetingAudioTranscriptor.slnx) was not found above the test output");
    }
}
