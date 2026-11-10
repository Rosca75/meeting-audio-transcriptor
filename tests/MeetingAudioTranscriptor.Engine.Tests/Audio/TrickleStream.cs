// A read-only, forward-only stream that hands out 1 to 7 bytes per read, the way a pipe can, to prove
// the WAV reader never assumes a read fills its buffer or that it may seek.
using System;
using System.IO;

namespace MeetingAudioTranscriptor.Engine.Tests.Audio;

/// <summary>Wraps bytes and returns a short, deterministic count from every read; cannot seek.</summary>
internal sealed class TrickleStream : Stream
{
    private readonly byte[] _bytes;
    private readonly int _maximumPerRead;
    private int _position;
    private int _reads;

    public TrickleStream(byte[] bytes, int maximumPerRead = 7)
    {
        _bytes = bytes;
        _maximumPerRead = maximumPerRead;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException("the trickle stream cannot report a length");

    public override long Position
    {
        get => throw new NotSupportedException("the trickle stream cannot report a position");
        set => throw new NotSupportedException("the trickle stream cannot seek");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        // Cycles through 1, 2, ... maximumPerRead so every frame boundary is crossed at some offset.
        _reads++;
        int size = Math.Min(Math.Min(buffer.Length, 1 + (_reads % _maximumPerRead)), _bytes.Length - _position);
        _bytes.AsSpan(_position, size).CopyTo(buffer);
        _position += size;
        return size;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("the trickle stream cannot seek");

    public override void SetLength(long value) => throw new NotSupportedException("the trickle stream is read-only");

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("the trickle stream is read-only");
}
