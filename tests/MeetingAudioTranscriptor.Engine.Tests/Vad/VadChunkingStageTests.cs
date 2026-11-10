// Tests of the VAD chunking stage over a fake graph: source to chunks, progress, totals, cancellation and a wrong sample rate.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Vad;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Vad;

[Trait("Category", "audio")]
public sealed class VadChunkingStageTests
{
    /// <summary>A tone where the plan says speech and quiet hiss elsewhere, so the fake energy detector hears the plan.</summary>
    private static float[] Bursts(params (double Seconds, bool Loud)[] plan)
    {
        List<float> samples = [];
        Random random = new(3);
        foreach ((double seconds, bool loud) in plan)
        {
            int count = (int)(seconds * 16000);
            for (int i = 0; i < count; i++)
            {
                samples.Add(loud ? 0.3f * MathF.Sin(i * 0.2f) : (float)((random.NextDouble() - 0.5) * 0.01));
            }
        }

        return samples.ToArray();
    }

    private static SileroVadModel Model(FakeSileroRunner runner) =>
        new(runner, new SileroVadOptions { Lanes = 4, BlockSeconds = 8, WarmUpSeconds = 0.5 });

    [Fact]
    public void ASourceBecomesChunksAtItsSilences()
    {
        float[] audio = Bursts((2, false), (5, true), (2, false), (4, true), (0.4, false), (3, true), (3, false));
        FakeSileroRunner runner = FakeSileroRunner.Energy();
        runner.Record = false;
        VadChunkingStage stage = new(Model(runner), 30);
        List<long> progress = [];

        List<SpeechChunk> chunks = stage.Run(new ArraySource(audio, 16000), new SynchronousProgress(progress), TestContext.Current.CancellationToken).ToList();

        Assert.True(chunks.Count == 2, $"got {chunks.Count} chunks, wanted 2 (the 0.4 s pause stays inside the second)");
        Assert.True(Math.Abs(chunks[0].StartSeconds - 2) < 0.2 && Math.Abs(chunks[0].DurationSeconds - 5.24) < 0.2, $"got the first chunk at {chunks[0].StartSeconds:F2} s for {chunks[0].DurationSeconds:F2} s, wanted about 2 s for 5.24 s");
        Assert.True(Math.Abs(chunks[1].StartSeconds - 9) < 0.2 && Math.Abs(chunks[1].DurationSeconds - 7.64) < 0.2, $"got the second chunk at {chunks[1].StartSeconds:F2} s for {chunks[1].DurationSeconds:F2} s, wanted about 9 s for 7.64 s");
        Assert.True(stage.TotalSamples == audio.Length && progress[^1] == audio.Length, $"got {stage.TotalSamples} samples and last progress {progress[^1]}, wanted {audio.Length}");
        Assert.True(progress.Zip(progress.Skip(1)).All(pair => pair.First <= pair.Second), "progress should never go backwards");
        Assert.True(stage.ChunkCount == 2 && stage.ForcedCuts == 0, $"got {stage.ChunkCount} chunks and {stage.ForcedCuts} forced cuts counted");
        Assert.True(stage.SpeechSamples > 11 * 16000 && stage.ChunkedSamples >= stage.SpeechSamples, $"got {stage.SpeechSamples} speech and {stage.ChunkedSamples} chunked samples");
        foreach (SpeechChunk chunk in chunks)
        {
            Assert.True(chunk.Samples.AsSpan().SequenceEqual(audio.AsSpan((int)chunk.StartSample, chunk.Samples.Length)), $"chunk {chunk.Index} does not hold the source's samples");
        }
    }

    [Fact]
    public void QuietAudioMakesNoChunk()
    {
        float[] audio = Bursts((40, false));
        FakeSileroRunner runner = FakeSileroRunner.Energy();
        VadChunkingStage stage = new(Model(runner), 30);

        List<SpeechChunk> chunks = stage.Run(new ArraySource(audio, 16000), null, TestContext.Current.CancellationToken).ToList();

        Assert.True(chunks.Count == 0 && stage.SpeechSamples == 0, $"got {chunks.Count} chunks from hiss, wanted none");
    }

    [Fact]
    public void ChunksArriveBeforeTheSourceIsFinished()
    {
        float[] audio = Bursts((1, false), (3, true), (40, false), (3, true), (1, false));
        ArraySource source = new(audio, 16000);
        VadChunkingStage stage = new(Model(FakeSileroRunner.Energy()), 30);

        using IEnumerator<SpeechChunk> chunks = stage.Run(source, null, TestContext.Current.CancellationToken).GetEnumerator();

        Assert.True(chunks.MoveNext(), "wanted a first chunk");
        Assert.True(source.Position < audio.Length / 2, $"the first chunk arrived after {source.Position} of {audio.Length} samples were read, wanted it well before the end");
    }

    [Fact]
    public void CancellingStopsTheEnumeration()
    {
        float[] audio = Bursts((30, true));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        VadChunkingStage stage = new(Model(FakeSileroRunner.Energy()), 30);

        Assert.Throws<OperationCanceledException>(() => stage.Run(new ArraySource(audio, 16000), null, cancellation.Token).ToList());
    }

    [Fact]
    public void ASourceAtAnotherRateIsRefused()
    {
        VadChunkingStage stage = new(Model(FakeSileroRunner.Energy()), 30);

        ArgumentException error = Assert.Throws<ArgumentException>(() => stage.Run(new ArraySource(new float[100], 48000), null, TestContext.Current.CancellationToken));
        Assert.True(error.Message.Contains("resample", StringComparison.Ordinal), $"got '{error.Message}', wanted it to say to resample");
    }

    [Fact]
    public void AStageRunsOnce()
    {
        VadChunkingStage stage = new(Model(FakeSileroRunner.Energy()), 30);
        _ = stage.Run(new ArraySource(new float[100], 16000), null, TestContext.Current.CancellationToken).ToList();

        Assert.Throws<InvalidOperationException>(() => stage.Run(new ArraySource(new float[100], 16000), null, TestContext.Current.CancellationToken));
    }

    /// <summary>Reports on the calling thread, unlike <see cref="Progress{T}"/>, which posts to a context.</summary>
    private sealed class SynchronousProgress(List<long> values) : IProgress<long>
    {
        public void Report(long value) => values.Add(value);
    }

    private sealed class ArraySource(float[] samples, int sampleRate) : IAudioSource
    {
        public int Position { get; private set; }

        public int SampleRate => sampleRate;

        public double? DurationSeconds => samples.Length / (double)sampleRate;

        public int Read(Span<float> buffer)
        {
            int count = Math.Min(Math.Min(buffer.Length, 3000), samples.Length - Position);
            samples.AsSpan(Position, count).CopyTo(buffer);
            Position += count;
            return count;
        }

        public void Dispose()
        {
        }
    }
}
