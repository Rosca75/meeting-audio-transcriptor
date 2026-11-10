// The per-language word lists the cleaning rules and the retention metric read, and the lookups built over them.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MeetingAudioTranscriptor.Engine.Cleaning.Lexicons;

/// <summary>The reviewed word lists of one language, built once and never changed.</summary>
/// <remarks>
/// Lists are written as space-separated text in the language files and parsed here, so a reviewer
/// reads words rather than collection syntax. Hesitation lists are stored squeezed (see
/// <see cref="WordKeys.Squeeze"/>) because a recogniser stretches a hesitation to any length.
/// </remarks>
internal sealed class Lexicon
{
    private readonly HashSet<string> _fillers;
    private readonly HashSet<string> _inSentenceFillers;
    private readonly HashSet<string> _contextFillers;
    private readonly HashSet<string> _contextFollowers;
    private readonly HashSet<string> _backchannelWords;
    private readonly HashSet<string> _backchannelVocabulary;
    private readonly HashSet<string> _answerWords;
    private readonly HashSet<string> _functionWords;
    private readonly HashSet<string> _numerals;
    private readonly HashSet<string> _allowlist;

    private Lexicon(
        CleaningLanguage language,
        string fillers,
        string inSentenceFillers,
        string contextFillers,
        string contextFollowers,
        string backchannels,
        string answerWords,
        string functionWords,
        string numerals,
        string repetitionAllowlist,
        bool capitalisedWordsAreNames)
    {
        Language = language;
        CapitalisedWordsAreNames = capitalisedWordsAreNames;
        _fillers = SqueezedSet(fillers);
        _inSentenceFillers = SqueezedSet(inSentenceFillers);
        _contextFillers = Set(contextFillers);
        _contextFollowers = Set(contextFollowers);
        _answerWords = Set(answerWords);
        _functionWords = Set(functionWords);
        _numerals = Set(numerals);
        _allowlist = Set(repetitionAllowlist);

        _backchannelWords = [];
        List<string[]> phrases = [];
        _backchannelVocabulary = [];
        foreach (string entry in backchannels.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] words = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1)
            {
                _backchannelWords.Add(words[0]);
            }
            else
            {
                phrases.Add(words);
            }

            foreach (string word in words)
            {
                foreach (string token in WordKeys.Tokens(word))
                {
                    _backchannelVocabulary.Add(token);
                }
            }
        }

        BackchannelPhrases = phrases;
    }

    /// <summary>The language the lists belong to.</summary>
    public CleaningLanguage Language { get; }

    /// <summary>True when a capitalised word inside a segment is read as a name, so its repetition is kept.</summary>
    /// <remarks>False for German, where every noun is capitalised and the hint carries no information.</remarks>
    public bool CapitalisedWordsAreNames { get; }

    /// <summary>The multi-word backchannel phrases, each as its words in key form.</summary>
    public IReadOnlyList<string[]> BackchannelPhrases { get; }

    /// <summary>Builds a lexicon from the language files' text lists.</summary>
    public static Lexicon Create(
        CleaningLanguage language,
        string fillers,
        string inSentenceFillers,
        string contextFillers,
        string contextFollowers,
        string backchannels,
        string answerWords,
        string functionWords,
        string numerals,
        string repetitionAllowlist,
        bool capitalisedWordsAreNames)
    {
        return new Lexicon(language, fillers, inSentenceFillers, contextFillers, contextFollowers, backchannels, answerWords, functionWords, numerals, repetitionAllowlist, capitalisedWordsAreNames);
    }

    /// <summary>The lexicon of a language.</summary>
    /// <param name="language">The language.</param>
    /// <returns>The shared instance.</returns>
    public static Lexicon For(CleaningLanguage language) => language switch
    {
        CleaningLanguage.English => EnglishLexicon.Instance,
        CleaningLanguage.French => FrenchLexicon.Instance,
        CleaningLanguage.German => GermanLexicon.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "the language is not one of the three the cleaning stage supports"),
    };

    /// <summary>True when the key is a pure hesitation sound, removed wherever it stands.</summary>
    public bool IsFiller(string key) => _fillers.Contains(WordKeys.Squeeze(key));

    /// <summary>True when the key is an acknowledgement sound that is noise only inside a sentence.</summary>
    public bool IsInSentenceFiller(string key) => _inSentenceFillers.Contains(WordKeys.Squeeze(key));

    /// <summary>True when the word may be a filler depending on its casing and its neighbour (French <c>ben</c>).</summary>
    public bool IsContextFiller(string key) => _contextFillers.Contains(key);

    /// <summary>True when a sentence-initial capitalised context filler is followed by this key.</summary>
    public bool IsContextFollower(string key) => _contextFollowers.Contains(key);

    /// <summary>True when the key is a single-word backchannel (an acknowledgement sound counts too).</summary>
    public bool IsBackchannelWord(string key) =>
        _backchannelWords.Contains(key) || IsInSentenceFiller(key) || IsFiller(key);

    /// <summary>True when the key can take part in a backchannel, as a word or as a part of a phrase.</summary>
    public bool IsBackchannelVocabulary(string key) =>
        IsBackchannelWord(key) || WordKeys.Tokens(key).All(_backchannelVocabulary.Contains);

    /// <summary>True when the key is a word that answers a question (yes, ok, oui, ja).</summary>
    public bool IsAnswerWord(string key) => _answerWords.Contains(key) || IsInSentenceFiller(key);

    /// <summary>True when the key, or every part of it, is a function word.</summary>
    public bool IsFunctionKey(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        if (_functionWords.Contains(key))
        {
            return true;
        }

        string[] parts = WordKeys.Tokens(key);
        return parts.Length > 0 && parts.All(_functionWords.Contains);
    }

    /// <summary>True when the key is a number word.</summary>
    public bool IsNumeral(string key) => _numerals.Contains(key);

    /// <summary>True when a doubled occurrence of the key is a legitimate construction and is kept.</summary>
    public bool IsRepetitionAllowed(string key) => _allowlist.Contains(key);

    /// <summary>
    /// True when a lower-case token carries no content for the retention metric: a function word, a
    /// hesitation or a part of the backchannel list. A token with a digit always carries content.
    /// </summary>
    /// <param name="token">A normalised token.</param>
    public bool IsNonContentToken(string token)
    {
        if (WordKeys.HasDigit(token))
        {
            return false;
        }

        return _functionWords.Contains(token) || IsFiller(token) || IsInSentenceFiller(token)
            || _backchannelVocabulary.Contains(token) || _backchannelWords.Contains(token);
    }

    private static HashSet<string> Set(string text) =>
        new(text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static HashSet<string> SqueezedSet(string text) =>
        new(text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(WordKeys.Squeeze), StringComparer.Ordinal);
}
