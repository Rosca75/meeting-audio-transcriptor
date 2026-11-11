// Tests of the WebVTT writer: timestamps, voice tags, escaping, variants and cue repair.
using System.Linq;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;
using static MeetingAudioTranscriptor.Engine.Tests.Writers.SampleTranscripts;

namespace MeetingAudioTranscriptor.Engine.Tests.Writers;

[Trait("Category", "writers")]
public sealed class VttWriterTests
{
    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(1.5, "00:00:01.500")]
    [InlineData(59.9995, "00:01:00.000")]
    [InlineData(3725.25, "01:02:05.250")]
    [InlineData(36000.001, "10:00:00.001")]
    [InlineData(37230.0, "10:20:30.000")]
    public void TimestampsAreFormattedAndRounded(double start, string expected)
    {
        string vtt = Vtt(Document([Segment(1, start, start + 5, "S1", "Hello.")]));

        Assert.True(vtt.Contains(expected + " -->"), $"got {vtt}, wanted a cue starting at {expected} for start {start}");
    }

    [Fact]
    public void CueHasIdentifierTimingAndVoiceTag()
    {
        string vtt = Vtt(Document([Segment(7, 1, 2.5, "S2", "Bonjour.")]));

        Assert.Equal("WEBVTT\n\n7\n00:00:01.000 --> 00:00:02.500\n<v Speaker 2>Bonjour.</v>\n", vtt);
    }

    [Fact]
    public void AGivenNameIsTheVoiceOfBothVariants()
    {
        TranscriptDocument document = Named([Segment(7, 1, 2.5, "S1", "Bonjour.", raw: "Euh bonjour."), Segment(8, 3, 4, "S2", "Salut.")]);

        Assert.Equal(
            "WEBVTT\n\n7\n00:00:01.000 --> 00:00:02.500\n<v Anne-Sophie Müller>Bonjour.</v>\n\n8\n00:00:03.000 --> 00:00:04.000\n<v R&amp;D>Salut.</v>\n",
            Vtt(document));
        Assert.Equal(
            "WEBVTT\n\n7\n00:00:01.000 --> 00:00:02.500\n<v Anne-Sophie Müller>Euh bonjour.</v>\n\n8\n00:00:03.000 --> 00:00:04.000\n<v R&amp;D>Salut.</v>\n",
            Vtt(document, TranscriptVariant.Raw));
    }

    [Fact]
    public void NoVoiceTagWhenSegmentHasNoSpeaker()
    {
        string vtt = Vtt(Document([Segment(1, 0, 1, null, "Hello.")], speakers: false));

        Assert.Equal("WEBVTT\n\n1\n00:00:00.000 --> 00:00:01.000\nHello.\n", vtt);
    }

    [Fact]
    public void UnlistedSpeakerFallsBackToItsId()
    {
        string vtt = Vtt(Document([Segment(1, 0, 1, "S9", "Hi.")]));

        Assert.Contains("<v S9>Hi.</v>", vtt);
    }

    [Fact]
    public void TextAndLabelAreEscaped()
    {
        TranscriptDocument document = Document([Segment(1, 0, 1, "S1", "a < b & c --> d")]);

        string vtt = Vtt(document);

        Assert.Contains("<v Speaker 1>a &lt; b &amp; c --&gt; d</v>", vtt);
    }

    [Fact]
    public void LineBreaksInTextBecomeSpaces()
    {
        string vtt = Vtt(Document([Segment(1, 0, 1, null, "one\n\ntwo")], speakers: false));

        Assert.Contains("\none two\n", vtt);
    }

    [Fact]
    public void CleanedSkipsRemovedSegmentsAndRawKeepsThem()
    {
        TranscriptDocument document = Document(
        [
            Segment(1, 0, 1, "S1", "Hello."),
            Segment(2, 1, 2, "S2", "", raw: "Mm-hmm.", removed: "backchannel"),
        ]);

        string cleaned = Vtt(document);
        string raw = Vtt(document, TranscriptVariant.Raw);

        Assert.DoesNotContain("Mm-hmm", cleaned);
        Assert.DoesNotContain("\n2\n", cleaned);
        Assert.Contains("\n2\n00:00:01.000 --> 00:00:02.000\n<v Speaker 2>Mm-hmm.</v>", raw);
    }

    [Fact]
    public void RawVariantUsesRawTextWhereItDiffers()
    {
        TranscriptDocument document = Document([Segment(1, 0, 1, "S1", "Hello.", raw: "uh hello")]);

        Assert.Contains("uh hello", Vtt(document, TranscriptVariant.Raw));
        Assert.DoesNotContain("uh hello", Vtt(document));
    }

    [Fact]
    public void EveryCueEndsAfterItStartsEvenForZeroLengthSegments()
    {
        string vtt = Vtt(Document([Segment(1, 5, 5, "S1", "Hi."), Segment(2, 6, 4, "S1", "There.")]));

        Assert.Contains("00:00:05.000 --> 00:00:05.010", vtt);
        Assert.Contains("00:00:06.000 --> 00:00:06.010", vtt);
    }

    [Fact]
    public void StartTimesNeverDecrease()
    {
        string vtt = Vtt(Document([Segment(1, 10, 12, "S1", "A."), Segment(2, 9, 13, "S1", "B.")]));

        string[] starts = vtt.Split('\n').Where(l => l.Contains("-->")).Select(l => l[..12]).ToArray();
        Assert.Equal(["00:00:10.000", "00:00:10.000"], starts);
    }
}
