// The Unicode folding the normaliser needs on a runtime whose own normalisation is switched off.
using System.Text;

namespace MeetingAudioTranscriptor.Engine.Scoring;

/// <summary>NFKC for the characters meeting transcripts in English, French and German contain.</summary>
/// <remarks>
/// The repository builds with invariant globalization, and on Linux the runtime then returns text
/// unchanged from <see cref="string.Normalize(NormalizationForm)"/>, while on Windows it normalises
/// fully. Scores must not depend on the machine, so the normaliser asks the runtime first and then
/// applies this table, which finishes what a runtime without data left undone: a base letter followed
/// by a combining accent is composed (a Mac keyboard writes <c>é</c> that way), and the ligatures,
/// full-width forms, no-break spaces, superscript and circled digits fold to their plain form. A character
/// outside the table is left as the runtime returned it. The fold is idempotent, so a runtime that
/// already normalised loses nothing.
/// </remarks>
internal static class CompatibilityFolding
{
    private static readonly (char Mark, string Bases, string Composed)[] Compositions =
    [
        ('̀', "aeiouAEIOUnN", "àèìòùÀÈÌÒÙǹǸ"),
        ('́', "aeiouyAEIOUYcnszCNSZ", "áéíóúýÁÉÍÓÚÝćńśźĆŃŚŹ"),
        ('̂', "aeiouAEIOUcghjswyCGHJSWY", "âêîôûÂÊÎÔÛĉĝĥĵŝŵŷĈĜĤĴŜŴŶ"),
        ('̃', "anoANOiuIU", "ãñõÃÑÕĩũĨŨ"),
        ('̈', "aeiouyAEIOUY", "äëïöüÿÄËÏÖÜŸ"),
        ('̊', "aAuU", "åÅůŮ"),
        ('̧', "cCsStT", "çÇşŞţŢ"),
        ('̌', "cdenrstzCDENRSTZ", "čďěňřšťžČĎĚŇŘŠŤŽ"),
        ('̆', "aAgG", "ăĂğĞ"),
        ('̨', "aeAE", "ąęĄĘ"),
        ('̇', "zZcCeEgG", "żŻċĊėĖġĠ"),
    ];

    /// <summary>Normalises to NFKC as far as the runtime and this table allow.</summary>
    /// <param name="text">Any text.</param>
    /// <returns>The folded text.</returns>
    public static string Nfkc(string text)
    {
        if (IsAscii(text))
        {
            return text;
        }

        StringBuilder builder = new(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormKC))
        {
            if (IsCombiningMark(c) && builder.Length > 0 && TryCompose(builder[^1], c, out char composed))
            {
                builder[^1] = composed;
                continue;
            }

            Append(builder, c);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, char c)
    {
        switch (c)
        {
            case 'ﬀ':
                builder.Append("ff");
                break;
            case 'ﬁ':
                builder.Append("fi");
                break;
            case 'ﬂ':
                builder.Append("fl");
                break;
            case 'ﬃ':
                builder.Append("ffi");
                break;
            case 'ﬄ':
                builder.Append("ffl");
                break;
            case 'ﬅ':
            case 'ﬆ':
                builder.Append("st");
                break;
            case 'ĳ':
                builder.Append("ij");
                break;
            case 'Ĳ':
                builder.Append("IJ");
                break;
            case '²':
                builder.Append('2');
                break;
            case '³':
                builder.Append('3');
                break;
            case '¹':
                builder.Append('1');
                break;
            case '⁰':
                builder.Append('0');
                break;
            case >= '⁴' and <= '⁹':
                builder.Append((char)('4' + (c - '⁴')));
                break;
            case >= '₀' and <= '₉':
                builder.Append((char)('0' + (c - '₀')));
                break;
            case >= '①' and <= '⑨':
                builder.Append((char)('1' + (c - '①')));
                break;
            case '⓪':
                builder.Append('0');
                break;
            case >= '！' and <= '～':
                builder.Append((char)(c - 0xFEE0));
                break;
            case ' ' or ' ' or '　':
                builder.Append(' ');
                break;
            case >= ' ' and <= ' ':
                builder.Append(' ');
                break;
            default:
                builder.Append(c);
                break;
        }
    }

    private static bool TryCompose(char baseLetter, char mark, out char composed)
    {
        foreach ((char m, string bases, string results) in Compositions)
        {
            if (m != mark)
            {
                continue;
            }

            int index = bases.IndexOf(baseLetter);
            if (index >= 0)
            {
                composed = results[index];
                return true;
            }
        }

        composed = default;
        return false;
    }

    private static bool IsCombiningMark(char c) => c is >= '̀' and <= 'ͯ';

    private static bool IsAscii(string text)
    {
        foreach (char c in text)
        {
            if (c > '\u007F')
            {
                return false;
            }
        }

        return true;
    }
}
