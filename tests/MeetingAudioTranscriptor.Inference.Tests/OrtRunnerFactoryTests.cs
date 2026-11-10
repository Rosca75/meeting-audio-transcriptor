// Unit tests of the factory's checks: argument validation, the native-library pre-check and the load-failure sentences.
using System;
using System.IO;
using System.Text;
using Xunit;

namespace MeetingAudioTranscriptor.Inference.Tests;

[Trait("Category", "errors")]
public sealed class OrtRunnerFactoryTests
{
    [Fact]
    public void RunnerOptionsDefaultToOneThreadWithoutSpinning()
    {
        RunnerOptions options = new();

        Assert.Equal(1, options.IntraOpThreads);
        Assert.False(options.AllowSpinning);
        Assert.Equal(Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL, options.GraphOptimization);
        Assert.Null(options.OptimizedModelCachePath);
    }

    [Fact]
    public void ANativeRuntimeIsAvailableAndReportsItsVersion()
    {
        Assert.Null(OrtRunnerFactory.CheckNativeRuntime());
        Assert.StartsWith("1.", OrtRunnerFactory.RuntimeVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingModelFileNamesTheFetchCommand()
    {
        string path = Path.Combine(Path.GetTempPath(), "mat-does-not-exist", "model.onnx");

        ModelLoadException error = Assert.Throws<ModelLoadException>(() => OrtRunnerFactory.Open(path, new RunnerOptions()));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.Contains("node build/fetch-models.mjs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotAModelIsReportedWithItsPathAndTheRemedy()
    {
        string path = TestPaths.WriteTemporary("garbage.onnx", Encoding.UTF8.GetBytes("this is not a model"));

        ModelLoadException error = Assert.Throws<ModelLoadException>(() => OrtRunnerFactory.Open(path, new RunnerOptions()));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.Contains("Fetch the model again", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void ZeroIntraOpThreadsIsRefused()
    {
        string path = TestPaths.WriteTemporary("add.onnx", TinyOnnx.AddFloats());

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => OrtRunnerFactory.Open(path, new RunnerOptions(IntraOpThreads: 0)));

        Assert.Contains("at least 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => OrtRunnerFactory.Open(" ", new RunnerOptions()));
    }

    [Fact]
    public void SpinningAndMultipleThreadsOpenAValidModel()
    {
        string path = TestPaths.WriteTemporary("add.onnx", TinyOnnx.AddFloats());

        using var runner = OrtRunnerFactory.Open(path, new RunnerOptions(IntraOpThreads: 2, AllowSpinning: true));

        Assert.NotNull(runner);
    }
}
