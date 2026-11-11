// Tests of the plain text writer: header, turn merging, paragraphing and variants.
using System;
using System.Collections.Generic;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;
using static MeetingAudioTranscriptor.Engine.Tests.Writers.SampleTranscripts;

namespace MeetingAudioTranscriptor.Engine.Tests.Writers;

[Trait("Category", "writers")]
public sealed class PlainTextWriterTests
{
    private const string Header =
        "Transcript of réunion.wav\nDuration: 0:12:34\nLanguage: fr\nDate: 2026-11-10\nSpeakers: 2";

    [Fact]
    public void HeaderBlockDescribesTheRecording()
    {
        string txt = Txt(Document([]));

        Assert.Equal(Header + "\n", txt);
    }

    [Fact]
    public void HeaderSaysNotSeparatedWithoutSpeakers()
    {
        string txt = Txt(Document([], speakers: false, duration: 3725));

        Assert.Contains("Duration: 1:02:05", txt);
        Assert.Contains("Speakers: not separated", txt);
    }

    [Fact]
    public void HeaderSaysNoneWhenSeparationFoundNoSpeaker()
    {
        string txt = Txt(Document([]) with { Speakers = [] });

        Assert.Contains("Speakers: none", txt);
    }

    [Fact]
    public void ConsecutiveSegmentsOfOneSpeakerMergeIntoOneTurn()
    {
        string txt = Txt(Document(
        [
            Segment(1, 0, 1, "S1", "Good morning."),
            Segment(2, 1.2, 2, "S1", "Shall we start?"),
            Segment(3, 2.5, 3.5, "S2", "Yes."),
        ]));

        Assert.Equal(
            Header + "\n\n[00:00:00] Speaker 1:\nGood morning. Shall we start?\n\n[00:00:02] Speaker 2:\nYes.\n",
            txt);
    }

    [Fact]
    public void AGivenNameHeadsTheTurnsOfBothVariants()
    {
        TranscriptDocument document = Named(
        [
            Segment(1, 0, 1, "S1", "Good morning.", raw: "Um good morning."),
            Segment(2, 2.5, 3.5, "S2", "Yes."),
        ]);

        Assert.Equal(Header + "\n\n[00:00:00] Anne-Sophie Müller:\nGood morning.\n\n[00:00:02] R&D:\nYes.\n", Txt(document));
        Assert.Equal(Header + "\n\n[00:00:00] Anne-Sophie Müller:\nUm good morning.\n\n[00:00:02] R&D:\nYes.\n", Txt(document, TranscriptVariant.Raw));
    }

    [Fact]
    public void ReturningSpeakerStartsANewTurn()
    {
        string txt = Txt(Document(
        [
            Segment(1, 0, 1, "S1", "A."),
            Segment(2, 1, 2, "S2", "B."),
            Segment(3, 2, 3, "S1", "C."),
        ]));

        Assert.Equal(3, Count(txt, "] Speaker"));
    }

    [Fact]
    public void LongPauseInsideATurnStartsANewParagraph()
    {
        string txt = Txt(Document(
        [
            Segment(1, 0, 1, "S1", "First."),
            Segment(2, 3.5, 4, "S1", "Second."),
        ]));

        Assert.EndsWith("Speaker 1:\nFirst.\n\nSecond.\n", txt);
    }

    [Fact]
    public void PauseOfExactlyTwoSecondsKeepsTheParagraph()
    {
        string txt = Txt(Document(
        [
            Segment(1, 0, 1, "S1", "First."),
            Segment(2, 3, 4, "S1", "Second."),
        ]));

        Assert.EndsWith("First. Second.\n", txt);
    }

    [Fact]
    public void LongParagraphIsSplitAtASentenceBoundary()
    {
        List<TranscriptSegment> segments = [];
        for (int i = 0; i < 10; i++)
        {
            segments.Add(Segment(i + 1, i, i + 0.9, "S1", new string('x', 98) + "."));
        }

        string txt = Txt(Document(segments));

        string body = txt[(txt.IndexOf("Speaker 1:\n", StringComparison.Ordinal) + 11)..];
        string[] paragraphs = body.TrimEnd('\n').Split("\n\n");
        Assert.Equal(2, paragraphs.Length);
        Assert.True(paragraphs[0].Length > 700 && paragraphs[0].Length < 900, $"got first paragraph of {paragraphs[0].Length} characters, wanted just over 700");
    }

    [Fact]
    public void WithoutSpeakersEveryParagraphCarriesItsOwnTimestamp()
    {
        string txt = Txt(Document(
        [
            Segment(1, 0, 1, null, "One."),
            Segment(2, 1.5, 2, null, "Two."),
            Segment(3, 65, 66, null, "Three."),
        ], speakers: false));

        Assert.EndsWith("[00:00:00] One. Two.\n\n[00:01:05] Three.\n", txt);
    }

    [Fact]
    public void CleanedOmitsRemovedSegmentsAndRawKeepsThem()
    {
        TranscriptDocument document = Document(
        [
            Segment(1, 0, 1, "S1", "Hello."),
            Segment(2, 1, 2, "S2", "", raw: "Mm-hmm.", removed: "backchannel"),
        ]);

        Assert.DoesNotContain("Speaker 2", Txt(document));
        Assert.Contains("[00:00:01] Speaker 2:\nMm-hmm.", Txt(document, TranscriptVariant.Raw));
    }

    [Fact]
    public void OnlyRemovedSegmentsLeavesJustTheHeader()
    {
        TranscriptDocument document = Document([Segment(1, 0, 1, "S1", "", raw: "Mm.", removed: "backchannel")]);

        Assert.Equal(Header + "\n", Txt(document));
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
