// Integration test: cleans real meeting speech (AMI reference transcripts) and measures content retention.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Scoring;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Cleaning;

[Trait("Category", "cleaning")]
[Trait("Tier", "integration")]
public sealed class AmiTranscriptTests
{
    private const string ClipsDirectory = "/home/user/meeting-audio-transcriptor/bench/data/clips/en";

    [Fact]
    public void RealMeetingSpeechKeepsAtLeastNinetyNinePercentOfContentWords()
    {
        Assert.SkipUnless(
            Directory.Exists(ClipsDirectory),
            $"needs the benchmark clips: {ClipsDirectory} is missing; fetch the test set (bench/testset/README.md)");

        string[] files = Directory.GetFiles(ClipsDirectory, "*ami*.ref.txt");
        Assert.SkipUnless(files.Length > 0, $"needs AMI reference transcripts: none matches *ami*.ref.txt in {ClipsDirectory}");

        SegmentCleaner cleaner = new(CleaningLanguage.English);
        List<ContentRetentionResult> retention = [];
        int words = 0;
        int removedWords = 0;
        int segments = 0;
        foreach (string file in files)
        {
            bool previousIsQuestion = false;
            foreach (string[] sentence in Sentences(File.ReadAllText(file)))
            {
                string raw = string.Join(' ', sentence);
                CleanedSegment result = cleaner.Clean(CleaningCorpus.Words(raw), previousIsQuestion);
                retention.Add(ContentRetention.Compute(CleaningLanguage.English, sentence, result.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                words += sentence.Length;
                removedWords += result.Removed.Count(r => r);
                segments++;
                previousIsQuestion = sentence[^1].EndsWith('?');
            }
        }

        ContentRetentionResult total = ContentRetention.Aggregate(retention);
        string line = $"AMI cleaning: {segments} segments, {words} words, content retention {total.Fraction:F4} ({total.Retained}/{total.Total}), removed {removedWords} words ({(double)removedWords / words:P1})";
        Console.WriteLine(line);

        Assert.True(total.Fraction >= 0.99, $"got retention {total.Fraction:F4}, wanted at least 0.99 ({line})");
        Assert.True(removedWords > 0, $"got no removed word, wanted the cleaner to act on real speech ({line})");
    }

    /// <summary>Splits text into sentences: a new one starts after a word ending in a full stop, question mark or exclamation mark.</summary>
    private static IEnumerable<string[]> Sentences(string text)
    {
        List<string> current = [];
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            current.Add(word);
            if (word.EndsWith('.') || word.EndsWith('?') || word.EndsWith('!'))
            {
                yield return [.. current];
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            yield return [.. current];
        }
    }
}
