// Guard: the ONNX Runtime package is referenced by the inference project alone.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace MeetingAudioTranscriptor.Guards.Tests;

[Trait("Category", "config")]
public sealed class InferenceIsolationGuardTests
{
    private const string Package = "Microsoft.ML.OnnxRuntime";

    [Fact]
    public void OnlyTheInferenceProjectReferencesTheOnnxRuntime()
    {
        IReadOnlyList<string> projects = RepositoryFiles.Under("", "*.csproj");
        Assert.True(projects.Count > 0, "got no .csproj file, wanted at least one; the guard cannot run");

        string[] referencing = projects
            .Where(path => File.ReadAllText(path).Contains($"<PackageReference Include=\"{Package}\"", System.StringComparison.Ordinal))
            .Select(RepositoryFiles.Relative)
            .ToArray();

        string[] wanted = ["src/MeetingAudioTranscriptor.Inference/MeetingAudioTranscriptor.Inference.csproj"];
        Assert.True(
            referencing.SequenceEqual(wanted),
            $"got PackageReference to {Package} in ({string.Join(", ", referencing)}), wanted only ({string.Join(", ", wanted)}); the engine names the model boundary and the inference project implements it");
    }
}
