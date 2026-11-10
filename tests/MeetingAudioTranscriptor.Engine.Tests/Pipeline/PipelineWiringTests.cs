// Tests of what the pipeline hands its recogniser and separator: the speech regions to a region-aware recogniser, the cancellation to the separator, and the shipped chunk target.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Pipeline;

[Trait("Category", "transcript")]
public sealed class PipelineWiringTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ARegionAwareRecogniserIsHandedEachChunksSpeechRegions()
    {
        RegionRecorder recognizer = new();
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(new ScriptedChunkSource(6), recognizer, threads: 3);

        PipelineResult result = pipeline.Run(new SilentSource(12), PipelineTestKit.Request(), null, Token);

        Assert.True(recognizer.PlainCalls == 0, $"got {recognizer.PlainCalls} calls without regions, wanted none");
        Assert.True(recognizer.Regions.Count == 6 && recognizer.Regions.All(regions => regions.SequenceEqual([(0, 16000)])), $"got {recognizer.Regions.Count} region lists, wanted the scripted (0, 16000) for each of 6 chunks");
        Assert.True(result.RecognizedWords == 12, $"got {result.RecognizedWords} words");
    }

    [Fact]
    public void TheSeparatorReceivesALiveCancellationToken()
    {
        TokenRecorder separator = new();
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(new ScriptedChunkSource(4), new ScriptedRecognizer(), threads: 2, separators: () => separator);

        pipeline.Run(new SilentSource(8), PipelineTestKit.Request(), null, Token);

        Assert.True(separator.Tokens.Count == 4 && separator.Tokens.All(token => token.CanBeCanceled), "the separator should be handed the run's cancellation with every block");
    }

    [Fact]
    public void TheShippedChunkTargetIsTwentyFiveSeconds()
    {
        PipelineOptions options = new();

        Assert.True(options.Chunker.TargetChunkSeconds == PipelineOptions.DefaultTargetChunkSeconds && PipelineOptions.DefaultTargetChunkSeconds == 25, $"got {options.Chunker.TargetChunkSeconds} s");
        Assert.True((options with { Chunker = options.Chunker with { TargetChunkSeconds = 20 } }).Chunker.TargetChunkSeconds == 20, "the target should stay an option");
    }

    /// <summary>Records the regions it is given and answers two words per chunk.</summary>
    private sealed class RegionRecorder : IRegionAwareRecognizer
    {
        private int _plainCalls;

        public double MaxChunkSeconds => 30;

        public ConcurrentBag<(int Start, int End)[]> Regions { get; } = [];

        public int PlainCalls => Volatile.Read(ref _plainCalls);

        public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _plainCalls);
            return ScriptedRecognizer.TwoWords((int)samples.Span[0] - 1);
        }

        public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
        {
            Regions.Add([.. speechRegions]);
            return ScriptedRecognizer.TwoWords((int)samples.Span[0] - 1);
        }
    }

    /// <summary>Records the token of every block and gives every word to one speaker.</summary>
    private sealed class TokenRecorder : ISpeakerSeparator
    {
        public ConcurrentBag<CancellationToken> Tokens { get; } = [];

        public void AddBlock(ReadOnlySpan<float> samples, long startSample, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation) =>
            Tokens.Add(cancellation);

        public SpeakerAssignment Finish(IReadOnlyList<RecognizedWord> words, int? speakerCountHint, CancellationToken cancellation) =>
            new(new int[words.Count], [1]);
    }
}
