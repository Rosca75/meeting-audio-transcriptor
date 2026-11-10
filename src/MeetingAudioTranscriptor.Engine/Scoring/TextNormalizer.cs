// The text normaliser every score is computed over, specified to match the Python scorer used elsewhere.
using System.Collections.Generic;
using System.Text;

namespace MeetingAudioTranscriptor.Engine.Scoring;

/// <summary>Turns text into the lower-case alphanumeric tokens that scores compare.</summary>
/// <remarks>
/// The specification is exact and shared with the Python scorer, so a score from either side is the
/// same number: Unicode NFKC, lower case (invariant), every character that is not a Unicode letter or
/// digit becomes a space (apostrophes and hyphens included, so <c>don't</c> is <c>don</c> and <c>t</c>),
/// whitespace collapses, and the text splits on spaces. Accents are kept: <c>é</c> and <c>e</c> differ.
/// </remarks>
public static class TextNormalizer
{
    /// <summary>Normalises text into tokens.</summary>
    /// <param name="text">Any text; null reads as empty.</param>
    /// <returns>The tokens, possibly none.</returns>
    public static string[] Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        string folded = CompatibilityFolding.Nfkc(text).ToLowerInvariant();
        List<string> tokens = [];
        StringBuilder current = new();
        foreach (Rune rune in folded.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                current.Append(rune.ToString());
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return [.. tokens];
    }
}
