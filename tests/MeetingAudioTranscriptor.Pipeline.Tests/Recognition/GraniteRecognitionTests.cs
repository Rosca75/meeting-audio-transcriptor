// Models-tier tests of the Granite recogniser: the derived graph agrees with the pinned export, the vocabulary matches the reference decoder, and VAD chunks of the benchmark clips are recognised within measured word error floors, with ordered times and no words from noise.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Engine.Vad;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Recognition;

/// <remarks>
/// The recogniser runs with its default settings (the low-yield retry is off by default), on the chunks
/// the VAD stage cuts with the recommended 25 s target and the 30 s maximum. The floors are the
/// measured rates plus a margin, so a regression in decoding, levelling or chunking fails here.
/// </remarks>
[Trait("Category", "transcript")]
[Trait("Tier", "models")]
public sealed class GraniteRecognitionTests
{
    private const double ChunkTarget = 25;

    private static GraniteCtcRecognizer DefaultRecognizer() => GraniteFixture.Recognizer();

    [Fact]
    public void TheDerivedGraphGivesTheSameIdsAsThePinnedExportAndScoresInRange()
    {
        GraniteFixture.RequireGranite();
        string source = Path.Combine(GraniteFixture.Catalog.Root, GraniteFixture.ModelName, "model.int8.onnx");
        Assert.SkipUnless(File.Exists(source), $"needs the pinned export {source} to compare with; run: node build/fetch-models.mjs --model {GraniteFixture.ModelName} --keep-sources --into {GraniteFixture.Catalog.Root}");
        float[] audio = GraniteFixture.Audio(GraniteFixture.Clips[1]).AsSpan(0, 20 * 16000).ToArray();
        TensorValue[] inputs = [TensorValue.OfFloats(GraniteCtcRecognizer.InputName, [1, audio.Length], audio)];

        using ITensorRunner original = OrtRunnerFactory.Open(source, new RunnerOptions());
        long[] expected = original.Run(inputs, [GraniteCtcRecognizer.IdsOutput], CancellationToken.None)[0].Int64s!;
        TensorValue[] derived = GraniteFixture.Model.Run(inputs, [GraniteCtcRecognizer.IdsOutput, GraniteCtcRecognizer.ScoresOutput], CancellationToken.None);

        long[] ids = derived[0].Int64s!;
        float[] scores = derived[1].Floats!;
        Assert.True(ids.SequenceEqual(expected), $"got {ids.Zip(expected).Count(p => p.First != p.Second)} differing frame ids of {expected.Length}, wanted the derived graph to agree with the export on every frame");
        Assert.True(scores.Length == ids.Length && scores.All(s => s > 0 && s <= 1), $"got {scores.Length} scores from {scores.Min()} to {scores.Max()}, wanted {ids.Length} in (0, 1]");
    }

    [Theory]
    [InlineData(new[] { 15723, 2370, 1305, 2139, 6751, 1030, 931, 1905, 549, 521, 956, 940, 7752, 2059, 951, 524, 956, 2078, 986, 2111, 2488, 649 }, "Ça coûte 403 € à l'hôtel, n'est-ce pas ?")]
    [InlineData(new[] { 13466, 13525, 1055, 7377, 2492, 3645, 931, 495, 414, 407, 383, 3796, 1087, 952 }, "Die Größe ist über \U0001F600 groß.")]
    [InlineData(new[] { 931, 1857, 13890, 5960, 931, 1867, 3491, 986, 2070, 952 }, "« Bonjour » dit-il.")]
    [InlineData(new[] { 1, 5, 255 + 0xC3, 0, 255 + 0xA9, 3 }, "é")]
    [InlineData(new[] { 255 + 0xE2, 255 + 0x82, 931, 255 + 0x41 }, "�� A")]
    public void TheVocabularyDecodesAsTheReferenceTokenizer(int[] ids, string expected)
    {
        string text = GraniteFixture.Vocabulary.Decode(ids);

        Assert.True(text == expected, $"got '{text}' from [{string.Join(", ", ids)}], wanted '{expected}' (the reference tokenizer's decoding)");
    }

    [Theory]
    [InlineData("en-single-01", 4.0)]
    [InlineData("fr-single-01", 5.0)]
    [InlineData("de-single-01", 8.0)]
    [InlineData("en-ami-01", 35.0)]
    [InlineData("en-ami-02", 25.0)]
    [InlineData("en-ami-sdm-01", 30.0)]
    public void AClipIsRecognisedWithinItsFloor(string name, double floorPercent)
    {
        BenchClip clip = GraniteFixture.Clips.Single(c => c.Name == name);
        List<SpeechChunk> chunks = GraniteFixture.Chunks(clip, GraniteFixture.Recognizer().MaxChunkSeconds, ChunkTarget);

        List<RecognizedWord> words = GraniteFixture.Transcribe(DefaultRecognizer(), chunks, TestContext.Current.CancellationToken);

        WordErrorRateResult result = GraniteFixture.Score(words, GraniteFixture.Reference(clip));
        GraniteFixture.Report(string.Create(CultureInfo.InvariantCulture, $"- {name}: WER {result.Wer * 100:F1} % ({result.Errors} errors in {result.ReferenceLength} words), floor {floorPercent} %"));
        Assert.True(result.Wer * 100 <= floorPercent, string.Create(CultureInfo.InvariantCulture, $"got WER {result.Wer * 100:F1} % on {name}, wanted at most {floorPercent} %"));
    }

    [Theory]
    [InlineData("silence-60s.wav")]
    [InlineData("noise-white-60s.wav")]
    [InlineData("noise-pink-60s.wav")]
    [InlineData("noise-office-60s.wav")]
    public void NoiseFedAsChunksYieldsNoWords(string file)
    {
        float[] audio = TestAudio.ReadWav(TestPaths.RequireBenchFile(Path.Combine("noise", file)));
        GraniteCtcRecognizer recognizer = DefaultRecognizer();
        List<string> words = [];
        int chunk = (int)(recognizer.MaxChunkSeconds * 16000);

        for (int start = 0; start < audio.Length; start += chunk)
        {
            int length = Math.Min(chunk, audio.Length - start);
            words.AddRange(recognizer.Recognize(audio.AsMemory(start, length), [(0, length)], TestContext.Current.CancellationToken).Select(w => w.Text));
        }

        Assert.True(words.Count == 0, $"got {words.Count} words from {file} ({string.Join(' ', words.Take(10))}), wanted none");
    }

    [Fact]
    public void WordTimesAreOrderedInsideTheirChunkWithConfidences()
    {
        BenchClip clip = GraniteFixture.Clips.Single(c => c.Name == "en-multi-01");
        GraniteCtcRecognizer recognizer = GraniteFixture.Recognizer();
        int total = 0;

        foreach (SpeechChunk chunk in GraniteFixture.Chunks(clip, recognizer.MaxChunkSeconds, ChunkTarget))
        {
            IReadOnlyList<RecognizedWord> words = recognizer.Recognize(chunk.Samples, chunk.SpeechRegions, TestContext.Current.CancellationToken);
            total += words.Count;
            for (int i = 0; i < words.Count; i++)
            {
                RecognizedWord word = words[i];
                bool inside = word.Start >= 0 && word.Start <= word.End && word.End <= chunk.DurationSeconds + 1e-9;
                bool ordered = i + 1 == words.Count || (word.Start <= words[i + 1].Start && word.End <= words[i + 1].Start + 1e-9);
                bool confident = word.Confidence > 0 && word.Confidence <= 1;
                Assert.True(inside && ordered && confident && word.Text.Length > 0,
                    string.Create(CultureInfo.InvariantCulture, $"chunk {chunk.Index} ({chunk.DurationSeconds:F2} s), word {i} '{word.Text}' {word.Start:F2}-{word.End:F2} at {word.Confidence:F3}: wanted a non-empty word inside the chunk, ending before the next starts, with a confidence in (0, 1]"));
            }
        }

        Assert.True(recognizer.HasScores == true, "got a graph without token_scores, wanted the derived graph");
        Assert.True(total > 100, $"got {total} words from {clip.Name}, wanted a full transcript");
    }
}
