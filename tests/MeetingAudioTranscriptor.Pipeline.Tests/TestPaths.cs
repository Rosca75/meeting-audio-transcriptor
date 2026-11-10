// Where the suite finds the repository, the installed models and the benchmark audio, and how a test skips when one is missing.
using System;
using System.IO;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests;

/// <summary>Locations the tests share.</summary>
internal static class TestPaths
{
    /// <summary>The environment variable that points the suite at a benchmark data folder elsewhere.</summary>
    public const string BenchDataVariable = "MAT_BENCH_DATA";

    /// <summary>The repository root: the nearest folder above the test's output holding the solution file.</summary>
    public static string RepositoryRoot { get; } = FindRoot();

    /// <summary>The benchmark data folder: MAT_BENCH_DATA, else the repository's bench/data.</summary>
    public static string BenchData
    {
        get
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(BenchDataVariable);
            return string.IsNullOrWhiteSpace(fromEnvironment)
                ? Path.Combine(RepositoryRoot, "bench", "data")
                : Path.GetFullPath(fromEnvironment);
        }
    }

    /// <summary>The path of a benchmark file relative to the data folder; the test skips when it is absent.</summary>
    public static string RequireBenchFile(string relativePath)
    {
        string path = Path.Combine(BenchData, relativePath);
        Assert.SkipUnless(
            File.Exists(path),
            $"needs the benchmark audio {path}; build it with node bench/testset/fetch-testset.mjs, or point {BenchDataVariable} at a folder that holds it");
        return path;
    }

    /// <summary>The Silero VAD graph's path; the test skips when the model is not installed.</summary>
    public static string RequireSileroModel()
    {
        ModelCatalog catalog = ModelCatalog.FromLockFile(ModelCatalog.ResolveRoot(), Path.Combine(RepositoryRoot, "build", "models.lock"));
        Assert.SkipUnless(
            catalog.MissingFiles("silero-vad").Count == 0,
            $"needs the silero-vad model in {catalog.Root}; run: node build/fetch-models.mjs --into {catalog.Root}");
        return catalog.ResolveRole("silero-vad", "model");
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
