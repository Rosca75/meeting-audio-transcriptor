// Converts interleaved PCM bytes of any channel count into mono float samples, keeping a partial frame
// between calls, so every decoder hands its raw buffers to one tested loop.
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Media;

/// <summary>The sample encodings a decoder can hand over, all little-endian.</summary>
internal enum PcmSampleFormat
{
    /// <summary>32-bit IEEE float in the range -1 to 1.</summary>
    Float32,

    /// <summary>Signed 16-bit integers.</summary>
    Int16,

    /// <summary>Signed 24-bit integers packed in three bytes.</summary>
    Int24,

    /// <summary>Signed 32-bit integers.</summary>
    Int32,
}

/// <summary>Turns interleaved little-endian PCM into mono float by averaging the channels of each frame.</summary>
/// <remarks>
/// A decoder's buffers end wherever the decoder chose, which need not be on a frame boundary: the bytes
/// of an unfinished frame are kept in a carry and completed by the next call, so no sample is lost or
/// shifted at a block edge. The converter is the only place sample arithmetic happens, which is why the
/// Windows decoder can stay a thin shell around it and still be correct on a machine the tests never run on.
/// </remarks>
internal sealed class PcmConverter
{
    /// <summary>The most channels a frame may have; more is a misread header rather than a recording.</summary>
    public const int MaxChannels = 64;

    private readonly byte[] _carry;
    private readonly float _channelScale;
    private int _carryLength;

    /// <summary>Creates a converter for one encoding and channel count.</summary>
    public PcmConverter(PcmSampleFormat format, int channels)
    {
        if (channels < 1 || channels > MaxChannels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels),
                channels,
                $"got {channels} channels, wanted 1 to {MaxChannels}; the decoder reported a channel count no recording has");
        }

        Format = format;
        Channels = channels;
        BytesPerSample = format switch
        {
            PcmSampleFormat.Float32 => 4,
            PcmSampleFormat.Int16 => 2,
            PcmSampleFormat.Int24 => 3,
            PcmSampleFormat.Int32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "got an unknown sample format, wanted float32, int16, int24 or int32"),
        };
        BytesPerFrame = BytesPerSample * channels;
        _carry = new byte[BytesPerFrame];
        _channelScale = 1f / channels;
    }

    /// <summary>The encoding of one sample.</summary>
    public PcmSampleFormat Format { get; }

    /// <summary>The number of interleaved channels in one frame.</summary>
    public int Channels { get; }

    /// <summary>The size of one sample of one channel.</summary>
    public int BytesPerSample { get; }

    /// <summary>The size of one frame: one sample of every channel.</summary>
    public int BytesPerFrame { get; }

    /// <summary>Whether bytes of an unfinished frame are waiting for the rest of it.</summary>
    public bool HasPartialFrame => _carryLength > 0;

    /// <summary>Converts as many whole frames as fit, one mono sample per frame.</summary>
    /// <param name="source">Interleaved bytes; they need not start or end on a frame boundary.</param>
    /// <param name="destination">Where the mono samples go.</param>
    /// <param name="bytesConsumed">How many source bytes were used, including those kept in the carry.</param>
    /// <returns>The number of samples written.</returns>
    /// <remarks>
    /// The caller passes the unconsumed rest again next time. A tail shorter than one frame is always
    /// consumed into the carry, so a caller whose source is consumed whole has nothing left to hold.
    /// </remarks>
    public int Convert(ReadOnlySpan<byte> source, Span<float> destination, out int bytesConsumed)
    {
        bytesConsumed = 0;
        if (destination.IsEmpty)
        {
            return 0;
        }

        int written = 0;

        // First finish the frame the previous block left open.
        if (_carryLength > 0)
        {
            int take = Math.Min(BytesPerFrame - _carryLength, source.Length);
            source[..take].CopyTo(_carry.AsSpan(_carryLength));
            _carryLength += take;
            bytesConsumed += take;
            source = source[take..];
            if (_carryLength < BytesPerFrame)
            {
                return 0;
            }

            ConvertFrames(_carry, destination[..1]);
            _carryLength = 0;
            written = 1;
        }

        // Then every whole frame that fits in the destination.
        int frames = Math.Min(source.Length / BytesPerFrame, destination.Length - written);
        int frameBytes = frames * BytesPerFrame;
        ConvertFrames(source[..frameBytes], destination.Slice(written, frames));
        written += frames;
        bytesConsumed += frameBytes;
        source = source[frameBytes..];

        // A tail shorter than a frame cannot be converted yet: keep it for the next block.
        if (source.Length > 0 && source.Length < BytesPerFrame)
        {
            source.CopyTo(_carry);
            _carryLength = source.Length;
            bytesConsumed += source.Length;
        }

        return written;
    }

    /// <summary>Drops an unfinished frame at the end of the audio and says how many bytes it held.</summary>
    /// <remarks>A truncated last frame has no complete sample of every channel, so it cannot be averaged.</remarks>
    public int DiscardPartialFrame()
    {
        int dropped = _carryLength;
        _carryLength = 0;
        return dropped;
    }

    /// <summary>Converts whole frames; the source holds exactly destination.Length frames.</summary>
    private void ConvertFrames(ReadOnlySpan<byte> source, Span<float> destination)
    {
        if (destination.IsEmpty)
        {
            return;
        }

        switch (Format)
        {
            case PcmSampleFormat.Float32:
                ConvertFloat32(source, destination);
                break;
            case PcmSampleFormat.Int16:
                ConvertInt16(source, destination);
                break;
            case PcmSampleFormat.Int24:
                ConvertInt24(source, destination);
                break;
            default:
                ConvertInt32(source, destination);
                break;
        }
    }

    private void ConvertFloat32(ReadOnlySpan<byte> source, Span<float> destination)
    {
        // Mono float on a little-endian machine is already the output: one copy, no per-sample work.
        if (Channels == 1 && BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, float>(source).CopyTo(destination);
            return;
        }

        int channels = Channels;
        int offset = 0;
        for (int frame = 0; frame < destination.Length; frame++)
        {
            float sum = 0f;
            for (int channel = 0; channel < channels; channel++, offset += 4)
            {
                sum += BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
            }

            destination[frame] = sum * _channelScale;
        }
    }

    private void ConvertInt16(ReadOnlySpan<byte> source, Span<float> destination)
    {
        const float Scale = 1f / 32768f;
        int channels = Channels;
        int offset = 0;
        for (int frame = 0; frame < destination.Length; frame++)
        {
            int sum = 0;
            for (int channel = 0; channel < channels; channel++, offset += 2)
            {
                sum += BinaryPrimitives.ReadInt16LittleEndian(source[offset..]);
            }

            destination[frame] = sum * Scale * _channelScale;
        }
    }

    private void ConvertInt24(ReadOnlySpan<byte> source, Span<float> destination)
    {
        const float Scale = 1f / 8388608f;
        int channels = Channels;
        int offset = 0;
        for (int frame = 0; frame < destination.Length; frame++)
        {
            int sum = 0;
            for (int channel = 0; channel < channels; channel++, offset += 3)
            {
                // The third byte carries the sign, so it is read as signed and shifted into place.
                sum += source[offset] | (source[offset + 1] << 8) | ((sbyte)source[offset + 2] << 16);
            }

            destination[frame] = sum * Scale * _channelScale;
        }
    }

    private void ConvertInt32(ReadOnlySpan<byte> source, Span<float> destination)
    {
        // The sum is a long: two full-scale 32-bit samples already overflow an int.
        const double Scale = 1d / 2147483648d;
        int channels = Channels;
        int offset = 0;
        for (int frame = 0; frame < destination.Length; frame++)
        {
            long sum = 0;
            for (int channel = 0; channel < channels; channel++, offset += 4)
            {
                sum += BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
            }

            destination[frame] = (float)(sum * Scale * _channelScale);
        }
    }
}
