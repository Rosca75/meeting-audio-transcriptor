// The languages the cleaning stage and the scoring utilities know, and the one place a language code is parsed.
using System;

namespace MeetingAudioTranscriptor.Engine.Cleaning;

/// <summary>A language the rule-based cleaning stage has a lexicon for.</summary>
/// <remarks>
/// The set is closed because every rule is driven by per-language word lists: a language without a
/// reviewed list would apply the English rules to foreign words, which is how content gets deleted.
/// </remarks>
public enum CleaningLanguage
{
    /// <summary>English, code <c>en</c>.</summary>
    English,

    /// <summary>French, code <c>fr</c>.</summary>
    French,

    /// <summary>German, code <c>de</c>.</summary>
    German,
}

/// <summary>Conversion between language codes and <see cref="CleaningLanguage"/>.</summary>
public static class CleaningLanguages
{
    /// <summary>Reads <c>en</c>, <c>fr</c> or <c>de</c> (any casing, surrounding spaces ignored).</summary>
    /// <param name="code">The language code.</param>
    /// <returns>The matching language.</returns>
    /// <exception cref="ArgumentException">The code is none of the three; the message names the accepted codes.</exception>
    public static CleaningLanguage Parse(string code)
    {
        if (TryParse(code, out CleaningLanguage language))
        {
            return language;
        }

        throw new ArgumentException(
            $"the language code '{code}' is not supported by the cleaning stage; use 'en', 'fr' or 'de'",
            nameof(code));
    }

    /// <summary>Reads a language code without throwing.</summary>
    /// <param name="code">The language code, possibly null.</param>
    /// <param name="language">The language when the code is known.</param>
    /// <returns>True when the code is <c>en</c>, <c>fr</c> or <c>de</c>.</returns>
    public static bool TryParse(string? code, out CleaningLanguage language)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "en":
                language = CleaningLanguage.English;
                return true;
            case "fr":
                language = CleaningLanguage.French;
                return true;
            case "de":
                language = CleaningLanguage.German;
                return true;
            default:
                language = default;
                return false;
        }
    }

    /// <summary>The two-letter code of a language.</summary>
    /// <param name="language">The language.</param>
    /// <returns><c>en</c>, <c>fr</c> or <c>de</c>.</returns>
    public static string Code(CleaningLanguage language) => language switch
    {
        CleaningLanguage.English => "en",
        CleaningLanguage.French => "fr",
        CleaningLanguage.German => "de",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "the language is not one of the three the cleaning stage supports"),
    };
}
