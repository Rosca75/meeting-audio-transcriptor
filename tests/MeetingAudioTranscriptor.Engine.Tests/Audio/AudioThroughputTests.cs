// Timed measurements of the audio input layer over a two-hour meeting's worth of samples, in the
// deep tier: they prove the reader and the resampler are a small fraction of a transcription's time.
using System;
using System.IO;
using MeetingAudioTranscriptor.Engine.Audio;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Audio;

[Trait("Category", "audio")]
[Trait("Tier", "deep")]
[Collection(ThroughputClock.Collection)]
public sealed class AudioThroughputTests
{
    private const int TwoHours = 2 * 60 * 60;

    private readonly ITestOutputHelper _output;

    public AudioThroughputTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void TwoHoursResampleTo16KilohertzWithinFiveSeconds(int sourceRate)
    {
        using PeriodicSource source = new(sourceRate, (long)TwoHours * sourceRate);
        using ResamplingAudioSource resampler = new(source);
        float[] block = new float[16000];

        ThroughputClock clock = new();
        long produced = 0;
        int read;
        while ((read = resampler.Read(block)) > 0)
        {
            produced += read;
        }

        clock.Stop();
        _output.WriteLine($"resampled 2 h of {sourceRate} Hz mono to {produced} samples at 16 kHz in {clock} ({resampler.TapsPerOutput} taps per output)");
        Assert.True(produced == (long)TwoHours * 16000, $"got {produced} samples, wanted {(long)TwoHours * 16000}");
        Assert.True(clock.ProcessorSeconds < 5, $"got {clock} for two hours, wanted under 5 s of processor time");
    }

    [Fact]
    public void TwoHoursOf48KilohertzStereo16BitPcmDecodeWithinAFewSeconds()
    {
        long frames = (long)TwoHours * 48000;
        using WavAudioSource source = new(new SyntheticWavStream(frames, channels: 2));
        float[] block = new float[16384];

        ThroughputClock clock = new();
        long decoded = 0;
        int read;
        while ((read = source.Read(block)) > 0)
        {
            decoded += read;
        }

        clock.Stop();
        _output.WriteLine($"decoded {frames * 4 / 1e9:F2} GB of 48 kHz stereo 16-bit PCM in {clock}, the synthetic stream's copying included");
        Assert.True(decoded == frames, $"got {decoded} samples, wanted {frames}");
        Assert.True(clock.ProcessorSeconds < 5, $"got {clock} for two hours, wanted under 5 s of processor time");
    }

    // A long source that repeats one second of a speech-band mixture, so the measurement is the resampler alone.
    private sealed class PeriodicSource : IAudioSource
    {
        private readonly float[] _period;
        private readonly long _length;
        private long _position;

        public PeriodicSource(int sampleRate, long length)
        {
            SampleRate = sampleRate;
            _length = length;
            _period = new float[sampleRate];
            for (int i = 0; i < sampleRate; i++)
            {
                double t = (double)i / sampleRate;
                _period[i] = (float)((0.3 * Math.Sin(2 * Math.PI * 220 * t)) + (0.2 * Math.Sin(2 * Math.PI * 1300 * t)) + (0.1 * Math.Sin(2 * Math.PI * 9000 * t)));
            }
        }

        public int SampleRate { get; }

        public double? DurationSeconds => (double)_length / SampleRate;

        public int Read(Span<float> buffer)
        {
            int offset = (int)(_position % _period.Length);
            int count = (int)Math.Min(Math.Min(buffer.Length, _period.Length - offset), _length - _position);
            _period.AsSpan(offset, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public void Dispose()
        {
        }
    }

    // A forward-only WAV stream of the given length whose data repeats one block, generated as it is read.
    private sealed class SyntheticWavStream : Stream
    {
        private readonly byte[] _header;
        private readonly byte[] _pattern;
        private readonly long _total;
        private long _position;

        public SyntheticWavStream(long frames, int channels)
        {
            WavBuilder builder = new() { Channels = channels, SampleRate = 48000 };
            long dataBytes = frames * channels * 2;
            _header = builder.Build([])[..44];
            BitConverterWrite(_header, 40, (uint)dataBytes);
            _pattern = builder.Build(WavAudioSourceTests.TestSignal(channels * 4096, channels))[44..];
            _total = 44 + dataBytes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException("the synthetic stream is forward-only");

        public override long Position
        {
            get => throw new NotSupportedException("the synthetic stream is forward-only");
            set => throw new NotSupportedException("the synthetic stream is forward-only");
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, _total - _position);
            int written = 0;
            while (written < count)
            {
                long at = _position + written;
                ReadOnlySpan<byte> from = at < 44 ? _header.AsSpan((int)at) : _pattern.AsSpan((int)((at - 44) % _pattern.Length));
                int take = Math.Min(from.Length, count - written);
                from[..take].CopyTo(buffer[written..]);
                written += take;
            }

            _position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("the synthetic stream is forward-only");

        public override void SetLength(long value) => throw new NotSupportedException("the synthetic stream is read-only");

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("the synthetic stream is read-only");

        private static void BitConverterWrite(byte[] bytes, int offset, uint value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        }
    }
}
