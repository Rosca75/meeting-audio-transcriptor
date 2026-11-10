// Tests the PCM converter and the pending-byte reader: every format, several channel counts, and a block
// split at every byte boundary, against an independently computed expectation.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[Trait("Category", "audio")]
public sealed class PcmConverterTests
{
    /// <summary>Every format (by its number, since the enum is internal) with 1, 2, 3 and 6 channels.</summary>
    public static TheoryData<int, int> Layouts()
    {
        TheoryData<int, int> data = [];
        foreach (PcmSampleFormat format in Enum.GetValues<PcmSampleFormat>())
        {
            foreach (int channels in new[] { 1, 2, 3, 6 })
            {
                data.Add((int)format, channels);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ConvertsWholeBlocksToTheChannelAverage(int formatNumber, int channels)
    {
        PcmSampleFormat format = (PcmSampleFormat)formatNumber;
        (byte[] bytes, float[] expected) = Interleaved(format, channels, frames: 37);
        PcmConverter converter = new(format, channels);
        float[] output = new float[64];

        int written = converter.Convert(bytes, output, out int consumed);

        Assert.True(written == 37, $"got {written} samples, wanted 37 for {format} x{channels}");
        Assert.True(consumed == bytes.Length, $"got {consumed} bytes consumed, wanted {bytes.Length}");
        AssertClose(expected, output.AsSpan(0, written).ToArray(), $"{format} x{channels}");
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ASplitAtEveryByteBoundaryGivesTheSameSamples(int formatNumber, int channels)
    {
        PcmSampleFormat format = (PcmSampleFormat)formatNumber;
        (byte[] bytes, float[] expected) = Interleaved(format, channels, frames: 5);
        for (int split = 0; split <= bytes.Length; split++)
        {
            PcmConverter converter = new(format, channels);
            List<float> got = [];
            float[] output = new float[16];

            int first = converter.Convert(bytes.AsSpan(0, split), output, out int consumedFirst);
            got.AddRange(output.AsSpan(0, first).ToArray());
            int second = converter.Convert(bytes.AsSpan(split), output, out int consumedSecond);
            got.AddRange(output.AsSpan(0, second).ToArray());

            Assert.True(
                consumedFirst == split && consumedSecond == bytes.Length - split,
                $"got {consumedFirst}+{consumedSecond} bytes consumed, wanted {split}+{bytes.Length - split} for {format} x{channels} split at {split}");
            Assert.True(!converter.HasPartialFrame, $"got a partial frame left after the whole input, wanted none for {format} x{channels} split at {split}");
            AssertClose(expected, got.ToArray(), $"{format} x{channels} split at {split}");
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ByteByByteInputThroughAOneSampleBufferGivesTheSameSamples(int formatNumber, int channels)
    {
        PcmSampleFormat format = (PcmSampleFormat)formatNumber;
        (byte[] bytes, float[] expected) = Interleaved(format, channels, frames: 4);
        PcmConverter converter = new(format, channels);
        List<float> got = [];
        float[] output = new float[1];
        foreach (byte b in bytes)
        {
            ReadOnlySpan<byte> one = [b];
            while (!one.IsEmpty)
            {
                int written = converter.Convert(one, output, out int consumed);
                got.AddRange(output.AsSpan(0, written).ToArray());
                one = one[consumed..];
            }
        }

        AssertClose(expected, got.ToArray(), $"{format} x{channels} byte by byte");
    }

    [Fact]
    public void ADestinationSmallerThanTheBlockLeavesTheRestUnconsumed()
    {
        (byte[] bytes, float[] expected) = Interleaved(PcmSampleFormat.Int16, 2, frames: 10);
        PcmConverter converter = new(PcmSampleFormat.Int16, 2);
        float[] output = new float[3];

        int written = converter.Convert(bytes, output, out int consumed);

        Assert.True(written == 3 && consumed == 12, $"got {written} samples and {consumed} bytes, wanted 3 and 12");
        AssertClose(expected[..3], output, "first three frames");
    }

    [Fact]
    public void FullScaleValuesMapToTheEdgesOfTheRange()
    {
        byte[] int16 = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(int16, short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(int16.AsSpan(2), short.MaxValue);
        byte[] int24 = [0x00, 0x00, 0x80, 0xFF, 0xFF, 0x7F];
        byte[] int32 = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(int32, int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(int32.AsSpan(4), int.MaxValue);

        foreach ((PcmSampleFormat format, byte[] bytes) in new[] { (PcmSampleFormat.Int16, int16), (PcmSampleFormat.Int24, int24), (PcmSampleFormat.Int32, int32) })
        {
            float[] output = new float[2];
            new PcmConverter(format, 1).Convert(bytes, output, out _);
            Assert.True(output[0] == -1f, $"got {output[0]}, wanted -1 for the most negative {format}");
            Assert.True(output[1] is > 0.9998f and <= 1f, $"got {output[1]}, wanted 1 or just under it (float rounds the largest 32-bit value to 1) for the most positive {format}");
        }
    }

    [Fact]
    public void ATruncatedLastFrameIsDroppedAndCounted()
    {
        (byte[] bytes, _) = Interleaved(PcmSampleFormat.Float32, 2, frames: 2);
        PcmConverter converter = new(PcmSampleFormat.Float32, 2);
        float[] output = new float[4];

        int written = converter.Convert(bytes.AsSpan(0, bytes.Length - 3), output, out _);
        int dropped = converter.DiscardPartialFrame();

        Assert.True(written == 1 && dropped == 5, $"got {written} samples and {dropped} dropped bytes, wanted 1 and 5");
        Assert.True(!converter.HasPartialFrame, "got a partial frame after the discard, wanted none");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void AnImpossibleChannelCountIsRefused(int channels)
    {
        ArgumentOutOfRangeException e = Assert.Throws<ArgumentOutOfRangeException>(() => new PcmConverter(PcmSampleFormat.Int16, channels));
        Assert.True(e.Message.Contains("wanted 1 to 64", StringComparison.Ordinal), $"got '{e.Message}', wanted the allowed range named");
    }

    [Fact]
    public void TheReaderRefillsUntilTheEndAndReturnsZeroOnlyThen()
    {
        (byte[] bytes, float[] expected) = Interleaved(PcmSampleFormat.Int24, 3, frames: 50);
        int offset = 0;
        int[] blockSizes = [1, 7, 0, 64, 13, 2];
        int call = 0;
        bool Refill(PcmPendingBytes pending)
        {
            if (offset >= bytes.Length)
            {
                return false;
            }

            int size = Math.Min(blockSizes[call++ % blockSizes.Length], bytes.Length - offset);
            pending.Append(bytes.AsSpan(offset, size));
            offset += size;
            return true;
        }

        PcmSampleReader reader = new(new PcmConverter(PcmSampleFormat.Int24, 3), Refill, initialCapacity: 4);
        List<float> got = [];
        float[] buffer = new float[3];
        int read;
        while ((read = reader.Read(buffer)) > 0)
        {
            got.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        AssertClose(expected, got.ToArray(), "reader over uneven blocks");
        Assert.True(reader.Read(buffer) == 0 && reader.Ended, "got more samples after the end, wanted zero again");
        Assert.True(reader.DiscardedTailBytes == 0, $"got {reader.DiscardedTailBytes} dropped bytes, wanted 0 for whole frames");
    }

    [Fact]
    public void ThePendingBufferGrowsAndCompactsWithoutLosingBytes()
    {
        PcmPendingBytes pending = new(16);
        pending.Append([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        float[] output = new float[2];
        int written = pending.Drain(new PcmConverter(PcmSampleFormat.Int16, 1), output);
        pending.Append(new byte[40]);

        Assert.True(written == 2 && pending.Count == 46, $"got {written} samples and {pending.Count} pending bytes, wanted 2 and 46");
        Assert.True(pending.Pending[0] == 5 && pending.Pending[5] == 10, "got the pending bytes moved out of order, wanted 5..10 first");
    }

    /// <summary>Builds deterministic interleaved bytes and the mono samples they must convert to.</summary>
    internal static (byte[] Bytes, float[] Expected) Interleaved(PcmSampleFormat format, int channels, int frames)
    {
        int bytesPerSample = format switch { PcmSampleFormat.Int16 => 2, PcmSampleFormat.Int24 => 3, _ => 4 };
        byte[] bytes = new byte[frames * channels * bytesPerSample];
        float[] expected = new float[frames];
        Random random = new(frames * 31 + channels);
        int offset = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (int channel = 0; channel < channels; channel++, offset += bytesPerSample)
            {
                double value = (random.NextDouble() * 2) - 1;
                switch (format)
                {
                    case PcmSampleFormat.Float32:
                        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), (float)value);
                        sum += (float)value;
                        break;
                    case PcmSampleFormat.Int16:
                        short s16 = (short)Math.Round(value * 32767);
                        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), s16);
                        sum += s16 / 32768d;
                        break;
                    case PcmSampleFormat.Int24:
                        int s24 = (int)Math.Round(value * 8388607);
                        bytes[offset] = (byte)s24;
                        bytes[offset + 1] = (byte)(s24 >> 8);
                        bytes[offset + 2] = (byte)(s24 >> 16);
                        sum += s24 / 8388608d;
                        break;
                    default:
                        int s32 = (int)Math.Round(value * int.MaxValue);
                        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), s32);
                        sum += s32 / 2147483648d;
                        break;
                }
            }

            expected[frame] = (float)(sum / channels);
        }

        return (bytes, expected);
    }

    private static void AssertClose(float[] expected, float[] got, string input)
    {
        Assert.True(got.Length == expected.Length, $"got {got.Length} samples, wanted {expected.Length} for {input}");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - got[i]) < 1e-5f, $"got {got[i]} at sample {i}, wanted {expected[i]} for {input}");
        }
    }
}
