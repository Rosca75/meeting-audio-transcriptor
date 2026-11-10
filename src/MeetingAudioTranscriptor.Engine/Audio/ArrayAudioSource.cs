// An audio source over samples already in memory, for callers that hold a clip and for tests.
using System;

namespace MeetingAudioTranscriptor.Engine.Audio;

/// <summary>Delivers the samples of a buffer in order, optionally a few at a time.</summary>
/// <remarks>
/// The optional maximum block size makes the source return short reads on purpose, so a test can
/// prove that a consumer gives the same answer however its input arrives in pieces.
/// </remarks>
public sealed class ArrayAudioSource : IAudioSource
{
    private readonly ReadOnlyMemory<float> _samples;
    private readonly int _maxBlockSize;
    private int _position;

    /// <summary>Creates a source over the samples, which are read in place without a copy.</summary>
    /// <param name="samples">Mono samples in the range -1 to 1.</param>
    /// <param name="sampleRate">Samples per second; at least 1.</param>
    /// <param name="maxBlockSize">The most samples one <see cref="Read"/> returns; at least 1.</param>
    public ArrayAudioSource(ReadOnlyMemory<float> samples, int sampleRate, int maxBlockSize = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBlockSize, 1);
        _samples = samples;
        SampleRate = sampleRate;
        _maxBlockSize = maxBlockSize;
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public double? DurationSeconds => (double)_samples.Length / SampleRate;

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        int count = Math.Min(Math.Min(buffer.Length, _maxBlockSize), _samples.Length - _position);
        _samples.Span.Slice(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    /// <summary>Does nothing: the source owns no resource.</summary>
    public void Dispose()
    {
    }
}
