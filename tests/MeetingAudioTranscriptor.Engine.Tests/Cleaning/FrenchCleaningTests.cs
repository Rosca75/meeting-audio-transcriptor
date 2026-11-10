// Table-driven tests of the French cleaning rules, their exceptions, idempotence and the disfluency-free corpus.
using System.Linq;
using MeetingAudioTranscriptor.Engine.Cleaning;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Cleaning;

[Trait("Category", "cleaning")]
public sealed class FrenchCleaningTests
{
    private const CleaningLanguage Lang = CleaningLanguage.French;

    public static TheoryData<string, bool, string, string?> Cases => new()
    {
        // Rule a: fillers, and "ben" only where it is a filler.
        { "Euh, on doit partir", false, "On doit partir", null },
        { "Euhh on doit partir", false, "On doit partir", null },
        { "Euh.", false, "", "filler" },
        { "Heu... je ne sais pas", false, "Je ne sais pas", null },
        { "Hum, d'accord, on signe", false, "D'accord, on signe", null },
        { "Bonjour à tous, euh, merci d'être là", false, "Bonjour à tous, merci d'être là", null },
        { "On se voit à 14 h euh 30", false, "On se voit à 14 h 30", null },
        { "Ben je pense qu'on doit partir", false, "Je pense qu'on doit partir", null },
        { "On doit ben partir", false, "On doit partir", null },
        { "Ben a dit oui", false, "Ben a dit oui", null },
        { "Ben oui", false, "", "backchannel" },

        // Rule b: stutters and fragments.
        { "Je je pense que oui", false, "Je pense que oui", null },
        { "Le le budget est validé.", false, "Le budget est validé.", null },
        { "J'ai, j'ai vu Marie", false, "J'ai vu Marie", null },
        { "l- le plan est prêt", false, "le plan est prêt", null },
        { "Nous devons nous nous", false, "Nous devons nous nous", null },

        // Rule b exceptions.
        { "Nous nous sommes vus hier", false, "Nous nous sommes vus hier", null },
        { "Vous vous trompez", false, "Vous vous trompez", null },
        { "On a deux deux fois", false, "On a deux deux fois", null },
        { "Rendez-vous le 12 12 mars", false, "Rendez-vous le 12 12 mars", null },
        { "C'est très très important", false, "C'est très très important", null },
        { "Non non, ce n'est pas ça", false, "Non non, ce n'est pas ça", null },
        { "Marie Marie est venue", false, "Marie Marie est venue", null },

        // Rule c: false starts.
        { "On va, on doit partir", false, "On doit partir", null },
        { "Nous avons, nous devons signer", false, "Nous devons signer", null },
        { "Je ne suis pas, je pense que non", false, "Je ne suis pas, je pense que non", null },
        { "On a dit, on veut signer", false, "On a dit, on veut signer", null },

        // Rule d: backchannels and answers.
        { "Oui.", false, "", "backchannel" },
        { "Oui.", true, "Oui.", null },
        { "Non.", false, "Non.", null },
        { "Non.", true, "Non.", null },
        { "D'accord.", false, "", "backchannel" },
        { "D'accord.", true, "D'accord.", null },
        { "Ah oui.", false, "", "backchannel" },
        { "Très bien.", false, "", "backchannel" },
        { "C'est ça.", false, "", "backchannel" },
        { "Voilà", false, "", "backchannel" },
        { "Mhm.", false, "", "backchannel" },
        { "Mhm, oui, d'accord", false, "", "backchannel" },
        { "Ah oui très bien", false, "Ah oui très bien", null },
        { "Exactement ?", false, "Exactement ?", null },
        { "Je vois", true, "", "backchannel" },
        { "Oui on signe demain", false, "Oui on signe demain", null },

        // Discourse markers and negations stay.
        { "On a, du coup, décidé de partir", false, "On a, du coup, décidé de partir", null },
        { "Genre, c'est trop cher", false, "Genre, c'est trop cher", null },
        { "C'est quasi fini en fait", false, "C'est quasi fini en fait", null },
        { "Je ne suis pas d'accord", false, "Je ne suis pas d'accord", null },
        { "Jamais", false, "Jamais", null },

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
        Assert.True(CleaningCorpus.French.Length >= 60, $"got {CleaningCorpus.French.Length} sentences, wanted at least 60");
        SegmentCleaner cleaner = new(Lang);
        foreach (string sentence in CleaningCorpus.French)
        {
            CleanedSegment result = cleaner.Clean(CleaningCorpus.Words(sentence), false);
            Assert.True(
                result.Text == sentence && result.RemovedReason is null && result.Removed.All(r => !r),
                $"got '{result.Text}' (reason {result.RemovedReason ?? "null"}), wanted the sentence unchanged (input '{sentence}')");
        }
    }

    [Fact]
    public void TypographicApostrophesAreFolded()
    {
        CleanedSegment result = new SegmentCleaner(Lang).Clean(CleaningCorpus.Words("D’accord."), false);

        Assert.True(result.RemovedReason == "backchannel", $"got '{result.RemovedReason ?? "null"}', wanted 'backchannel' (input with a typographic apostrophe)");
    }
}
