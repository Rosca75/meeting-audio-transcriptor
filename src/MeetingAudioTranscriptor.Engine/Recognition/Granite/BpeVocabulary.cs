// The Granite tokenizer's id-to-text half: the piece table read from tokenizer.json, which ids are
// control tokens, and the decoding of ids into text exactly as the tokenizer's own decoder does it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;

namespace MeetingAudioTranscriptor.Engine.Recognition.Granite;

/// <summary>The pieces of a SentencePiece-style BPE vocabulary and their decoding into text.</summary>
/// <remarks>
/// <para>
/// A piece is one of three kinds. A control token (an added token marked special, every
/// <c>&lt;|...|&gt;</c> piece and the blank, id 0) is never text. A byte piece <c>&lt;0xNN&gt;</c>
/// stands for one UTF-8 byte of a character the vocabulary has no piece for: consecutive byte
/// pieces are joined and read as UTF-8, and a run that is not valid UTF-8 becomes one U+FFFD per
/// byte, as the reference decoder writes it, never an exception. Every other piece is text in
/// which the word marker U+2581 stands for a space.
/// </para>
/// <para>
/// <see cref="Decode"/> reproduces the tokenizer's decoder chain (replace the marker by a space,
/// byte fallback, concatenate, strip one leading space) with special tokens skipped, which is the
/// reference decoding of the model's output. The vocabulary is read from the file's bytes, never
/// from a path, and parsed with <see cref="JsonDocument"/>, which needs no reflection, so a trimmed
/// build keeps it. An instance is immutable and safe to share between threads.
/// </para>
/// </remarks>
public sealed class BpeVocabulary
{
    /// <summary>The character SentencePiece writes for the space before a word.</summary>
    internal const char WordMarker = '▁';

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly PieceKind[] _kinds;
    private readonly string[] _texts;
    private readonly byte[] _bytes;

    private BpeVocabulary(PieceKind[] kinds, string[] texts, byte[] bytes)
    {
        _kinds = kinds;
        _texts = texts;
        _bytes = bytes;
    }

    /// <summary>What an id stands for.</summary>
    internal enum PieceKind : byte
    {
        /// <summary>An id that is not text: the blank, a special token, or an id the file does not define.</summary>
        Control,

        /// <summary>One UTF-8 byte, from a <c>&lt;0xNN&gt;</c> piece.</summary>
        Byte,

        /// <summary>Text, with the word marker already turned into a space.</summary>
        Text,
    }

    /// <summary>The number of ids, the highest id plus one.</summary>
    public int Count => _kinds.Length;

    /// <summary>Reads the vocabulary from the bytes of a Hugging Face <c>tokenizer.json</c>.</summary>
    /// <param name="tokenizerJson">The file's content; read to its end and not disposed.</param>
    /// <exception cref="InvalidDataException">The content is not a BPE tokenizer file.</exception>
    public static BpeVocabulary Load(Stream tokenizerJson)
    {
        ArgumentNullException.ThrowIfNull(tokenizerJson);
        try
        {
            using JsonDocument document = JsonDocument.Parse(tokenizerJson, new JsonDocumentOptions { MaxDepth = 64 });
            return FromDocument(document.RootElement);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(
                $"the tokenizer file is not valid JSON ({e.Message}); it must be the tokenizer.json the model lock pins, fetched again with node build/fetch-models.mjs", e);
        }
    }

    /// <summary>Reads the vocabulary from the bytes of a <c>tokenizer.json</c> already in memory.</summary>
    public static BpeVocabulary Load(ReadOnlyMemory<byte> tokenizerJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(tokenizerJson, new JsonDocumentOptions { MaxDepth = 64 });
            return FromDocument(document.RootElement);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(
                $"the tokenizer file is not valid JSON ({e.Message}); it must be the tokenizer.json the model lock pins, fetched again with node build/fetch-models.mjs", e);
        }
    }

    /// <summary>Decodes ids to text as the reference decoder does with special tokens skipped.</summary>
    /// <param name="ids">The ids, already collapsed (no CTC repeats, no blanks needed).</param>
    public string Decode(ReadOnlySpan<int> ids)
    {
        StringBuilder text = new();
        AppendDecoded(ids, text);
        if (text.Length > 0 && text[0] == ' ')
        {
            text.Remove(0, 1);
        }

        return text.ToString();
    }

    /// <summary>What an id stands for; an id outside the table is a control id.</summary>
    internal PieceKind KindOf(int id) => (uint)id < (uint)_kinds.Length ? _kinds[id] : PieceKind.Control;

    /// <summary>Whether a text piece begins with the word marker, which makes it the start of a word.</summary>
    internal bool StartsWord(int id) => KindOf(id) == PieceKind.Text && _texts[id].Length > 0 && _texts[id][0] == ' ';

    /// <summary>The text of a text piece, with the marker as a space; empty for other kinds.</summary>
    internal string TextOf(int id) => KindOf(id) == PieceKind.Text ? _texts[id] : string.Empty;

    /// <summary>
    /// Appends the decoding of ids to a builder, without the final leading-space strip: control ids
    /// are skipped, runs of byte pieces are read as UTF-8, text pieces are appended as they are.
    /// </summary>
    internal void AppendDecoded(ReadOnlySpan<int> ids, StringBuilder text)
    {
        // A run of byte pieces is a few characters long; a longer one moves to a growing array.
        Span<byte> run = stackalloc byte[64];
        int runLength = 0;
        foreach (int id in ids)
        {
            switch (KindOf(id))
            {
                case PieceKind.Byte:
                    if (runLength == run.Length)
                    {
                        byte[] larger = new byte[run.Length * 2];
                        run.CopyTo(larger);
                        run = larger;
                    }

                    run[runLength++] = _bytes[id];
                    break;
                case PieceKind.Text:
                    FlushBytes(run[..runLength], text);
                    runLength = 0;
                    text.Append(_texts[id]);
                    break;
                default:
                    // A skipped token is removed before the decoder runs, so it does not end a byte run.
                    break;
            }
        }

        FlushBytes(run[..runLength], text);
    }

    /// <summary>
    /// Appends a run of byte pieces: its UTF-8 reading when the whole run is valid, else one
    /// U+FFFD per byte, which is how the reference decoder treats a run it cannot read.
    /// </summary>
    private static void FlushBytes(ReadOnlySpan<byte> run, StringBuilder text)
    {
        if (run.IsEmpty)
        {
            return;
        }

        if (Utf8.IsValid(run))
        {
            text.Append(StrictUtf8.GetString(run));
        }
        else
        {
            text.Append('�', run.Length);
        }
    }

    private static BpeVocabulary FromDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("model", out JsonElement model)
            || !model.TryGetProperty("vocab", out JsonElement vocab)
            || vocab.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("the tokenizer file has no model.vocab object; it must be a Hugging Face tokenizer.json of a BPE model such as the one the Granite lock entry pins");
        }

        Dictionary<int, string> pieces = [];
        foreach (JsonProperty entry in vocab.EnumerateObject())
        {
            if (!entry.Value.TryGetInt32(out int id) || id < 0)
            {
                throw new InvalidDataException($"the tokenizer's piece '{entry.Name}' has the id {entry.Value}, wanted a non-negative integer; the file is damaged, fetch it again");
            }

            pieces[id] = entry.Name;
        }

        HashSet<int> special = [0];
        if (root.TryGetProperty("added_tokens", out JsonElement added) && added.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement token in added.EnumerateArray())
            {
                if (!token.TryGetProperty("id", out JsonElement idElement) || !idElement.TryGetInt32(out int id) || id < 0
                    || !token.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                // An added token's content overrides the model's piece, as in the reference tokenizer.
                pieces[id] = content.GetString()!;
                if (token.TryGetProperty("special", out JsonElement flag) && flag.ValueKind == JsonValueKind.True)
                {
                    special.Add(id);
                }
            }
        }

        int count = 0;
        foreach (int id in pieces.Keys)
        {
            count = Math.Max(count, id + 1);
        }

        PieceKind[] kinds = new PieceKind[count];
        string[] texts = new string[count];
        byte[] bytes = new byte[count];
        Array.Fill(texts, string.Empty);
        foreach ((int id, string piece) in pieces)
        {
            if (special.Contains(id) || (piece.StartsWith("<|", StringComparison.Ordinal) && piece.EndsWith("|>", StringComparison.Ordinal)))
            {
                kinds[id] = PieceKind.Control;
            }
            else if (TryParseBytePiece(piece, out byte value))
            {
                kinds[id] = PieceKind.Byte;
                bytes[id] = value;
            }
            else
            {
                kinds[id] = PieceKind.Text;
                texts[id] = piece.Replace(WordMarker, ' ');
            }
        }

        return new BpeVocabulary(kinds, texts, bytes);
    }

    /// <summary>Reads a byte piece: exactly <c>&lt;0x</c>, two hex digits and <c>&gt;</c>, as the reference decoder requires.</summary>
    private static bool TryParseBytePiece(string piece, out byte value)
    {
        value = 0;
        if (piece.Length != 6 || !piece.StartsWith("<0x", StringComparison.Ordinal) || piece[5] != '>')
        {
            return false;
        }

        return byte.TryParse(piece.AsSpan(3, 2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
