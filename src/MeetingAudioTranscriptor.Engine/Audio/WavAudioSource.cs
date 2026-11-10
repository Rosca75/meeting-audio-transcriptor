// The engine's own WAV reader: parses a RIFF or RF64 header from a caller's stream and delivers the
// data chunk as mono float samples, block by block, without holding the file.
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Engine.Audio;

/// <summary>Reads PCM and IEEE float WAV audio from a stream and downmixes it to mono.</summary>
/// <remarks>
/// <para>
/// The engine never opens a path, so the caller passes the stream. The header is parsed in the
/// constructor, which reads forward only (seeking is used just to skip chunks faster when the stream
/// allows it), so a pipe or a network-free child process stream works as well as a file.
/// </para>
/// <para>
/// Supported: RIFF/WAVE and RF64 (with the <c>ds64</c> sizes); PCM 8-bit unsigned and 16, 24 and
/// 32-bit signed; IEEE float 32 and 64-bit; <c>WAVE_FORMAT_EXTENSIBLE</c> carrying either of those
/// sub-formats; any channel count, averaged to mono; 8 kHz to 192 kHz. A <c>data</c> size of 0 or
/// 0xFFFFFFFF marks a streamed recording whose writer never went back to fill the size in, and the
/// data is read to the end of the stream. A data chunk shorter than its stated size is delivered as
/// far as it goes, because a recording cut off by a crash still holds a meeting worth transcribing.
/// </para>
/// <para>
/// Conversion runs over whole blocks reinterpreted as typed spans, because a two-hour 48 kHz stereo
/// file is about 1.4 GB of PCM and a per-byte decoder would dominate the run.
/// </para>
/// </remarks>
public sealed class WavAudioSource : IAudioSource
{
    /// <summary>The lowest sample rate accepted: telephone recordings.</summary>
    public const int MinimumSampleRate = 8000;

    /// <summary>The highest sample rate accepted: studio recordings.</summary>
    public const int MaximumSampleRate = 192000;

    private const ushort FormatPcm = 0x0001;
    private const ushort FormatIeeeFloat = 0x0003;
    private const ushort FormatExtensible = 0xFFFE;

    // The bytes of the sub-format GUID after its first two: KSDATAFORMAT_SUBTYPE_PCM and _IEEE_FLOAT
    // both end in 0000-0000-0010-8000-00AA00389B71, and the first two bytes are the format tag.
    private static readonly byte[] SubFormatGuidTail =
        [0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    private const string SupportedFormats =
        "supported: PCM 8, 16, 24 or 32-bit, IEEE float 32 or 64-bit, or WAVE_FORMAT_EXTENSIBLE carrying either, in a RIFF or RF64 file";

    // Frames per conversion block: large enough that per-call overhead vanishes, small enough to stay in cache.
    private const int FramesPerBlock = 8192;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SampleEncoding _encoding;
    private readonly int _channels;
    private readonly int _blockAlign;
    private readonly byte[] _bytes;

    // Bytes of the data chunk not yet taken from the stream, or -1 when the size is unknown.
    private long _remaining;

    // Bytes at the start of _bytes already read but not yet converted (a partial frame from a short read).
    private int _pending;
    private bool _ended;
    private bool _disposed;

    /// <summary>Parses the WAV header and positions the reader at the first sample.</summary>
    /// <param name="stream">A readable stream positioned at the start of the file.</param>
    /// <param name="leaveOpen">True to leave the stream open when this source is disposed.</param>
    /// <exception cref="AudioFormatException">The header is malformed, truncated or describes an unsupported format.</exception>
    public WavAudioSource(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("the stream passed to the WAV reader cannot be read; pass a stream opened for reading", nameof(stream));
        }

        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("the WAV reader decodes little-endian samples in place and needs a little-endian processor");
        }

        _stream = stream;
        _leaveOpen = leaveOpen;

        Header header = ReadHeader(stream);
        _encoding = header.Encoding;
        _channels = header.Channels;
        _blockAlign = header.BlockAlign;
        SampleRate = header.SampleRate;
        _remaining = header.DataSize;
        _bytes = new byte[FramesPerBlock * _blockAlign];

        long? dataBytes = header.DataSize >= 0 ? header.DataSize : null;
        if (dataBytes is null && stream.CanSeek)
        {
            // A streamed file read from disk still has a length: the data runs to its end.
            dataBytes = Math.Max(0, stream.Length - stream.Position);
        }

        DurationSeconds = dataBytes is long size ? (double)(size / _blockAlign) / SampleRate : null;
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public double? DurationSeconds { get; }

    /// <summary>The number of interleaved channels in the file, before the downmix.</summary>
    public int Channels => _channels;

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
        {
            return 0;
        }

        int wantedFrames = Math.Min(buffer.Length, FramesPerBlock);
        int wantedBytes = wantedFrames * _blockAlign;

        // Read until at least one whole frame is buffered or the data ends; a short read from a pipe
        // may deliver a fraction of a frame, which waits in the buffer for its other bytes.
        while (_pending < _blockAlign && !_ended)
        {
            FillOnce(wantedBytes);
        }

        // One more read when the first one left room, so a fast stream fills the caller's whole block.
        if (_pending < wantedBytes && !_ended)
        {
            FillOnce(wantedBytes);
        }

        int frames = Math.Min(_pending / _blockAlign, wantedFrames);
        if (frames == 0)
        {
            // The data ended; a trailing partial frame of a truncated file is dropped.
            return 0;
        }

        int used = frames * _blockAlign;
        Convert(_bytes.AsSpan(0, used), buffer[..frames]);

        _pending -= used;
        if (_pending > 0)
        {
            _bytes.AsSpan(used, _pending).CopyTo(_bytes);
        }

        return frames;
    }

    /// <summary>Releases the stream unless the caller asked to keep it open.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    // Takes one read from the stream into the free part of the buffer, bounded by what the data chunk has left.
    private void FillOnce(int wantedBytes)
    {
        int room = wantedBytes - _pending;
        if (_remaining >= 0)
        {
            room = (int)Math.Min(room, _remaining);
        }

        if (room <= 0)
        {
            _ended = true;
            return;
        }

        int read = _stream.Read(_bytes, _pending, room);
        if (read == 0)
        {
            _ended = true;
            return;
        }

        _pending += read;
        if (_remaining >= 0)
        {
            _remaining -= read;
        }
    }

    // Converts whole frames to mono floats: each sample scaled to -1..1 and the channels of a frame averaged.
    private void Convert(ReadOnlySpan<byte> bytes, Span<float> output)
    {
        switch (_encoding)
        {
            case SampleEncoding.Unsigned8:
                ConvertUnsigned8(bytes, output, _channels);
                break;
            case SampleEncoding.Signed16:
                ConvertSigned16(MemoryMarshal.Cast<byte, short>(bytes), output, _channels);
                break;
            case SampleEncoding.Signed24:
                ConvertSigned24(bytes, output, _channels);
                break;
            case SampleEncoding.Signed32:
                ConvertSigned32(MemoryMarshal.Cast<byte, int>(bytes), output, _channels);
                break;
            case SampleEncoding.Float32:
                ConvertFloat32(MemoryMarshal.Cast<byte, float>(bytes), output, _channels);
                break;
            case SampleEncoding.Float64:
                ConvertFloat64(MemoryMarshal.Cast<byte, double>(bytes), output, _channels);
                break;
            default:
                throw new InvalidOperationException($"the WAV reader has no converter for {_encoding}; this is a defect in the reader");
        }
    }

    private static void ConvertUnsigned8(ReadOnlySpan<byte> samples, Span<float> output, int channels)
    {
        // 8-bit WAV is unsigned with silence at 128.
        float scale = 1f / (128f * channels);
        int s = 0;
        for (int frame = 0; frame < output.Length; frame++)
        {
            int sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[s++] - 128;
            }

            output[frame] = sum * scale;
        }
    }

    private static void ConvertSigned16(ReadOnlySpan<short> samples, Span<float> output, int channels)
    {
        if (channels == 1)
        {
            const float monoScale = 1f / 32768f;
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = samples[i] * monoScale;
            }

            return;
        }

        if (channels == 2)
        {
            const float stereoScale = 1f / 65536f;
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (samples[2 * i] + samples[(2 * i) + 1]) * stereoScale;
            }

            return;
        }

        float scale = 1f / (32768f * channels);
        int s = 0;
        for (int frame = 0; frame < output.Length; frame++)
        {
            int sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[s++];
            }

            output[frame] = sum * scale;
        }
    }

    private static void ConvertSigned24(ReadOnlySpan<byte> bytes, Span<float> output, int channels)
    {
        // Three little-endian bytes per sample; the top byte is read signed so the sign extends.
        double scale = 1.0 / (8388608.0 * channels);
        int b = 0;
        for (int frame = 0; frame < output.Length; frame++)
        {
            long sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += bytes[b] | (bytes[b + 1] << 8) | ((sbyte)bytes[b + 2] << 16);
                b += 3;
            }

            output[frame] = (float)(sum * scale);
        }
    }

    private static void ConvertSigned32(ReadOnlySpan<int> samples, Span<float> output, int channels)
    {
        // Summed in double: four full-scale 32-bit samples overflow an int, and float loses the low bits.
        double scale = 1.0 / (2147483648.0 * channels);
        int s = 0;
        for (int frame = 0; frame < output.Length; frame++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[s++];
            }

            output[frame] = (float)(sum * scale);
        }
    }

    private static void ConvertFloat32(ReadOnlySpan<float> samples, Span<float> output, int channels)
    {
        if (channels == 1)
        {
            samples[..output.Length].CopyTo(output);
            return;
        }

        float scale = 1f / channels;
        int s = 0;
        for (int frame = 0; frame < output.Length; frame++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[s++];
            }

            output[frame] = sum * scale;
        }
    }

    private static void ConvertFloat64(ReadOnlySpan<double> samples, Span<float> output, int channels)
    {
        double scale = 1.0 / channels;
        int s = 0;
        for (int frame = 0; frame < output.Length; frame++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[s++];
            }

            output[frame] = (float)(sum * scale);
        }
    }

    // Walks the chunks from the RIFF header to the start of the data chunk.
    private static Header ReadHeader(Stream stream)
    {
        Span<byte> riff = stackalloc byte[12];
        if (!TryReadExactly(stream, riff))
        {
            throw new AudioFormatException(
                "the WAV header is truncated: the stream ends within its first 12 bytes; the file is empty or not a WAV file, so record or export it again");
        }

        string container = Ascii(riff[..4]);
        bool isRf64 = container is "RF64" or "BW64";
        if ((container != "RIFF" && !isRf64) || Ascii(riff.Slice(8, 4)) != "WAVE")
        {
            throw new AudioFormatException(
                $"the stream is not a WAV file: it starts with '{Printable(riff[..4])}' and declares '{Printable(riff.Slice(8, 4))}' where 'RIFF' (or 'RF64') and 'WAVE' were expected; convert the recording to WAV first");
        }

        long ds64DataSize = -1;
        Format? format = null;
        Span<byte> chunkHeader = stackalloc byte[8];
        while (true)
        {
            if (!TryReadExactly(stream, chunkHeader))
            {
                string missing = format is null ? "'fmt ' and 'data' chunks" : "'data' chunk";
                throw new AudioFormatException(
                    $"the WAV header is truncated: the stream ends before the {missing}; the file was cut off while it was being written, so export it again");
            }

            string id = Ascii(chunkHeader[..4]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);

            switch (id)
            {
                case "ds64":
                    ds64DataSize = ReadDs64(stream, size);
                    break;

                case "fmt ":
                    format = ReadFormat(stream, size);
                    break;

                case "data":
                    if (format is not Format found)
                    {
                        throw new AudioFormatException(
                            "the WAV file has its 'data' chunk before its 'fmt ' chunk, so the sample format is unknown; re-export the recording with a standard WAV writer");
                    }

                    long dataSize = size;
                    if (isRf64 && size == uint.MaxValue)
                    {
                        dataSize = ds64DataSize > 0 ? ds64DataSize : -1;
                    }
                    else if (size is 0 or uint.MaxValue)
                    {
                        // Streamed writers leave the size at 0 or all ones: read to the end of the stream.
                        dataSize = -1;
                    }

                    return new Header(found.Encoding, found.Channels, found.BlockAlign, found.SampleRate, dataSize);

                default:
                    // LIST, bext, fact, JUNK, cue and anything unknown: skipped with their pad byte.
                    Skip(stream, size + (size & 1u), id);
                    break;
            }
        }
    }

    // Reads the RF64 size chunk and keeps only the 64-bit data size; the other fields are not needed.
    private static long ReadDs64(Stream stream, uint size)
    {
        if (size < 24)
        {
            throw new AudioFormatException(
                $"the RF64 'ds64' chunk is {size} bytes where at least 24 are needed for the 64-bit sizes; the file is damaged, so export it again");
        }

        byte[] body = new byte[size + (size & 1u)];
        if (!TryReadExactly(stream, body))
        {
            throw new AudioFormatException("the WAV header is truncated inside its 'ds64' chunk; the file was cut off, so export it again");
        }

        ulong dataSize = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(8, 8));
        return dataSize > long.MaxValue ? -1 : (long)dataSize;
    }

    // Reads the format chunk and decides the sample encoding, refusing anything this reader does not decode.
    private static Format ReadFormat(Stream stream, uint size)
    {
        if (size < 16)
        {
            throw new AudioFormatException(
                $"the WAV 'fmt ' chunk is {size} bytes where at least 16 are needed; the file is damaged, so export it again");
        }

        if (size > 4096)
        {
            throw new AudioFormatException(
                $"the WAV 'fmt ' chunk claims {size} bytes, far more than any format description needs; the file is damaged, so export it again");
        }

        byte[] body = new byte[size + (size & 1u)];
        if (!TryReadExactly(stream, body))
        {
            throw new AudioFormatException("the WAV header is truncated inside its 'fmt ' chunk; the file was cut off, so export it again");
        }

        ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(body);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2));
        uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4));
        int blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(12));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(14));

        ushort effectiveTag = tag;
        if (tag == FormatExtensible)
        {
            if (size < 40)
            {
                throw new AudioFormatException(
                    $"the WAV file declares WAVE_FORMAT_EXTENSIBLE (tag 0xFFFE) but its 'fmt ' chunk is {size} bytes where 40 are needed for the sub-format; the file is damaged, so export it again");
            }

            ReadOnlySpan<byte> subFormat = body.AsSpan(24, 16);
            effectiveTag = BinaryPrimitives.ReadUInt16LittleEndian(subFormat);
            if (!subFormat[2..].SequenceEqual(SubFormatGuidTail) || (effectiveTag != FormatPcm && effectiveTag != FormatIeeeFloat))
            {
                throw new AudioFormatException(
                    $"the WAV file uses WAVE_FORMAT_EXTENSIBLE (tag 0xFFFE) with sub-format {new Guid(subFormat)}, which this reader does not decode; {SupportedFormats}. Convert the recording to 16-bit PCM WAV");
            }
        }

        if (effectiveTag != FormatPcm && effectiveTag != FormatIeeeFloat)
        {
            throw new AudioFormatException(
                $"the WAV file uses format tag 0x{tag:X4} ({DescribeTag(tag)}), which this reader does not decode; {SupportedFormats}. Convert the recording to 16-bit PCM WAV");
        }

        if (channels == 0)
        {
            throw new AudioFormatException("the WAV file declares 0 channels where at least 1 is needed; the file is damaged, so export it again");
        }

        if (sampleRate < MinimumSampleRate || sampleRate > MaximumSampleRate)
        {
            throw new AudioFormatException(
                $"the WAV file is sampled at {sampleRate} Hz where {MinimumSampleRate} to {MaximumSampleRate} Hz is supported; resample the recording into that range");
        }

        // The container size per sample comes from the bit depth; 20 valid bits sit left-justified in 3 bytes.
        int containerBytes = (bits + 7) / 8;
        SampleEncoding? encoding = (effectiveTag, containerBytes) switch
        {
            (FormatPcm, 1) => SampleEncoding.Unsigned8,
            (FormatPcm, 2) => SampleEncoding.Signed16,
            (FormatPcm, 3) => SampleEncoding.Signed24,
            (FormatPcm, 4) => SampleEncoding.Signed32,
            (FormatIeeeFloat, 4) when bits == 32 => SampleEncoding.Float32,
            (FormatIeeeFloat, 8) when bits == 64 => SampleEncoding.Float64,
            _ => null,
        };

        string kind = effectiveTag == FormatPcm ? "PCM" : "IEEE float";
        if (encoding is not SampleEncoding chosen)
        {
            throw new AudioFormatException(
                $"the WAV file holds {bits}-bit {kind} samples (format tag 0x{tag:X4}), a bit depth this reader does not decode; {SupportedFormats}. Convert the recording to 16-bit PCM WAV");
        }

        if (blockAlign != channels * containerBytes)
        {
            throw new AudioFormatException(
                $"the WAV file declares a block size of {blockAlign} bytes where {channels} channel(s) of {bits}-bit {kind} need {channels * containerBytes}; the header is inconsistent, so export the recording again");
        }

        return new Format(chosen, channels, blockAlign, (int)sampleRate);
    }

    // Moves past a chunk this reader does not use: by seeking when the stream allows it, else by reading.
    private static void Skip(Stream stream, long count, string id)
    {
        if (stream.CanSeek)
        {
            long target = stream.Position + count;
            if (target > stream.Length)
            {
                throw new AudioFormatException(
                    $"the WAV header is truncated: the '{Printable(System.Text.Encoding.Latin1.GetBytes(id))}' chunk runs past the end of the stream before any 'data' chunk; the file was cut off, so export it again");
            }

            stream.Position = target;
            return;
        }

        byte[] scratch = new byte[(int)Math.Min(count, 65536)];
        while (count > 0)
        {
            int read = stream.Read(scratch, 0, (int)Math.Min(count, scratch.Length));
            if (read == 0)
            {
                throw new AudioFormatException(
                    $"the WAV header is truncated: the '{Printable(System.Text.Encoding.Latin1.GetBytes(id))}' chunk runs past the end of the stream before any 'data' chunk; the file was cut off, so export it again");
            }

            count -= read;
        }
    }

    // Fills the buffer from the stream across short reads; false when the stream ends first.
    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        return System.Text.Encoding.Latin1.GetString(bytes);
    }

    // A four-character code for a message, with unprintable bytes shown as '?'.
    private static string Printable(ReadOnlySpan<byte> bytes)
    {
        char[] chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '?';
        }

        return new string(chars);
    }

    // Names the common encodings a user is likely to meet, so the message says what the file is.
    private static string DescribeTag(ushort tag)
    {
        return tag switch
        {
            0x0000 => "unknown",
            0x0002 => "Microsoft ADPCM",
            0x0006 => "A-law",
            0x0007 => "mu-law",
            0x0011 => "IMA ADPCM",
            0x0031 => "GSM 6.10",
            0x0050 => "MPEG audio",
            0x0055 => "MP3",
            0x00FF => "AAC",
            0x1610 => "AAC",
            0xF1AC => "FLAC",
            _ => "a compressed or vendor encoding",
        };
    }

    // The sample layouts the converter handles, one tight loop each.
    private enum SampleEncoding
    {
        Unsigned8,
        Signed16,
        Signed24,
        Signed32,
        Float32,
        Float64,
    }

    private readonly record struct Format(SampleEncoding Encoding, int Channels, int BlockAlign, int SampleRate);

    // DataSize is -1 when the data runs to the end of the stream.
    private readonly record struct Header(SampleEncoding Encoding, int Channels, int BlockAlign, int SampleRate, long DataSize);
}
