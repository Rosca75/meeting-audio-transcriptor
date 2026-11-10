// Tests of the content-word retention metric: its definition, its exceptions and the cleaner-versus-metric agreement.
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Engine.Tests.Cleaning;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Scoring;

[Trait("Category", "scoring")]
public sealed class ContentRetentionTests
{
    [Theory]
    [InlineData(CleaningLanguage.English, "um so we need the the budget", "So we need the budget", 2, 2)]
    [InlineData(CleaningLanguage.English, "we need the budget today", "we need the budget", 2, 3)]
    [InlineData(CleaningLanguage.English, "pay 500 euros", "pay euros", 2, 3)]
    [InlineData(CleaningLanguage.English, "budget budget budget", "budget", 1, 1)]
    [InlineData(CleaningLanguage.English, "budget budget budget", "", 0, 1)]
    [InlineData(CleaningLanguage.English, "two two five", "two five", 2, 3)]
    [InlineData(CleaningLanguage.English, "th- the plan", "the plan", 1, 1)]
    [InlineData(CleaningLanguage.English, "we should we should leave", "we should leave", 1, 1)]
    [InlineData(CleaningLanguage.English, "yeah okay mhm", "", 0, 0)]
    [InlineData(CleaningLanguage.English, "we do not agree", "we do agree", 1, 2)]
    [InlineData(CleaningLanguage.English, "plan", "plan plan plan", 1, 1)]
    [InlineData(CleaningLanguage.French, "d'accord on signe demain", "on signe demain", 2, 2)]
    [InlineData(CleaningLanguage.French, "euh le budget est de 500 euros", "le budget est de 500 euros", 3, 3)]
    [InlineData(CleaningLanguage.German, "ähm wir müssen das Budget prüfen", "Wir müssen das Budget prüfen", 2, 2)]
    [InlineData(CleaningLanguage.German, "wir müssen das Budget prüfen", "wir müssen das Budget", 1, 2)]
    public void CountsRetainedAndTotalContentTokens(CleaningLanguage language, string raw, string cleaned, int retained, int total)
    {
        ContentRetentionResult got = ContentRetention.Compute(language, raw, cleaned);

        Assert.True(
            got.Retained == retained && got.Total == total,
            $"got {got.Retained}/{got.Total}, wanted {retained}/{total} (raw '{raw}', cleaned '{cleaned}')");
    }

    [Fact]
    public void FractionIsOneWhenTheRawTextHasNoContent()
    {
        ContentRetentionResult got = ContentRetention.Compute(CleaningLanguage.English, "um yeah", "");

        Assert.Equal(1.0, got.Fraction);
    }

    [Fact]
    public void FractionIsRetainedOverTotal()
    {
        ContentRetentionResult got = ContentRetention.Compute(CleaningLanguage.English, "we need the budget today", "we need the budget");

        Assert.Equal(2.0 / 3.0, got.Fraction, 10);
    }

    [Fact]
    public void AggregateSumsCounts()
    {
        ContentRetentionResult a = ContentRetention.Compute(CleaningLanguage.English, "budget today", "budget");
        ContentRetentionResult b = ContentRetention.Compute(CleaningLanguage.English, "plan", "plan");

        ContentRetentionResult total = ContentRetention.Aggregate([a, b]);

        Assert.True(total.Retained == 2 && total.Total == 3, $"got {total.Retained}/{total.Total}, wanted 2/3");
    }

    [Fact]
    public void WordListOverloadMatchesTheTextOverload()
    {
        ContentRetentionResult fromText = ContentRetention.Compute(CleaningLanguage.English, "we need the budget today", "we need the budget");
        ContentRetentionResult fromWords = ContentRetention.Compute(
            CleaningLanguage.English,
            ["we", "need", "the", "budget", "today"],
            ["we", "need", "the", "budget"]);

        Assert.Equal(fromText, fromWords);
    }

    [Fact]
    public void CleaningRealDisfluenciesKeepsEveryContentWord()
    {
        string raw = "Um, I I think we we should, uh, move the the review to Thursday, th- the 14th";
        CleanedSegment cleaned = new SegmentCleaner(CleaningLanguage.English).Clean(CleaningCorpus.Words(raw), false);

        ContentRetentionResult got = ContentRetention.Compute(CleaningLanguage.English, raw, cleaned.Text);

        Assert.True(got.Fraction == 1.0, $"got {got.Fraction} (cleaned '{cleaned.Text}'), wanted 1.0 (raw '{raw}')");
    }
}
