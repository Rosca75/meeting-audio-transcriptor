// The content-word retention metric: how much of the raw transcript's content survives cleaning.
using System;
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Cleaning.Lexicons;

namespace MeetingAudioTranscriptor.Engine.Scoring;

/// <summary>The counts behind a retention figure.</summary>
/// <param name="Retained">Content tokens of the raw text still present in the cleaned text.</param>
/// <param name="Total">Content tokens of the raw text after immediate repeats are collapsed.</param>
public readonly record struct ContentRetentionResult(int Retained, int Total)
{
    /// <summary>Retained divided by total; 1 when the raw text has no content token.</summary>
    public double Fraction => Total == 0 ? 1.0 : (double)Retained / Total;
}

/// <summary>Measures how many content words cleaning kept.</summary>
/// <remarks>
/// <para>This is a published gate: cleaned against raw must reach 0.99 on the test set.</para>
/// <para>
/// A <b>content token</b> is a token of <see cref="TextNormalizer"/> that is not a function word, not
/// a hesitation and not part of the language's backchannel list; every token containing a digit is
/// content, and so are negations and number words. A word that is a stutter fragment (it ends in a
/// hyphen and the next word starts with its letters) has no content token, because the cleaner
/// removes it by design.
/// </para>
/// <para>
/// The raw content tokens are listed in order and every immediate repeat of one token, or of a run of
/// two or three tokens, is collapsed to one (a token with a digit or a number word is never collapsed),
/// because the stutter rule removes those on purpose. The collapsed list has <c>Total</c> positions.
/// For each distinct token, <c>min(count in cleaned, count in collapsed raw)</c> is retained, and
/// <c>Retained</c> is the sum. The fraction is <c>Retained / Total</c>. A removed content word, or a
/// duplicate removed beyond the collapse, lowers it; a token present more often in the cleaned text
/// than in the raw does not raise it.
/// </para>
/// </remarks>
public static class ContentRetention
{
    /// <summary>The longest run of tokens collapsed as a repeat; the same as the stutter rule's.</summary>
    private const int MaxRun = 3;

    /// <summary>Computes retention over two word lists.</summary>
    /// <param name="language">The language whose lists define a content word.</param>
    /// <param name="rawWords">The words of the raw text, as written (hyphens attached).</param>
    /// <param name="cleanedWords">The words of the cleaned text.</param>
    /// <returns>The counts and the fraction.</returns>
    public static ContentRetentionResult Compute(
        CleaningLanguage language,
        IReadOnlyList<string> rawWords,
        IReadOnlyList<string> cleanedWords)
    {
        ArgumentNullException.ThrowIfNull(rawWords);
        ArgumentNullException.ThrowIfNull(cleanedWords);

        Lexicon lexicon = Lexicon.For(language);
        List<string> raw = CollapseRepeats(ContentTokens(lexicon, rawWords), lexicon);
        List<string> cleaned = ContentTokens(lexicon, cleanedWords);

        Dictionary<string, int> rawCounts = Count(raw);
        Dictionary<string, int> cleanedCounts = Count(cleaned);
        int retained = 0;
        foreach ((string token, int rawCount) in rawCounts)
        {
            retained += Math.Min(rawCount, cleanedCounts.GetValueOrDefault(token));
        }

        return new ContentRetentionResult(retained, raw.Count);
    }

    /// <summary>Computes retention over two texts split on white space.</summary>
    /// <param name="language">The language whose lists define a content word.</param>
    /// <param name="rawText">The raw text.</param>
    /// <param name="cleanedText">The cleaned text.</param>
    /// <returns>The counts and the fraction.</returns>
    public static ContentRetentionResult Compute(CleaningLanguage language, string rawText, string cleanedText)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentNullException.ThrowIfNull(cleanedText);
        char[]? whitespace = null;
        return Compute(
            language,
            rawText.Split(whitespace, StringSplitOptions.RemoveEmptyEntries),
            cleanedText.Split(whitespace, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Sums the counts of several results, so the fraction is a micro-average over the set.</summary>
    /// <param name="results">The per-item results.</param>
    /// <returns>The summed counts.</returns>
    public static ContentRetentionResult Aggregate(IEnumerable<ContentRetentionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        int retained = 0;
        int total = 0;
        foreach (ContentRetentionResult result in results)
        {
            retained += result.Retained;
            total += result.Total;
        }

        return new ContentRetentionResult(retained, total);
    }

    private static List<string> ContentTokens(Lexicon lexicon, IReadOnlyList<string> words)
    {
        List<string> tokens = [];
        for (int i = 0; i < words.Count; i++)
        {
            if (IsFragment(words, i))
            {
                continue;
            }

            foreach (string token in TextNormalizer.Normalize(words[i]))
            {
                if (!lexicon.IsNonContentToken(token))
                {
                    tokens.Add(token);
                }
            }
        }

        return tokens;
    }

    private static bool IsFragment(IReadOnlyList<string> words, int i)
    {
        string word = words[i];
        if (i + 1 >= words.Count || !(word.EndsWith('-') || word.EndsWith('–')))
        {
            return false;
        }

        string fragment = WordKeys.Key(word);
        return fragment.Length > 0
            && !WordKeys.HasDigit(fragment)
            && WordKeys.Key(words[i + 1]).StartsWith(fragment, StringComparison.Ordinal);
    }

    private static List<string> CollapseRepeats(List<string> tokens, Lexicon lexicon)
    {
        for (int p = 0; p < tokens.Count;)
        {
            bool collapsed = false;
            for (int len = Math.Min(MaxRun, (tokens.Count - p) / 2); len >= 1 && !collapsed; len--)
            {
                if (IsRepeat(tokens, p, len, lexicon))
                {
                    tokens.RemoveRange(p, len);
                    collapsed = true;
                }
            }

            if (!collapsed)
            {
                p++;
            }
        }

        return tokens;
    }

    private static bool IsRepeat(List<string> tokens, int p, int len, Lexicon lexicon)
    {
        for (int k = 0; k < len; k++)
        {
            if (tokens[p + k] != tokens[p + len + k] || WordKeys.HasDigit(tokens[p + k]) || lexicon.IsNumeral(tokens[p + k]))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, int> Count(List<string> tokens)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (string token in tokens)
        {
            counts[token] = counts.GetValueOrDefault(token) + 1;
        }

        return counts;
    }
}
