// Cuts the recording's word stream into sentence-like segments: at sentence ends, pauses, speaker changes and a maximum length.
using System;
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>One segment as a run of consecutive words.</summary>
/// <param name="First">The index of its first word.</param>
/// <param name="Count">How many words it holds, at least one.</param>
internal readonly record struct WordRange(int First, int Count)
{
    public int End => First + Count;
}

/// <summary>Decides where one segment ends and the next begins.</summary>
/// <remarks>
/// <para>
/// A cut falls between two words when the first ends a sentence (a question mark, an exclamation
/// mark, an ellipsis, or a full stop that is not an abbreviation or an initial), when the gap
/// between them reaches <see cref="SegmentationOptions.PauseSeconds"/>, or when the speaker
/// changes. The recogniser sometimes omits punctuation, which is why the pause alone also cuts.
/// </para>
/// <para>
/// A segment still longer than the maximum (words or seconds) is split at its largest gap, among the
/// gaps that leave at least a quarter of its words on each side so no sliver is cut off, and the
/// halves are split again until each fits. Every segment holds at least one word.
/// </para>
/// </remarks>
internal static class SegmentBuilder
{
    /// <summary>Abbreviations whose full stop does not end a sentence, lower case, in the three languages.</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        "mr.", "mrs.", "ms.", "dr.", "prof.", "st.", "vs.", "e.g.", "i.e.", "approx.", "no.",
        "m.", "mme.", "mlle.", "cf.", "p.ex.",
        "z.b.", "bzw.", "usw.", "ca.", "nr.", "hr.", "fr.", "d.h.", "u.a.", "evtl.", "ggf.",
    };

    /// <summary>Characters that may follow a sentence's last mark and still leave it the last mark.</summary>
    private static readonly char[] Closers = ['"', '\'', ')', ']', '»', '”', '’'];

    /// <summary>Cuts the words into segments.</summary>
    /// <param name="words">The words in time order, times in seconds from the start of the recording.</param>
    /// <param name="speakers">One speaker index per word, or null when speakers were not told apart.</param>
    /// <param name="options">The pause and the maxima.</param>
    public static List<WordRange> Build(IReadOnlyList<RecognizedWord> words, IReadOnlyList<int>? speakers, SegmentationOptions options)
    {
        List<WordRange> segments = [];
        int start = 0;
        for (int i = 0; i < words.Count; i++)
        {
            bool last = i == words.Count - 1;
            if (last || CutsAfter(words, speakers, options, i))
            {
                SplitLong(words, options, new WordRange(start, i - start + 1), segments);
                start = i + 1;
            }
        }

        return segments;
    }

    /// <summary>True when the text ends a sentence by its punctuation.</summary>
    internal static bool EndsSentence(string text, double gapAfter, SegmentationOptions options)
    {
        string core = text.TrimEnd().TrimEnd(Closers);
        if (core.Length == 0)
        {
            return false;
        }

        if (core.EndsWith('?') || core.EndsWith('!') || core.EndsWith('…') || core.EndsWith("...", StringComparison.Ordinal))
        {
            return true;
        }

        if (!core.EndsWith('.'))
        {
            return false;
        }

        string body = core[..^1];
        if (Abbreviations.Contains(core.ToLowerInvariant()))
        {
            return false;
        }

        // An initial such as "J." or "A.B." is part of a name; the English pronoun "I." is not one.
        if (body.Length >= 1 && body != "I" && IsInitials(body))
        {
            return false;
        }

        // "am 3. Mai" is a German date, and "the 3." rarely ends a sentence without a pause after it.
        if (body.Length > 0 && IsAllDigits(body))
        {
            return gapAfter >= options.OrdinalPauseSeconds;
        }

        return true;
    }

    private static bool CutsAfter(IReadOnlyList<RecognizedWord> words, IReadOnlyList<int>? speakers, SegmentationOptions options, int i)
    {
        double gap = words[i + 1].Start - words[i].End;
        if (speakers is not null && speakers[i] != speakers[i + 1])
        {
            return true;
        }

        if (gap >= options.PauseSeconds)
        {
            return true;
        }

        return EndsSentence(words[i].Text, gap, options);
    }

    /// <summary>Adds the range, split at its largest admissible gap while it is over a maximum.</summary>
    private static void SplitLong(IReadOnlyList<RecognizedWord> words, SegmentationOptions options, WordRange range, List<WordRange> into)
    {
        double seconds = words[range.End - 1].End - words[range.First].Start;
        if ((range.Count <= options.MaxWords && seconds <= options.MaxSeconds) || range.Count < 2)
        {
            into.Add(range);
            return;
        }

        // A cut after word k leaves k - First + 1 words on the left; each side keeps a quarter at least.
        int margin = Math.Max(1, range.Count / 4);
        int best = -1;
        double bestGap = double.NegativeInfinity;
        double middle = range.First + ((range.Count - 1) / 2.0);
        for (int k = range.First + margin - 1; k <= range.End - 1 - margin; k++)
        {
            double gap = words[k + 1].Start - words[k].End;

            // Ties go to the cut nearer the middle, so equal gaps give balanced halves.
            if (gap > bestGap || (gap == bestGap && Math.Abs(k - middle) < Math.Abs(best - middle)))
            {
                bestGap = gap;
                best = k;
            }
        }

        if (best < 0)
        {
            into.Add(range);
            return;
        }

        SplitLong(words, options, new WordRange(range.First, best - range.First + 1), into);
        SplitLong(words, options, new WordRange(best + 1, range.End - best - 1), into);
    }

    private static bool IsInitials(string body)
    {
        // Letters separated by full stops, each one letter long: "J", "A.B".
        string[] parts = body.Split('.');
        foreach (string part in parts)
        {
            if (part.Length != 1 || !char.IsUpper(part[0]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllDigits(string body)
    {
        foreach (char c in body)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
