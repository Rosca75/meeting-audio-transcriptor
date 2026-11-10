// The Granite Speech TurboCTC recogniser: levels a chunk's input, runs the graph once, and decodes its per-frame ids and scores into timed words.
using System;
using System.Collections.Generic;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;

namespace MeetingAudioTranscriptor.Engine.Recognition.Granite;

/// <summary>Recognises speech with IBM Granite Speech 5.0 470M TurboCTC.</summary>
/// <remarks>
/// <para>
/// The graph holds the whole model: the waveform goes in (<c>waveforms</c>, float32 [1, samples],
/// 16 kHz in [-1, 1]) and one id per 80 ms frame comes out (<c>token_ids</c>, int64 [1, frames],
/// the argmax taken inside the graph). The shipped graph is derived at fetch time to also return
/// <c>token_scores</c> (float32 [1, frames]), the highest token probability of each frame, which is
/// what a word's confidence is made of. With a graph that lacks it, found on the first call, every
/// confidence is 1.
/// </para>
/// <para>
/// Before the model, the input level is evened out (<see cref="GainOptions"/>), because the
/// graph's features read the absolute level and quiet speech is otherwise dropped. A chunk whose
/// length is an odd multiple of 160 samples gets one zero sample appended, because the graph's
/// frame stacking needs whole pairs of 10 ms frames and fails otherwise.
/// </para>
/// <para>
/// The recogniser keeps no state between calls beyond the read-only vocabulary, the options and a
/// flag set once when the graph proves to have no score output; every buffer belongs to one call,
/// so it is safe to call from several threads sharing one loaded model.
/// </para>
/// <para>
/// On one core the recogniser reads about 30 seconds of audio per CPU second, 99 % of it inside
/// the graph; the input gain and the decoding are the rest. The level schemes and their measured
/// word error rates are in the remarks of <see cref="GainOptions"/>.
/// </para>
/// </remarks>
public sealed class GraniteCtcRecognizer : IRegionAwareRecognizer
{
    /// <summary>The graph's input name.</summary>
    public const string InputName = "waveforms";

    /// <summary>The graph's per-frame id output.</summary>
    public const string IdsOutput = "token_ids";

    /// <summary>The per-frame peak probability output the derived graph adds.</summary>
    public const string ScoresOutput = "token_scores";

    /// <summary>The shortest chunk read, 0.1 s; anything shorter holds no word and is below the graph's analysis window.</summary>
    private const int MinSamples = 1600;

    private static readonly string[] BothOutputs = [IdsOutput, ScoresOutput];
    private static readonly string[] IdsOnly = [IdsOutput];

    private readonly ITensorRunner _model;
    private readonly BpeVocabulary _vocabulary;
    private readonly GraniteOptions _options;

    /// <summary>0 until the first run, 1 when the graph returns scores, 2 when it does not.</summary>
    private volatile int _scoresState;

    /// <summary>Creates the recogniser over a loaded graph and its vocabulary.</summary>
    /// <param name="model">The Granite graph, shared between threads.</param>
    /// <param name="vocabulary">The pieces of the graph's tokenizer.json.</param>
    /// <param name="options">The settings; null takes the defaults.</param>
    public GraniteCtcRecognizer(ITensorRunner model, BpeVocabulary vocabulary, GraniteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(vocabulary);
        _model = model;
        _vocabulary = vocabulary;
        _options = options ?? new GraniteOptions();
        _options.Validate();
    }

    /// <inheritdoc />
    public double MaxChunkSeconds => _options.MaxChunkSeconds;

    /// <summary>The settings in use.</summary>
    public GraniteOptions Options => _options;

    /// <summary>Whether the graph returns per-frame scores; null until the first chunk is read.</summary>
    public bool? HasScores => _scoresState switch { 1 => true, 2 => false, _ => null };

    /// <inheritdoc />
    public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation) =>
        Recognize(samples, [], cancellation);

    /// <inheritdoc />
    public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(speechRegions);
        cancellation.ThrowIfCancellationRequested();
        int length = samples.Length;
        if (length < MinSamples)
        {
            return [];
        }

        // The graph stacks 10 ms frames in pairs and needs one more sample when the count is odd.
        int padded = length % 160 == 0 && (length / 160) % 2 == 1 ? length + 1 : length;
        float[] input = new float[padded];
        GainNormalizer.Apply(samples.Span, input, speechRegions, _options.Gain);

        TensorValue[] inputs = [TensorValue.OfFloats(InputName, [1, padded], input)];
        (long[] ids, float[]? scores) = Run(inputs, cancellation);

        List<CtcToken> tokens = CtcDecoder.Collapse(ids, scores, _vocabulary);
        return CtcDecoder.Words(tokens, _vocabulary, length / (double)Audio.AudioFormat.ModelSampleRate);
    }

    /// <summary>Runs the graph, asking for the scores unless the graph proved to have none.</summary>
    private (long[] Ids, float[]? Scores) Run(TensorValue[] inputs, CancellationToken cancellation)
    {
        if (_scoresState != 2)
        {
            try
            {
                TensorValue[] outputs = _model.Run(inputs, BothOutputs, cancellation);
                _scoresState = 1;
                long[] ids = IdsOf(outputs[0]);
                float[] scores = outputs[1].Floats
                    ?? throw new InvalidOperationException($"the Granite graph's {ScoresOutput} output is not float32; fetch the model again with node build/fetch-models.mjs");
                if (scores.Length != ids.Length)
                {
                    throw new InvalidOperationException(
                        $"the Granite graph returned {ids.Length} ids but {scores.Length} scores; the graph is not the one the lock pins, fetch it again with node build/fetch-models.mjs");
                }

                return (ids, scores);
            }
            catch (ArgumentException) when (_scoresState == 0)
            {
                // The runner refuses an output the graph does not declare before running anything:
                // this is the original export, which has ids alone.
                _scoresState = 2;
            }
        }

        return (IdsOf(_model.Run(inputs, IdsOnly, cancellation)[0]), null);
    }

    private static long[] IdsOf(TensorValue output) =>
        output.Int64s ?? throw new InvalidOperationException($"the Granite graph's {IdsOutput} output is not int64; the graph is not the Granite TurboCTC export, fetch it again with node build/fetch-models.mjs");
}
