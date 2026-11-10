// The result of cleaning one segment: the cleaned text, which words went, why the segment went, and the raw text.
namespace MeetingAudioTranscriptor.Engine.Cleaning;

/// <summary>The outcome of <see cref="SegmentCleaner.Clean"/> for one segment.</summary>
/// <param name="Text">The cleaned text; empty when the whole segment was removed.</param>
/// <param name="Removed">One flag per input word, true when the word is not in <paramref name="Text"/>.</param>
/// <param name="RemovedReason">
/// Why the whole segment was dropped: <see cref="Backchannel"/>, <see cref="Filler"/> or <see cref="Empty"/>;
/// null when the segment keeps text.
/// </param>
/// <param name="RawText">The input words joined by single spaces, unchanged.</param>
/// <remarks>
/// The raw text travels with the result because the raw transcript is kept beside the cleaned one,
/// and a caller that has the result needs no second pass over the words.
/// </remarks>
public sealed record CleanedSegment(string Text, bool[] Removed, string? RemovedReason, string RawText)
{
    /// <summary>The segment was a pure acknowledgement.</summary>
    public const string Backchannel = "backchannel";

    /// <summary>Only hesitation sounds were left.</summary>
    public const string Filler = "filler";

    /// <summary>The segment had no words.</summary>
    public const string Empty = "empty";
}
