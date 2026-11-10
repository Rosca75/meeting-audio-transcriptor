// Builds synthetic speech timelines, feeds them to the chunker as a streaming VAD would, and checks every chunk invariant.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Vad;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Vad;

/// <summary>A probability per frame and samples that identify their own position.</summary>
internal sealed class Timeline
{
    private readonly List<float> _probabilities = [];

    /// <summary>Adds frames of one probability for a duration, rounded to whole frames.</summary>
    public Timeline Add(double seconds, float probability)
    {
        int frames = (int)Math.Round(seconds * 16000 / 512);
        for (int i = 0; i < frames; i++)
        {
            _probabilities.Add(probability);
        }

        return this;
    }

    public Timeline Speech(double seconds) => Add(seconds, 0.9f);

    public Timeline Silence(double seconds) => Add(seconds, 0.05f);

    /// <summary>Sets one frame's probability, for a forced cut's target.</summary>
    public Timeline SetFrame(int frame, float probability)
    {
        _probabilities[frame] = probability;
        return this;
    }

    public float[] Probabilities => _probabilities.ToArray();

    /// <summary>Samples whose value encodes their index, so a chunk's samples can be traced to their source.</summary>
    public float[] Samples(int trailing = 0) => Enumerable.Range(0, (_probabilities.Count * 512) - trailing).Select(i => i / 1e7f).ToArray();
}

/// <summary>Runs a chunker and checks what it produced.</summary>
internal static class ChunkerHarness
{
    /// <summary>
    /// Feeds samples in blocks of <paramref name="appendSize"/> and, after each, the probabilities of
    /// the frames that were complete <paramref name="lagFrames"/> frames earlier, as a VAD that
    /// answers late does; the rest at the end.
    /// </summary>
    public static (List<SpeechChunk> Chunks, SpeechChunker Chunker) Run(
        float[] samples, float[] probabilities, double maxSeconds, SpeechChunkerOptions? options = null, int appendSize = 16000, int lagFrames = 0)
    {
        SpeechChunker chunker = new(maxSeconds, options);
        List<SpeechChunk> chunks = [];
        int given = 0;
        for (int offset = 0; offset < samples.Length; offset += appendSize)
        {
            int end = Math.Min(samples.Length, offset + appendSize);
            chunker.AppendSamples(samples.AsSpan(offset, end - offset));
            int ready = Math.Max(0, (end / 512) - lagFrames);
            if (ready > given)
            {
                chunker.AppendProbabilities(probabilities.AsSpan(given, ready - given), chunks);
                given = ready;
            }
        }

        chunker.AppendProbabilities(probabilities.AsSpan(given), chunks);
        chunker.Complete(chunks);
        return (chunks, chunker);
    }

    /// <summary>Checks order, contiguity, length, the samples' origin, the regions' bounds and that no long silence is inside a chunk.</summary>
    public static void AssertInvariants(IReadOnlyList<SpeechChunk> chunks, float[] samples, float[] probabilities, double maxSeconds, SpeechChunkerOptions? options = null)
    {
        options ??= new SpeechChunkerOptions();
        long max = (long)Math.Round(maxSeconds * 16000);
        long splitFrames = (long)Math.Ceiling(options.SplitSilenceSeconds * 16000 / 512);
        long previousEnd = 0;
        for (int c = 0; c < chunks.Count; c++)
        {
            SpeechChunk chunk = chunks[c];
            Assert.True(chunk.Index == c, $"got index {chunk.Index} for chunk {c}");
            Assert.True(chunk.StartSample >= previousEnd, $"chunk {c} starts at {chunk.StartSample}, before the previous chunk's end {previousEnd}");
            Assert.True(chunk.Samples.Length > 0 && chunk.Samples.Length <= max, $"chunk {c} has {chunk.Samples.Length} samples, wanted 1 to {max}");
            Assert.True(chunk.EndSample <= samples.Length, $"chunk {c} ends at {chunk.EndSample}, past the stream's {samples.Length}");
            Assert.True(chunk.Samples.AsSpan().SequenceEqual(samples.AsSpan((int)chunk.StartSample, chunk.Samples.Length)), $"chunk {c} does not hold the stream's samples from {chunk.StartSample}");
            Assert.True(chunk.SpeechRegions.Count > 0, $"chunk {c} has no speech region");
            int regionEnd = 0;
            foreach ((int start, int end) in chunk.SpeechRegions)
            {
                Assert.True(start >= regionEnd && end > start && end <= chunk.Samples.Length, $"chunk {c} has region [{start}, {end}) out of order or outside [0, {chunk.Samples.Length})");
                regionEnd = end;
            }

            Assert.True(chunk.SpeechRegions[0].Start == 0 && chunk.SpeechRegions[^1].End == chunk.Samples.Length, $"chunk {c} starts or ends outside speech: regions {chunk.SpeechRegions[0]} to {chunk.SpeechRegions[^1]} in {chunk.Samples.Length} samples");

            // No run of frames below the negative threshold as long as the split silence lies wholly inside.
            long run = 0;
            for (long frame = (chunk.StartSample + 511) / 512; (frame + 1) * 512 <= chunk.EndSample; frame++)
            {
                run = probabilities[frame] < options.Vad.NegativeThreshold ? run + 1 : 0;
                if (run >= splitFrames)
                {
                    Assert.Fail($"chunk {c} holds a silence of {run} frames ending at frame {frame}, the split silence is {splitFrames}");
                }
            }

            previousEnd = chunk.EndSample;
        }
    }

    /// <summary>Checks that every padded speech region the segmenter finds is covered by exactly one chunk, sample for sample.</summary>
    public static void AssertSpeechCoveredOnce(IReadOnlyList<SpeechChunk> chunks, float[] probabilities, long totalSamples, SpeechChunkerOptions? options = null)
    {
        options ??= new SpeechChunkerOptions();
        SpeechSegmenter segmenter = new(options.Vad);
        List<SpeechRegion> regions = [];
        foreach (float probability in probabilities)
        {
            if (segmenter.Push(probability, out SpeechRegion region))
            {
                regions.Add(region);
            }
        }

        if (segmenter.Complete(totalSamples, out SpeechRegion last))
        {
            regions.Add(last);
        }

        int[] cover = new int[totalSamples];
        foreach (SpeechChunk chunk in chunks)
        {
            foreach ((int start, int end) in chunk.SpeechRegions)
            {
                for (long i = chunk.StartSample + start; i < chunk.StartSample + end; i++)
                {
                    cover[i]++;
                }
            }
        }

        foreach (SpeechRegion region in regions)
        {
            for (long i = region.Start; i < region.End; i++)
            {
                if (cover[i] != 1)
                {
                    Assert.Fail($"sample {i} of speech region [{region.Start}, {region.End}) is covered {cover[i]} times, wanted once");
                }
            }
        }

        long speech = regions.Sum(region => region.Length);
        long covered = cover.Count(count => count > 0);
        Assert.True(covered == speech, $"the chunks' speech regions cover {covered} samples, the segmenter's regions {speech}");
    }
}
