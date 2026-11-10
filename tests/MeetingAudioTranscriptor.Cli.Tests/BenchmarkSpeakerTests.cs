// Tests of the benchmark's speaker figures: a transcript's segments scored against reference turns, the group sums, and the hinted variant's option.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Cli.Benchmark;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using Xunit;

namespace MeetingAudioTranscriptor.Cli.Tests;

[Trait("Category", "transcript")]
public sealed class BenchmarkSpeakerTests
{
    private const string Rttm = """
        SPEAKER m 1 0.00 10.00 <NA> <NA> alice <NA> <NA>
        SPEAKER m 1 10.00 10.00 <NA> <NA> bob <NA> <NA>
        """;

    [Fact]
    public void SegmentsThatMatchTheTurnsScoreNoError()
    {
        SpeakerMetrics? metrics = SpeakerMetrics.Compute(Rttm, Document(("S1", 0, 10), ("S2", 10, 20)));

        Assert.True(metrics is not null && metrics.ReferenceSpeakers == 2 && metrics.EstimatedSpeakers == 2, $"got {metrics}");
        Assert.True(metrics.Excluded.ErrorRate < 0.001 && metrics.Included.ErrorRate < 0.001 && metrics.Attribution > 0.999, $"got DER {metrics.Excluded.ErrorRate} attribution {metrics.Attribution}");
    }

    [Fact]
    public void ASegmentGivenToTheWrongSpeakerIsConfusion()
    {
        SpeakerMetrics metrics = SpeakerMetrics.Compute(Rttm, Document(("S1", 0, 10), ("S1", 10, 15), ("S2", 15, 20)))!;
        SpeakerGroupMetrics group = SpeakerGroupMetrics.Of([metrics, null])!;

        Assert.True(metrics.Excluded.ConfusionSeconds > 4 && metrics.Excluded.ConfusionSeconds < 5.5, $"got confusion {metrics.Excluded.ConfusionSeconds} s, wanted about 5 s less the collar");
        Assert.True(group.Files == 1 && Math.Abs(group.ErrorRateExcluded - metrics.Excluded.ErrorRate) < 1e-9 && group.ConfusionExcluded > 0.2 && group.ExactCounts == 1, $"got {group}");
    }

    [Fact]
    public void ATranscriptWithoutSpeakersIsNotScored()
    {
        Assert.True(SpeakerMetrics.Compute(Rttm, Document()) is null, "a transcript without speakers should have no speaker score");
        Assert.True(SpeakerGroupMetrics.Of([null, null]) is null, "a group without a scored file should have no speaker figures");
    }

    [Fact]
    public void TheHintedVariantIsAnOptionThatExcludesNoSpeakers()
    {
        ParsedCommandLine parsed = CommandLine.Parse(["benchmark", "--set", "clips", "--speakers-hint-from-reference"]);
        ParsedCommandLine both = CommandLine.Parse(["benchmark", "--speakers-hint-from-reference", "--no-speakers"]);

        Assert.True(parsed.Kind == CommandKind.Benchmark && parsed.Benchmark!.SpeakersHintFromReference, $"got {parsed.Kind}: {parsed.Error}");
        Assert.True(both.Kind == CommandKind.UsageError && both.Error!.Contains("--no-speakers", StringComparison.Ordinal), $"got {both.Kind}: {both.Error}");
    }

    private static TranscriptDocument Document(params (string Speaker, double Start, double End)[] segments)
    {
        List<string> ids = segments.Select(segment => segment.Speaker).Distinct().ToList();
        return new TranscriptDocument
        {
            SchemaVersion = "1",
            Metadata = new TranscriptMetadata
            {
                Application = "test",
                ApplicationVersion = "1",
                SourceFile = "m.wav",
                DurationSeconds = 20,
                SpeechSeconds = 20,
                Language = "en",
                CreatedUtc = "2026-01-01T00:00:00Z",
                ProcessingSeconds = 1,
                Models = [],
                Settings = new TranscriptSettings { SpeakerSeparation = ids.Count > 0, Threads = 1, Formats = ["json"], RawFiles = false },
            },
            Speakers = ids.Select(id => new TranscriptSpeaker { Id = id, Label = id, SpeakingSeconds = 1 }).ToList(),
            Segments = segments.Select((segment, index) => new TranscriptSegment
            {
                Id = index + 1,
                Start = segment.Start,
                End = segment.End,
                Speaker = segment.Speaker,
                Text = "x",
                RawText = "x",
                Confidence = 1,
            }).ToList(),
        };
    }
}
