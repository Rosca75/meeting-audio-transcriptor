// Unit tests of the low-yield retry's bookkeeping: which regions are read again, what the second reading receives, and how its words replace the first reading's.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Recognition;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Recognition;

[Trait("Category", "transcript")]
public sealed class LowYieldRetryTests
{
    private const int Rate = 16000;

    /// <summary>Samples whose value is their own index, so a call's first sample says where its slice starts.</summary>
    private static float[] Indexed(double seconds) => Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (float)i).ToArray();

    private static string Describe(IEnumerable<RecognizedWord> words) => string.Join(" | ", words.Select(w => $"{w.Text} {w.Start:F2}-{w.End:F2}"));

    /// <summary>A region-aware recogniser answering from a function of (slice start in samples, slice length, regions).</summary>
    private sealed class ScriptedRecognizer(Func<int, int, IReadOnlyList<(int Start, int End)>, IReadOnlyList<RecognizedWord>> answer) : IRegionAwareRecognizer
    {
        public List<(int SliceStart, int Length, (int Start, int End)[] Regions)> Calls { get; } = [];

        public double MaxChunkSeconds => 30;

        public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation) => Recognize(samples, [], cancellation);

        public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
        {
            int sliceStart = (int)samples.Span[0];
            Calls.Add((sliceStart, samples.Length, speechRegions.ToArray()));
            return answer(sliceStart, samples.Length, speechRegions);
        }
    }

    private static RecognizedWord Word(string text, double start, double end) => new(text, start, end, 0.9f);

    [Fact]
    public void WithoutRegionsTheInnerRecogniserIsCalledOnce()
    {
        ScriptedRecognizer inner = new((_, _, _) => [Word("a", 0, 0.5)]);
        LowYieldRetry retry = new(inner);

        IReadOnlyList<RecognizedWord> words = retry.Recognize(Indexed(10), CancellationToken.None);

        Assert.True(inner.Calls.Count == 1 && words.Count == 1, $"got {inner.Calls.Count} calls and {Describe(words)}, wanted one call and its word");
    }

    [Fact]
    public void ARegionWithTooFewWordsIsReadAgainOnItsOwnAndItsWordsReplaced()
    {
        // The chunk is 10 s; region (2 s, 5 s) yields nothing on the first reading and three words alone.
        ScriptedRecognizer inner = new((sliceStart, _, _) => sliceStart == 0
            ? [Word("before", 0.5, 1.0), Word("after", 6.0, 6.5)]
            : [Word("one", 0.3, 0.6), Word("two", 1.0, 1.4), Word("three", 2.0, 2.5)]);
        LowYieldRetry retry = new(inner);
        (int, int)[] regions = [(0, 1 * Rate + 8000), (2 * Rate, 5 * Rate), (6 * Rate, 7 * Rate)];

        IReadOnlyList<RecognizedWord> words = retry.Recognize(Indexed(10), regions, CancellationToken.None);

        string got = Describe(words);
        Assert.True(got == "before 0.50-1.00 | one 2.10-2.40 | two 2.80-3.20 | three 3.80-4.30 | after 6.00-6.50", $"got {got}, wanted the region's words shifted by its slice start (1.8 s)");
        (int sliceStart, int length, (int Start, int End)[] sliceRegions) = inner.Calls[1];
        Assert.True(sliceStart == (2 * Rate) - 3200 && length == (3 * Rate) + 6400, $"got a slice at {sliceStart} of {length} samples, wanted the region plus 0.2 s each side");
        Assert.True(sliceRegions.SequenceEqual([(3200, (3 * Rate) + 3200)]), $"got regions [{string.Join(", ", sliceRegions)}], wanted the region relative to the slice");
        Assert.True(retry.Retries == 1 && retry.Replaced == 1 && retry.Chunks == 1, $"got {retry.Chunks} chunks, {retry.Retries} retries, {retry.Replaced} replaced, wanted 1, 1, 1");
    }

    [Fact]
    public void ARegionWithEnoughWordsIsNotReadAgain()
    {
        ScriptedRecognizer inner = new((_, _, _) => [Word("a", 2.2, 2.6), Word("b", 3.0, 3.4)]);
        LowYieldRetry retry = new(inner, new LowYieldRetryOptions { MinWordsPerSecond = 0.5 });

        retry.Recognize(Indexed(10), [(2 * Rate, 6 * Rate)], CancellationToken.None);

        Assert.True(inner.Calls.Count == 1 && retry.Retries == 0, $"got {inner.Calls.Count} calls, wanted one: 2 words in 4 s is 0.5 words per second");
    }

    [Fact]
    public void AShortRegionIsNotReadAgain()
    {
        ScriptedRecognizer inner = new((_, _, _) => []);
        LowYieldRetry retry = new(inner);

        retry.Recognize(Indexed(10), [(2 * Rate, (3 * Rate) + 4000)], CancellationToken.None);

        Assert.True(inner.Calls.Count == 1, $"got {inner.Calls.Count} calls, wanted one: a 1.25 s region is below the 1.5 s minimum");
    }

    [Fact]
    public void ARegionCoveringNearlyTheWholeChunkIsNotReadAgain()
    {
        ScriptedRecognizer inner = new((_, _, _) => []);
        LowYieldRetry retry = new(inner);

        retry.Recognize(Indexed(5), [(1600, 5 * Rate)], CancellationToken.None);

        Assert.True(inner.Calls.Count == 1, $"got {inner.Calls.Count} calls, wanted one: the slice would be the chunk again");
    }

    [Fact]
    public void ASecondReadingWithNoMoreWordsKeepsTheFirst()
    {
        ScriptedRecognizer inner = new((sliceStart, _, _) => sliceStart == 0 ? [Word("kept", 3.0, 3.5)] : [Word("other", 1.0, 1.5)]);
        LowYieldRetry retry = new(inner);

        IReadOnlyList<RecognizedWord> words = retry.Recognize(Indexed(10), [(2 * Rate, 6 * Rate)], CancellationToken.None);

        Assert.True(Describe(words) == "kept 3.00-3.50" && retry.Retries == 1 && retry.Replaced == 0, $"got {Describe(words)} with {retry.Replaced} replaced, wanted the first reading kept");
    }

    [Fact]
    public void WordsOfTheSecondReadingInTheMarginAreDropped()
    {
        // The slice starts at 1.8 s; a word at 0.0-0.1 in the slice lies before the region (2 s).
        ScriptedRecognizer inner = new((sliceStart, _, _) => sliceStart == 0 ? [] : [Word("margin", 0.0, 0.1), Word("in", 1.0, 1.2), Word("side", 1.5, 1.8)]);
        LowYieldRetry retry = new(inner);

        IReadOnlyList<RecognizedWord> words = retry.Recognize(Indexed(10), [(2 * Rate, 5 * Rate)], CancellationToken.None);

        Assert.True(Describe(words) == "in 2.80-3.00 | side 3.30-3.60", $"got {Describe(words)}, wanted the margin word dropped");
    }

    [Fact]
    public void ReplacingKeepsTimeOrderAndEndsBeforeTheNextStart()
    {
        List<RecognizedWord> words = LowYieldRetry.Replace(
            [Word("a", 0.0, 1.2), Word("old", 2.0, 2.4), Word("z", 5.0, 5.5)],
            [Word("new1", 1.0, 1.6), Word("new2", 1.6, 3.0)],
            1.0,
            3.0);

        Assert.True(Describe(words) == "a 0.00-1.00 | new1 1.00-1.60 | new2 1.60-3.00 | z 5.00-5.50", $"got {Describe(words)}, wanted the old word replaced and a's end clamped to new1's start");
    }

    [Fact]
    public void APlainRecogniserIsCalledWithoutRegions()
    {
        PlainRecognizer inner = new();
        LowYieldRetry retry = new(inner);

        retry.Recognize(Indexed(10), [(2 * Rate, 5 * Rate)], CancellationToken.None);

        Assert.True(inner.Lengths.SequenceEqual([10 * Rate, (3 * Rate) + 6400]), $"got calls of [{string.Join(", ", inner.Lengths)}] samples, wanted the chunk and then the region with its margins");
    }

    [Fact]
    public void BadSettingsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LowYieldRetry(new PlainRecognizer(), new LowYieldRetryOptions { MinWordsPerSecond = 0 }));
    }

    private sealed class PlainRecognizer : ISpeechRecognizer
    {
        public List<int> Lengths { get; } = [];

        public double MaxChunkSeconds => 20;

        public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation)
        {
            Lengths.Add(samples.Length);
            return [];
        }
    }
}
