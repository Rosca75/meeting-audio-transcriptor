// Tests of the word error rate: known counts, edge cases, aggregation and a large input.
using System;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Scoring;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Scoring;

[Trait("Category", "scoring")]
public sealed class WordErrorRateTests
{
    [Theory]
    [InlineData("the cat sat", "the cat sat", 0, 0, 0)]
    [InlineData("the cat sat", "the dog sat", 1, 0, 0)]
    [InlineData("the cat sat", "the sat", 0, 1, 0)]
    [InlineData("the cat sat", "the big cat sat", 0, 0, 1)]
    [InlineData("a b c d", "a x c", 1, 1, 0)]
    [InlineData("a b c", "x y z", 3, 0, 0)]
    [InlineData("a b c", "", 0, 3, 0)]
    [InlineData("", "a b", 0, 0, 2)]
    [InlineData("one two three four five", "one three four six seven eight", 1, 1, 2)]
    [InlineData("Don't stop!", "dont stop", 1, 1, 0)]
    public void CountsSubstitutionsDeletionsAndInsertions(string reference, string hypothesis, int s, int d, int i)
    {
        WordErrorRateResult got = WordErrorRate.Compute(reference, hypothesis);

        Assert.True(
            (got.Substitutions, got.Deletions, got.Insertions) == (s, d, i),
            $"got S{got.Substitutions} D{got.Deletions} I{got.Insertions}, wanted S{s} D{d} I{i} (reference '{reference}', hypothesis '{hypothesis}')");
    }

    [Fact]
    public void WerIsErrorsOverReferenceLength()
    {
        WordErrorRateResult got = WordErrorRate.Compute("a b c d", "a x c");

        Assert.Equal(4, got.ReferenceLength);
        Assert.Equal(2, got.Errors);
        Assert.Equal(0.5, got.Wer, 10);
    }

    [Fact]
    public void WerCanExceedOne()
    {
        WordErrorRateResult got = WordErrorRate.Compute("a", "b c d");

        Assert.Equal(3.0, got.Wer, 10);
    }

    [Fact]
    public void EmptyReferenceGivesAFiniteValue()
    {
        Assert.Equal(0.0, WordErrorRate.Compute("", "").Wer);
        Assert.Equal(1.0, WordErrorRate.Compute("", "x").Wer);
    }

    [Fact]
    public void AggregateIsAMicroAverage()
    {
        WordErrorRateResult shortFile = WordErrorRate.Compute("a b", "a x");
        WordErrorRateResult longFile = WordErrorRate.Compute("a b c d e f g h i j", "a b c d e f g h i j");

        WordErrorRateResult total = WordErrorRate.Aggregate([shortFile, longFile]);

        Assert.Equal(12, total.ReferenceLength);
        Assert.Equal(1, total.Errors);
        Assert.Equal(1.0 / 12.0, total.Wer, 10);
    }

    [Fact]
    public void TotalErrorsEqualTheEditDistanceOfAReferenceImplementation()
    {
        Random random = new(7);
        string[] vocabulary = ["a", "b", "c", "d", "e"];
        for (int round = 0; round < 200; round++)
        {
            string[] reference = Enumerable.Range(0, random.Next(0, 12)).Select(_ => vocabulary[random.Next(vocabulary.Length)]).ToArray();
            string[] hypothesis = Enumerable.Range(0, random.Next(0, 12)).Select(_ => vocabulary[random.Next(vocabulary.Length)]).ToArray();

            WordErrorRateResult got = WordErrorRate.Compute(reference, hypothesis);

            int wanted = FullMatrixDistance(reference, hypothesis);
            Assert.True(
                got.Errors == wanted && got.ReferenceLength - got.Deletions + got.Insertions == hypothesis.Length,
                $"got {got.Errors} errors, wanted {wanted} (reference '{string.Join(' ', reference)}', hypothesis '{string.Join(' ', hypothesis)}')");
        }
    }

    [Fact]
    public void ALongReferenceRunsInBoundedMemory()
    {
        string[] reference = Enumerable.Range(0, 3000).Select(i => "w" + (i % 97)).ToArray();
        string[] hypothesis = reference.Where((_, i) => i % 50 != 0).ToArray();

        WordErrorRateResult got = WordErrorRate.Compute(reference, hypothesis);

        Assert.True(got.Deletions == 60 && got.Substitutions == 0 && got.Insertions == 0, $"got S{got.Substitutions} D{got.Deletions} I{got.Insertions}, wanted D60 only");
    }

    private static int FullMatrixDistance(string[] a, string[] b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }

        return d[a.Length, b.Length];
    }
}
