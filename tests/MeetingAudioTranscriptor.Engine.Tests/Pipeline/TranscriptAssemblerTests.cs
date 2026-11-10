// Tests of the transcript assembly: segment ids and times, removed flags, the low-confidence guard, question context and metadata.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Pipeline;

[Trait("Category", "transcript")]
public sealed class TranscriptAssemblerTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 10, 18, 30, 5, TimeSpan.Zero);

    private static TranscriptDocument Build(IReadOnlyList<RecognizedWord> words, IReadOnlyList<int>? speakers = null, SegmentationOptions? options = null)
    {
        TranscriptAssembler.Facts facts = new(PipelineTestKit.Request(), 12.5, 6.25, 3.5, Created, 4, speakers is not null);
        return TranscriptAssembler.Build(words, speakers, options ?? new SegmentationOptions(), new SegmentCleaner(CleaningLanguage.English), facts, null, TestContext.Current.CancellationToken);
    }

    private static RecognizedWord W(string text, double start, float confidence = 0.9f) => new(text, start, start + 0.3, confidence);

    [Fact]
    public void SegmentsAreNumberedTimedAndCarryTheirWordsWithRemovedFlags()
    {
        TranscriptDocument document = Build([W("um", 1.0), W("we", 1.4), W("ship", 1.8), W("today.", 2.2), W("Then", 4.0, 0.5f), W("lunch.", 4.4, 0.7f)]);

        Assert.True(document.Segments.Count == 2, $"got {document.Segments.Count} segments, wanted 2");
        TranscriptSegment first = document.Segments[0];
        Assert.True(first.Id == 1 && document.Segments[1].Id == 2, "segment ids should count from 1");
        Assert.True(first.Start == 1.0 && Math.Abs(first.End - 2.5) < 1e-9, $"got {first.Start} to {first.End}, wanted 1.0 to 2.5");
        Assert.True(first.RawText == "um we ship today." && first.Text == "we ship today." && first.Removed is null, $"got text '{first.Text}' raw '{first.RawText}' removed {first.Removed}");
        Assert.True(first.Words![0].Removed == true && first.Words.Skip(1).All(w => w.Removed is null), "only the filler should carry removed: true");
        Assert.True(Math.Abs(document.Segments[1].Confidence - 0.6) < 1e-6, $"got confidence {document.Segments[1].Confidence}, wanted the mean 0.6");
    }

    [Fact]
    public void TheGuardRemovesAShortUnsureSegment()
    {
        TranscriptDocument document = Build([W("Okay", 0, 0.2f), W("fine.", 0.4, 0.1f), W("Real", 3, 0.8f), W("words.", 3.4, 0.9f)]);

        TranscriptSegment guarded = document.Segments[0];
        Assert.True(guarded.Text == "" && guarded.Removed == TranscriptAssembler.LowConfidence && guarded.RawText == "Okay fine.", $"got '{guarded.Text}' removed {guarded.Removed} raw '{guarded.RawText}'");
        Assert.True(guarded.Words!.All(w => w.Removed == true), "every word of a guarded segment should be marked removed");
        Assert.True(document.Segments[1].Text == "Real words.", $"got '{document.Segments[1].Text}' for the confident segment");
    }

    [Theory]
    [InlineData("seven twenty.", 0.1f, 0.1f, true)]
    [InlineData("call 112.", 0.1f, 0.1f, false)]
    [InlineData("bonus 3x.", 0.1f, 0.1f, false)]
    [InlineData("hmm yes.", 0.1f, 0.35f, false)]
    [InlineData("one two three.", 0.1f, 0.1f, false)]
    public void TheGuardIsConservative(string text, float first, float rest, bool removed)
    {
        string[] parts = text.Split(' ');
        RecognizedWord[] words = parts.Select((part, i) => W(part, i * 0.4, i == 0 ? first : rest)).ToArray();

        bool got = TranscriptAssembler.IsLowConfidence(words, new SegmentationOptions());

        Assert.True(got == removed, $"got removed={got} for '{text}' with confidences {first} and {rest}, wanted {removed}");
    }

    [Fact]
    public void ThresholdZeroSwitchesTheGuardOff()
    {
        bool got = TranscriptAssembler.IsLowConfidence([W("eh", 0, 0.01f)], new SegmentationOptions { LowConfidenceThreshold = 0 });

        Assert.True(!got, "a threshold of 0 should keep everything");
    }

    [Fact]
    public void AnAnswerToAnotherSpeakersQuestionIsKept()
    {
        RecognizedWord[] words = [W("Do", 0), W("we", 0.4), W("agree?", 0.8), W("Yes.", 3)];

        TranscriptDocument answered = Build(words, [0, 0, 0, 1]);
        TranscriptDocument selfAnswered = Build(words, [0, 0, 0, 0]);
        TranscriptDocument noQuestion = Build([W("We", 0), W("agree.", 0.4), W("Yes.", 3)], [0, 0, 1]);

        Assert.True(answered.Segments[1].Text == "Yes.", $"got '{answered.Segments[1].Text}' removed {answered.Segments[1].Removed}, wanted the answer kept");
        Assert.True(selfAnswered.Segments[1].Removed == CleanedSegment.Backchannel, $"got removed {selfAnswered.Segments[1].Removed} when the same speaker answers, wanted backchannel");
        Assert.True(noQuestion.Segments[1].Removed == CleanedSegment.Backchannel, $"got removed {noQuestion.Segments[1].Removed} without a question, wanted backchannel");
    }

    [Fact]
    public void WithoutSpeakersThePreviousSegmentsQuestionCounts()
    {
        TranscriptDocument document = Build([W("Do", 0), W("we", 0.4), W("agree?", 0.8), W("Yes.", 3)]);

        Assert.True(document.Segments[1].Text == "Yes.", $"got '{document.Segments[1].Text}', wanted the answer kept");
    }

    [Fact]
    public void SpeakersAreNumberedByFirstAppearance()
    {
        TranscriptDocument document = Build([W("One.", 0), W("Two.", 2), W("Three.", 4)], [5, 2, 5]);

        Assert.True(document.Segments.Select(s => s.Speaker).SequenceEqual(["S1", "S2", "S1"]), $"got {string.Join(",", document.Segments.Select(s => s.Speaker))}");
        Assert.True(document.Speakers.Count == 2 && document.Speakers[1].Label == "Speaker 2", "got the wrong speaker list");
        Assert.True(Math.Abs(document.Speakers[0].SpeakingSeconds - 0.6) < 1e-9, $"got {document.Speakers[0].SpeakingSeconds}, wanted 0.6 summed over S1's segments");
    }

    [Fact]
    public void TheMetadataCarriesTheFactsPassedIn()
    {
        TranscriptDocument document = Build([W("Hi.", 0)]);
        TranscriptMetadata metadata = document.Metadata;

        Assert.True(document.SchemaVersion == "1", $"got schema {document.SchemaVersion}");
        Assert.True(metadata.Application == "meeting-audio-transcriptor" && metadata.ApplicationVersion == "1.2.3" && metadata.SourceFile == "meeting.wav", "got the wrong identity");
        Assert.True(metadata.DurationSeconds == 12.5 && metadata.SpeechSeconds == 6.25 && metadata.ProcessingSeconds == 3.5, "got the wrong durations");
        Assert.True(metadata.Language == "en" && metadata.CreatedUtc == "2026-11-10T18:30:05Z", $"got {metadata.Language} {metadata.CreatedUtc}");
        Assert.True(metadata.Models.Single().Name == "fake" && metadata.Settings.Threads == 4 && !metadata.Settings.SpeakerSeparation, "got the wrong models or settings");
    }
}
