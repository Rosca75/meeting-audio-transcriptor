// The buffer management every block-based decoder shares: raw bytes in, mono floats out, refilled on demand.
using System;

namespace MeetingAudioTranscriptor.Media;

/// <summary>Holds the decoded bytes not yet converted, in one growable array that is reused for every block.</summary>
/// <remarks>
/// A decoder writes a block in, and the reader converts from the front of it as the caller's buffer
/// allows. The array is compacted rather than reallocated, so a two-hour recording runs through one
/// buffer the size of the largest block.
/// </remarks>
internal sealed class PcmPendingBytes
{
    private byte[] _bytes;
    private int _start;
    private int _end;

    /// <summary>Creates an empty buffer with the given starting capacity.</summary>
    public PcmPendingBytes(int initialCapacity)
    {
        _bytes = new byte[Math.Max(initialCapacity, 16)];
    }

    /// <summary>The number of bytes waiting to be converted.</summary>
    public int Count => _end - _start;

    /// <summary>The bytes waiting to be converted.</summary>
    public ReadOnlySpan<byte> Pending => _bytes.AsSpan(_start, Count);

    /// <summary>Returns free space of at least the given size after the pending bytes; call Commit with what was written.</summary>
    public Span<byte> GetWriteSpan(int minimum)
    {
        if (_bytes.Length - _end < minimum)
        {
            // Move the pending bytes to the front first, and grow only if that is still not enough.
            int count = Count;
            if (_bytes.Length - count < minimum)
            {
                byte[] larger = new byte[Math.Max(_bytes.Length * 2, count + minimum)];
                Pending.CopyTo(larger);
                _bytes = larger;
            }
            else
            {
                _bytes.AsSpan(_start, count).CopyTo(_bytes);
            }

            _start = 0;
            _end = count;
        }

        return _bytes.AsSpan(_end);
    }

    /// <summary>Marks the given number of bytes of the last write span as written.</summary>
    public void Commit(int count)
    {
        if (count < 0 || count > _bytes.Length - _end)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "got a commit larger than the space handed out; write into GetWriteSpan first");
        }

        _end += count;
    }

    /// <summary>Copies a block in after the pending bytes.</summary>
    public void Append(ReadOnlySpan<byte> block)
    {
        block.CopyTo(GetWriteSpan(block.Length));
        Commit(block.Length);
    }

    /// <summary>Converts from the front of the pending bytes into the destination and returns the samples written.</summary>
    public int Drain(PcmConverter converter, Span<float> destination)
    {
        int written = converter.Convert(Pending, destination, out int consumed);
        _start += consumed;
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }

        return written;
    }
}

/// <summary>Answers a caller's read from the pending bytes, asking the decoder for another block when they run out.</summary>
/// <remarks>
/// This is the loop that makes a decoder an <c>IAudioSource</c>: it returns zero only once the decoder
/// says there is no more, and never returns zero while a partial frame is still being completed. The
/// decoder's refill writes one block and returns true, or returns false at the end; it is called only
/// when no whole frame is pending, so it may change the converter (a format change mid-file) without
/// losing a sample.
/// </remarks>
internal sealed class PcmSampleReader
{
    private readonly PcmPendingBytes _pending;
    private readonly Func<PcmPendingBytes, bool> _refill;
    private bool _ended;

    /// <summary>Creates a reader over a decoder's refill callback.</summary>
    public PcmSampleReader(PcmConverter converter, Func<PcmPendingBytes, bool> refill, int initialCapacity = 1 << 16)
    {
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(refill);
        Converter = converter;
        _refill = refill;
        _pending = new PcmPendingBytes(initialCapacity);
    }

    /// <summary>The converter in use; a decoder may replace it from its refill when the format changes.</summary>
    public PcmConverter Converter { get; set; }

    /// <summary>The bytes of an unfinished last frame dropped at the end, zero for a whole file.</summary>
    public int DiscardedTailBytes { get; private set; }

    /// <summary>Whether the decoder has reported its end and every sample has been handed out.</summary>
    public bool Ended => _ended && _pending.Count == 0;

    /// <summary>Fills the buffer with the next samples; zero only at the end of the audio.</summary>
    public int Read(Span<float> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        while (true)
        {
            int written = _pending.Drain(Converter, destination);
            if (written > 0)
            {
                return written;
            }

            if (_ended)
            {
                return 0;
            }

            if (!_refill(_pending))
            {
                _ended = true;
                DiscardedTailBytes = Converter.DiscardPartialFrame();
            }
        }
    }
}
