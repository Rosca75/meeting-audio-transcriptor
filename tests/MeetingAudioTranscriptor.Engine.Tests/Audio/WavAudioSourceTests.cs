// Tests that the WAV reader decodes every supported layout to the right mono samples and refuses the
// rest with a message that names what it found.
using System;
using System.IO;
using System.Text;
using MeetingAudioTranscriptor.Engine.Audio;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Audio;

[Trait("Category", "audio")]
public sealed class WavAudioSourceTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 16)]
    [InlineData(1, 24)]
    [InlineData(1, 32)]
    [InlineData(3, 32)]
    [InlineData(3, 64)]
    public void EverySupportedEncodingRoundTripsWithinOneQuantisationStep(int tag, int bits)
    {
        WavBuilder builder = new() { FormatTag = (ushort)tag, BitsPerSample = bits, SampleRate = 44100 };
        double[] samples = TestSignal(3001, 1);

        float[] decoded = Decode(builder.Build(samples));

        AssertMatches(builder.ExpectedMono(samples), decoded, $"tag {tag}, {bits} bits");
        AssertWithinStep(samples, decoded, bits, tag == 3);
    }

    [Theory]
    [InlineData(1, 8, 2)]
    [InlineData(1, 16, 2)]
    [InlineData(1, 16, 3)]
    [InlineData(1, 16, 6)]
    [InlineData(1, 24, 8)]
    [InlineData(1, 32, 4)]
    [InlineData(3, 32, 2)]
    [InlineData(3, 32, 5)]
    [InlineData(3, 64, 8)]
    [InlineData(1, 16, 12)]
    public void SeveralChannelsAreAveragedToMono(int tag, int bits, int channels)
    {
        WavBuilder builder = new() { FormatTag = (ushort)tag, BitsPerSample = bits, Channels = channels, SampleRate = 48000 };
        double[] samples = TestSignal(1000, channels);

        float[] decoded = Decode(builder.Build(samples));

        AssertMatches(builder.ExpectedMono(samples), decoded, $"{channels} channels of tag {tag}, {bits} bits");
    }

    [Fact]
    public void OppositeChannelsCancelInTheDownmix()
    {
        WavBuilder builder = new() { Channels = 2 };
        double[] samples = [0.5, -0.5, 0.25, -0.25];

        float[] decoded = Decode(builder.Build(samples));

        Assert.True(decoded is [0f, 0f], $"got [{string.Join(", ", decoded)}], wanted [0, 0] for left and right in opposition");
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(1, 24)]
    [InlineData(1, 32)]
    [InlineData(3, 32)]
    [InlineData(3, 64)]
    public void ExtensibleFormatsReadLikeTheirSubFormat(int tag, int bits)
    {
        WavBuilder builder = new() { FormatTag = (ushort)tag, BitsPerSample = bits, Channels = 2, Extensible = true };
        double[] samples = TestSignal(800, 2);

        float[] decoded = Decode(builder.Build(samples));

        AssertMatches(builder.ExpectedMono(samples), decoded, $"extensible tag {tag}, {bits} bits");
    }

    [Fact]
    public void Rf64SizesAreTakenFromTheDs64Chunk()
    {
        WavBuilder builder = new() { Rf64 = true, Channels = 2, BitsPerSample = 24 };
        double[] samples = TestSignal(1200, 2);
        builder.ChunksAfterData.Add(("LIST", Encoding.ASCII.GetBytes("INFOtrailing chunk that is not audio")));

        byte[] bytes = builder.Build(samples);
        using WavAudioSource source = new(new MemoryStream(bytes));
        float[] decoded = source.ReadAll();

        AssertMatches(builder.ExpectedMono(samples), decoded, "RF64");
        AssertClose(600.0 / 16000, source.DurationSeconds, "RF64 duration");
    }

    [Fact]
    public void UnknownChunksAndOddPaddingAreSkipped()
    {
        WavBuilder builder = new() { BitsPerSample = 16 };
        builder.ChunksBeforeFormat.Add(("JUNK", new byte[27]));
        builder.ChunksBeforeData.Add(("bext", new byte[602]));
        builder.ChunksBeforeData.Add(("LIST", Encoding.ASCII.GetBytes("INFOISFT\u0005\0\0\0abcde")));
        builder.ChunksBeforeData.Add(("fact", new byte[] { 1, 2, 3, 4 }));
        builder.ChunksBeforeData.Add(("odd!", new byte[] { 9 }));
        builder.ChunksAfterData.Add(("LIST", new byte[13]));
        double[] samples = TestSignal(500, 1);

        byte[] bytes = builder.Build(samples);

        AssertMatches(builder.ExpectedMono(samples), Decode(bytes), "chunks before data on a seekable stream");
        AssertMatches(builder.ExpectedMono(samples), Decode(new TrickleStream(bytes)), "chunks before data on a trickling stream");
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void AStreamedDataSizeReadsToTheEndOfTheStream(uint statedSize)
    {
        WavBuilder builder = new() { DataSizeOverride = statedSize, Channels = 2 };
        double[] samples = TestSignal(2000, 2);
        byte[] bytes = builder.Build(samples);

        using WavAudioSource seekable = new(new MemoryStream(bytes));
        AssertClose(1000.0 / 16000, seekable.DurationSeconds, "streamed size on a seekable stream");
        AssertMatches(builder.ExpectedMono(samples), seekable.ReadAll(), "streamed size, seekable");

        using WavAudioSource forwardOnly = new(new TrickleStream(bytes));
        Assert.True(forwardOnly.DurationSeconds is null, $"got {forwardOnly.DurationSeconds}, wanted no duration for an unsized forward-only stream");
        AssertMatches(builder.ExpectedMono(samples), forwardOnly.ReadAll(), "streamed size, forward-only");
    }

    [Fact]
    public void ATruncatedDataChunkDeliversTheWholeFramesThatArePresent()
    {
        // Stereo 16-bit is 4 bytes a frame: cutting 6 bytes removes one frame and half of another.
        WavBuilder builder = new() { Channels = 2, TruncateDataBytes = 6 };
        double[] samples = TestSignal(400, 2);

        byte[] bytes = builder.Build(samples);
        using WavAudioSource source = new(new MemoryStream(bytes));
        float[] decoded = source.ReadAll();

        double[] expected = builder.ExpectedMono(samples)[..198];
        AssertMatches(expected, decoded, "truncated data");
        AssertClose(200.0 / 16000, source.DurationSeconds, "duration states the header's size");
    }

    [Theory]
    [InlineData(1, 8, 1)]
    [InlineData(1, 16, 2)]
    [InlineData(1, 24, 2)]
    [InlineData(1, 24, 3)]
    [InlineData(3, 64, 2)]
    public void ShortReadsFromTheStreamGiveTheSameSamples(int tag, int bits, int channels)
    {
        WavBuilder builder = new() { FormatTag = (ushort)tag, BitsPerSample = bits, Channels = channels };
        builder.ChunksBeforeData.Add(("LIST", new byte[41]));
        double[] samples = TestSignal(900 * channels, channels);
        byte[] bytes = builder.Build(samples);

        float[] whole = Decode(bytes);
        float[] trickled = Decode(new TrickleStream(bytes));

        Assert.True(whole.AsSpan().SequenceEqual(trickled), $"got {trickled.Length} samples from a trickling stream that differ from the {whole.Length} read whole, wanted identical samples");
    }

    [Fact]
    public void SmallCallerBuffersGiveTheSameSamplesAsLargeOnes()
    {
        WavBuilder builder = new() { BitsPerSample = 24, Channels = 2 };
        double[] samples = TestSignal(20000, 2);
        byte[] bytes = builder.Build(samples);

        using WavAudioSource source = new(new MemoryStream(bytes));
        float[] collected = new float[10000];
        int filled = 0;
        Span<float> block = stackalloc float[3];
        int read;
        while ((read = source.Read(block)) > 0)
        {
            block[..read].CopyTo(collected.AsSpan(filled));
            filled += read;
        }

        Assert.True(filled == 10000, $"got {filled} samples, wanted 10000");
        Assert.True(collected.AsSpan().SequenceEqual(Decode(bytes)), "got samples read three at a time that differ from one large read, wanted identical");
    }

    [Theory]
    [InlineData(0x0002, "0x0002")]
    [InlineData(0x0007, "0x0007")]
    [InlineData(0x0055, "0x0055")]
    [InlineData(0x0011, "0x0011")]
    public void CompressedFormatsAreRefusedNamingTheirTag(int tag, string expected)
    {
        WavBuilder builder = new() { FormatTag = (ushort)tag, BitsPerSample = 4 };

        AudioFormatException error = Assert.Throws<AudioFormatException>(() => Decode(builder.Build(new double[16])));

        Assert.True(error.Message.Contains(expected, StringComparison.Ordinal), $"got '{error.Message}', wanted the tag {expected} named");
        Assert.True(error.Message.Contains("supported", StringComparison.Ordinal), $"got '{error.Message}', wanted the supported formats listed");
    }

    [Fact]
    public void AnExtensibleFileWithAForeignSubFormatIsRefused()
    {
        WavBuilder builder = new() { Extensible = true, SubFormatTail = new byte[14] };

        AudioFormatException error = Assert.Throws<AudioFormatException>(() => Decode(builder.Build(new double[16])));

        Assert.True(error.Message.Contains("0xFFFE", StringComparison.Ordinal), $"got '{error.Message}', wanted the extensible tag named");
    }

    [Theory]
    [InlineData(1, 40)]
    [InlineData(3, 16)]
    [InlineData(1, 48)]
    public void UnsupportedBitDepthsAreRefused(int tag, int bits)
    {
        WavBuilder builder = new() { FormatTag = (ushort)tag, BitsPerSample = bits };

        AudioFormatException error = Assert.Throws<AudioFormatException>(() => Decode(builder.Build(new double[16])));

        Assert.True(error.Message.Contains($"{bits}-bit", StringComparison.Ordinal), $"got '{error.Message}', wanted the depth {bits}-bit named");
    }

    [Fact]
    public void ZeroChannelsAreRefused()
    {
        WavBuilder builder = new() { Channels = 0, BlockAlignOverride = 0 };

        AudioFormatException error = Assert.Throws<AudioFormatException>(() => Decode(builder.Build([])));

        Assert.True(error.Message.Contains("0 channels", StringComparison.Ordinal), $"got '{error.Message}', wanted '0 channels' named");
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(384000)]
    public void SampleRatesOutsideTheSupportedRangeAreRefused(int rate)
    {
        WavBuilder builder = new() { SampleRate = rate };

        AudioFormatException error = Assert.Throws<AudioFormatException>(() => Decode(builder.Build(new double[16])));

        Assert.True(error.Message.Contains($"{rate} Hz", StringComparison.Ordinal), $"got '{error.Message}', wanted the rate {rate} Hz named");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(43)]
    public void ATruncatedHeaderIsRefused(int keptBytes)
    {
        byte[] bytes = new WavBuilder().Build(TestSignal(100, 1));

        AudioFormatException seekable = Assert.Throws<AudioFormatException>(() => Decode(bytes[..keptBytes]));
        AudioFormatException trickled = Assert.Throws<AudioFormatException>(() => Decode(new TrickleStream(bytes[..keptBytes])));

        Assert.True(seekable.Message.Contains("truncated", StringComparison.Ordinal), $"got '{seekable.Message}' for {keptBytes} bytes, wanted 'truncated'");
        Assert.True(trickled.Message.Contains("truncated", StringComparison.Ordinal), $"got '{trickled.Message}' for {keptBytes} bytes, wanted 'truncated'");
    }

    [Fact]
    public void AFileThatIsNotWavIsRefusedNamingWhatItStartsWith()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("ID3\u0004 this is an MP3 file pretending");

        AudioFormatException error = Assert.Throws<AudioFormatException>(() => Decode(bytes));

        Assert.True(error.Message.Contains("ID3", StringComparison.Ordinal), $"got '{error.Message}', wanted the leading bytes named");
    }

    [Fact]
    public void ABlockSizeThatContradictsTheChannelsIsRefused()
    {
        WavBuilder builder = new() { Channels = 2, BlockAlignOverride = 2 };

        AudioFormatException error = Assert.Throws<AudioFormatException>(() => Decode(builder.Build(new double[16])));

        Assert.True(error.Message.Contains("block size", StringComparison.Ordinal), $"got '{error.Message}', wanted the block size named");
    }

    [Fact]
    public void DurationAndRateComeFromTheHeader()
    {
        WavBuilder builder = new() { SampleRate = 22050, Channels = 2, BitsPerSample = 24 };
        using WavAudioSource source = new(new MemoryStream(builder.Build(TestSignal(22050 * 2, 2))));

        Assert.True(source.SampleRate == 22050, $"got {source.SampleRate} Hz, wanted 22050");
        Assert.True(source.Channels == 2, $"got {source.Channels} channels, wanted 2");
        AssertClose(1.0, source.DurationSeconds, "one second of stereo");
    }

    [Fact]
    public void TheStreamIsLeftOpenOnlyWhenAsked()
    {
        byte[] bytes = new WavBuilder().Build(TestSignal(10, 1));
        MemoryStream kept = new(bytes);
        MemoryStream closed = new(bytes);

        new WavAudioSource(kept, leaveOpen: true).Dispose();
        new WavAudioSource(closed).Dispose();

        Assert.True(kept.CanRead, "got a closed stream, wanted it left open");
        Assert.True(!closed.CanRead, "got an open stream, wanted it closed with the source");
    }

    // A deterministic signal spanning the full range, including both extremes, per channel.
    internal static double[] TestSignal(int count, int channels)
    {
        double[] values = new double[count];
        for (int i = 0; i < count; i++)
        {
            int channel = i % channels;
            values[i] = i switch
            {
                0 => -1.0,
                1 => 0.999,
                _ => 0.9 * Math.Sin((0.0123 * (i + 1) * (channel + 1)) + channel),
            };
        }

        return values;
    }

    private static float[] Decode(byte[] bytes) => Decode(new MemoryStream(bytes));

    private static float[] Decode(Stream stream)
    {
        using WavAudioSource source = new(stream);
        return source.ReadAll();
    }

    private static void AssertMatches(double[] expected, float[] actual, string what)
    {
        Assert.True(expected.Length == actual.Length, $"{what}: got {actual.Length} samples, wanted {expected.Length}");
        for (int i = 0; i < expected.Length; i++)
        {
            double error = Math.Abs(expected[i] - actual[i]);
            Assert.True(error <= 1e-6, $"{what}: sample {i} is {actual[i]}, wanted {expected[i]} (error {error})");
        }
    }

    private static void AssertWithinStep(double[] original, float[] actual, int bits, bool isFloat)
    {
        double step = isFloat ? 1e-7 : Math.Pow(2, 1 - bits);
        for (int i = 0; i < original.Length; i++)
        {
            double error = Math.Abs(original[i] - actual[i]);
            Assert.True(error <= step + 1e-7, $"{bits}-bit: sample {i} decoded {actual[i]} from {original[i]}, wanted within one step {step}");
        }
    }

    private static void AssertClose(double expected, double? actual, string what)
    {
        Assert.True(actual is double value && Math.Abs(value - expected) < 1e-9, $"{what}: got {actual}, wanted {expected}");
    }
}
