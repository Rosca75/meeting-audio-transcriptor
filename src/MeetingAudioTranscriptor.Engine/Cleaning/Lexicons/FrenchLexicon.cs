// The French word lists of the cleaning stage: fillers, backchannels, function words, numerals and allowed doublings.
namespace MeetingAudioTranscriptor.Engine.Cleaning.Lexicons;

/// <summary>The French lexicon.</summary>
/// <remarks>
/// Only hesitation sounds are fillers. <c>ben</c> is also a first name, so it is a filler only in lower
/// case, or capitalised at the start of a segment before a pronoun or a discourse word. The discourse
/// markers <c>genre</c>, <c>du coup</c>, <c>quasi</c>, <c>en fait</c>, <c>bon</c> and <c>alors</c> stay,
/// because each has a meaning in some sentences. <c>non</c>, <c>pas</c>, <c>ne</c>, <c>jamais</c> and
/// <c>rien</c> are neither function words nor backchannels: negations count as content.
/// </remarks>
internal static class FrenchLexicon
{
    /// <summary>The shared French lexicon.</summary>
    public static readonly Lexicon Instance = Lexicon.Create(
        CleaningLanguage.French,
        fillers: "euh heu hum hem bah beh hein mmh mm mmm hmm",
        inSentenceFillers: "mhm mm-hmm mm-hm hm-hm",
        contextFillers: "ben",
        contextFollowers: "je tu on nous vous oui non alors bon c'est ça moi il elle ils euh",
        backchannels: "oui|ouais|ouaip|ok|okay|d'accord|d'acc|voilà|c'est ça|exactement|exact|mhm|mm-hmm|mm-hm|ah oui|très bien|tout à fait|bien sûr|absolument|je vois|entendu|compris",
        answerWords: "oui ouais ouaip ok okay d'accord d'acc exactement exact absolument sûr fait entendu compris",
        functionWords: """
            le la les l un une des du de d au aux à a ai as avons avez ont avais avait avaient eu avoir aura
            auront aurait être est suis es sommes êtes sont étais était étaient été sera seront serait soit sois
            je tu il elle on nous vous ils elles me m te t se s lui leur leurs eux moi toi soi y en
            ce cet cette ces c ça ca cela ceci celui celle ceux celles mon ma mes ton ta tes son sa ses notre nos
            votre vos qui que qu quoi dont où quand comment pourquoi si et ou mais donc or ni car puis alors
            dans sur sous avec sans pour par chez vers entre contre pendant avant après depuis selon comme
            jusqu lorsqu puisqu quoiqu lorsque puisque quoique
            très trop assez aussi encore déjà ici là peu tout tous toute toutes même autre autres
            va vais vas vont allons allez allait iront irai ira aller
            plus moins bien quel quelle quels quelles chaque quelque quelques
            """,
        numerals: """
            zéro un une deux trois quatre cinq six sept huit neuf dix onze douze treize quatorze quinze seize
            vingt trente quarante cinquante soixante septante huitante nonante cent cents mille million millions milliard
            """,
        repetitionAllowlist: "nous vous très bien non oui si bye",
        capitalisedWordsAreNames: true);
}
