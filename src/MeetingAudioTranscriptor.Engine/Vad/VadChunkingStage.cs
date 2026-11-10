// The pipeline's voice-activity stage: pulls 16 kHz samples from a source, runs the VAD and yields speech chunks as they close.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Audio;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>Wires an <see cref="IAudioSource"/> through <see cref="SileroVadSession"/> into <see cref="SpeechChunker"/>.</summary>
/// <remarks>
/// The stage holds no thread of its own: the pipeline's producer thread pulls chunks from
/// <see cref="Run"/>, and each pull reads the source until the next chunk closes, so decoding, the
/// VAD and the cutting all run on that thread and at most one block of audio is read ahead. One
/// stage instance serves one recording; the totals are final once the enumeration ends.
/// </remarks>
public sealed class VadChunkingStage
{
    /// <summary>Samples read from the source per pull: about one second.</summary>
    private const int ReadSamples = 16384;

    private readonly SileroVadModel _model;
    private readonly double _maxChunkSeconds;
    private readonly SpeechChunkerOptions _options;
    private SpeechChunker? _chunker;
    private bool _started;

    /// <summary>Creates the stage for one recording.</summary>
    /// <param name="model">The VAD model, shared between recordings.</param>
    /// <param name="maxChunkSeconds">The recogniser's <c>MaxChunkSeconds</c>.</param>
    /// <param name="options">The cutting policy; null takes the defaults.</param>
    public VadChunkingStage(SileroVadModel model, double maxChunkSeconds, SpeechChunkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _maxChunkSeconds = maxChunkSeconds;
        _options = options ?? new SpeechChunkerOptions();

        // Validates the settings now, so a bad configuration fails before any audio is read.
        _ = new SpeechChunker(maxChunkSeconds, _options);
    }

    /// <summary>The samples read from the source so far.</summary>
    public long TotalSamples => _chunker?.TotalSamples ?? 0;

    /// <summary>The samples inside padded speech regions of the chunks yielded so far.</summary>
    public long SpeechSamples => _chunker?.SpeechSamples ?? 0;

    /// <summary>The samples of the chunks yielded so far, silences inside them included.</summary>
    public long ChunkedSamples => _chunker?.ChunkedSamples ?? 0;

    /// <summary>The chunks yielded so far.</summary>
    public int ChunkCount => _chunker?.ChunkCount ?? 0;

    /// <summary>The chunks yielded so far that end at a forced cut.</summary>
    public int ForcedCuts => _chunker?.ForcedCuts ?? 0;

    /// <summary>Reads the whole source and yields its speech chunks in order.</summary>
    /// <param name="source">The audio, already at 16 kHz mono; the caller disposes it.</param>
    /// <param name="progress">Receives the samples consumed after each read; null for none.</param>
    /// <param name="cancellation">Stops the reading and the VAD; the enumeration then throws <see cref="OperationCanceledException"/>.</param>
    /// <exception cref="ArgumentException">The source is not at 16 kHz.</exception>
    public IEnumerable<SpeechChunk> Run(IAudioSource source, IProgress<long>? progress = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SampleRate != AudioFormat.ModelSampleRate)
        {
            throw new ArgumentException(
                $"the audio source delivers {source.SampleRate} Hz but the VAD reads {AudioFormat.ModelSampleRate} Hz; resample the source before the VAD stage.",
                nameof(source));
        }

        if (_started)
        {
            throw new InvalidOperationException("this VAD stage already ran; create a new VadChunkingStage for each recording.");
        }

        _started = true;
        _chunker = new SpeechChunker(_maxChunkSeconds, _options);
        return Chunks(source, _chunker, progress, cancellation);
    }

    private IEnumerable<SpeechChunk> Chunks(IAudioSource source, SpeechChunker chunker, IProgress<long>? progress, CancellationToken cancellation)
    {
        SileroVadSession vad = _model.Create();
        float[] buffer = new float[ReadSamples];
        List<float> probabilities = new(_model.Options.BlockFrames);
        List<SpeechChunk> closed = [];
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            int read = source.Read(buffer);
            if (read == 0)
            {
                break;
            }

            ReadOnlySpan<float> samples = buffer.AsSpan(0, read);

            // The chunker must hold the samples before the probabilities that describe them arrive.
            chunker.AppendSamples(samples);
            vad.Append(samples, probabilities, cancellation);
            if (probabilities.Count > 0)
            {
                chunker.AppendProbabilities(CollectionsMarshal.AsSpan(probabilities), closed);
                probabilities.Clear();
            }

            progress?.Report(chunker.TotalSamples);
            foreach (SpeechChunk chunk in Drain(closed))
            {
                yield return chunk;
            }
        }

        vad.Flush(probabilities, cancellation);
        chunker.AppendProbabilities(CollectionsMarshal.AsSpan(probabilities), closed);
        chunker.Complete(closed);
        progress?.Report(chunker.TotalSamples);
        foreach (SpeechChunk chunk in Drain(closed))
        {
            yield return chunk;
        }
    }

    /// <summary>Hands out the closed chunks and empties the list, so a chunk is never held after it is yielded.</summary>
    private static SpeechChunk[] Drain(List<SpeechChunk> closed)
    {
        if (closed.Count == 0)
        {
            return [];
        }

        SpeechChunk[] taken = closed.ToArray();
        closed.Clear();
        return taken;
    }
}
