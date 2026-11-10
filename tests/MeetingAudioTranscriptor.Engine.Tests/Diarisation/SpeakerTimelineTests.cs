// Tests of the speaker timeline: frame voting, smoothing of short runs, numbering by first appearance,
// filling of regions no window reached, speaking time and the speaker of each word.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Diarisation;

[Trait("Category", "diarisation")]
public sealed class SpeakerTimelineTests
{
    private const int Rate = 16000;

    private static SpeakerTimeline Build((double Start, double End, int Label)[] windows, (double Start, double End)[] regions, double minimumTurn = 0.5, double strongTurn = 1.0)
    {
        List<SpeakerWindow> list = windows.Select(w => new SpeakerWindow((long)(w.Start * Rate), (long)(w.End * Rate), [1f])).ToList();
        List<int> labels = windows.Select(w => w.Label).ToList();
        List<(long Start, long End)> speech = regions.Select(r => ((long)(r.Start * Rate), (long)(r.End * Rate))).ToList();
        return SpeakerTimeline.Build(list, labels, speech, Rate, minimumTurn, strongTurn);
    }

    // Sliding windows of 1.5 s every 0.75 s over [start, end), labelled by the function of their centre.
    private static (double Start, double End, int Label)[] Sliding(double start, double end, Func<double, int> label)
    {
        List<(double, double, int)> windows = [];
        for (double s = start; s + 1.5 <= end + 1e-9; s += 0.75)
        {
            windows.Add((s, s + 1.5, label(s + 0.75)));
        }

        return [.. windows];
    }

    private static string Describe(SpeakerTimeline timeline) =>
        string.Join(" ", timeline.Turns.Select(t => $"{t.Speaker}:{t.Start:F2}-{t.End:F2}"));

    [Fact]
    public void OverlappingWindowsSplitAtTheChangeOfSpeaker()
    {
        SpeakerTimeline timeline = Build(Sliding(0, 12, c => c < 6 ? 0 : 1), [(0, 12)]);

        Assert.True(timeline.Turns.Count == 2 && timeline.SpeakerCount == 2, $"got [{Describe(timeline)}], wanted two turns");
        double change = timeline.Turns[0].End;
        Assert.True(Math.Abs(change - 6.0) <= 0.4, $"got the change at {change:F2} s, wanted about 6 s");
        Assert.True(timeline.Turns[0].Start == 0 && timeline.Turns[1].End == 12, $"got [{Describe(timeline)}], wanted turns covering 0 to 12 s");
    }

    [Fact]
    public void SpeakersAreNumberedByFirstAppearance()
    {
        SpeakerTimeline timeline = Build([(0, 1.5, 7), (2, 3.5, 2), (4, 5.5, 7), (6, 7.5, 0)], [(0, 1.5), (2, 3.5), (4, 5.5), (6, 7.5)]);

        string got = string.Join(",", timeline.Turns.Select(t => t.Speaker));
        Assert.True(got == "0,1,0,2", $"got speakers {got}, wanted 0,1,0,2 (clusters 7, 2, 7, 0 by first appearance)");
        Assert.Equal("Speaker 1", SpeakerTimeline.Label(0));
        Assert.Equal("Speaker 3", SpeakerTimeline.Label(2));
    }

    [Fact]
    public void ARunShorterThanTheMinimumJoinsAgreeingNeighbours()
    {
        // One window of another speaker inside continuous speech gives a run of about 0.75 s.
        SpeakerTimeline timeline = Build(Sliding(0, 9, c => Math.Abs(c - 4.5) < 0.1 ? 1 : 0), [(0, 9)], minimumTurn: 1.0);
        SpeakerTimeline lenient = Build(Sliding(0, 9, c => Math.Abs(c - 4.5) < 0.1 ? 1 : 0), [(0, 9)], minimumTurn: 0.25);

        Assert.True(timeline.Turns.Count == 1 && timeline.SpeakerCount == 1, $"got [{Describe(timeline)}] with a 1 s minimum, wanted one turn");
        Assert.True(lenient.Turns.Count == 3, $"got [{Describe(lenient)}] with a 0.25 s minimum, wanted the short turn kept");
    }

    [Fact]
    public void ARunBetweenTwoOtherSpeakersGoesToTheSideWithMoreVotes()
    {
        // Speaker 0 then speaker 2 with one window of speaker 1 between them, overlapping speaker 2's first window more.
        (double, double, int)[] windows = [(0, 1.5, 0), (0.75, 2.25, 0), (1.5, 3.0, 0), (2.4, 3.3, 1), (2.4, 3.9, 2), (3.15, 4.65, 2), (3.9, 5.4, 2)];

        SpeakerTimeline timeline = Build(windows, [(0, 5.4)], minimumTurn: 0.5);

        Assert.True(timeline.Turns.Count == 2, $"got [{Describe(timeline)}], wanted two turns");
        Assert.True(timeline.Turns[0].End > 2.5 && timeline.Turns[0].End < 3.4, $"got [{Describe(timeline)}], wanted the change between 2.5 and 3.4 s");
    }

    [Fact]
    public void AShortRegionKeepsItsOwnSpeaker()
    {
        SpeakerTimeline timeline = Build([.. Sliding(0, 3, _ => 0), (5, 5.4, 1), .. Sliding(6, 9, _ => 0)], [(0, 3), (5, 5.4), (6, 9)]);

        Assert.True(Describe(timeline) == "0:0.00-3.00 1:5.00-5.40 0:6.00-9.00", $"got [{Describe(timeline)}], wanted the 0.4 s answer kept as speaker 1");
    }

    [Fact]
    public void ARegionNoWindowReachedTakesTheNearestSpeakerInTime()
    {
        SpeakerTimeline timeline = Build([.. Sliding(0, 3, _ => 0), .. Sliding(8, 11, _ => 1)], [(0, 3), (3.5, 3.8), (7.5, 7.7), (8, 11)]);

        Assert.True(
            Describe(timeline) == "0:0.00-3.00 0:3.50-3.80 1:7.50-7.70 1:8.00-11.00",
            $"got [{Describe(timeline)}], wanted each unwindowed region to follow its nearer neighbour");
    }

    [Fact]
    public void SpeakingSecondsAddUpPerSpeaker()
    {
        SpeakerTimeline timeline = Build([.. Sliding(0, 3, _ => 4), .. Sliding(5, 9.5, _ => 1), .. Sliding(10, 12, _ => 4)], [(0, 3), (5, 9.5), (10, 12)]);

        double[] seconds = timeline.SpeakingSeconds();

        Assert.True(seconds.Length == 2 && Math.Abs(seconds[0] - 5.0) < 1e-6 && Math.Abs(seconds[1] - 4.5) < 1e-6, $"got [{string.Join(", ", seconds)}], wanted [5, 4.5]");
    }

    [Fact]
    public void AnEmptyTimelineAssignsNoSpeaker()
    {
        SpeakerTimeline timeline = Build([], [(0, 2)]);

        int[] speakers = timeline.AssignWords([new RecognizedWord("hello", 0.1, 0.5, 1f)]);

        Assert.True(timeline.SpeakerCount == 0 && speakers.SequenceEqual([-1]), $"got {timeline.SpeakerCount} speakers and [{string.Join(",", speakers)}], wanted none and -1");
    }

    private static readonly SpeakerTimeline TwoSpeakers = Build([.. Sliding(0, 4, _ => 0), .. Sliding(4.5, 8, _ => 1), (8.2, 8.8, 0), .. Sliding(9, 13, _ => 1)], [(0, 4), (4.5, 8), (8.2, 8.8), (9, 13)]);

    [Fact]
    public void WordsTakeTheSpeakerAtTheirMidPointOrTheNearestTurn()
    {
        RecognizedWord[] words =
        [
            new("one", 1.0, 1.4, 1f),
            new("two.", 3.8, 4.4, 1f),
            new("three", 4.4, 4.7, 1f),
            new("four", 13.5, 14.0, 1f),
        ];

        int[] speakers = TwoSpeakers.AssignWords(words);

        Assert.True(speakers.SequenceEqual([0, 0, 1, 1]), $"got [{string.Join(",", speakers)}], wanted [0,0,1,1]");
    }

    [Fact]
    public void ALoneWordInsideASentenceFollowsItsNeighbours()
    {
        // "and" falls in speaker 0's 0.6 s turn while the words around it, 0.2 s away, are speaker 1's.
        RecognizedWord[] words =
        [
            new("we", 7.6, 7.9, 1f),
            new("need", 7.9, 8.15, 1f),
            new("and", 8.3, 8.7, 1f),
            new("more", 8.85, 9.2, 1f),
            new("time", 9.2, 9.5, 1f),
        ];

        int[] speakers = TwoSpeakers.AssignWords(words);

        Assert.True(speakers.SequenceEqual([1, 1, 1, 1, 1]), $"got [{string.Join(",", speakers)}], wanted every word speaker 1");
    }

    [Fact]
    public void ASentenceBoundaryOrAStrongTurnKeepsTheLoneWord()
    {
        RecognizedWord[] afterFullStop =
        [
            new("need", 7.7, 7.95, 1f),
            new("time.", 8.0, 8.1, 1f),
            new("Yes", 8.3, 8.7, 1f),
            new("more", 8.85, 9.2, 1f),
        ];
        int[] boundary = TwoSpeakers.AssignWords(afterFullStop);

        SpeakerTimeline strong = Build([.. Sliding(0, 4, _ => 0), .. Sliding(4.5, 8, _ => 1), (8.2, 8.8, 0), .. Sliding(9, 13, _ => 1)], [(0, 4), (4.5, 8), (8.2, 8.8), (9, 13)], strongTurn: 0.5);
        int[] trusted = strong.AssignWords([afterFullStop[0], new RecognizedWord("and", 8.3, 8.7, 1f), afterFullStop[3]]);

        Assert.True(boundary[2] == 0, $"got speaker {boundary[2]} for a word after a full stop, wanted its own turn's speaker 0");
        Assert.True(trusted[1] == 0, $"got speaker {trusted[1]} inside a turn the strong-turn length trusts, wanted 0");
    }
}
