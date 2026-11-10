// Table-driven tests of the English cleaning rules, their exceptions, idempotence and the disfluency-free corpus.
using System.Linq;
using MeetingAudioTranscriptor.Engine.Cleaning;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Cleaning;

[Trait("Category", "cleaning")]
public sealed class EnglishCleaningTests
{
    private const CleaningLanguage Lang = CleaningLanguage.English;

    public static TheoryData<string, bool, string, string?> Cases => new()
    {
        // Rule a: fillers.
        { "Um, so we should go", false, "So we should go", null },
        { "Umm, uh, erm", false, "", "filler" },
        { "Uh... we should go", false, "We should go", null },
        { "Ummm we should go", false, "We should go", null },
        { "We should, uh, go home.", false, "We should, go home.", null },
        { "We should go, um.", false, "We should go.", null },
        { "so, um", false, "so", null },
        { "Mhm, we agree", false, "We agree", null },
        { "Hmm, the numbers look fine", false, "The numbers look fine", null },
        { "Erm the budget is 40,000 euros", false, "The budget is 40,000 euros", null },

        // Rule b: stutters, repetitions, fragments.
        { "I I think we should go.", false, "I think we should go.", null },
        { "We we we need the budget.", false, "We need the budget.", null },
        { "I think I think we should wait.", false, "I think we should wait.", null },
        { "The the plan is ready", false, "The plan is ready", null },
        { "th- the plan is ready", false, "the plan is ready", null },
        { "We should th- the plan", false, "We should the plan", null },
        { "We are, we are going", false, "We are going", null },

        // Rule b exceptions: numbers, allowlist, names, sentence ends.
        { "two two five", false, "two two five", null },
        { "Call 555 555 tomorrow", false, "Call 555 555 tomorrow", null },
        { "We had had enough", false, "We had had enough", null },
        { "I said that that is wrong", false, "I said that that is wrong", null },
        { "It is very very important", false, "It is very very important", null },
        { "He said no, no, no", false, "He said no, no, no", null },
        { "Anna Anna called", false, "Anna Anna called", null },
        { "We need it by March 3, March 3", false, "We need it by March 3, March 3", null },
        { "Yes. Yes we will", true, "Yes. Yes we will", null },

        // Rule c: abandoned false starts.
        { "I was, I think we should wait", false, "I think we should wait", null },
        { "We have, we need more time", false, "We need more time", null },
        { "I was not, I think we", false, "I was not, I think we", null },
        { "I said, I think we should", false, "I said, I think we should", null },
        { "We are 3, we think so", false, "We are 3, we think so", null },

        // Rule d: backchannels and answers.
        { "Yeah.", false, "", "backchannel" },
        { "Yeah.", true, "Yeah.", null },
        { "Okay, got it.", false, "", "backchannel" },
        { "Mm-hmm.", false, "", "backchannel" },
        { "Uh-huh", false, "", "backchannel" },
        { "Yes, I see", false, "", "backchannel" },
        { "I see", true, "", "backchannel" },
        { "Right?", false, "Right?", null },
        { "No.", false, "No.", null },
        { "No.", true, "No.", null },
        { "Okay, yeah, right, sure", false, "Okay, yeah, right, sure", null },
        { "Yes we will", false, "Yes we will", null },
        { "Um, yeah", false, "", "backchannel" },

        // Discourse markers stay.
        { "We like pizza, you know, and well, so", false, "We like pizza, you know, and well, so", null },
        { "I mean we should go", false, "I mean we should go", null },
        { "So basically it is actually fine", false, "So basically it is actually fine", null },
        { "It is, like, really good", false, "It is, like, really good", null },

        // Negations are content.
        { "We do not agree", false, "We do not agree", null },
        { "Never mind", false, "Never mind", null },

        // Empty input.
        { "", false, "", "empty" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void CleansEachCase(string input, bool previousIsQuestion, string expectedText, string? expectedReason) =>
        CleaningAssert.Cleans(Lang, input, previousIsQuestion, expectedText, expectedReason);

    [Theory]
    [MemberData(nameof(Cases))]
    public void CleaningCleanedWordsChangesNothing(string input, bool previousIsQuestion, string expectedText, string? expectedReason)
    {
        _ = (expectedText, expectedReason);
        CleaningAssert.IsIdempotent(Lang, input, previousIsQuestion);
    }

    [Fact]
    public void SentencesWithoutDisfluencyLoseNothing()
    {
        Assert.True(CleaningCorpus.English.Length >= 60, $"got {CleaningCorpus.English.Length} sentences, wanted at least 60");
        SegmentCleaner cleaner = new(Lang);
        foreach (string sentence in CleaningCorpus.English)
        {
            CleanedSegment result = cleaner.Clean(CleaningCorpus.Words(sentence), false);
            Assert.True(
                result.Text == sentence && result.RemovedReason is null && result.Removed.All(r => !r),
                $"got '{result.Text}' (reason {result.RemovedReason ?? "null"}), wanted the sentence unchanged (input '{sentence}')");
        }
    }

    [Fact]
    public void RemovedFlagsMarkExactlyTheDroppedWords()
    {
        CleanedSegment result = new SegmentCleaner(Lang).Clean(CleaningCorpus.Words("Um, I I think we should"), false);

        Assert.Equal([true, true, false, false, false, false], result.Removed);
        Assert.Equal("Um, I I think we should", result.RawText);
    }

    [Fact]
    public void WordsAreNeverRewrittenBeyondTheFirstCapital()
    {
        CleanedSegment result = new SegmentCleaner(Lang).Clean(CleaningCorpus.Words("Uh, iPhone sales rose 12%, Anna said."), false);

        Assert.True(result.Text == "IPhone sales rose 12%, Anna said.", $"got '{result.Text}', wanted only the first letter changed");
    }

    [Fact]
    public void AllFillersOfAStretchedFormAreRecognised()
    {
        foreach (string filler in new[] { "um", "UM", "Uhhh", "ERM", "Err", "ummmm", "Hmmm" })
        {
            CleanedSegment result = new SegmentCleaner(Lang).Clean(CleaningCorpus.Words("so " + filler + " go"), false);
            Assert.True(result.Text == "so go", $"got '{result.Text}', wanted 'so go' (filler '{filler}')");
        }
    }

    [Fact]
    public void ACleanerIsSafeToShareAcrossThreads()
    {
        SegmentCleaner cleaner = new(Lang);
        string[] results = Enumerable.Range(0, 64).AsParallel()
            .Select(_ => cleaner.Clean(CleaningCorpus.Words("Um, I I think we should"), false).Text)
            .ToArray();

        Assert.All(results, text => Assert.Equal("I think we should", text));
    }
}
