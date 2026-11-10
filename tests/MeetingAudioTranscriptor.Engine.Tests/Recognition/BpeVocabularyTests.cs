// Unit tests of the Granite vocabulary: the piece kinds read from tokenizer.json and the decoding of ids, accents, byte pieces and control tokens.
using System;
using System.IO;
using System.Linq;
using System.Text;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;
using Xunit;
using static MeetingAudioTranscriptor.Engine.Tests.Recognition.TinyTokenizer;

namespace MeetingAudioTranscriptor.Engine.Tests.Recognition;

[Trait("Category", "transcript")]
public sealed class BpeVocabularyTests
{
    private static readonly BpeVocabulary Vocabulary = Load();

    [Fact]
    public void FrenchAccentedPiecesDecodeWithTheMarkerAsASpaceAndNoLeadingSpace()
    {
        string text = Vocabulary.Decode([Ca, Co, UCircumflex, Te, Pas, Question]);

        Assert.True(text == "Ça coûte pas ?", $"got '{text}', wanted 'Ça coûte pas ?'");
    }

    [Fact]
    public void ByteFallbackPiecesJoinIntoOneCharacter()
    {
        int[] ids = [Die, Marker, .. Bytes("\U0001F600"), Gro, Eszett, Period];

        string text = Vocabulary.Decode(ids);

        Assert.True(text == "Die \U0001F600 groß.", $"got '{text}' from [{string.Join(", ", ids)}], wanted 'Die 😀 groß.'");
    }

    [Fact]
    public void AnInvalidByteRunBecomesOneReplacementCharacterPerByte()
    {
        // E2 82 is an incomplete three-byte sequence; the marker ends the run and 0x41 is 'A'.
        string text = Vocabulary.Decode([Byte(0xE2), Byte(0x82), Marker, Byte(0x41)]);

        Assert.True(text == "�� A", $"got '{text}', wanted two U+FFFD, a space and A");
    }

    [Fact]
    public void ControlTokensAreSkippedWithoutBreakingAByteRun()
    {
        string text = Vocabulary.Decode([NoSpeech, Byte(0xC3), Blank, Byte(0xA9), EndOfText]);

        Assert.True(text == "é", $"got '{text}', wanted 'é': the blank between the two bytes is dropped before decoding");
    }

    [Fact]
    public void OnlyControlTokensDecodeToNothing()
    {
        string text = Vocabulary.Decode([NoSpeech, Blank, EndOfText, 2, 99999, -1]);

        Assert.True(text.Length == 0, $"got '{text}', wanted an empty text from control and unknown ids");
    }

    [Fact]
    public void PieceKindsFollowTheFile()
    {
        Assert.True(Vocabulary.Count == 279, $"got {Vocabulary.Count} ids, wanted 279");
        Assert.True(Vocabulary.KindOf(Blank) == BpeVocabulary.PieceKind.Control, "the blank must be a control id");
        Assert.True(Vocabulary.KindOf(NoSpeech) == BpeVocabulary.PieceKind.Control, "<|nospeech|> must be a control id");
        Assert.True(Vocabulary.KindOf(Byte(0x41)) == BpeVocabulary.PieceKind.Byte, "<0x41> must be a byte piece");
        Assert.True(Vocabulary.StartsWord(Hello) && !Vocabulary.StartsWord(Jour) && !Vocabulary.StartsWord(Byte(0x20)), "only a marker piece starts a word");
        Assert.True(Vocabulary.TextOf(Hello) == " hello", $"got '{Vocabulary.TextOf(Hello)}', wanted ' hello'");
    }

    [Fact]
    public void ThePipeFormIsAControlTokenEvenWhenNotDeclaredSpecial()
    {
        string json = "{\"model\":{\"vocab\":{\"<unk>\":0,\"<|en|>\":1,\"▁yes\":2}}}";
        BpeVocabulary vocabulary = BpeVocabulary.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        string text = vocabulary.Decode([1, 2, 0]);

        Assert.True(text == "yes", $"got '{text}', wanted 'yes'");
    }

    [Fact]
    public void LoadingFromMemoryMatchesLoadingFromAStream()
    {
        BpeVocabulary fromMemory = BpeVocabulary.Load(Encoding.UTF8.GetBytes(Json()).AsMemory());
        int[] ids = [Hello, World, Comma, Again];

        Assert.True(fromMemory.Decode(ids) == Vocabulary.Decode(ids), $"got '{fromMemory.Decode(ids)}', wanted '{Vocabulary.Decode(ids)}'");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"model\":{\"type\":\"WordPiece\"}}")]
    [InlineData("[1, 2]")]
    public void AFileThatIsNotABpeTokenizerIsRefusedByName(string content)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => BpeVocabulary.Load(new MemoryStream(Encoding.UTF8.GetBytes(content))));

        Assert.True(error.Message.Contains("tokenizer", StringComparison.Ordinal), $"got '{error.Message}', wanted a message naming the tokenizer file");
    }

    [Fact]
    public void DecodingIsTheSameFromSeveralThreads()
    {
        int[] ids = [Ca, Co, UCircumflex, Te, .. Bytes("été")];
        string expected = Vocabulary.Decode(ids);

        string[] results = Enumerable.Range(0, 64).AsParallel().Select(_ => Vocabulary.Decode(ids)).ToArray();

        Assert.True(results.All(r => r == expected), $"got {results.Distinct().Count()} different texts from parallel decoding, wanted one");
    }
}
