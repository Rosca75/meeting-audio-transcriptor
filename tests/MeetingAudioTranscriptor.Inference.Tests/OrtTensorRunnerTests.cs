// Unit tests of the runner and the factory over tiny graphs written in the tests: they need no model file.
using System;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;
using Xunit;

namespace MeetingAudioTranscriptor.Inference.Tests;

[Trait("Category", "errors")]
public sealed class OrtTensorRunnerTests
{
    private static ITensorRunner OpenAdd() =>
        OrtRunnerFactory.Open(TestPaths.WriteTemporary("add.onnx", TinyOnnx.AddFloats()), new RunnerOptions());

    private static TensorValue Floats(string name, params float[] values) => TensorValue.OfFloats(name, [2, 3], values);

    [Fact]
    public void RunnerAddsTwoFloatTensorsAndReturnsTheRequestedOutput()
    {
        using ITensorRunner runner = OpenAdd();

        TensorValue[] result = runner.Run(
            [Floats("a", 1, 2, 3, 4, 5, 6), Floats("b", 10, 20, 30, 40, 50, 60)],
            ["sum"],
            CancellationToken.None);

        TensorValue sum = Assert.Single(result);
        Assert.Equal("sum", sum.Name);
        Assert.Equal([2, 3], sum.Shape);
        Assert.Equal([11f, 22f, 33f, 44f, 55f, 66f], sum.Floats!);
    }

    [Fact]
    public void InputOrderDoesNotMatter()
    {
        using ITensorRunner runner = OpenAdd();

        TensorValue[] result = runner.Run(
            [Floats("b", 1, 1, 1, 1, 1, 1), Floats("a", 0, 1, 2, 3, 4, 5)],
            ["sum"],
            CancellationToken.None);

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], result[0].Floats!);
    }

    [Fact]
    public void RunnerDoesNotChangeTheCallersInputArrays()
    {
        using ITensorRunner runner = OpenAdd();
        float[] a = [1, 2, 3, 4, 5, 6];

        runner.Run([TensorValue.OfFloats("a", [2, 3], a), Floats("b", 1, 1, 1, 1, 1, 1)], ["sum"], CancellationToken.None);

        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], a);
    }

    [Fact]
    public void RunnerExposesTheGraphsInputAndOutputNames()
    {
        using OrtTensorRunner runner = Assert.IsType<OrtTensorRunner>(OpenAdd());

        Assert.Equal(["a", "b"], [.. runner.InputNames]);
        Assert.Equal(["sum"], [.. runner.OutputNames]);
    }

    [Fact]
    public void UnknownInputNameListsTheExpectedNames()
    {
        using ITensorRunner runner = OpenAdd();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            runner.Run([Floats("a", 0, 0, 0, 0, 0, 0), Floats("c", 0, 0, 0, 0, 0, 0)], ["sum"], CancellationToken.None));

        Assert.Contains("no input named 'c'", error.Message, StringComparison.Ordinal);
        Assert.Contains("a, b", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingInputIsNamed()
    {
        using ITensorRunner runner = OpenAdd();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            runner.Run([Floats("a", 0, 0, 0, 0, 0, 0)], ["sum"], CancellationToken.None));

        Assert.Contains("needs the input(s) b", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownOutputNameListsTheExpectedNames()
    {
        using ITensorRunner runner = OpenAdd();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            runner.Run([Floats("a", 0, 0, 0, 0, 0, 0), Floats("b", 0, 0, 0, 0, 0, 0)], ["total"], CancellationToken.None));

        Assert.Contains("no output named 'total'", error.Message, StringComparison.Ordinal);
        Assert.Contains("sum", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongElementTypeNamesTheInputAndTheFactoryToUse()
    {
        using ITensorRunner runner = OpenAdd();
        TensorValue wrong = TensorValue.OfInt64s("a", [2, 3], new long[6]);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            runner.Run([wrong, Floats("b", 0, 0, 0, 0, 0, 0)], ["sum"], CancellationToken.None));

        Assert.Contains("'a'", error.Message, StringComparison.Ordinal);
        Assert.Contains("OfFloats", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongShapeIsReportedAsAnActionableRunFailure()
    {
        using ITensorRunner runner = OpenAdd();
        TensorValue wrong = TensorValue.OfFloats("a", [3, 2], new float[6]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            runner.Run([wrong, Floats("b", 0, 0, 0, 0, 0, 0)], ["sum"], CancellationToken.None));

        Assert.Contains("failed while running", error.Message, StringComparison.Ordinal);
        Assert.Contains("shape", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Int64OutputsAreCopiedAndAnUnsupportedOutputTypeIsNamed()
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(
            TestPaths.WriteTemporary("ints.onnx", TinyOnnx.IntegersAndAnUnsupportedOutput()),
            new RunnerOptions());
        TensorValue input = TensorValue.OfInt64s("x", [4], [5, 6, 7, 8]);

        TensorValue same = runner.Run([input], ["same"], CancellationToken.None)[0];
        Assert.Equal([5L, 6L, 7L, 8L], same.Int64s!);
        Assert.Equal([4], same.Shape);

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            runner.Run([input], ["wide"], CancellationToken.None));
        Assert.Contains("'wide'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Double", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACancelledTokenThrowsOperationCancelledWithoutRunning()
    {
        using ITensorRunner runner = OpenAdd();
        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            runner.Run([Floats("a", 0, 0, 0, 0, 0, 0), Floats("b", 0, 0, 0, 0, 0, 0)], ["sum"], source.Token));
    }

    [Fact]
    public void OneRunnerServesFourThreadsAtOnce()
    {
        using ITensorRunner runner = OpenAdd();
        Exception?[] failures = new Exception?[4];
        Thread[] threads = new Thread[4];
        for (int t = 0; t < threads.Length; t++)
        {
            int id = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 200; i++)
                    {
                        float offset = (id * 1000) + i;
                        TensorValue[] result = runner.Run(
                            [Floats("a", offset, offset, offset, offset, offset, offset), Floats("b", 1, 1, 1, 1, 1, 1)],
                            ["sum"],
                            CancellationToken.None);
                        Assert.Equal(offset + 1, result[0].Floats![5]);
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
}
