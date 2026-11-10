// Tests of the speaker floor: a speaker of the finished transcript who barely speaks is folded into
// the acoustically nearest other speaker, never dropped, scaled by the total, overridden by the hint.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Diarisation;

[Trait("Category", "diarisation")]
public sealed class SpeakerFloorTests
{
    private const int Rate = 16000;

    // Voice prints: A and B are two clear voices; Near B lies close to B and far from A.
    private static readonly float[] VoiceA = Unit(1, 0, 0, 0);
    private static readonly float[] VoiceB = Unit(0, 1, 0, 0);
    private static readonly float[] NearB = Unit(0.2f, 0.9f, 0.3f, 0);

    /// <summary>A recording built from speech regions, each with one voice and one cluster label.</summary>
    private sealed class Recording
    {
        public List<SpeakerWindow> Windows { get; } = [];

        public List<int> Labels { get; } = [];

        public List<(long Start, long End)> Regions { get; } = [];

        public List<RecognizedWord> Words { get; } = [];

        /// <summary>Adds a region from start to end seconds: 3 s windows every 2 s (one window when shorter) and one word per second.</summary>
        public Recording Speak(double start, double end, float[] voice, int label)
        {
            Regions.Add(((long)(start * Rate), (long)(end * Rate)));
            for (double at = start; at < end; at += 2)
            {
                double windowEnd = Math.Min(end, at + 3);
                Windows.Add(new SpeakerWindow((long)(at * Rate), (long)(windowEnd * Rate), voice));
                Labels.Add(label);
                if (windowEnd >= end)
                {
                    break;
                }
            }

            for (double at = start; at + 0.6 <= end; at += 1)
            {
                Words.Add(new RecognizedWord("word", at + 0.1, at + 0.6, 0.9f));
            }

            return this;
        }

        public (int[] Labels, SpeakerTimeline Timeline, int[] SpeakerOfWord) Apply(DiarisationOptions options) =>
            SpeakerFloor.Apply(Windows, [.. Labels], Regions, Rate, options, Words);
    }

    private static float[] Unit(params float[] values)
    {
        VectorMath.Normalise(values);
        return values;
    }

    private static int Speakers(int[] speakerOfWord) => speakerOfWord.Distinct().Count();

    // Two voices of 40 s each, and a stray of 2 s that sounds like B.
    private static Recording TwoVoicesAndAStray() =>
        new Recording()
            .Speak(0, 40, VoiceA, 0)
            .Speak(41, 81, VoiceB, 1)
            .Speak(82, 84, NearB, 2);

    [Fact]
    public void AStrayBelowTheFloorIsFoldedIntoTheNearestVoice()
    {
        Recording recording = TwoVoicesAndAStray();

        (_, SpeakerTimeline timeline, int[] speakers) = recording.Apply(new DiarisationOptions());

        // The stray's two words (at 82 and 83 s) now belong to B, the speaker of the words at 41 to 80 s.
        int speakerOfB = speakers[recording.Words.FindIndex(w => w.Start > 41)];
        int[] strayWords = [.. recording.Words.Select((w, i) => (w, i)).Where(p => p.w.Start > 82).Select(p => speakers[p.i])];
        Assert.True(Speakers(speakers) == 2 && timeline.SpeakerCount == 2, $"got {Speakers(speakers)} speakers, wanted the stray folded into one of two");
        Assert.True(strayWords.Length == 2 && strayWords.All(s => s == speakerOfB), $"got the stray's words on speakers {string.Join(",", strayWords)}, wanted B's {speakerOfB}");
    }

    [Fact]
    public void NoWordIsDroppedByTheFloor()
    {
        Recording recording = TwoVoicesAndAStray();

        (_, _, int[] speakers) = recording.Apply(new DiarisationOptions());

        Assert.True(speakers.Length == recording.Words.Count && speakers.All(s => s >= 0), $"got {speakers.Length} speakers for {recording.Words.Count} words");
    }

    [Fact]
    public void AZeroFloorKeepsTheStray()
    {
        (_, _, int[] speakers) = TwoVoicesAndAStray().Apply(new DiarisationOptions { SpeakerFloorSeconds = 0, SpeakerFloorShare = 0 });

        Assert.True(Speakers(speakers) == 3, $"got {Speakers(speakers)} speakers with the floor off, wanted 3");
    }

    [Fact]
    public void TheHintOverridesTheFloor()
    {
        (_, _, int[] speakers) = TwoVoicesAndAStray().Apply(new DiarisationOptions { SpeakerCountHint = 3 });

        Assert.True(Speakers(speakers) == 3, $"got {Speakers(speakers)} speakers with a hint of 3, wanted 3");
    }

    [Fact]
    public void TheShareScalesTheFloorWithTheTotal()
    {
        // The stray speaks 2 s of about 84 s (2.4 %): a 3 % share folds it, a 2 % share keeps it.
        (_, _, int[] folded) = TwoVoicesAndAStray().Apply(new DiarisationOptions { SpeakerFloorSeconds = 0, SpeakerFloorShare = 0.03 });
        (_, _, int[] kept) = TwoVoicesAndAStray().Apply(new DiarisationOptions { SpeakerFloorSeconds = 0, SpeakerFloorShare = 0.02 });

        Assert.True(Speakers(folded) == 2, $"got {Speakers(folded)} speakers with a 3 % share, wanted 2");
        Assert.True(Speakers(kept) == 3, $"got {Speakers(kept)} speakers with a 2 % share, wanted 3");
    }

    [Fact]
    public void TheFloorIsTheLargerOfTheTimeAndTheShare()
    {
        DiarisationOptions options = new() { SpeakerFloorSeconds = 4, SpeakerFloorShare = 0.015 };

        // A 90 s clip: 1.35 s from the share, so 4 s; a two-hour meeting with 6000 s of speech: 90 s.
        Assert.Equal(4.0, SpeakerFloor.Seconds(options, 90), 6);
        Assert.Equal(90.0, SpeakerFloor.Seconds(options, 6000), 6);
    }

    [Fact]
    public void TwoStraysGoToALargeVoiceRatherThanToEachOther()
    {
        // Two strays with the same voice print, near B: 2 s and 3 s, 5 s together. Folding the smaller
        // into the other would leave a 5 s speaker above the floor; both belong with B.
        Recording recording = new Recording()
            .Speak(0, 40, VoiceA, 0)
            .Speak(41, 81, VoiceB, 1)
            .Speak(82, 85, NearB, 2)
            .Speak(86, 88, NearB, 3);

        (_, _, int[] speakers) = recording.Apply(new DiarisationOptions());

        Assert.True(Speakers(speakers) == 2, $"got {Speakers(speakers)} speakers, wanted both strays folded into B");
    }

    [Fact]
    public void TheFloorStopsAtOneSpeaker()
    {
        (_, _, int[] speakers) = TwoVoicesAndAStray().Apply(new DiarisationOptions { SpeakerFloorSeconds = 600 });

        Assert.True(Speakers(speakers) == 1 && speakers.All(s => s >= 0), $"got {Speakers(speakers)} speakers with a floor above everyone, wanted every word on one speaker");
    }

    [Fact]
    public void ARecordingWithoutWindowsIsLeftAlone()
    {
        RecognizedWord[] words = [new("hello", 0.1, 0.5, 0.9f)];

        (int[] labels, SpeakerTimeline timeline, int[] speakers) = SpeakerFloor.Apply([], [], [(0, 8000)], Rate, new DiarisationOptions(), words);

        Assert.True(labels.Length == 0 && timeline.SpeakerCount == 0 && speakers.SequenceEqual([-1]), $"got {timeline.SpeakerCount} speakers and word speakers {string.Join(",", speakers)}");
    }
}
