// Small text helpers every cleaning rule shares: the lookup key of a word, hesitation squeezing and punctuation tests.
using System;
using System.Collections.Generic;
using System.Text;

namespace MeetingAudioTranscriptor.Engine.Cleaning;

/// <summary>Derives the comparison forms of a recognised word.</summary>
/// <remarks>
/// A word reaches the cleaner with its casing and attached punctuation, so every rule compares a key
/// instead: lower case with the outer punctuation trimmed. The inner apostrophe and hyphen stay,
/// because <c>d'accord</c> and <c>uh-huh</c> are single words with a single meaning.
/// </remarks>
internal static class WordKeys
{
    /// <summary>Lower-cases a word, folds typographic apostrophes and trims leading and trailing non-alphanumerics.</summary>
    /// <param name="text">The word as written.</param>
    /// <returns>The key, empty when the word has no letter or digit.</returns>
    public static string Key(string text)
    {
        int start = 0;
        int end = text.Length;
        while (start < end && !char.IsLetterOrDigit(text[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetterOrDigit(text[end - 1]))
        {
            end--;
        }

        StringBuilder builder = new(end - start);
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            builder.Append(c is '’' or '‘' or 'ʼ' or '`' ? '\'' : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Collapses every run of one repeated character to a single character (<c>ummm</c> becomes <c>um</c>).</summary>
    /// <param name="key">A key.</param>
    /// <returns>The squeezed key.</returns>
    /// <remarks>
    /// Only hesitation lookups use it: a recogniser stretches a hesitation to any length, while a real
    /// word such as <c>good</c> must never be squeezed into another word.
    /// </remarks>
    public static string Squeeze(string key)
    {
        StringBuilder builder = new(key.Length);
        foreach (char c in key)
        {
            if (builder.Length == 0 || builder[^1] != c)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Splits text into its letter-and-digit runs (<c>d'accord</c> gives <c>d</c> and <c>accord</c>).</summary>
    /// <param name="key">A key or any text.</param>
    /// <returns>The runs, unchanged in case.</returns>
    public static string[] Tokens(string key)
    {
        List<string> runs = [];
        int start = -1;
        for (int i = 0; i <= key.Length; i++)
        {
            bool alnum = i < key.Length && char.IsLetterOrDigit(key[i]);
            if (alnum && start < 0)
            {
                start = i;
            }
            else if (!alnum && start >= 0)
            {
                runs.Add(key[start..i]);
                start = -1;
            }
        }

        return [.. runs];
    }

    /// <summary>True when the key holds a decimal digit.</summary>
    /// <param name="key">A key.</param>
    /// <returns>True when any character is a digit.</returns>
    public static bool HasDigit(string key)
    {
        foreach (char c in key)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The trailing sentence-final punctuation of a word (full stop, question mark, exclamation mark, ellipsis character).</summary>
    /// <param name="text">The word as written.</param>
    /// <returns>The run, possibly empty.</returns>
    public static string Terminal(string text)
    {
        int end = text.Length;
        while (end > 0 && IsTerminalMark(text[end - 1]))
        {
            end--;
        }

        return text[end..];
    }

    /// <summary>True when the word ends a sentence.</summary>
    /// <param name="text">The word as written.</param>
    /// <returns>True when it ends in a full stop, question mark, exclamation mark or ellipsis character.</returns>
    public static bool EndsSentence(string text) => Terminal(text).Length > 0;

    /// <summary>True when the word ends in a comma, a dash or an ellipsis, the marks a speaker leaves where a sentence is dropped.</summary>
    /// <param name="text">The word as written.</param>
    /// <returns>True for a break mark.</returns>
    public static bool EndsWithBreak(string text)
    {
        return text.EndsWith(',') || text.EndsWith('-') || text.EndsWith('\u2013') || text.EndsWith('\u2014')
            || text.EndsWith('…') || text.EndsWith("...", StringComparison.Ordinal);
    }

    /// <summary>True when the word ends in punctuation, so a new clause may start after it.</summary>
    /// <param name="text">The word as written.</param>
    /// <returns>True when the last character is not a letter or digit.</returns>
    public static bool EndsWithPunctuation(string text) => text.Length > 0 && !char.IsLetterOrDigit(text[^1]);

    private static bool IsTerminalMark(char c) => c is '.' or '?' or '!' or '…';
}
