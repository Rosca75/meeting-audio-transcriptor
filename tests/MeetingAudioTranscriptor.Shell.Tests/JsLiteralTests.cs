// Tests that any string becomes a JavaScript string literal that evaluates back to itself.
using System.Text.Json;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class JsLiteralTests
{
    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("a\nb\rc\td", "\"a\\nb\\rc\\td\"")]
    [InlineData("\u0000\u001f\u007f", "\"\\u0000\\u001f\\u007f\"")]
    [InlineData("a\u2028b\u2029c", "\"a\\u2028b\\u2029c\"")]
    [InlineData("caf\u00e9 \u4f1a\u8bae", "\"caf\u00e9 \u4f1a\u8bae\"")]
    [InlineData("\ud83d\ude00", "\"\ud83d\ude00\"")]
    [InlineData("</script>", "\"</script>\"")]
    public void EachCharacterIsWrittenByTheRules(string text, string wanted)
    {
        string got = JsLiteral.Quote(text);

        Assert.True(got == wanted, $"got {got} for the input, wanted {wanted}");
    }

    [Theory]
    [InlineData("\ud800")]
    [InlineData("a\udc00b")]
    [InlineData("\ude00\ud83d")]
    public void AnUnpairedSurrogateIsEscaped(string text)
    {
        string got = JsLiteral.Quote(text);

        foreach (char character in got)
        {
            Assert.True(!char.IsSurrogate(character), $"got a raw surrogate in {got}, wanted every unpaired one as \\uXXXX");
        }
    }

    [Theory]
    [InlineData("{\"id\":1,\"result\":{\"name\":\"r\u00e9union \\\"one\\\".wav\"}}")]
    [InlineData("C:\\Users\\Ana\\Music\\meeting.m4a")]
    [InlineData("line\nbreak and \u2028 and \u2029 and \u0001 and \ud83d\ude00 and \u00fc")]
    public void TheLiteralParsesBackToTheSameString(string text)
    {
        // Every literal the writer produces is also a valid JSON string, so a JSON parser reads it back
        // exactly as a JavaScript engine would.
        string literal = JsLiteral.Quote(text);
        string? parsed = JsonSerializer.Deserialize<string>(literal);

        Assert.True(parsed == text, $"got '{parsed}' back from {literal}, wanted the input");
    }
}
