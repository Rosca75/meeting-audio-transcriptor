// The rule-based cleaner of one segment: removes fillers, stutters, clear false starts and pure backchannels.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Cleaning.Lexicons;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Cleaning;

/// <summary>
/// Cleans one segment (a sentence-like stretch by one speaker) of recognised words in one language.
/// </summary>
/// <remarks>
/// <para>
/// The cleaner only decides which words to drop; it never rewrites one. The sole text changes are
/// re-capitalising the first kept word and trimming punctuation a removal left orphaned. The
/// rules run in a fixed order, and the first three repeat until nothing changes so a second pass over
/// cleaned words is a no-op (idempotence):
/// </para>
/// <list type="number">
/// <item><description>Fillers: a hesitation sound (<c>um</c>, <c>euh</c>, <c>ähm</c>, stretched forms included) goes.</description></item>
/// <item><description>Stutters: <c>I I think</c>, <c>I think I think we</c> and a fragment <c>th- the</c> collapse to one.</description></item>
/// <item><description>False starts: <c>I was, I think we should</c> loses <c>I was,</c>.</description></item>
/// <item><description>Backchannel: a segment that is only an acknowledgement goes whole.</description></item>
/// <item><description>Reassembly: spacing, capital letter and sentence-final punctuation are restored.</description></item>
/// </list>
/// <para>
/// An instance holds only immutable lists, so it is thread-safe and one instance serves a whole run.
/// </para>
/// </remarks>
public sealed class SegmentCleaner
{
    /// <summary>The longest word run a stutter or a false start may span.</summary>
    private const int MaxRun = 3;

    /// <summary>The most words a pure backchannel segment may hold.</summary>
    private const int MaxBackchannelWords = 3;

    private readonly Lexicon _lexicon;

    /// <summary>Creates the cleaner of one language.</summary>
    /// <param name="language">The language of the words it will be given.</param>
    public SegmentCleaner(CleaningLanguage language)
    {
        Language = language;
        _lexicon = Lexicon.For(language);
    }

    /// <summary>The language this cleaner was built for.</summary>
    public CleaningLanguage Language { get; }

    /// <summary>Cleans one segment.</summary>
    /// <param name="words">The segment's words, in order, with their casing and attached punctuation.</param>
    /// <param name="previousSegmentIsQuestion">
    /// True when the previous segment, by another speaker, ended with a question mark. An answer
    /// (<c>yes</c>, <c>oui</c>, <c>ja</c>, <c>ok</c>) to a question may be a decision, so it is then kept.
    /// </param>
    /// <returns>The cleaned text, the per-word removal flags and the reason a whole segment went.</returns>
    public CleanedSegment Clean(IReadOnlyList<RecognizedWord> words, bool previousSegmentIsQuestion)
    {
        ArgumentNullException.ThrowIfNull(words);

        int n = words.Count;
        string[] texts = new string[n];
        string[] keys = new string[n];
        for (int i = 0; i < n; i++)
        {
            texts[i] = words[i].Text ?? string.Empty;
            keys[i] = WordKeys.Key(texts[i]);
        }

        string raw = string.Join(' ', texts);
        bool[] removed = new bool[n];
        if (n == 0)
        {
            return new CleanedSegment(string.Empty, removed, CleanedSegment.Empty, raw);
        }

        Pass pass = new(this, texts, keys, removed);
        pass.RemoveFillers();
        List<int> kept = [];
        for (int i = 0; i < n; i++)
        {
            if (!removed[i])
            {
                kept.Add(i);
            }
        }

        bool changed;
        do
        {
            changed = pass.DropFragments(kept);
            changed |= pass.CollapseRepetitions(kept);
            changed |= pass.DropFalseStart(kept);
        }
        while (changed);

        if (kept.Count == 0)
        {
            return new CleanedSegment(string.Empty, removed, CleanedSegment.Filler, raw);
        }

        if (pass.IsPureBackchannel(kept, previousSegmentIsQuestion))
        {
            Array.Fill(removed, true);
            return new CleanedSegment(string.Empty, removed, CleanedSegment.Backchannel, raw);
        }

        return new CleanedSegment(Reassemble(texts, kept), removed, null, raw);
    }

    /// <summary>
    /// Joins the kept words, restores a capital where the first word of the segment went and moves the
    /// segment's final punctuation onto the last kept word when the word that carried it went.
    /// </summary>
    private static string Reassemble(string[] texts, List<int> kept)
    {
        List<string> parts = kept.ConvertAll(i => texts[i]);

        // A removal that took the segment's first word would otherwise leave a lower-case start.
        if (kept[0] != 0 && StartsUpper(texts[0]))
        {
            parts[0] = Capitalise(parts[0]);
        }

        // Dangling punctuation: "so, um." must end "so." and "so, um" must end "so", never "so,".
        if (kept[^1] != texts.Length - 1)
        {
            string last = parts[^1];
            if (!WordKeys.EndsSentence(last))
            {
                parts[^1] = last.TrimEnd(',', ';', ':') + WordKeys.Terminal(texts[^1]);
            }
        }

        return string.Join(' ', parts);
    }

    private static bool StartsUpper(string text)
    {
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                return char.IsUpper(c);
            }

            if (char.IsDigit(c))
            {
                return false;
            }
        }

        return false;
    }

    private static string Capitalise(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLetter(text[i]))
            {
                return char.IsLower(text[i]) ? string.Concat(text.AsSpan(0, i), char.ToUpperInvariant(text[i]).ToString(), text.AsSpan(i + 1)) : text;
            }

            if (char.IsDigit(text[i]))
            {
                return text;
            }
        }

        return text;
    }

    /// <summary>The working state of one <see cref="Clean"/> call, so the rules share arrays without the cleaner holding any.</summary>
    private sealed class Pass(SegmentCleaner owner, string[] texts, string[] keys, bool[] removed)
    {
        private Lexicon Lex => owner._lexicon;

        /// <summary>
        /// Rule a. Removes hesitation sounds and punctuation-only orphans. An acknowledgement sound such as
        /// <c>mhm</c> goes only when the segment has other substance, since alone it is a backchannel.
        /// </summary>
        /// <example>"Um, so we should go" becomes "So we should go".</example>
        public void RemoveFillers()
        {
            bool substance = keys.Any(k => k.Length > 0 && !Lex.IsBackchannelVocabulary(k));
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (key.Length == 0)
                {
                    removed[i] = IsOrphanPunctuation(texts[i]);
                }
                else if (Lex.IsFiller(key))
                {
                    removed[i] = true;
                }
                else if (Lex.IsContextFiller(key))
                {
                    removed[i] = IsContextFillerHere(i);
                }
                else if (substance && Lex.IsInSentenceFiller(key))
                {
                    removed[i] = true;
                }
            }
        }

        /// <summary>
        /// Rule b, fragments. A token ending in a hyphen whose letters start the next word is a restart
        /// the recogniser wrote out, and goes.
        /// </summary>
        /// <example>"th- the plan" becomes "the plan".</example>
        public bool DropFragments(List<int> kept)
        {
            bool changed = false;
            for (int p = 0; p + 1 < kept.Count;)
            {
                int i = kept[p];
                string fragment = keys[i];
                if ((texts[i].EndsWith('-') || texts[i].EndsWith('–'))
                    && fragment.Length > 0
                    && !WordKeys.HasDigit(fragment)
                    && keys[kept[p + 1]].StartsWith(fragment, StringComparison.Ordinal))
                {
                    Remove(kept, p);
                    changed = true;
                }
                else
                {
                    p++;
                }
            }

            return changed;
        }

        /// <summary>
        /// Rule b, repetitions. An immediate repetition of one word or of a run of two or three words
        /// collapses to one occurrence (the earlier copy goes, the later keeps the punctuation). Kept:
        /// numbers, the per-language allowlist of meaningful doublings, a name repeated, and a repetition
        /// across a sentence end. The allowlist trades a few kept stutters for never deleting grammar.
        /// </summary>
        /// <example>"I I think we we should" becomes "I think we should"; "two two five" stays.</example>
        public bool CollapseRepetitions(List<int> kept)
        {
            bool changed = false;
            for (int p = 0; p < kept.Count;)
            {
                bool collapsed = false;
                for (int len = Math.Min(MaxRun, (kept.Count - p) / 2); len >= 1 && !collapsed; len--)
                {
                    if (IsRepeatedRun(kept, p, len) && !IsProtectedRun(kept, p, len))
                    {
                        for (int k = 0; k < len; k++)
                        {
                            Remove(kept, p);
                        }

                        collapsed = true;
                        changed = true;
                    }
                }

                if (!collapsed)
                {
                    p++;
                }
            }

            return changed;
        }

        /// <summary>
        /// Rule c. A prefix of two or three function words that ends in a comma, dash or ellipsis and is
        /// followed by a restart opening with the same first word but continuing differently is an
        /// abandoned start and goes. A prefix with a content word or a number is never touched. A
        /// one-word prefix is an ordinary repetition and belongs to rule b, with its allowlist.
        /// </summary>
        /// <example>"I was, I think we should" becomes "I think we should"; "on va, on doit partir" becomes "on doit partir".</example>
        public bool DropFalseStart(List<int> kept)
        {
            for (int p = 0; p < kept.Count; p++)
            {
                if (p > 0 && !WordKeys.EndsWithPunctuation(texts[kept[p - 1]]))
                {
                    continue;
                }

                for (int len = 2; len <= MaxRun; len++)
                {
                    if (p + len + 1 >= kept.Count || !IsFalseStartAt(kept, p, len))
                    {
                        continue;
                    }

                    for (int k = 0; k < len; k++)
                    {
                        Remove(kept, p);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>Rule d. True when what is left is only an acknowledgement that carries no answer or question.</summary>
        public bool IsPureBackchannel(List<int> kept, bool previousSegmentIsQuestion)
        {
            if (kept.Count > MaxBackchannelWords)
            {
                return false;
            }

            string[] ks = kept.ConvertAll(i => keys[i]).ToArray();
            if (!Tiles(ks, 0))
            {
                return false;
            }

            // "Okay?" or "Right?" asks something of the listener, so it is content.
            if (texts[^1].TrimEnd('"', '\'', ')', '”').EndsWith('?'))
            {
                return false;
            }

            // A "yes" after a question is the answer, possibly a decision.
            return !(previousSegmentIsQuestion && ks.Any(Lex.IsAnswerWord));
        }

        private void Remove(List<int> kept, int position)
        {
            removed[kept[position]] = true;
            kept.RemoveAt(position);
        }

        private bool Tiles(string[] ks, int pos)
        {
            if (pos == ks.Length)
            {
                return true;
            }

            if (Lex.IsBackchannelWord(ks[pos]) && Tiles(ks, pos + 1))
            {
                return true;
            }

            foreach (string[] phrase in Lex.BackchannelPhrases)
            {
                if (pos + phrase.Length <= ks.Length
                    && phrase.Where((w, k) => ks[pos + k] == w).Count() == phrase.Length
                    && Tiles(ks, pos + phrase.Length))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRepeatedRun(List<int> kept, int p, int len)
        {
            for (int k = 0; k < len; k++)
            {
                string key = keys[kept[p + k]];
                if (key.Length == 0 || key != keys[kept[p + len + k]])
                {
                    return false;
                }
            }

            // A full stop, question mark or ellipsis between the copies makes them two sentences.
            return !WordKeys.EndsSentence(texts[kept[p + len - 1]]);
        }

        private bool IsProtectedRun(List<int> kept, int p, int len)
        {
            for (int k = 0; k < len; k++)
            {
                string key = keys[kept[p + k]];
                if (WordKeys.HasDigit(key) || Lex.IsNumeral(key))
                {
                    return true;
                }
            }

            if (len == 1 && Lex.IsRepetitionAllowed(keys[kept[p]]))
            {
                return true;
            }

            // A capitalised word inside a sentence is probably a name: "Anna Anna" is kept, "I I" is not.
            int second = kept[p + len];
            bool sentenceStart = p > 0 && WordKeys.EndsSentence(texts[kept[p - 1]]);
            return Lex.CapitalisedWordsAreNames && second != 0 && !sentenceStart
                && StartsUpperCase(texts[second]) && keys[second] != "i";
        }

        private bool IsFalseStartAt(List<int> kept, int p, int len)
        {
            for (int k = 0; k < len; k++)
            {
                string key = keys[kept[p + k]];
                if (!Lex.IsFunctionKey(key) || WordKeys.HasDigit(key) || Lex.IsNumeral(key))
                {
                    return false;
                }

                // Only the last word of the prefix carries the break mark.
                if (k < len - 1 && WordKeys.EndsWithPunctuation(texts[kept[p + k]]))
                {
                    return false;
                }
            }

            if (!WordKeys.EndsWithBreak(texts[kept[p + len - 1]]) || keys[kept[p + len]] != keys[kept[p]])
            {
                return false;
            }

            // An identical restart is a plain repetition, which rule b owns.
            for (int k = 0; k < len; k++)
            {
                if (keys[kept[p + k]] != keys[kept[p + len + k]])
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsContextFillerHere(int i)
        {
            string trimmed = TrimOuter(texts[i]);
            if (trimmed == keys[i])
            {
                return true;
            }

            return i == 0 && i + 1 < keys.Length && Lex.IsContextFollower(keys[i + 1]);
        }

        private static bool IsOrphanPunctuation(string text) =>
            text.Length > 0 && text.All(c => c is ',' or ';' or ':');

        private static string TrimOuter(string text)
        {
            int start = 0;
            int end = text.Length;
            while (start < end && !char.IsLetterOrDigit(text[start]))
            {
                start++;
            }

            while (end > start && !char.IsLetterOrDigit(text[end - 1]))
            {
                end--;
            }

            return text[start..end];
        }

        private static bool StartsUpperCase(string text) => StartsUpper(text);
    }
}
