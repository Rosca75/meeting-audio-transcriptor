// Tests of the diarisation error rate, the attribution accuracy, the speaker mapping and the RTTM parser on hand-made timelines with known answers.
using System;
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Scoring;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Scoring;

[Trait("Category", "scoring")]
public sealed class DiarisationErrorRateTests
{
    private static readonly DiarisationScoringOptions NoCollar = new(0, ExcludeOverlap: false);

    private static readonly List<SpeakerSegment> Reference =
    [
        new("alice", 0, 10),
        new("bob", 10, 20),
    ];

    private static void Near(double got, double wanted, string what)
    {
        Assert.True(Math.Abs(got - wanted) < 1e-6, $"{what}: got {got}, wanted {wanted}");
    }

    [Fact]
    public void APerfectHypothesisScoresZeroWhateverItsLabels()
    {
        DiarisationScore score = DiarisationErrorRate.Compute(Reference, [new("Speaker 2", 0, 10), new("Speaker 1", 10, 20)], NoCollar);

        Near(score.ErrorRate, 0, "DER");
        Near(score.AttributionAccuracy, 1, "attribution");
        Assert.True(score.Mapping["alice"] == "Speaker 2" && score.Mapping["bob"] == "Speaker 1", "the mapping does not pair alice with Speaker 2 and bob with Speaker 1");
    }

    [Fact]
    public void MissedSpeechIsCounted()
    {
        DiarisationScore score = DiarisationErrorRate.Compute(Reference, [new("a", 0, 10), new("b", 10, 15)], NoCollar);

        Near(score.MissedSeconds, 5, "missed");
        Near(score.ErrorRate, 0.25, "DER");
        Near(score.AttributionAccuracy, 0.75, "attribution");
    }

    [Fact]
    public void FalseAlarmIsCounted()
    {
        DiarisationScore score = DiarisationErrorRate.Compute(Reference, [new("a", 0, 10), new("b", 10, 25)], NoCollar);

        Near(score.FalseAlarmSeconds, 5, "false alarm");
        Near(score.ErrorRate, 0.25, "DER");
        Near(score.AttributionAccuracy, 1, "attribution");
    }

    [Fact]
    public void ConfusionUsesTheBestOneToOneMapping()
    {
        // One hypothesis speaker covers alice and the first 4 s of bob; the second covers bob's last 6 s.
        DiarisationScore score = DiarisationErrorRate.Compute(Reference, [new("x", 0, 14), new("y", 14, 20)], NoCollar);

        Near(score.ConfusionSeconds, 4, "confusion");
        Near(score.ErrorRate, 0.2, "DER");
        Near(score.AttributionAccuracy, 0.8, "attribution");
        Assert.True(score.Mapping["alice"] == "x" && score.Mapping["bob"] == "y", "wanted alice to x and bob to y");
    }

    [Fact]
    public void OneHypothesisSpeakerForTwoPeopleConfusesTheSmallerOne()
    {
        DiarisationScore score = DiarisationErrorRate.Compute([new("alice", 0, 12), new("bob", 12, 20)], [new("x", 0, 20)], NoCollar);

        Near(score.ConfusionSeconds, 8, "confusion");
        Near(score.ErrorRate, 0.4, "DER");
        Assert.True(score.Mapping.Count == 1 && score.Mapping["alice"] == "x", "wanted only alice mapped, to x");
    }

    [Fact]
    public void TheCollarRemovesTimeAroundReferenceBoundaries()
    {
        // The hypothesis changes speaker 0.2 s late; a 0.25 s collar hides the error.
        List<SpeakerSegment> hypothesis = [new("a", 0, 10.2), new("b", 10.2, 20)];

        DiarisationScore strict = DiarisationErrorRate.Compute(Reference, hypothesis, NoCollar);
        DiarisationScore collared = DiarisationErrorRate.Compute(Reference, hypothesis, new DiarisationScoringOptions(0.25));

        Near(strict.ConfusionSeconds, 0.2, "confusion without collar");
        Near(collared.ConfusionSeconds, 0, "confusion with collar");

        // Boundaries at 0, 10 and 20: 0.25 s after 0, 0.5 s around 10 and 0.25 s before 20 are not scored.
        Near(collared.ReferenceSeconds, 19, "scored reference time");
    }

    [Fact]
    public void OverlapCountsBothSpeakersOrIsExcluded()
    {
        List<SpeakerSegment> reference = [new("alice", 0, 10), new("bob", 8, 20)];
        List<SpeakerSegment> hypothesis = [new("a", 0, 9), new("b", 9, 20)];

        DiarisationScore included = DiarisationErrorRate.Compute(reference, hypothesis, NoCollar);
        DiarisationScore excluded = DiarisationErrorRate.Compute(reference, hypothesis, new DiarisationScoringOptions(0, ExcludeOverlap: true));

        // Reference time 22 s; in the 2 s overlap one speaker is found each frame: 2 s missed.
        Near(included.ReferenceSeconds, 22, "reference with overlap");
        Near(included.MissedSeconds, 2, "missed in overlap");
        Near(included.ErrorRate, 2.0 / 22, "DER with overlap");
        Near(excluded.ReferenceSeconds, 18, "reference without overlap");
        Near(excluded.ErrorRate, 0, "DER without overlap");
        Near(included.AttributionAccuracy, 1, "attribution reads non-overlapped time only");
    }

    [Fact]
    public void TheHungarianAssignmentFindsTheBestTotal()
    {
        // The greedy choice (row 0 takes its 9) gives 9 + 1 = 10; the best is 8 + 7 = 15.
        int[] got = DiarisationErrorRate.MaximumAssignment(new double[,] { { 9, 8 }, { 7, 1 } });

        Assert.True(got[0] == 1 && got[1] == 0, $"got [{string.Join(",", got)}], wanted [1,0]");
    }

    [Fact]
    public void MoreReferenceThanHypothesisSpeakersLeavesSomeUnmapped()
    {
        int[] got = DiarisationErrorRate.MaximumAssignment(new double[,] { { 1 }, { 5 }, { 3 } });

        Assert.True(got[0] == -1 && got[1] == 0 && got[2] == -1, $"got [{string.Join(",", got)}], wanted [-1,0,-1]");
    }

    [Fact]
    public void TheParserReadsSpeakerLinesAndSkipsOthers()
    {
        string text = "SPKR-INFO f 1 <NA> <NA> <NA> unknown alice <NA> <NA>\n"
            + "SPEAKER f 1 5.180 0.820 <NA> <NA> ami-FEE016 <NA> <NA>\n"
            + "\n"
            + "SPEAKER f 1   12.27\t8.39 <NA> <NA> ami-MEO015 <NA> <NA>\r\n";

        List<SpeakerSegment> segments = DiarisationRttm.Parse(text);

        Assert.True(segments.Count == 2, $"got {segments.Count} segments, wanted 2");
        Assert.Equal("ami-FEE016", segments[0].Speaker);
        Near(segments[0].Start, 5.18, "start");
        Near(segments[0].End, 6.0, "end");
        Near(segments[1].End, 20.66, "second end");
        Assert.Equal(segments, DiarisationRttm.Parse(DiarisationRttm.Format("f", segments)));
    }

    [Fact]
    public void AMalformedSpeakerLineIsNamed()
    {
        FormatException error = Assert.Throws<FormatException>(() => DiarisationRttm.Parse("SPEAKER f 1 abc 1.0 <NA> <NA> s <NA> <NA>\n"));

        Assert.Contains("line 1", error.Message, StringComparison.Ordinal);
    }
}
