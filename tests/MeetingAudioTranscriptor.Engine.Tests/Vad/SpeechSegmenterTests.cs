// Tests of the speech segmenter's hysteresis on hand-written probability sequences: thresholds, minimum speech and silence, padding and the stream's end.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Vad;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Vad;

/// <remarks>
/// A pattern is one character per 512-sample frame: S is speech (0.9), '.' silence (0.1) and m a
/// probability between the two thresholds (0.4). The tests use the reference's thresholds (0.5 and
/// 0.35) and minimum speech (0.25 s) with the default silence and padding: a region needs more than
/// 4000 samples (8 frames) of speech, a silence closes it on the frame at least 4800 samples (10
/// frames) after the silence began, and the padding is 1920 samples.
/// </remarks>
[Trait("Category", "audio")]
public sealed class SpeechSegmenterTests
{
    private const int Pad = 1920;

    private static readonly VadOptions Reference = new() { Threshold = 0.5f, NegativeThreshold = 0.35f, MinSpeechSeconds = 0.25 };

    private static float Probability(char c) => c switch
    {
        'S' => 0.9f,
        '.' => 0.1f,
        'm' => 0.4f,
        _ => throw new ArgumentException($"unknown pattern character {c}"),
    };

    private static string Repeat(char c, int count) => new(c, count);

    /// <summary>Runs a pattern and returns each region with the frame index whose push reported it (-1 for Complete).</summary>
    private static List<(SpeechRegion Region, long ReportedAt)> Segment(string pattern, long? totalSamples = null, VadOptions? options = null)
    {
        SpeechSegmenter segmenter = new(options ?? Reference);
        List<(SpeechRegion, long)> regions = [];
        for (int i = 0; i < pattern.Length; i++)
        {
            if (segmenter.Push(Probability(pattern[i]), out SpeechRegion region))
            {
                regions.Add((region, i));
            }
        }

        if (segmenter.Complete(totalSamples ?? pattern.Length * 512L, out SpeechRegion last))
        {
            regions.Add((last, -1));
        }

        return regions;
    }

    [Fact]
    public void OneRegionIsPaddedAndReportedWhenItsSilenceIsLongEnough()
    {
        string pattern = Repeat('.', 10) + Repeat('S', 20) + Repeat('.', 15);

        List<(SpeechRegion Region, long ReportedAt)> got = Segment(pattern);

        Assert.Single(got);
        SpeechRegion region = got[0].Region;
        Assert.Equal(new SpeechRegion((10 * 512) - Pad, (30 * 512) + Pad, 10 * 512, 30 * 512), region);
        Assert.True(got[0].ReportedAt == 40, $"got the region reported at frame {got[0].ReportedAt}, wanted 40 (10 frames after the silence began)");
    }

    [Theory]
    [InlineData(3, 0.4f, 0)]
    [InlineData(4, 0.4f, 1)]
    [InlineData(4, 0.39f, 0)]
    public void TheMeetingDefaultsKeepShortQuieterSpeech(int speechFrames, float probability, int expectedRegions)
    {
        SpeechSegmenter segmenter = new(new VadOptions());
        int found = 0;
        float[] probabilities = [.. Enumerable.Repeat(0.1f, 10), .. Enumerable.Repeat(probability, speechFrames), .. Enumerable.Repeat(0.1f, 20)];
        foreach (float p in probabilities)
        {
            found += segmenter.Push(p, out _) ? 1 : 0;
        }

        Assert.True(found == expectedRegions, $"got {found} region(s) for {speechFrames} frames at {probability} under the defaults, wanted {expectedRegions}");
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(8, 1)]
    public void ARegionMustBeLongerThanTheMinimumSpeech(int speechFrames, int expectedRegions)
    {
        List<(SpeechRegion Region, long ReportedAt)> got = Segment(Repeat('.', 10) + Repeat('S', speechFrames) + Repeat('.', 20));

        Assert.True(got.Count == expectedRegions, $"got {got.Count} region(s) for {speechFrames} speech frames, wanted {expectedRegions}");
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    public void APauseShorterThanTheMinimumSilenceStaysInside(int pauseFrames, int expectedRegions)
    {
        List<(SpeechRegion Region, long ReportedAt)> got = Segment(Repeat('S', 20) + Repeat('.', pauseFrames) + "S" + Repeat('S', 19) + Repeat('.', 30));

        Assert.True(got.Count == expectedRegions, $"got {got.Count} region(s) for a pause of {pauseFrames} frames, wanted {expectedRegions}");
    }

    [Fact]
    public void FramesBetweenTheThresholdsNeitherCloseNorForgetASilence()
    {
        // The possible end is set at frame 20 and survives the m frames; the '.' at frame 30 closes it there.
        string pattern = Repeat('S', 20) + "." + Repeat('m', 9) + "." + Repeat('.', 10);

        List<(SpeechRegion Region, long ReportedAt)> got = Segment(pattern);

        Assert.Single(got);
        Assert.True(got[0].Region.SpeechEnd == 20 * 512 && got[0].ReportedAt == 30, $"got end {got[0].Region.SpeechEnd} reported at {got[0].ReportedAt}, wanted {20 * 512} at 30");
    }

    [Fact]
    public void FramesBetweenTheThresholdsAfterSpeechKeepTheRegionOpen()
    {
        string pattern = Repeat('S', 20) + Repeat('m', 40);

        List<(SpeechRegion Region, long ReportedAt)> got = Segment(pattern);

        Assert.Single(got);
        Assert.True(got[0].ReportedAt == -1 && got[0].Region.SpeechEnd == 60 * 512, $"got a region ending at {got[0].Region.SpeechEnd} reported at {got[0].ReportedAt}, wanted the stream's end at Complete");
    }

    [Fact]
    public void SpeechAgainForgetsTheSilenceSeenSoFar()
    {
        string pattern = Repeat('S', 20) + Repeat('.', 5) + "S" + Repeat('.', 12);

        List<(SpeechRegion Region, long ReportedAt)> got = Segment(pattern);

        Assert.Single(got);
        Assert.True(got[0].Region.SpeechEnd == 26 * 512, $"got end {got[0].Region.SpeechEnd}, wanted {26 * 512} (after the last speech frame)");
    }

    [Fact]
    public void ARegionOpenAtTheEndEndsAtTheStreamsEndWithPaddingClipped()
    {
        long total = (40 * 512) - 100;

        List<(SpeechRegion Region, long ReportedAt)> got = Segment(Repeat('.', 20) + Repeat('S', 20), total);

        Assert.Single(got);
        Assert.Equal(new SpeechRegion((20 * 512) - Pad, total, 20 * 512, total), got[0].Region);
    }

    [Fact]
    public void ARegionOpenAtTheEndWithAShortTrailingSilenceEndsAtThatSilence()
    {
        List<(SpeechRegion Region, long ReportedAt)> got = Segment(Repeat('S', 20) + Repeat('.', 4));

        Assert.Single(got);
        Assert.Equal(new SpeechRegion(0, (20 * 512) + Pad, 0, 20 * 512), got[0].Region);
    }

    [Fact]
    public void ARegionOpenAtTheEndShorterThanTheMinimumIsDropped()
    {
        List<(SpeechRegion Region, long ReportedAt)> got = Segment(Repeat('.', 20) + Repeat('S', 5));

        Assert.Empty(got);
    }

    [Fact]
    public void PaddingIsClippedAtTheStreamsStart()
    {
        List<(SpeechRegion Region, long ReportedAt)> got = Segment("." + Repeat('S', 20) + Repeat('.', 15));

        Assert.True(got[0].Region.Start == 0, $"got padded start {got[0].Region.Start}, wanted 0");
    }

    [Fact]
    public void NeighboursSeparatedByTheShortestClosingSilenceDoNotOverlapOnceClipped()
    {
        VadOptions options = Reference with { MinSilenceSeconds = 0.064, PadSeconds = 0.032 };
        string pattern = Repeat('S', 20) + Repeat('.', 2) + "." + Repeat('S', 20) + Repeat('.', 10);

        List<(SpeechRegion Region, long ReportedAt)> got = Segment(pattern, options: options);

        Assert.True(got.Count == 2, $"got {got.Count} regions, wanted 2");
        Assert.True(got[0].Region.End <= got[1].Region.Start, $"got regions {got[0].Region} and {got[1].Region}, which overlap");
        Assert.True(got[1].Region.SpeechStart - got[0].Region.SpeechEnd >= 2 * 512, "the raw gap should be the closing silence");
    }

    [Fact]
    public void BoundariesLieOnTheFrameGrid()
    {
        List<(SpeechRegion Region, long ReportedAt)> got = Segment(Repeat('.', 33) + Repeat('S', 17) + Repeat('.', 41) + Repeat('S', 9) + Repeat('.', 12));

        Assert.True(got.Count == 2, $"got {got.Count} regions, wanted 2");
        foreach ((SpeechRegion region, _) in got)
        {
            Assert.True(region.SpeechStart % 512 == 0 && region.SpeechEnd % 512 == 0, $"got raw bounds {region.SpeechStart} and {region.SpeechEnd}, wanted multiples of 512");
            Assert.True(region.SpeechStart - region.Start == Pad && region.End - region.SpeechEnd == Pad, $"got padding {region.SpeechStart - region.Start} and {region.End - region.SpeechEnd}, wanted {Pad}");
        }
    }

    [Theory]
    [InlineData(0.5f, 0.6f, 0.3, 0.1)]
    [InlineData(0.5f, 0.35f, 0.3, 0.2)]
    [InlineData(1.0f, 0.35f, 0.3, 0.1)]
    [InlineData(0.5f, 0.35f, 0.0, 0.0)]
    public void InconsistentSettingsAreRefused(float threshold, float negative, double minSilence, double pad)
    {
        VadOptions options = new() { Threshold = threshold, NegativeThreshold = negative, MinSilenceSeconds = minSilence, PadSeconds = pad };

        Assert.Throws<ArgumentOutOfRangeException>(() => new SpeechSegmenter(options));
    }
}
