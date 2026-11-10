// Unit tests of the Granite recogniser against a scripted graph: CTC collapse, word assembly, timing, confidence, control tokens, the padding quirk, the missing-score fallback, the input gain and cancellation.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;
using Xunit;
using static MeetingAudioTranscriptor.Engine.Tests.Recognition.TinyTokenizer;

namespace MeetingAudioTranscriptor.Engine.Tests.Recognition;

[Trait("Category", "transcript")]
public sealed class GraniteCtcRecognizerTests
{
    private static readonly BpeVocabulary Vocabulary = Load();

    /// <summary>Two seconds of a quiet tone, long enough to be read.</summary>
    private static float[] Tone(int samples = 32000, float amplitude = 0.05f) =>
        Enumerable.Range(0, samples).Select(i => amplitude * MathF.Sin(i * 0.3f)).ToArray();

    private static IReadOnlyList<RecognizedWord> Recognize(FakeGraniteRunner runner, float[]? samples = null, GraniteOptions? options = null) =>
        new GraniteCtcRecognizer(runner, Vocabulary, options).Recognize(samples ?? Tone(), CancellationToken.None);

    private static string Describe(IEnumerable<RecognizedWord> words) =>
        string.Join(" | ", words.Select(w => $"{w.Text} {w.Start:F2}-{w.End:F2} {w.Confidence:F2}"));

    [Fact]
    public void RepeatsCollapseAndBlanksSeparateTwoEqualTokens()
    {
        List<CtcToken> tokens = CtcDecoder.Collapse([0, Hello, Hello, 0, Hello, World, World, NoSpeech, 0], [0.1f, 0.5f, 0.8f, 0.2f, 0.6f, 0.7f, 0.9f, 0.99f, 0.3f], Vocabulary);

        CtcToken[] expected = [new(Hello, 1, 2, 0.8f), new(Hello, 4, 4, 0.6f), new(World, 5, 6, 0.9f)];
        Assert.True(tokens.SequenceEqual(expected), $"got [{string.Join(", ", tokens)}], wanted [{string.Join(", ", expected)}]");
    }

    [Fact]
    public void WordsTakeTheirFramesTimesAndLowestTokenScore()
    {
        // hello at frames 2-3; world at 5 and its continuation ',' at 6-7; again at 12.
        long[] ids = [0, 0, Hello, Hello, 0, World, Comma, Comma, 0, 0, 0, 0, Again, 0];
        float[] scores = [0.9f, 0.9f, 0.7f, 0.95f, 0.9f, 0.8f, 0.4f, 0.6f, 0.9f, 0.9f, 0.9f, 0.9f, 0.85f, 0.9f];

        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed(ids, scores));

        RecognizedWord[] expected =
        [
            new("hello", 0.16, 0.32, 0.95f),
            new("world,", 0.40, 0.64, 0.6f),
            new("again", 0.96, 1.04, 0.85f),
        ];
        Assert.True(words.Count == 3, $"got {Describe(words)}, wanted three words");
        for (int i = 0; i < expected.Length; i++)
        {
            bool same = words[i].Text == expected[i].Text && Math.Abs(words[i].Start - expected[i].Start) < 1e-9
                && Math.Abs(words[i].End - expected[i].End) < 1e-9 && words[i].Confidence == expected[i].Confidence;
            Assert.True(same, $"word {i}: got {Describe([words[i]])}, wanted {Describe([expected[i]])}");
        }
    }

    [Fact]
    public void AWordEndsNoLaterThanTheNextStartsOrTheChunkEnds()
    {
        // hello holds frames 0-1 and world 2-25, in a chunk of 1600 samples (0.1 s): world's frames
        // run past the chunk's end, so every time is clamped to 0.1 s.
        long[] ids = [Hello, Hello, .. Enumerable.Repeat((long)World, 24)];

        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed(ids), Tone(1600));

        Assert.True(words.Count == 2, $"got {Describe(words)}, wanted two words");
        Assert.True(words[0].End <= words[1].Start + 1e-9, $"got {Describe(words)}, wanted hello to end where world starts");
        Assert.True(words.All(w => w.End <= 0.1 + 1e-9 && w.Start <= w.End), $"got {Describe(words)}, wanted every time inside the 0.1 s chunk");
    }

    [Fact]
    public void FrenchPunctuationWrittenAsAWordAttachesToTheWordBefore()
    {
        long[] ids = [Pas, 0, Question, 0];

        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed(ids));

        Assert.True(words.Count == 1 && words[0].Text == "pas ?", $"got {Describe(words)}, wanted one word 'pas ?'");
        Assert.True(Math.Abs(words[0].End - 0.24) < 1e-9, $"got end {words[0].End}, wanted 0.24 (the end of '?')");
    }

    [Fact]
    public void QuotesAttachOutwardsAndTheWordsJoinIntoTheDecodersText()
    {
        long[] ids = [Marker, OpenQuote, 0, Bon, Jour, 0, Marker, CloseQuote, 0, Hello];

        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed(ids));

        string[] texts = words.Select(w => w.Text).ToArray();
        Assert.True(texts.SequenceEqual(["« Bonjour »", "hello"]), $"got [{string.Join(", ", texts)}], wanted ['« Bonjour »', 'hello']");
        int[] collapsed = CtcDecoder.Collapse(ids, [], Vocabulary).Select(t => t.Id).ToArray();
        Assert.True(string.Join(' ', texts) == Vocabulary.Decode(collapsed), $"got '{string.Join(' ', texts)}', wanted the decoder's '{Vocabulary.Decode(collapsed)}'");
    }

    [Fact]
    public void PunctuationWithNoWordToAttachToIsDropped()
    {
        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed([0, Question, 0, Marker, OpenQuote, 0]));

        Assert.True(words.Count == 0, $"got {Describe(words)}, wanted no word from punctuation alone");
    }

    [Fact]
    public void ByteFallbackPiecesStayInsideTheirWord()
    {
        long[] ids = [Die, Marker, .. Bytes("\U0001F600").Select(b => (long)b), Gro, Eszett, Period];

        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed(ids));

        string[] texts = words.Select(w => w.Text).ToArray();
        Assert.True(texts.SequenceEqual(["Die", "\U0001F600", "groß."]), $"got [{string.Join(", ", texts)}], wanted [Die, 😀, groß.]");
    }

    [Fact]
    public void ControlTokensAloneYieldNoWords()
    {
        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed([NoSpeech, NoSpeech, 0, EndOfText, 0]));

        Assert.True(words.Count == 0, $"got {Describe(words)}, wanted none from <|nospeech|>");
    }

    [Fact]
    public void AnEmptyOutputYieldsNoWords()
    {
        IReadOnlyList<RecognizedWord> words = Recognize(FakeGraniteRunner.Fixed([]));

        Assert.True(words.Count == 0, $"got {Describe(words)}, wanted none");
    }

    [Theory]
    [InlineData(32000, 32000)]
    [InlineData(31 * 160, (31 * 160) + 1)]
    [InlineData(30 * 160, 30 * 160)]
    [InlineData((31 * 160) + 1, (31 * 160) + 1)]
    public void AnOddMultipleOf160SamplesGetsOneZeroAppended(int length, int expected)
    {
        FakeGraniteRunner runner = FakeGraniteRunner.Fixed([Hello]);

        Recognize(runner, Tone(length), new GraniteOptions { Gain = new GainOptions { Mode = GainMode.None } });

        float[] input = runner.Inputs.Single();
        Assert.True(input.Length == expected, $"got {input.Length} samples into the graph for a chunk of {length}, wanted {expected}");
        if (expected > length)
        {
            Assert.True(input[^1] == 0f, $"got {input[^1]} as the appended sample, wanted 0");
        }
    }

    [Fact]
    public void AChunkShorterThanATenthOfASecondIsNotRead()
    {
        FakeGraniteRunner runner = FakeGraniteRunner.Fixed([Hello]);

        IReadOnlyList<RecognizedWord> words = Recognize(runner, Tone(1599));

        Assert.True(words.Count == 0 && runner.Requests.Count == 0, $"got {words.Count} words and {runner.Requests.Count} runs, wanted neither");
    }

    [Fact]
    public void AGraphWithoutScoresIsDetectedOnceAndEveryConfidenceIsOne()
    {
        FakeGraniteRunner runner = FakeGraniteRunner.Fixed([Hello, 0, World], hasScores: false);
        GraniteCtcRecognizer recognizer = new(runner, Vocabulary);

        IReadOnlyList<RecognizedWord> first = recognizer.Recognize(Tone(), CancellationToken.None);
        IReadOnlyList<RecognizedWord> second = recognizer.Recognize(Tone(), CancellationToken.None);

        Assert.True(recognizer.HasScores == false, $"got HasScores {recognizer.HasScores}, wanted false");
        Assert.True(first.Concat(second).All(w => w.Confidence == 1f) && first.Count == 2, $"got {Describe(first)}, wanted two words at confidence 1");
        string requests = string.Join(" ; ", runner.Requests.Select(r => string.Join(",", r)));
        Assert.True(requests == "token_ids,token_scores ; token_ids ; token_ids", $"got requests '{requests}', wanted the scores asked for once only");
    }

    [Fact]
    public void AGraphWithScoresIsAskedForBoth()
    {
        FakeGraniteRunner runner = FakeGraniteRunner.Fixed([Hello]);
        GraniteCtcRecognizer recognizer = new(runner, Vocabulary);

        recognizer.Recognize(Tone(), CancellationToken.None);

        Assert.True(recognizer.HasScores == true, $"got HasScores {recognizer.HasScores}, wanted true");
    }

    [Fact]
    public void TheChunkGainBringsTheInputToTheTargetLevel()
    {
        FakeGraniteRunner runner = FakeGraniteRunner.Fixed([Hello]);

        Recognize(runner, Tone(32000, 0.01f), new GraniteOptions { Gain = new GainOptions { Mode = GainMode.ChunkRms, TargetRms = 0.1 } });

        double rms = GainNormalizer.Rms(runner.Inputs.Single());
        Assert.True(Math.Abs(rms - 0.1) < 1e-3, $"got input RMS {rms:F4}, wanted 0.1");
    }

    [Fact]
    public void SpeechRegionsReachTheGain()
    {
        // Loud first half, quiet second half; per-region gain levels both halves.
        float[] samples = [.. Tone(16000, 0.2f), .. Tone(16000, 0.01f)];
        FakeGraniteRunner runner = FakeGraniteRunner.Fixed([Hello]);
        GraniteCtcRecognizer recognizer = new(runner, Vocabulary, new GraniteOptions { Gain = new GainOptions { Mode = GainMode.RegionRms } });

        recognizer.Recognize(samples, [(0, 16000), (16000, 32000)], CancellationToken.None);

        float[] input = runner.Inputs.Single();
        double first = GainNormalizer.Rms(input.AsSpan(0, 16000));
        double second = GainNormalizer.Rms(input.AsSpan(16000, 16000));
        Assert.True(Math.Abs(first - 0.1) < 2e-3 && Math.Abs(second - 0.1) < 2e-3, $"got region RMS {first:F4} and {second:F4}, wanted both 0.1");
    }

    [Fact]
    public void ACancelledCallThrows()
    {
        GraniteCtcRecognizer recognizer = new(FakeGraniteRunner.Fixed([Hello]), Vocabulary);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => recognizer.Recognize(Tone(), cancelled.Token));
    }

    [Fact]
    public void TheDefaultsAreThirtySecondsAndValidated()
    {
        GraniteCtcRecognizer recognizer = new(FakeGraniteRunner.Fixed([Hello]), Vocabulary);

        Assert.True(recognizer.MaxChunkSeconds == 30, $"got {recognizer.MaxChunkSeconds} s, wanted 30");
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraniteCtcRecognizer(FakeGraniteRunner.Fixed([]), Vocabulary, new GraniteOptions { MaxChunkSeconds = 60 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraniteCtcRecognizer(FakeGraniteRunner.Fixed([]), Vocabulary, new GraniteOptions { Gain = new GainOptions { MaxGain = 0.5 } }));
    }

    [Fact]
    public void ConcurrentCallsGetTheirOwnResults()
    {
        static (long[] Ids, float[] Scores) Answer(int length) =>
            length > 40000 ? ([Hello, 0, World], [0.9f, 0.9f, 0.9f]) : ([Again], [0.5f]);
        FakeGraniteRunner runner = new(Answer);
        GraniteCtcRecognizer recognizer = new(new LockedRunner(runner), Vocabulary);

        string[] results = Enumerable.Range(0, 32).AsParallel().Select(i =>
            string.Join(' ', recognizer.Recognize(Tone(i % 2 == 0 ? 48000 : 32000), CancellationToken.None).Select(w => w.Text))).ToArray();

        for (int i = 0; i < results.Length; i++)
        {
            string wanted = i % 2 == 0 ? "hello world" : "again";
            Assert.True(results[i] == wanted, $"call {i}: got '{results[i]}', wanted '{wanted}'");
        }
    }

    /// <summary>Serialises calls into the fake, whose recording lists are not thread-safe; the recogniser itself is not locked.</summary>
    private sealed class LockedRunner(FakeGraniteRunner inner) : Engine.Models.ITensorRunner
    {
        private readonly Lock _gate = new();

        public Engine.Models.TensorValue[] Run(ReadOnlySpan<Engine.Models.TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
        {
            lock (_gate)
            {
                return inner.Run(inputs, outputNames, cancellation);
            }
        }

        public void Dispose()
        {
        }
    }
}
