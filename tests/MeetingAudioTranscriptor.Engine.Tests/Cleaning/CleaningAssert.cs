// Shared assertions for the cleaning suites: one table row checked, and idempotence of a cleaned result.
using System.Linq;
using MeetingAudioTranscriptor.Engine.Cleaning;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Cleaning;

/// <summary>Assertions whose failure messages state what was got, what was wanted and for which input.</summary>
internal static class CleaningAssert
{
    /// <summary>Cleans the input and checks the text and the reason.</summary>
    public static void Cleans(CleaningLanguage language, string input, bool previousIsQuestion, string expectedText, string? expectedReason)
    {
        CleanedSegment result = new SegmentCleaner(language).Clean(CleaningCorpus.Words(input), previousIsQuestion);

        Assert.True(
            result.Text == expectedText,
            $"got text '{result.Text}', wanted '{expectedText}' (input '{input}', previousIsQuestion {previousIsQuestion})");
        Assert.True(
            result.RemovedReason == expectedReason,
            $"got reason '{result.RemovedReason ?? "null"}', wanted '{expectedReason ?? "null"}' (input '{input}')");
        Assert.True(
            result.Removed.Length == CleaningCorpus.Words(input).Length,
            $"got {result.Removed.Length} flags, wanted one per word (input '{input}')");
        Assert.True(
            result.RawText == string.Join(' ', input.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)),
            $"got raw '{result.RawText}', wanted the input words joined (input '{input}')");
        if (expectedReason is not null)
        {
            Assert.True(result.Removed.All(r => r), $"got a kept word in a removed segment (input '{input}')");
        }
    }

    /// <summary>Cleans, then cleans the cleaned words again, and checks the second pass removes nothing.</summary>
    public static void IsIdempotent(CleaningLanguage language, string input, bool previousIsQuestion)
    {
        SegmentCleaner cleaner = new(language);
        CleanedSegment first = cleaner.Clean(CleaningCorpus.Words(input), previousIsQuestion);
        if (first.RemovedReason is not null)
        {
            return;
        }

        CleanedSegment second = cleaner.Clean(CleaningCorpus.Words(first.Text), previousIsQuestion);
        Assert.True(
            second.Text == first.Text && second.RemovedReason is null && second.Removed.All(r => !r),
            $"got '{second.Text}' with {second.Removed.Count(r => r)} removed on the second pass, wanted '{first.Text}' unchanged (input '{input}')");
    }
}
