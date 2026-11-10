// Greedy CTC decoding of the Granite model's per-frame ids into tokens, and of tokens into timed words with a confidence.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MeetingAudioTranscriptor.Engine.Recognition.Granite;

/// <summary>One emitted token: its id, the run of frames it held, and its peak probability over that run.</summary>
internal readonly record struct CtcToken(int Id, int FirstFrame, int LastFrame, float Score);

/// <summary>Turns the frame ids the graph returns into tokens and the tokens into words.</summary>
/// <remarks>
/// <para>
/// The model emits one id per 80 ms frame. A run of equal ids is one token, the blank and every
/// control token are dropped, and a token keeps its run of frames, so its time is where the model
/// emitted it and its score is the highest per-frame probability inside the run.
/// </para>
/// <para>
/// A token whose piece begins with the word marker starts a word; any other token (a piece
/// without the marker, a byte piece, punctuation) continues the current word, which is how the
/// tokenizer's decoder places spaces. A word made only of punctuation characters that the model
/// wrote as a word of its own (French puts a space before ? ! : ;) is attached to the word before it, and an
/// opening bracket or quote to the word after it, with the space kept: joining the words with
/// single spaces gives exactly the decoder's text, and no word is punctuation alone. Punctuation
/// with no word to attach to is dropped, because it carries nothing that was said.
/// </para>
/// <para>
/// A word's confidence is the lowest score of its tokens: a word is wrong when any of its pieces
/// is, so its weakest piece bounds it, whereas a mean lets confident pieces hide a doubtful one.
/// </para>
/// </remarks>
internal static class CtcDecoder
{
    /// <summary>The time one output frame covers: 8x subsampling of 10 ms feature frames.</summary>
    public const double FrameSeconds = 0.08;

    /// <summary>Collapses frame ids into tokens.</summary>
    /// <param name="ids">One id per frame.</param>
    /// <param name="scores">One peak probability per frame, or empty when the graph gives none (every token then scores 1).</param>
    /// <param name="vocabulary">Says which ids are control tokens.</param>
    public static List<CtcToken> Collapse(ReadOnlySpan<long> ids, ReadOnlySpan<float> scores, BpeVocabulary vocabulary)
    {
        List<CtcToken> tokens = [];
        int runStart = 0;
        for (int frame = 1; frame <= ids.Length; frame++)
        {
            if (frame < ids.Length && ids[frame] == ids[runStart])
            {
                continue;
            }

            long id = ids[runStart];
            if (id >= 0 && id <= int.MaxValue && vocabulary.KindOf((int)id) != BpeVocabulary.PieceKind.Control)
            {
                float score = 1f;
                if (!scores.IsEmpty)
                {
                    score = 0f;
                    for (int f = runStart; f < frame; f++)
                    {
                        score = Math.Max(score, scores[f]);
                    }
                }

                tokens.Add(new CtcToken((int)id, runStart, frame - 1, score));
            }

            runStart = frame;
        }

        return tokens;
    }

    /// <summary>Assembles tokens into timed words.</summary>
    /// <param name="tokens">The tokens in frame order.</param>
    /// <param name="vocabulary">The pieces' text.</param>
    /// <param name="chunkSeconds">The chunk's length; no word ends after it.</param>
    public static List<RecognizedWord> Words(IReadOnlyList<CtcToken> tokens, BpeVocabulary vocabulary, double chunkSeconds)
    {
        List<Draft> drafts = Group(tokens, vocabulary);
        List<Draft> merged = AttachPunctuation(drafts);
        List<RecognizedWord> words = new(merged.Count);
        for (int i = 0; i < merged.Count; i++)
        {
            Draft draft = merged[i];
            double start = Math.Min(draft.FirstFrame * FrameSeconds, chunkSeconds);
            double end = Math.Min((draft.LastFrame + 1) * FrameSeconds, chunkSeconds);
            if (i + 1 < merged.Count)
            {
                end = Math.Min(end, merged[i + 1].FirstFrame * FrameSeconds);
            }

            words.Add(new RecognizedWord(draft.Text, start, Math.Max(start, end), draft.Confidence));
        }

        return words;
    }

    /// <summary>Splits tokens into words at word-marker pieces and decodes each word's text.</summary>
    private static List<Draft> Group(IReadOnlyList<CtcToken> tokens, BpeVocabulary vocabulary)
    {
        List<Draft> drafts = [];
        StringBuilder text = new();
        int[] ids = new int[tokens.Count];
        for (int i = 0; i < tokens.Count; i++)
        {
            ids[i] = tokens[i].Id;
        }

        int first = 0;
        for (int i = 1; i <= tokens.Count; i++)
        {
            if (i < tokens.Count && !vocabulary.StartsWord(tokens[i].Id))
            {
                continue;
            }

            if (i > first)
            {
                text.Clear();
                vocabulary.AppendDecoded(ids.AsSpan(first, i - first), text);
                if (text.Length > 0 && text[0] == ' ')
                {
                    text.Remove(0, 1);
                }

                if (text.Length > 0)
                {
                    float confidence = 1f;
                    for (int t = first; t < i; t++)
                    {
                        confidence = Math.Min(confidence, tokens[t].Score);
                    }

                    drafts.Add(new Draft(text.ToString(), tokens[first].FirstFrame, tokens[i - 1].LastFrame, confidence));
                }
            }

            first = i;
        }

        return drafts;
    }

    /// <summary>Attaches each punctuation-only word to a neighbour, keeping the decoder's space between them.</summary>
    private static List<Draft> AttachPunctuation(List<Draft> drafts)
    {
        List<Draft> result = new(drafts.Count);
        Draft? opening = null;
        foreach (Draft draft in drafts)
        {
            Draft current = draft;
            if (opening is { } open)
            {
                current = new Draft(open.Text + " " + current.Text, open.FirstFrame, current.LastFrame, Math.Min(open.Confidence, current.Confidence));
                opening = null;
                if (IsPunctuationOnly(current.Text))
                {
                    opening = current;
                    continue;
                }
            }

            if (!IsPunctuationOnly(current.Text))
            {
                result.Add(current);
            }
            else if (IsOpening(current.Text) || result.Count == 0)
            {
                opening = current;
            }
            else
            {
                Draft previous = result[^1];
                result[^1] = new Draft(previous.Text + " " + current.Text, previous.FirstFrame, current.LastFrame, Math.Min(previous.Confidence, current.Confidence));
            }
        }

        // Punctuation left at the end attaches backwards when it can, else it is dropped.
        if (opening is { } trailing && result.Count > 0)
        {
            Draft previous = result[^1];
            result[^1] = new Draft(previous.Text + " " + trailing.Text, previous.FirstFrame, trailing.LastFrame, Math.Min(previous.Confidence, trailing.Confidence));
        }

        return result;
    }

    /// <summary>True when every character is punctuation or a space (a symbol such as an emoji or a currency sign is a word of its own).</summary>
    internal static bool IsPunctuationOnly(string text)
    {
        foreach (char c in text)
        {
            if (!char.IsPunctuation(c) && !char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the text begins with an opening bracket, an opening quote, or an inverted ? or !.</summary>
    private static bool IsOpening(string text)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, 0);
        return category is UnicodeCategory.OpenPunctuation or UnicodeCategory.InitialQuotePunctuation || text[0] is '¿' or '¡';
    }

    /// <summary>A word under assembly.</summary>
    private readonly record struct Draft(string Text, int FirstFrame, int LastFrame, float Confidence);
}
