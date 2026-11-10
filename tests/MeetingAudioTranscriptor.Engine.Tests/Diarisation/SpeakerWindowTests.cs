// Tests of how speech regions are cut into embedding windows, and of the embedder and the collector with a fake model.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Models;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Diarisation;

[Trait("Category", "diarisation")]
public sealed class SpeakerWindowTests
{
    private const int Window = 24000;
    private const int Hop = 12000;
    private const int Minimum = 6400;

    private static string Describe(List<(int Start, int Length)> windows) => string.Join(" ", windows.Select(w => $"{w.Start}+{w.Length}"));

    [Theory]
    [InlineData(6399, "")]
    [InlineData(6400, "0+6400")]
    [InlineData(10005, "0+9920")]
    [InlineData(24000, "0+24000")]
    [InlineData(36000, "0+24000 12000+24000")]
    [InlineData(40000, "0+24000 12000+24000 16000+24000")]
    [InlineData(48000, "0+24000 12000+24000 24000+24000")]
    [InlineData(48100, "0+24000 12000+24000 24000+24000")]
    [InlineData(48170, "0+24000 12000+24000 24000+24000 24160+24000")]
    [InlineData(48320, "0+24000 12000+24000 24000+24000 24320+24000")]
    public void CutsSlidingWindowsWithAFlushLastOne(int regionLength, string expected)
    {
        string got = Describe(SpeakerWindowCutter.Cut(regionLength, Window, Hop, Minimum));

        Assert.True(got == expected, $"got [{got}], wanted [{expected}] for a region of {regionLength} samples");
    }

    [Fact]
    public void EveryWindowIsFrameAlignedAndInsideItsRegion()
    {
        for (int length = 6000; length < 100000; length += 777)
        {
            foreach ((int start, int windowLength) in SpeakerWindowCutter.Cut(length, Window, Hop, Minimum))
            {
                Assert.True(
                    start % 160 == 0 && windowLength % 160 == 0 && start + windowLength <= length,
                    $"got window {start}+{windowLength} in a region of {length}, wanted 160-sample alignment inside the region");
            }
        }
    }

    [Fact]
    public void TheEmbedderBatchesEqualWindowsAndNormalises()
    {
        FakeEmbeddingRunner runner = new();
        SpeakerEmbedder embedder = new(runner, batchSize: 2);
        float[] region = KaldiFbankTests.Signal(48320);
        List<(int Start, int Length)> windows = SpeakerWindowCutter.Cut(region.Length, Window, Hop, Minimum);
        float[][] embeddings = new float[windows.Count][];

        embedder.EmbedRegion(region, windows, embeddings, CancellationToken.None);

        Assert.True(runner.Shapes.SequenceEqual(["2x148x80", "2x148x80"]), $"got calls [{string.Join(", ", runner.Shapes)}], wanted two batches of two 148-frame windows");
        foreach (float[] embedding in embeddings)
        {
            double norm = Math.Sqrt(embedding.Sum(v => (double)v * v));
            Assert.True(Math.Abs(norm - 1) < 1e-5, $"got an embedding of norm {norm}, wanted 1");
        }

        // The fake returns the window's per-bin feature means (zero after normalisation) plus its
        // first frame, so equal inputs give equal outputs and the slicing is visible.
        Assert.True(runner.FeatureMeansWereZero, "the features a window passes to the model do not have zero mean per bin; wanted the mean subtracted per window");
    }

    [Fact]
    public void AShortRegionGoesThroughTheModelAlone()
    {
        FakeEmbeddingRunner runner = new();
        SpeakerEmbedder embedder = new(runner, batchSize: 8);
        float[] region = KaldiFbankTests.Signal(10000);

        float[] embedding = embedder.Embed(region, CancellationToken.None);

        Assert.True(runner.Shapes.SequenceEqual(["1x60x80"]), $"got calls [{string.Join(", ", runner.Shapes)}], wanted one call of 60 frames (9920 samples)");
        Assert.True(embedding.Length == FakeEmbeddingRunner.Dimension, $"got {embedding.Length} values, wanted {FakeEmbeddingRunner.Dimension}");
    }

    [Fact]
    public void TheCollectorPlacesWindowsInRecordingTimeFromParallelBlocks()
    {
        FakeEmbeddingRunner runner = new();
        DiarisationCollector collector = new(new SpeakerEmbedder(runner, 4), new DiarisationOptions { WindowSeconds = 1.5, HopSeconds = 0.75 });
        float[] block = KaldiFbankTests.Signal(16000 * 10);

        Parallel.For(0, 4, b => collector.AddBlock(block, b * 160000L, [(0, 40000), (64000, 70000), (100000, 160000)], CancellationToken.None));

        // Per block: 2.5 s gives 3 windows, 0.375 s gives none, 3.75 s gives 4.
        Assert.True(collector.WindowCount == 4 * 7, $"got {collector.WindowCount} windows, wanted 28");
        DiarisationResult result = collector.Finish(null, CancellationToken.None);
        Assert.True(result.WindowCount == 28, $"got {result.WindowCount} windows clustered, wanted 28");
        Assert.True(result.Timeline.Turns[0].Start == 0 && Math.Abs(result.Timeline.Turns[^1].End - 40.0) < 1e-9, $"got turns from {result.Timeline.Turns[0].Start} to {result.Timeline.Turns[^1].End}, wanted 0 to 40");
    }

    [Fact]
    public void TheCollectorRefusesARegionOutsideItsBlock()
    {
        DiarisationCollector collector = new(new SpeakerEmbedder(new FakeEmbeddingRunner()), new DiarisationOptions());

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => collector.AddBlock(new float[1000], 0, [(0, 2000)], CancellationToken.None));

        Assert.Contains("relative to the block", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOptionOutOfRangeIsNamed()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiarisationCollector(new SpeakerEmbedder(new FakeEmbeddingRunner()), new DiarisationOptions { HopSeconds = 5 }));

        Assert.Contains("HopSeconds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeparationSwitchedOffCreatesNoCollector()
    {
        Assert.Throws<ArgumentException>(() => new DiarisationCollector(new SpeakerEmbedder(new FakeEmbeddingRunner()), new DiarisationOptions { Enabled = false }));
    }

    /// <summary>A model stand-in: returns, per window, its first feature frame padded to 16 values, and records every call's shape.</summary>
    private sealed class FakeEmbeddingRunner : ITensorRunner
    {
        public const int Dimension = 16;
        private readonly Lock _lock = new();

        public List<string> Shapes { get; } = [];

        public bool FeatureMeansWereZero { get; private set; } = true;

        public TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
        {
            TensorValue features = inputs[0];
            Assert.Equal(SpeakerEmbedder.InputName, features.Name);
            Assert.Equal(SpeakerEmbedder.OutputName, outputNames[0]);
            int batch = features.Shape[0];
            int frames = features.Shape[1];
            int bins = features.Shape[2];
            float[] output = new float[batch * Dimension];
            for (int b = 0; b < batch; b++)
            {
                for (int d = 0; d < Dimension; d++)
                {
                    output[(b * Dimension) + d] = features.Floats![(b * frames * bins) + d] + 1f;
                }

                for (int bin = 0; bin < bins; bin++)
                {
                    double sum = 0;
                    for (int f = 0; f < frames; f++)
                    {
                        sum += features.Floats![(b * frames * bins) + (f * bins) + bin];
                    }

                    if (Math.Abs(sum / frames) > 1e-3)
                    {
                        FeatureMeansWereZero = false;
                    }
                }
            }

            lock (_lock)
            {
                Shapes.Add($"{batch}x{frames}x{bins}");
            }

            return [TensorValue.OfFloats(SpeakerEmbedder.OutputName, [batch, Dimension], output)];
        }

        public void Dispose()
        {
        }
    }
}
