// Tests of language-code parsing and of the cleaner's argument handling.
using System;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Cleaning;

[Trait("Category", "cleaning")]
public sealed class CleaningLanguageTests
{
    [Theory]
    [InlineData("en", CleaningLanguage.English)]
    [InlineData("FR", CleaningLanguage.French)]
    [InlineData(" de ", CleaningLanguage.German)]
    public void ParsesTheThreeCodes(string code, CleaningLanguage expected)
    {
        CleaningLanguage got = CleaningLanguages.Parse(code);

        Assert.True(got == expected, $"got {got}, wanted {expected} (code '{code}')");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("")]
    [InlineData("english")]
    public void RejectsOtherCodesWithAnActionableMessage(string code)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => CleaningLanguages.Parse(code));

        Assert.Contains("'en', 'fr' or 'de'", error.Message);
        Assert.False(CleaningLanguages.TryParse(code, out _), $"got a parse of '{code}', wanted a refusal");
    }

    [Fact]
    public void TryParseRefusesNull() => Assert.False(CleaningLanguages.TryParse(null, out _));

    [Theory]
    [InlineData(CleaningLanguage.English, "en")]
    [InlineData(CleaningLanguage.French, "fr")]
    [InlineData(CleaningLanguage.German, "de")]
    public void CodeRoundTrips(CleaningLanguage language, string code)
    {
        Assert.Equal(code, CleaningLanguages.Code(language));
        Assert.Equal(language, CleaningLanguages.Parse(code));
    }

    [Fact]
    public void CleanRefusesNullWords()
    {
        SegmentCleaner cleaner = new(CleaningLanguage.English);

        Assert.Throws<ArgumentNullException>(() => cleaner.Clean(null!, false));
    }

    [Fact]
    public void EmptySegmentIsReportedEmpty()
    {
        CleanedSegment result = new SegmentCleaner(CleaningLanguage.French).Clean(Array.Empty<RecognizedWord>(), false);

        Assert.True(result.RemovedReason == "empty" && result.Text == "" && result.Removed.Length == 0 && result.RawText == "",
            $"got reason '{result.RemovedReason}', text '{result.Text}', wanted an empty segment");
    }
}
