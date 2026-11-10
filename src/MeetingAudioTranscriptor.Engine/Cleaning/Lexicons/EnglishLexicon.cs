// The English word lists of the cleaning stage: fillers, backchannels, function words, numerals and allowed doublings.
namespace MeetingAudioTranscriptor.Engine.Cleaning.Lexicons;

/// <summary>The English lexicon.</summary>
/// <remarks>
/// A token is a filler only when it is almost never a content word. The discourse markers
/// <c>like</c>, <c>well</c>, <c>so</c>, <c>you know</c>, <c>I mean</c>, <c>actually</c> and <c>basically</c> are
/// not listed anywhere as removable: each has a real meaning in some sentences, and a rule cannot tell
/// which. <c>ah</c> and <c>oh</c> are left out as well, since they carry surprise or understanding.
/// Negations (<c>no</c>, <c>not</c>, <c>never</c>, <c>n't</c> forms) are neither function words nor
/// backchannels, so they count as content and can never be dropped by a rule or ignored by the metric.
/// </remarks>
internal static class EnglishLexicon
{
    /// <summary>The shared English lexicon.</summary>
    public static readonly Lexicon Instance = Lexicon.Create(
        CleaningLanguage.English,
        fillers: "um uh uhm er erm hmm mm mmm eh ehm uhh",
        inSentenceFillers: "mhm mm-hmm mm-hm uh-huh hm-hm mmhmm",
        contextFillers: "",
        contextFollowers: "",
        backchannels: "yeah|yes|yep|yup|yea|ok|okay|alright|right|sure|exactly|absolutely|indeed|mhm|mm-hmm|mm-hm|uh-huh|hm-hm|mmhmm|i see|got it|all right|i know|makes sense|fair enough|understood",
        answerWords: "yeah yes yep yup yea ok okay alright right sure exactly absolutely indeed understood",
        functionWords: """
            a an the this that these those i me my mine myself you your yours yourself yourselves he him his himself
            she her hers herself it its itself we us our ours ourselves they them their theirs themselves
            who whom whose which what where when why how am is are was were be been being have has had having
            do does did doing will would shall should can could may might must and but or if because as until
            while of at by for with about against between into through during before after above below to from
            up down in out on off over under again further then once here there all any both each few more most
            other some such only own same so than too very just also even still already really quite rather much
            many yet ever s t re ve ll d m cause gonna wanna gotta let lets
            i'm i've i'll i'd you're you've you'll you'd he's he'll he'd she's she'll she'd it's it'll we're we've
            we'll we'd they're they've they'll they'd that's there's here's what's who's let's
            another every either neither whether though although unless since whenever wherever
            """,
        numerals: """
            zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen
            seventeen eighteen nineteen twenty thirty forty fifty sixty seventy eighty ninety hundred thousand
            million billion
            """,
        repetitionAllowlist: "had that very bye so no really many more",
        capitalisedWordsAreNames: true);
}
