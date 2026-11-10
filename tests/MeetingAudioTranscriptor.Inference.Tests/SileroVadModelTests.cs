// Models-tier tests over the real Silero VAD graph: its input and output contract, determinism, concurrency and cancellation.
using System;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;
using Xunit;

namespace MeetingAudioTranscriptor.Inference.Tests;

/// <remarks>
/// The graph's contract (read from the model): input float [1, 576] (64 samples of context and 512
/// new samples), state float [2, 1, 128], sr int64 scalar; outputs output float [1, 1] and stateN
/// float [2, 1, 128].
/// </remarks>
[Trait("Category", "audio")]
[Trait("Tier", "models")]
public sealed class SileroVadModelTests
{
    private const int FrameLength = 576;
    private static readonly string[] ExpectedInputs = ["input", "state", "sr"];
    private static readonly string[] ExpectedOutputs = ["output", "stateN"];

    private static string RequireModel()
    {
        ModelCatalog catalog = TestPaths.Catalog();
        Assert.SkipUnless(
            catalog.MissingFiles("silero-vad").Count == 0,
            $"needs the silero-vad model in {catalog.Root}; run: node build/fetch-models.mjs --into {catalog.Root}");
        return catalog.ResolveRole("silero-vad", "model");
    }

    private static TensorValue[] Inputs(float[] frame, float[]? state = null) =>
    [
        TensorValue.OfFloats("input", [1, FrameLength], frame),
        TensorValue.OfFloats("state", [2, 1, 128], state ?? new float[2 * 128]),
        TensorValue.OfInt64s("sr", [], [16000L]),
    ];

    private static float[] Sine(double frequency)
    {
        float[] frame = new float[FrameLength];
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / 16000.0));
        }

        return frame;
    }

    [Fact]
    public void ZerosAndASyntheticSignalProduceTheDeclaredShapes()
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(RequireModel(), new RunnerOptions());

        TensorValue[] silence = runner.Run(Inputs(new float[FrameLength]), ["output", "stateN"], CancellationToken.None);
        TensorValue[] tone = runner.Run(Inputs(Sine(220), silence[1].Floats), ["output", "stateN"], CancellationToken.None);

        foreach (TensorValue[] result in (TensorValue[][])[silence, tone])
        {
            Assert.Equal("output", result[0].Name);
            Assert.Equal([1, 1], result[0].Shape);
            Assert.Equal("stateN", result[1].Name);
            Assert.Equal([2, 1, 128], result[1].Shape);
            Assert.InRange(result[0].Floats![0], 0f, 1f);
        }

        Assert.True(silence[0].Floats![0] < 0.5f, $"got speech probability {silence[0].Floats![0]} for silence, wanted below 0.5");
    }

    [Fact]
    public void TheSameInputGivesBitIdenticalOutput()
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(RequireModel(), new RunnerOptions());
        float[] frame = Sine(300);

        TensorValue[] first = runner.Run(Inputs(frame), ["output", "stateN"], CancellationToken.None);
        TensorValue[] second = runner.Run(Inputs(frame), ["output", "stateN"], CancellationToken.None);

        Assert.Equal(first[0].Floats!, second[0].Floats!);
        Assert.Equal(first[1].Floats!, second[1].Floats!);
    }

    [Fact]
    public void FourThreadsSharingOneRunnerGetTheSingleThreadedAnswer()
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(RequireModel(), new RunnerOptions());
        float[] frame = Sine(440);
        float expected = runner.Run(Inputs(frame), ["output"], CancellationToken.None)[0].Floats![0];

        Exception?[] failures = new Exception?[4];
        Thread[] threads = new Thread[4];
        for (int t = 0; t < threads.Length; t++)
        {
            int id = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 100; i++)
                    {
                        float got = runner.Run(Inputs(frame), ["output"], CancellationToken.None)[0].Floats![0];
                        Assert.Equal(expected, got);
                    }
                }
                catch (Exception e)
                {
                    failures[id] = e;
                }
            });
            threads[t].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.All(failures, failure => Assert.Null(failure));
    }

    [Fact]
    public void CancellingAFeedingLoopEndsItWithOperationCancelled()
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(RequireModel(), new RunnerOptions());
        float[] frame = Sine(440);
        using CancellationTokenSource source = new(TimeSpan.FromMilliseconds(50));

        Action feed = () =>
        {
            while (true)
            {
                runner.Run(Inputs(frame), ["output"], source.Token);
            }
        };
        Assert.Throws<OperationCanceledException>(feed);
    }

    [Fact]
    public void ACancelledTokenIsHonouredBeforeTheModelRuns()
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(RequireModel(), new RunnerOptions());
        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => runner.Run(Inputs(Sine(440)), ["output"], source.Token));
    }

    [Fact]
    public void ARunnerOverTheRealModelNamesItsGraphInputs()
    {
        using OrtTensorRunner runner = Assert.IsType<OrtTensorRunner>(OrtRunnerFactory.Open(RequireModel(), new RunnerOptions()));

        Assert.Equivalent(ExpectedInputs, runner.InputNames);
        Assert.Equivalent(ExpectedOutputs, runner.OutputNames);
    }
}
