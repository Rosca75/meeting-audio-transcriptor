// Turns windows of speech into unit-length speaker embeddings through the embedding model, batching
// equal-length windows into one model call.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>Computes L2-normalised speaker embeddings with a WeSpeaker-style model.</summary>
/// <remarks>
/// <para>
/// The model's contract is an input <c>feats</c> of shape [B, T, 80] (Kaldi log mel features with
/// the mean of each bin over the window subtracted) and an output <c>embs</c> of shape [B, D]
/// (D = 512 for CAM++). Every window of one call has the same length; a window of another length
/// goes through a call of its own.
/// </para>
/// <para>
/// One instance is shared by every recognition worker: the runner is thread-safe, the feature
/// tables are read-only, and every buffer a call writes belongs to that call.
/// </para>
/// </remarks>
public sealed class SpeakerEmbedder
{
    /// <summary>The model input the features go to.</summary>
    public const string InputName = "feats";

    /// <summary>The model output that holds the embeddings.</summary>
    public const string OutputName = "embs";

    private readonly ITensorRunner _runner;
    private readonly KaldiFbank _fbank;

    /// <summary>Wraps a loaded embedding model; the caller keeps ownership of the runner.</summary>
    /// <param name="runner">The embedding model.</param>
    /// <param name="batchSize">How many equal-length windows go through one model call.</param>
    public SpeakerEmbedder(ITensorRunner runner, int batchSize = 4)
        : this(runner, batchSize, FbankWindow.Hamming)
    {
    }

    internal SpeakerEmbedder(ITensorRunner runner, int batchSize, FbankWindow window)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        _runner = runner;
        BatchSize = batchSize;
        _fbank = new KaldiFbank(window);
    }

    /// <summary>How many equal-length windows go through one model call.</summary>
    public int BatchSize { get; }

    /// <summary>Computes the embedding of one stretch of speech.</summary>
    /// <param name="samples">Mono samples in [-1, 1] at 16 kHz; at least 25 ms.</param>
    /// <param name="cancellation">Stops the model call.</param>
    public float[] Embed(ReadOnlySpan<float> samples, CancellationToken cancellation)
    {
        float[][] result = new float[1][];
        EmbedRegion(samples, [(0, samples.Length / KaldiFbank.FrameShift * KaldiFbank.FrameShift)], result, cancellation);
        return result[0];
    }

    /// <summary>Computes the embedding of every window of one speech region.</summary>
    /// <param name="region">The region's samples.</param>
    /// <param name="windows">Window placements relative to the region; every start a multiple of 160 samples.</param>
    /// <param name="destination">Receives one embedding per window, in the order of <paramref name="windows"/>.</param>
    /// <param name="cancellation">Stops between model calls and inside one.</param>
    public void EmbedRegion(ReadOnlySpan<float> region, IReadOnlyList<(int Start, int Length)> windows, float[][] destination, CancellationToken cancellation)
    {
        if (windows.Count == 0)
        {
            return;
        }

        if (region.Length < KaldiFbank.FrameLength)
        {
            throw new ArgumentException($"the speech holds {region.Length} samples where an embedding needs at least {KaldiFbank.FrameLength} (25 ms)", nameof(region));
        }

        // The features of the whole region are computed once; each window reads a slice of them.
        int frames = KaldiFbank.FrameCount(region.Length);
        float[] features = ArrayPool<float>.Shared.Rent(frames * KaldiFbank.BinCount);
        try
        {
            _fbank.Compute(region, features, new KaldiFbank.Scratch());

            // Windows of one length are batched together, in order of appearance.
            int index = 0;
            List<int> batch = new(BatchSize);
            while (index < windows.Count)
            {
                batch.Clear();
                int length = windows[index].Length;
                for (int i = index; i < windows.Count && batch.Count < BatchSize; i++)
                {
                    if (windows[i].Length == length)
                    {
                        batch.Add(i);
                    }
                    else if (batch.Count > 0 && i > index)
                    {
                        break;
                    }
                }

                RunBatch(features, frames, windows, batch, destination, cancellation);
                index = batch[^1] + 1;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(features);
        }
    }

    private void RunBatch(float[] features, int regionFrames, IReadOnlyList<(int Start, int Length)> windows, List<int> batch, float[][] destination, CancellationToken cancellation)
    {
        int bins = KaldiFbank.BinCount;
        int windowFrames = KaldiFbank.FrameCount(windows[batch[0]].Length);
        float[] input = new float[batch.Count * windowFrames * bins];
        for (int b = 0; b < batch.Count; b++)
        {
            int firstFrame = windows[batch[b]].Start / KaldiFbank.FrameShift;
            if (firstFrame + windowFrames > regionFrames)
            {
                throw new ArgumentException($"window {batch[b]} ends after the region; a window must lie inside its region", nameof(windows));
            }

            Span<float> slot = input.AsSpan(b * windowFrames * bins, windowFrames * bins);
            features.AsSpan(firstFrame * bins, windowFrames * bins).CopyTo(slot);
            KaldiFbank.SubtractMean(slot);
        }

        TensorValue[] outputs = _runner.Run(
            [TensorValue.OfFloats(InputName, [batch.Count, windowFrames, bins], input)],
            [OutputName],
            cancellation);
        TensorValue embeddings = outputs[0];
        if (embeddings.Floats is null || embeddings.Shape.Length != 2 || embeddings.Shape[0] != batch.Count)
        {
            throw new InvalidOperationException(
                $"the speaker embedding model returned {OutputName} with shape [{string.Join(", ", embeddings.Shape)}] where [{batch.Count}, D] floats were expected; install the WeSpeaker CAM++ model the application ships");
        }

        int dimension = embeddings.Shape[1];
        for (int b = 0; b < batch.Count; b++)
        {
            float[] vector = embeddings.Floats.AsSpan(b * dimension, dimension).ToArray();
            VectorMath.Normalise(vector);
            destination[batch[b]] = vector;
        }
    }
}
