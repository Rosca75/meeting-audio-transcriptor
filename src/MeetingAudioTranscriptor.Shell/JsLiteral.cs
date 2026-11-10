// Turns any .NET string into a JavaScript string literal, so the Linux host can hand a JSON
// envelope to the page by evaluating a call whose argument is that literal.
using System;
using System.Globalization;
using System.Text;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>Writes a string as a double-quoted JavaScript literal that evaluates back to the same string.</summary>
/// <remarks>
/// The quote and the backslash are escaped, every control character and the two characters JavaScript
/// once treated as line ends (U+2028, U+2029) are written as \uXXXX, and so is an unpaired surrogate,
/// which UTF-8 cannot carry to the web view. Every other character, non-ASCII included, is written as
/// itself, because the script travels as UTF-8.
/// </remarks>
internal static class JsLiteral
{
    /// <summary>The literal, quotes included.</summary>
    public static string Quote(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder literal = new(text.Length + 2);
        literal.Append('"');
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            switch (character)
            {
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                case '\n':
                    literal.Append("\\n");
                    break;
                case '\r':
                    literal.Append("\\r");
                    break;
                case '\t':
                    literal.Append("\\t");
                    break;
                case '\u2028':
                case '\u2029':
                    AppendEscape(literal, character);
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        AppendEscape(literal, character);
                    }
                    else if (char.IsHighSurrogate(character) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        // A complete pair is one character outside the basic plane: kept as is.
                        literal.Append(character).Append(text[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(character))
                    {
                        AppendEscape(literal, character);
                    }
                    else
                    {
                        literal.Append(character);
                    }

                    break;
            }
        }

        literal.Append('"');
        return literal.ToString();
    }

    private static void AppendEscape(StringBuilder literal, char character) =>
        literal.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
}
