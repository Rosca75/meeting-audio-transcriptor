// Table tests of where the segment builder cuts: sentence ends, pauses, speaker changes and the maximum length.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Pipeline;

[Trait("Category", "transcript")]
public sealed class SegmentBuilderTests
{
    private static readonly SegmentationOptions Defaults = new();

    /// <summary>Words 0.3 s long with a 0.1 s gap, or the gap a "|" before a word asks for (in tenths: "|15|word" is 1.5 s).</summary>
    private static List<RecognizedWord> Words(string script)
    {
        List<RecognizedWord> words = [];
        double time = 0;
        foreach (string token in script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string text = token;
            double gap = 0.1;
            if (token.StartsWith('|'))
            {
                int close = token.IndexOf('|', 1);
                gap = int.Parse(token[1..close], System.Globalization.CultureInfo.InvariantCulture) / 10.0;
                text = token[(close + 1)..];
            }

            double start = words.Count == 0 ? 0 : time + gap;
            words.Add(new RecognizedWord(text, start, start + 0.3, 0.9f));
            time = start + 0.3;
        }

        return words;
    }

    private static string Cut(IReadOnlyList<RecognizedWord> words, IReadOnlyList<int>? speakers = null, SegmentationOptions? options = null)
    {
        List<WordRange> ranges = SegmentBuilder.Build(words, speakers, options ?? Defaults);
        return string.Join(" / ", ranges.Select(range => string.Join(' ', words.Skip(range.First).Take(range.Count).Select(w => w.Text))));
    }

    [Theory]
    [InlineData("so we agreed. then we left", "so we agreed. / then we left")]
    [InlineData("is it done? yes it is! great", "is it done? / yes it is! / great")]
    [InlineData("well… maybe later", "well… / maybe later")]
    [InlineData("well... maybe later", "well... / maybe later")]
    [InlineData("he said \"stop.\" and left", "he said \"stop.\" / and left")]
    [InlineData("ask Mr. Smith and Dr. Jones", "ask Mr. Smith and Dr. Jones")]
    [InlineData("J. R. R. Tolkien wrote it", "J. R. R. Tolkien wrote it")]
    [InlineData("then I. we went", "then I. / we went")]
    [InlineData("z.B. heute nicht", "z.B. heute nicht")]
    [InlineData("am 3. Mai treffen wir uns", "am 3. Mai treffen wir uns")]
    [InlineData("we sold 30. |5|next item", "we sold 30. / next item")]
    [InlineData("the value is 3.5 percent", "the value is 3.5 percent")]
    public void PunctuationEndsASentence(string script, string expected)
    {
        string got = Cut(Words(script));

        Assert.True(got == expected, $"got '{got}', wanted '{expected}' for '{script}'");
    }

    [Theory]
    [InlineData("no punctuation here |11|but a pause", "no punctuation here / but a pause")]
    [InlineData("a short |9|pause stays", "a short pause stays")]
    [InlineData("one |25|two |30|three", "one / two / three")]
    public void APauseEndsASegment(string script, string expected)
    {
        string got = Cut(Words(script));

        Assert.True(got == expected, $"got '{got}', wanted '{expected}' for '{script}'");
    }

    [Fact]
    public void ASpeakerChangeEndsASegment()
    {
        List<RecognizedWord> words = Words("hello there how are you fine thanks");
        int[] speakers = [0, 0, 1, 1, 1, 0, 0];

        string got = Cut(words, speakers);

        Assert.True(got == "hello there / how are you / fine thanks", $"got '{got}'");
    }

    [Fact]
    public void ALongRunWithoutPunctuationIsSplitAtItsLargestGap()
    {
        // 60 words with one gap of 0.6 s after word 24 and another of 0.5 s after word 44.
        string script = string.Join(' ', Enumerable.Range(0, 60).Select(i => i switch
        {
            25 => "|6|w25",
            45 => "|5|w45",
            _ => "w" + i,
        }));
        List<RecognizedWord> words = Words(script);

        List<WordRange> ranges = SegmentBuilder.Build(words, null, Defaults);

        Assert.True(ranges.All(range => range.Count <= Defaults.MaxWords), $"got segment sizes {string.Join(",", ranges.Select(r => r.Count))}, wanted none over {Defaults.MaxWords}");
        Assert.True(ranges[0] == new WordRange(0, 25), $"got the first segment {ranges[0]}, wanted the cut at the largest gap after word 24");
        Assert.True(ranges.Sum(r => r.Count) == 60 && ranges.Zip(ranges.Skip(1)).All(p => p.First.End == p.Second.First), "the segments should cover every word once, in order");
    }

    [Fact]
    public void ALongRunIsSplitByDurationToo()
    {
        // 20 slow words of 2 s each: 40 s with no gap, so only the duration limit cuts.
        List<RecognizedWord> words = Enumerable.Range(0, 20).Select(i => new RecognizedWord("w" + i, i * 2.0, (i * 2.0) + 2.0, 0.9f)).ToList();

        List<WordRange> ranges = SegmentBuilder.Build(words, null, Defaults);

        Assert.True(ranges.Count == 2 && ranges[0].Count == 10, $"got sizes {string.Join(",", ranges.Select(r => r.Count))}, wanted two halves of 20 s");
    }

    [Fact]
    public void EveryWordLandsInExactlyOneNonEmptySegment()
    {
        Random random = new(11);
        List<RecognizedWord> words = [];
        double time = 0;
        for (int i = 0; i < 500; i++)
        {
            time += random.NextDouble() * 1.5;
            string text = random.Next(8) == 0 ? "end." : "word";
            words.Add(new RecognizedWord(text, time, time + 0.2, 0.9f));
            time += 0.2;
        }

        List<WordRange> ranges = SegmentBuilder.Build(words, null, Defaults);

        Assert.True(ranges.All(r => r.Count >= 1), "got an empty segment");
        Assert.True(ranges.Sum(r => r.Count) == 500 && ranges[0].First == 0 && ranges.Zip(ranges.Skip(1)).All(p => p.First.End == p.Second.First), "the segments should tile the words");
    }

    [Fact]
    public void NoWordsGiveNoSegments()
    {
        Assert.True(SegmentBuilder.Build([], null, Defaults).Count == 0, "got segments from no words");
    }
}
