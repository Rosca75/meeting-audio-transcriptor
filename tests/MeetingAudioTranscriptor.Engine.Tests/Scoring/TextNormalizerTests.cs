// Tests of the text normaliser against its exact specification (NFKC, lower case, non-alphanumerics to spaces).
using System.Linq;
using MeetingAudioTranscriptor.Engine.Scoring;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Scoring;

[Trait("Category", "scoring")]
public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Hello, World!", "hello|world")]
    [InlineData("don't", "don|t")]
    [InlineData("don’t", "don|t")]
    [InlineData("well-known e-mail", "well|known|e|mail")]
    [InlineData("  many   spaces\tand\nlines ", "many|spaces|and|lines")]
    [InlineData("Café Crème", "café|crème")]
    [InlineData("Straße ÄÖÜ", "straße|äöü")]
    [InlineData("STRASSE", "strasse")]
    [InlineData("Call 555-1234 at 3.5%", "call|555|1234|at|3|5")]
    [InlineData("ﬁnal oﬃce", "final|office")]
    [InlineData("① ²", "1|2")]
    [InlineData("ＡＢＣ １２", "abc|12")]
    [InlineData("a b", "a|b")]
    [InlineData("...?!", "")]
    [InlineData("", "")]
    [InlineData("l'été 2024", "l|été|2024")]
    [InlineData("日本語 123", "日本語|123")]
    public void NormalisesToTokens(string input, string expectedJoined)
    {
        string[] expected = expectedJoined.Length == 0 ? [] : expectedJoined.Split('|');

        string[] got = TextNormalizer.Normalize(input);

        Assert.True(
            got.SequenceEqual(expected),
            $"got [{string.Join('|', got)}], wanted [{expectedJoined}] (input '{input}')");
    }

    [Fact]
    public void NullReadsAsNoTokens() => Assert.Empty(TextNormalizer.Normalize(null));

    [Fact]
    public void CombiningAccentsComposeUnderNfkc()
    {
        string[] got = TextNormalizer.Normalize("café");

        Assert.True(got.SequenceEqual(["café"]), $"got [{string.Join('|', got)}], wanted [café] (decomposed input)");
    }

    [Fact]
    public void SurrogatePairLettersAreKept()
    {
        string[] got = TextNormalizer.Normalize("\U00010400x");

        Assert.True(got.SequenceEqual(["\U00010428x"]), $"got [{string.Join('|', got)}], wanted one lower-cased Deseret letter token");
    }
}
