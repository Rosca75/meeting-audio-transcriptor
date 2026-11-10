// Tests of the speech chunker on synthetic timelines: splits at long silences, target-length cuts, forced cuts, contiguity, buffering and streaming.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Vad;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Vad;

[Trait("Category", "audio")]
public sealed class SpeechChunkerTests
{
    private const int Pad = 1920;

    [Fact]
    public void ALongSilenceSplitsTwoChunksTrimmedToTheirPadding()
    {
        Timeline timeline = new Timeline().Silence(1).Speech(3).Silence(1).Speech(3).Silence(1);
        float[] samples = timeline.Samples();

        (List<SpeechChunk> chunks, _) = ChunkerHarness.Run(samples, timeline.Probabilities, 30);

        Assert.True(chunks.Count == 2, $"got {chunks.Count} chunks, wanted 2");
        long speechStart = 31 * 512;
        long speechEnd = (31 + 94) * 512;
        Assert.True(chunks[0].StartSample == speechStart - Pad && chunks[0].EndSample == speechEnd + Pad, $"got chunk [{chunks[0].StartSample}, {chunks[0].EndSample}), wanted [{speechStart - Pad}, {speechEnd + Pad})");
        Assert.True(chunks.All(chunk => !chunk.ForcedCut), "no chunk should be a forced cut");
        ChunkerHarness.AssertInvariants(chunks, samples, timeline.Probabilities, 30);
        ChunkerHarness.AssertSpeechCoveredOnce(chunks, timeline.Probabilities, samples.Length);
    }

    [Fact]
    public void AShortSilenceStaysInsideOneChunk()
    {
        Timeline timeline = new Timeline().Silence(1).Speech(3).Silence(0.45).Speech(3).Silence(1);
        float[] samples = timeline.Samples();

        (List<SpeechChunk> chunks, _) = ChunkerHarness.Run(samples, timeline.Probabilities, 30);

        Assert.True(chunks.Count == 1 && chunks[0].SpeechRegions.Count == 2, $"got {chunks.Count} chunk(s), wanted 1 holding two regions");
        ChunkerHarness.AssertInvariants(chunks, samples, timeline.Probabilities, 30);
    }

    [Fact]
    public void AChunkPastTheTargetIsCutInASilenceInItsSecondHalf()
    {
        // Speech of 2 s and pauses of 0.4 s (closing a region but shorter than the split silence) for 60 s.
        Timeline timeline = new Timeline().Silence(0.5);
        for (int i = 0; i < 25; i++)
        {
            timeline.Speech(2).Silence(0.4);
        }

        float[] samples = timeline.Samples();
        SpeechChunkerOptions options = new() { TargetChunkSeconds = 10 };

        (List<SpeechChunk> chunks, _) = ChunkerHarness.Run(samples, timeline.Probabilities, 30, options);

        Assert.True(chunks.Count >= 4, $"got {chunks.Count} chunks for 60 s at a 10 s target, wanted at least 4");
        foreach (SpeechChunk chunk in chunks.Take(chunks.Count - 1))
        {
            Assert.True(!chunk.ForcedCut && chunk.DurationSeconds >= 5 && chunk.DurationSeconds <= 13, $"chunk {chunk.Index} is {chunk.DurationSeconds:F2} s (forced {chunk.ForcedCut}), wanted 5 to 13 s cut at a silence");
        }

        ChunkerHarness.AssertInvariants(chunks, samples, timeline.Probabilities, 30, options);
        ChunkerHarness.AssertSpeechCoveredOnce(chunks, timeline.Probabilities, samples.Length, options);
    }

    [Fact]
    public void APauseInsideARegionIsAPlaceToCut()
    {
        // 25 s of speech with one 0.26 s pause (8 frames, too short to close the region) at 15 s.
        Timeline timeline = new Timeline().Speech(15).Silence(0.256).Speech(10).Silence(1);
        float[] samples = timeline.Samples();

        (List<SpeechChunk> chunks, _) = ChunkerHarness.Run(samples, timeline.Probabilities, 20, new SpeechChunkerOptions { TargetChunkSeconds = 12 });

        Assert.True(chunks.Count == 2, $"got {chunks.Count} chunks, wanted 2");
        long pauseStart = 469 * 512;
        long pauseMiddle = pauseStart + (4 * 512);
        Assert.True(chunks[0].EndSample == pauseMiddle && !chunks[0].ForcedCut, $"got the first chunk ending at {chunks[0].EndSample} (forced {chunks[0].ForcedCut}), wanted the pause's middle {pauseMiddle}");
        Assert.True(chunks[1].StartSample == pauseMiddle, $"got the second chunk starting at {chunks[1].StartSample}, wanted {pauseMiddle}");
        ChunkerHarness.AssertInvariants(chunks, samples, timeline.Probabilities, 20);
        ChunkerHarness.AssertSpeechCoveredOnce(chunks, timeline.Probabilities, samples.Length);
    }

    [Fact]
    public void SpeechWithoutASilenceIsCutAtTheLeastSpeechLikeFrameBeforeTheMaximum()
    {
        Timeline timeline = new Timeline().Speech(70);
        int weakest = (int)(27.0 * 16000 / 512);
        timeline.SetFrame(weakest, 0.6f).SetFrame(weakest - 200, 0.55f);
        float[] samples = timeline.Samples();

        (List<SpeechChunk> chunks, SpeechChunker chunker) = ChunkerHarness.Run(samples, timeline.Probabilities, 30);

        Assert.True(chunks.Count == 3, $"got {chunks.Count} chunks for 70 s at a 30 s maximum, wanted 3");
        Assert.True(chunks[0].ForcedCut && chunks[1].ForcedCut && !chunks[2].ForcedCut, "the first two chunks should end at forced cuts");
        long cut = (weakest * 512L) + 256;
        Assert.True(chunks[0].EndSample == cut, $"got the first cut at {chunks[0].EndSample}, wanted the middle of frame {weakest} ({cut}); frame {weakest - 200} is outside the last quarter");
        Assert.True(chunker.ForcedCuts == 2, $"got {chunker.ForcedCuts} forced cuts counted, wanted 2");
        ChunkerHarness.AssertInvariants(chunks, samples, timeline.Probabilities, 30);
        ChunkerHarness.AssertSpeechCoveredOnce(chunks, timeline.Probabilities, samples.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    public void RandomTimelinesKeepEveryInvariant(int seed)
    {
        Random random = new(seed);
        Timeline timeline = new();
        while (timeline.Probabilities.Length < 15000)
        {
            timeline.Add(random.NextDouble() * 3, 0.02f + (float)(random.NextDouble() * 0.3));
            int speechFrames = random.Next(1, 600);
            for (int i = 0; i < speechFrames; i++)
            {
                // Mostly speech, with dips that sometimes cross the thresholds.
                timeline.Add(0.032, random.NextDouble() < 0.1 ? (float)random.NextDouble() : 0.6f + (float)(random.NextDouble() * 0.4));
            }
        }

        float[] samples = timeline.Samples(trailing: 300);
        double max = 8 + (random.NextDouble() * 25);
        SpeechChunkerOptions options = new() { TargetChunkSeconds = 4 + (random.NextDouble() * 20) };

        (List<SpeechChunk> chunks, SpeechChunker chunker) = ChunkerHarness.Run(samples, timeline.Probabilities, max, options, appendSize: 3000 + random.Next(20000), lagFrames: random.Next(0, 1000));

        Assert.True(chunks.Count > 3, $"got {chunks.Count} chunks, wanted several from {samples.Length / 16000} s");
        ChunkerHarness.AssertInvariants(chunks, samples, timeline.Probabilities, max, options);
        ChunkerHarness.AssertSpeechCoveredOnce(chunks, timeline.Probabilities, samples.Length, options);
        Assert.True(chunker.ChunkedSamples == chunks.Sum(chunk => (long)chunk.Samples.Length), "the chunked-sample total disagrees with the chunks");
        Assert.True(chunker.SpeechSamples == chunks.Sum(chunk => chunk.SpeechRegions.Sum(region => (long)(region.End - region.Start))), "the speech-sample total disagrees with the chunks");
        Assert.True(chunker.TotalSamples == samples.Length, $"got {chunker.TotalSamples} total samples, wanted {samples.Length}");
    }

    [Fact]
    public void NoiseAndBlipsShorterThanTheMinimumSpeechMakeNoChunk()
    {
        Timeline timeline = new Timeline().Add(5, 0.3f);
        for (int i = 0; i < 20; i++)
        {
            timeline.Speech(0.064).Silence(0.8);
        }

        float[] samples = timeline.Samples();

        (List<SpeechChunk> chunks, SpeechChunker chunker) = ChunkerHarness.Run(samples, timeline.Probabilities, 30);

        Assert.True(chunks.Count == 0 && chunker.SpeechSamples == 0, $"got {chunks.Count} chunks from blips of 64 ms, wanted none");
    }

    [Fact]
    public void AnEmptyStreamMakesNoChunk()
    {
        (List<SpeechChunk> chunks, SpeechChunker chunker) = ChunkerHarness.Run([], [], 30);

        Assert.True(chunks.Count == 0 && chunker.TotalSamples == 0, $"got {chunks.Count} chunks and {chunker.TotalSamples} samples from nothing");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(511)]
    [InlineData(600)]
    [InlineData(1600)]
    [InlineData(2000)]
    [InlineData(5000)]
    public void ATinyStreamOfSpeechIsOneChunkOrNone(int length)
    {
        float[] samples = Enumerable.Range(0, length).Select(i => i / 1e7f).ToArray();
        float[] probabilities = Enumerable.Repeat(0.9f, (length + 511) / 512).ToArray();

        (List<SpeechChunk> chunks, _) = ChunkerHarness.Run(samples, probabilities, 30);

        int expected = length > 1600 ? 1 : 0;
        Assert.True(chunks.Count == expected, $"got {chunks.Count} chunks for {length} samples of speech, wanted {expected}");
        if (expected == 1)
        {
            Assert.True(chunks[0].StartSample == 0 && chunks[0].EndSample == length, $"got [{chunks[0].StartSample}, {chunks[0].EndSample}), wanted the whole stream");
        }
    }

    [Fact]
    public void TheChunksDoNotDependOnHowTheStreamIsFed()
    {
        Random random = new(5);
        Timeline timeline = new();
        for (int i = 0; i < 40; i++)
        {
            timeline.Speech(random.NextDouble() * 12).Silence(random.NextDouble() * 1.2);
        }

        float[] samples = timeline.Samples(trailing: 77);
        string Describe(List<SpeechChunk> chunks) => string.Join(";", chunks.Select(chunk =>
            $"{chunk.StartSample}+{chunk.Samples.Length}{(chunk.ForcedCut ? "F" : "")}:{string.Join(",", chunk.SpeechRegions)}"));

        string reference = Describe(ChunkerHarness.Run(samples, timeline.Probabilities, 15).Chunks);
        foreach ((int appendSize, int lag) in new[] { (512, 0), (1, 0), (999, 3), (16384, 250), (100000, 4000), (int.MaxValue, 0) })
        {
            string got = Describe(ChunkerHarness.Run(samples, timeline.Probabilities, 15, appendSize: appendSize, lagFrames: lag).Chunks);
            Assert.True(got == reference, $"feeding {appendSize} samples at a time with a lag of {lag} frames changed the chunks");
        }
    }

    [Fact]
    public void TheSamplesHeldStayBoundedByTheMaximumAndTheLag()
    {
        // Ten minutes of alternating speech and pauses, plus a long monologue, fed with a 32 s VAD lag.
        Timeline timeline = new();
        for (int i = 0; i < 60; i++)
        {
            timeline.Speech(7).Silence(0.3).Speech(2).Silence(0.7);
        }

        timeline.Speech(120).Silence(30);
        float[] samples = timeline.Samples();
        const int lagFrames = 1000;

        (List<SpeechChunk> chunks, SpeechChunker chunker) = ChunkerHarness.Run(samples, timeline.Probabilities, 30, appendSize: 16384, lagFrames: lagFrames);

        long bound = (30 * 16000) + (lagFrames * 512) + 16384 + Pad + 512;
        Assert.True(chunker.PeakBufferedSamples <= bound, $"the chunker held up to {chunker.PeakBufferedSamples} samples, wanted at most {bound} (maximum chunk plus lag plus one read)");
        ChunkerHarness.AssertInvariants(chunks, samples, timeline.Probabilities, 30);
    }

    [Fact]
    public void CompletingWithoutEveryProbabilityIsRefused()
    {
        SpeechChunker chunker = new(30);
        chunker.AppendSamples(new float[5000]);
        chunker.AppendProbabilities([0.1f, 0.1f], []);

        Assert.Throws<InvalidOperationException>(() => chunker.Complete([]));
    }

    [Fact]
    public void MoreProbabilitiesThanFramesAreRefused()
    {
        SpeechChunker chunker = new(30);
        chunker.AppendSamples(new float[600]);
        chunker.AppendProbabilities([0.1f, 0.1f, 0.1f], []);

        Assert.Throws<InvalidOperationException>(() => chunker.Complete([]));
    }

    [Theory]
    [InlineData(1.5, 20, 0.6, 0.2)]
    [InlineData(30, 0, 0.6, 0.2)]
    [InlineData(30, 20, 0.6, 0.8)]
    public void SettingsOutsideTheirRangeAreRefused(double max, double target, double split, double minCut)
    {
        SpeechChunkerOptions options = new() { TargetChunkSeconds = target, SplitSilenceSeconds = split, MinCutSilenceSeconds = minCut };

        Assert.Throws<ArgumentOutOfRangeException>(() => new SpeechChunker(max, options));
    }
}
