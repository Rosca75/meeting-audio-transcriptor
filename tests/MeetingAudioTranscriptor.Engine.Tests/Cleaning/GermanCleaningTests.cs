// Table-driven tests of the German cleaning rules, their exceptions, idempotence and the disfluency-free corpus.
using System.Linq;
using MeetingAudioTranscriptor.Engine.Cleaning;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Cleaning;

[Trait("Category", "cleaning")]
public sealed class GermanCleaningTests
{
    private const CleaningLanguage Lang = CleaningLanguage.German;

    public static TheoryData<string, bool, string, string?> Cases => new()
    {
        // Rule a: fillers. "er" is a pronoun, never a filler.
        { "Ähm, wir müssen das prüfen", false, "Wir müssen das prüfen", null },
        { "Äh ähm wir müssen", false, "Wir müssen", null },
        { "Ähhh, wir müssen", false, "Wir müssen", null },
        { "Tja, das war's", false, "Das war's", null },
        { "Hmm, ich weiß nicht", false, "Ich weiß nicht", null },
        { "Wir müssen, äh, bis Freitag liefern.", false, "Wir müssen, bis Freitag liefern.", null },
        { "Ich glaube, äh.", false, "Ich glaube.", null },
        { "Äh.", false, "", "filler" },
        { "Öhm... wir starten Montag", false, "Wir starten Montag", null },
        { "Er kommt morgen", false, "Er kommt morgen", null },

        // Rule b: stutters and fragments.
        { "Wir wir müssen das prüfen", false, "Wir müssen das prüfen", null },
        { "Er er kommt morgen", false, "Er kommt morgen", null },
        { "Wir wir wir haben haben Zeit", false, "Wir haben Zeit", null },
        { "Wi- wir sollten gehen", false, "Wir sollten gehen", null },
        { "Wir sollten wi- wir gehen", false, "Wir sollten wir gehen", null },
        { "Ich denke ich denke wir warten", false, "Ich denke wir warten", null },

        // Rule b exceptions.
        { "Die die Kosten steigen", false, "Die die Kosten steigen", null },
        { "Der der Vertrag ist fertig", false, "Der der Vertrag ist fertig", null },
        { "Das das stimmt", false, "Das das stimmt", null },
        { "Sie sie wissen es", false, "Sie sie wissen es", null },
        { "Zwei zwei fünf", false, "Zwei zwei fünf", null },
        { "Wir brauchen 5 5 Stück", false, "Wir brauchen 5 5 Stück", null },
        { "Sehr sehr gut", false, "Sehr sehr gut", null },
        { "Nein nein, das stimmt nicht", false, "Nein nein, das stimmt nicht", null },

        // Rule c: false starts.
        { "Wir haben, wir müssen das prüfen", false, "Wir müssen das prüfen", null },
        { "Wir sind, wir müssen gehen", false, "Wir müssen gehen", null },
        { "Wir haben nicht, wir müssen gehen", false, "Wir haben nicht, wir müssen gehen", null },
        { "Wir haben 3, wir brauchen 4", false, "Wir haben 3, wir brauchen 4", null },

        // Rule d: backchannels and answers.
        { "Ja.", false, "", "backchannel" },
        { "Ja.", true, "Ja.", null },
        { "Nein.", false, "Nein.", null },
        { "Nein.", true, "Nein.", null },
        { "Genau.", false, "", "backchannel" },
        { "Genau.", true, "Genau.", null },
        { "Alles klar.", false, "", "backchannel" },
        { "Ach so.", false, "", "backchannel" },
        { "Na gut, okay.", false, "", "backchannel" },
        { "Richtig.", false, "", "backchannel" },
        { "Mhm, genau", false, "", "backchannel" },
        { "Ja genau, okay, stimmt", false, "Ja genau, okay, stimmt", null },
        { "Okay?", false, "Okay?", null },
        { "Ja, wir machen das", false, "Ja, wir machen das", null },
        { "Verstehe", false, "", "backchannel" },
        { "Verstehe", true, "Verstehe", null },

        // Discourse markers and negations stay.
        { "Wir sind sozusagen fertig", false, "Wir sind sozusagen fertig", null },
        { "Das ist halt so", false, "Das ist halt so", null },
        { "Also wir starten Montag", false, "Also wir starten Montag", null },
        { "Das ist nicht richtig", false, "Das ist nicht richtig", null },
        { "Niemals", false, "Niemals", null },

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
        Assert.True(CleaningCorpus.German.Length >= 60, $"got {CleaningCorpus.German.Length} sentences, wanted at least 60");
        SegmentCleaner cleaner = new(Lang);
        foreach (string sentence in CleaningCorpus.German)
        {
            CleanedSegment result = cleaner.Clean(CleaningCorpus.Words(sentence), false);
            Assert.True(
                result.Text == sentence && result.RemovedReason is null && result.Removed.All(r => !r),
                $"got '{result.Text}' (reason {result.RemovedReason ?? "null"}), wanted the sentence unchanged (input '{sentence}')");
        }
    }
}
