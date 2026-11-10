// Unit tests of the model catalog over the repository's lock and over small folders written by the tests.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace MeetingAudioTranscriptor.Inference.Tests;

[Trait("Category", "config")]
public sealed class ModelCatalogTests
{
    private static ModelCatalog Small(string root, byte[] content, string? hash = null) =>
        new(root, [new CatalogModel("tiny", "default", false, [new CatalogFile("tiny/a.onnx", hash ?? Convert.ToHexStringLower(SHA256.HashData(content)), content.Length, "model")])]);

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mat-catalog-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "tiny"));
        return folder;
    }

    [Fact]
    public void TheShippedLockDescribesTheDefaultModels()
    {
        ModelCatalog catalog = ModelCatalog.FromLockFile(Path.Combine(Path.GetTempPath(), "nowhere"), TestPaths.ModelLock);

        Assert.Contains("silero-vad", catalog.ModelNames);
        Assert.Contains("wespeaker-campplus-lm", catalog.ModelNames);
        Assert.Contains("parakeet-tdt-0.6b-v3", catalog.ModelNames);
    }

    [Fact]
    public void ResolvingAFileThatIsNotInstalledNamesTheFetchCommand()
    {
        string root = Path.Combine(Path.GetTempPath(), "mat-empty-" + Guid.NewGuid().ToString("N"));
        ModelCatalog catalog = ModelCatalog.FromLockFile(root, TestPaths.ModelLock);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => catalog.ResolveRole("silero-vad", "model"));

        Assert.Contains("node build/fetch-models.mjs --model silero-vad", error.Message, StringComparison.Ordinal);
        Assert.Contains(ModelCatalog.ModelsDirectoryVariable, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownModelListsTheKnownOnes()
    {
        ModelCatalog catalog = ModelCatalog.FromLockFile(Path.GetTempPath(), TestPaths.ModelLock);

        ArgumentException error = Assert.Throws<ArgumentException>(() => catalog.ResolveRole("nope", "model"));

        Assert.Contains("silero-vad", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARoleWithSeveralFilesAsksForAFileName()
    {
        ModelCatalog catalog = ModelCatalog.FromLockFile(Path.GetTempPath(), TestPaths.ModelLock);

        ArgumentException error = Assert.Throws<ArgumentException>(() => catalog.ResolveRole("parakeet-tdt-0.6b-v3", "model"));

        Assert.Contains("ResolveFile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstalledFileResolvesByRoleAndByFileName()
    {
        string root = NewFolder();
        byte[] content = Encoding.UTF8.GetBytes("model bytes");
        File.WriteAllBytes(Path.Combine(root, "tiny", "a.onnx"), content);
        ModelCatalog catalog = Small(root, content);

        Assert.Equal(Path.Combine(root, "tiny", "a.onnx"), catalog.ResolveRole("tiny", "model"));
        Assert.Equal(Path.Combine(root, "tiny", "a.onnx"), catalog.ResolveFile("tiny", "a.onnx"));
        Assert.True(catalog.IsInstalled("tiny"));
        Assert.Empty(catalog.MissingFiles("tiny"));
        Assert.Empty(catalog.Verify());
    }

    [Fact]
    public void VerifyReportsAMissingAMismatchedAndATruncatedFile()
    {
        string root = NewFolder();
        byte[] content = Encoding.UTF8.GetBytes("model bytes");

        Assert.Contains("is missing", Assert.Single(Small(root, content).Verify()), StringComparison.Ordinal);

        File.WriteAllBytes(Path.Combine(root, "tiny", "a.onnx"), content);
        string mismatch = Assert.Single(Small(root, content, new string('0', 64)).Verify("tiny"));
        Assert.Contains("wanted " + new string('0', 64), mismatch, StringComparison.Ordinal);

        File.WriteAllBytes(Path.Combine(root, "tiny", "a.onnx"), Encoding.UTF8.GetBytes("short"));
        Assert.Contains("bytes, wanted", Assert.Single(Small(root, content).Verify()), StringComparison.Ordinal);
        Assert.False(Small(root, content).IsInstalled("tiny"));
    }

    [Fact]
    public void TheModelsDirectoryVariableOverridesTheDefaultRoot()
    {
        string? previous = Environment.GetEnvironmentVariable(ModelCatalog.ModelsDirectoryVariable);
        try
        {
            string folder = NewFolder();
            Environment.SetEnvironmentVariable(ModelCatalog.ModelsDirectoryVariable, folder);

            Assert.Equal(folder, ModelCatalog.ResolveRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ModelCatalog.ModelsDirectoryVariable, previous);
        }
    }
}
