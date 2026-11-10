// The German word lists of the cleaning stage: fillers, backchannels, function words, numerals and allowed doublings.
namespace MeetingAudioTranscriptor.Engine.Cleaning.Lexicons;

/// <summary>The German lexicon.</summary>
/// <remarks>
/// Only hesitation sounds are fillers. <c>er</c> is the pronoun "he" and is never a filler, and
/// <c>also</c>, <c>halt</c>, <c>eben</c>, <c>sozusagen</c>, <c>quasi</c> and <c>ja</c> inside a sentence are
/// discourse markers with meaning, so they stay. <c>nicht</c>, <c>kein</c>, <c>nie</c> and <c>nein</c> are
/// negations: content for the metric and never a backchannel. Capitalisation says nothing about names in
/// German (every noun is capitalised), so the name guard of the repetition rule is off.
/// </remarks>
internal static class GermanLexicon
{
    /// <summary>The shared German lexicon.</summary>
    public static readonly Lexicon Instance = Lexicon.Create(
        CleaningLanguage.German,
        fillers: "äh ähm öhm ehm hm hmm mm mmm tja",
        inSentenceFillers: "mhm mm-hmm mm-hm hm-hm",
        contextFillers: "",
        contextFollowers: "",
        backchannels: "ja|jap|jawohl|genau|ok|okay|mhm|mm-hmm|mm-hm|stimmt|richtig|klar|aha|alles klar|ach so|na gut|in ordnung|ich verstehe|verstehe|stimmt genau|ja genau",
        answerWords: "ja jap jawohl genau ok okay stimmt richtig klar ordnung verstehe",
        functionWords: """
            der die das den dem des ein eine einen einem einer eines ich du er sie es wir ihr mich dich ihn uns euch
            ihnen mir dir ihm mein meine meinen meinem meiner dein deine sein seine seinen seinem seiner ihre
            ihren ihrem ihrer unser unsere unseren euer eure man sich selbst dieser diese dieses diesen diesem
            welche welcher welches welchen wer wen wem wessen was wo wann warum wie wieso weshalb wohin woher
            und oder aber denn doch sondern weil dass daß ob wenn als obwohl damit bevor nachdem während bis
            in an auf aus bei mit nach von zu zum zur vor über unter durch für gegen ohne um seit zwischen im am
            ans ins vom beim bin bist ist sind seid war waren wäre wären gewesen werden werde wirst wird wurde
            wurden worden geworden haben habe hast hat habt hatte hatten gehabt hätte hätten
            können kann kannst könnt konnte könnte müssen muss musst müsst musste müsste sollen soll sollst sollte
            wollen will willst wollt wollte dürfen darf durfte möchte möchten
            auch noch schon nur sehr so dann da hier dort jetzt nun immer mal eben wieder ganz etwas viel mehr
            also halt denn mal gar ziemlich eigentlich vielleicht
            alle alles jede jeder jedes jeden andere anderen anderer
            """,
        numerals: """
            null eins ein eine zwei zwo drei vier fünf sechs sieben acht neun zehn elf zwölf dreizehn vierzehn
            fünfzehn sechzehn siebzehn achtzehn neunzehn zwanzig dreißig dreissig vierzig fünfzig sechzig
            siebzig achtzig neunzig hundert tausend million millionen milliarde milliarden
            """,
        repetitionAllowlist: "die der das den dem sie sehr nein ja bye so",
        capitalisedWordsAreNames: false);
}
