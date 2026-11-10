// Word error rate by word-level Levenshtein distance, with the substitution, deletion and insertion counts.
using System;
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Engine.Scoring;

/// <summary>The error counts of one reference and hypothesis pair.</summary>
/// <param name="Substitutions">Reference words replaced by another word.</param>
/// <param name="Deletions">Reference words missing from the hypothesis.</param>
/// <param name="Insertions">Hypothesis words with no reference word.</param>
/// <param name="ReferenceLength">The number of reference words.</param>
public readonly record struct WordErrorRateResult(int Substitutions, int Deletions, int Insertions, int ReferenceLength)
{
    /// <summary>Substitutions plus deletions plus insertions.</summary>
    public int Errors => Substitutions + Deletions + Insertions;

    /// <summary>
    /// Errors divided by the reference length. An empty reference gives 0 when there is no error and 1
    /// otherwise, because a ratio over zero words has no value and a score must stay a finite number.
    /// </summary>
    public double Wer => ReferenceLength == 0 ? (Errors == 0 ? 0.0 : 1.0) : (double)Errors / ReferenceLength;
}

/// <summary>Computes the word error rate and aggregates it over many pairs.</summary>
/// <remarks>
/// The distance is computed with two rows of cells that each carry their own counts, so no
/// backtrace and no matrix are stored: a two-hour reference of 20,000 words needs a few hundred
/// kilobytes instead of hundreds of millions of back-pointers. Where several alignments share the
/// minimum distance the split between substitutions, deletions and insertions is chosen by a fixed
/// preference (substitution, then deletion, then insertion); the total is the same for all of them.
/// </remarks>
public static class WordErrorRate
{
    /// <summary>Compares a hypothesis against a reference.</summary>
    /// <param name="reference">The reference tokens (see <see cref="TextNormalizer"/>).</param>
    /// <param name="hypothesis">The hypothesis tokens.</param>
    /// <returns>The counts.</returns>
    public static WordErrorRateResult Compute(IReadOnlyList<string> reference, IReadOnlyList<string> hypothesis)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(hypothesis);

        // Words become integers once, so the inner loop compares ints rather than strings.
        Dictionary<string, int> ids = new(StringComparer.Ordinal);
        int[] r = Intern(reference, ids);
        int[] h = Intern(hypothesis, ids);

        Cell[] previous = new Cell[h.Length + 1];
        Cell[] current = new Cell[h.Length + 1];
        for (int j = 0; j <= h.Length; j++)
        {
            previous[j] = new Cell(0, 0, j);
        }

        for (int i = 1; i <= r.Length; i++)
        {
            current[0] = new Cell(0, i, 0);
            for (int j = 1; j <= h.Length; j++)
            {
                if (r[i - 1] == h[j - 1])
                {
                    current[j] = previous[j - 1];
                    continue;
                }

                Cell sub = previous[j - 1].WithSubstitution();
                Cell del = previous[j].WithDeletion();
                Cell ins = current[j - 1].WithInsertion();
                Cell best = sub;
                if (del.Cost < best.Cost)
                {
                    best = del;
                }

                if (ins.Cost < best.Cost)
                {
                    best = ins;
                }

                current[j] = best;
            }

            (previous, current) = (current, previous);
        }

        Cell end = previous[h.Length];
        return new WordErrorRateResult(end.S, end.D, end.I, r.Length);
    }

    /// <summary>Normalises two texts and compares them.</summary>
    /// <param name="reference">The reference text.</param>
    /// <param name="hypothesis">The hypothesis text.</param>
    /// <returns>The counts.</returns>
    public static WordErrorRateResult Compute(string reference, string hypothesis) =>
        Compute(TextNormalizer.Normalize(reference), TextNormalizer.Normalize(hypothesis));

    /// <summary>Micro-averages several pairs: the counts are summed, so a long file weighs more than a short one.</summary>
    /// <param name="results">The per-pair results.</param>
    /// <returns>The summed counts, whose <see cref="WordErrorRateResult.Wer"/> is the micro-averaged rate.</returns>
    public static WordErrorRateResult Aggregate(IEnumerable<WordErrorRateResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        int s = 0;
        int d = 0;
        int i = 0;
        int n = 0;
        foreach (WordErrorRateResult result in results)
        {
            s += result.Substitutions;
            d += result.Deletions;
            i += result.Insertions;
            n += result.ReferenceLength;
        }

        return new WordErrorRateResult(s, d, i, n);
    }

    private static int[] Intern(IReadOnlyList<string> words, Dictionary<string, int> ids)
    {
        int[] result = new int[words.Count];
        for (int k = 0; k < result.Length; k++)
        {
            if (!ids.TryGetValue(words[k], out int id))
            {
                id = ids.Count;
                ids[words[k]] = id;
            }

            result[k] = id;
        }

        return result;
    }

    private readonly record struct Cell(int S, int D, int I)
    {
        public int Cost => S + D + I;

        public Cell WithSubstitution() => new(S + 1, D, I);

        public Cell WithDeletion() => new(S, D + 1, I);

        public Cell WithInsertion() => new(S, D, I + 1);
    }
}
