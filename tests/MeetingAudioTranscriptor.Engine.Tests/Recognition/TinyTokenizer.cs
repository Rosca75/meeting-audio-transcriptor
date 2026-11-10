// A hand-written tokenizer.json shaped like Granite's (special tokens first, the 256 byte pieces, then word pieces), and a fake Granite graph that answers with scripted frame ids and scores.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;

namespace MeetingAudioTranscriptor.Engine.Tests.Recognition;

/// <summary>A small vocabulary whose ids the tests name.</summary>
internal static class TinyTokenizer
{
    public const int Blank = 0;
    public const int NoSpeech = 1;
    public const int EndOfText = 3;
    public const int Ca = 260;
    public const int Co = 261;
    public const int UCircumflex = 262;
    public const int Te = 263;
    public const int Marker = 264;
    public const int OpenQuote = 265;
    public const int Bon = 266;
    public const int Jour = 267;
    public const int CloseQuote = 268;
    public const int Question = 269;
    public const int Pas = 270;
    public const int Comma = 271;
    public const int Die = 272;
    public const int Gro = 273;
    public const int Eszett = 274;
    public const int Period = 275;
    public const int Hello = 276;
    public const int World = 277;
    public const int Again = 278;

    /// <summary>The piece of each word-piece id, from 260 on.</summary>
    private static readonly string[] Pieces =
    [
        "▁Ça", "▁co", "û", "te", "▁", "«", "▁Bon", "jour", "»", "▁?",
        "▁pas", ",", "▁Die", "▁gro", "ß", ".", "▁hello", "▁world", "▁again",
    ];

    /// <summary>The id of the byte piece for one byte.</summary>
    public static int Byte(int value) => 4 + value;

    /// <summary>The ids of a string's UTF-8 bytes as byte pieces.</summary>
    public static int[] Bytes(string text) => Encoding.UTF8.GetBytes(text).Select(b => Byte(b)).ToArray();

    /// <summary>The tokenizer.json text.</summary>
    public static string Json()
    {
        StringBuilder vocab = new();
        void Add(string piece, int id)
        {
            if (vocab.Length > 0)
            {
                vocab.Append(',');
            }

            vocab.Append(CultureInfo.InvariantCulture, $"\"{Escape(piece)}\":{id}");
        }

        string[] specials = ["<unk>", "<|nospeech|>", "<pad>", "<|endoftext|>"];
        for (int i = 0; i < specials.Length; i++)
        {
            Add(specials[i], i);
        }

        for (int b = 0; b < 256; b++)
        {
            Add(string.Create(CultureInfo.InvariantCulture, $"<0x{b:X2}>"), Byte(b));
        }

        for (int i = 0; i < Pieces.Length; i++)
        {
            Add(Pieces[i], 260 + i);
        }

        string added = string.Join(',', specials.Select((content, id) =>
            string.Create(CultureInfo.InvariantCulture, $"{{\"id\":{id},\"content\":\"{content}\",\"special\":true}}")));
        return $"{{\"version\":\"1.0\",\"added_tokens\":[{added}],\"model\":{{\"type\":\"BPE\",\"byte_fallback\":true,\"vocab\":{{{vocab}}},\"merges\":[]}}}}";
    }

    /// <summary>The vocabulary loaded from <see cref="Json"/>.</summary>
    public static BpeVocabulary Load()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(Json()));
        return BpeVocabulary.Load(stream);
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

/// <summary>Stands in for the Granite graph: returns the ids and scores the test scripts, and records what it was given.</summary>
internal sealed class FakeGraniteRunner : ITensorRunner
{
    private readonly Func<int, (long[] Ids, float[] Scores)> _answer;
    private readonly bool _hasScores;

    /// <param name="answer">The ids and scores for an input of the given sample count.</param>
    /// <param name="hasScores">False to behave like the original export, which refuses the score output.</param>
    public FakeGraniteRunner(Func<int, (long[] Ids, float[] Scores)> answer, bool hasScores = true)
    {
        _answer = answer;
        _hasScores = hasScores;
    }

    /// <summary>The inputs received, one array per run.</summary>
    public List<float[]> Inputs { get; } = [];

    /// <summary>The output names asked for, one list per call, refused calls included.</summary>
    public List<string[]> Requests { get; } = [];

    /// <summary>Always answers with the same ids and scores, whatever the input.</summary>
    public static FakeGraniteRunner Fixed(long[] ids, float[]? scores = null, bool hasScores = true) =>
        new(_ => (ids, scores ?? ids.Select(_ => 0.9f).ToArray()), hasScores);

    public TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
    {
        Requests.Add(outputNames.ToArray());
        cancellation.ThrowIfCancellationRequested();
        if (!_hasScores && outputNames.Contains(GraniteCtcRecognizer.ScoresOutput))
        {
            // The real runner refuses an undeclared output before running, with this exception type.
            throw new ArgumentException($"the model has no output named '{GraniteCtcRecognizer.ScoresOutput}'");
        }

        float[] input = inputs[0].Floats!;
        Inputs.Add((float[])input.Clone());
        (long[] ids, float[] scores) = _answer(input.Length);
        List<TensorValue> outputs = [];
        foreach (string name in outputNames)
        {
            outputs.Add(name == GraniteCtcRecognizer.IdsOutput
                ? TensorValue.OfInt64s(name, [1, ids.Length], ids)
                : TensorValue.OfFloats(name, [1, scores.Length], scores));
        }

        return [.. outputs];
    }

    public void Dispose()
    {
    }
}
