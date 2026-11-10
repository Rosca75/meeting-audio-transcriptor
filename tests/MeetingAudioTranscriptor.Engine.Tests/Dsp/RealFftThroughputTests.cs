// Timed measurement of the real FFT over the number of frames a two-hour meeting yields, in the
// deep tier: 720,000 frames of 512 samples, one every 10 ms.
using System;
using MeetingAudioTranscriptor.Engine.Dsp;
using MeetingAudioTranscriptor.Engine.Tests.Audio;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Dsp;

[Trait("Category", "audio")]
[Trait("Tier", "deep")]
[Collection(ThroughputClock.Collection)]
public sealed class RealFftThroughputTests
{
    private readonly ITestOutputHelper _output;

    public RealFftThroughputTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TheFramesOfTwoHoursTransformWithinFiveSeconds()
    {
        const int frames = 720_000;
        RealFft fft = new(512);
        float[] scratch = fft.CreateScratch();
        float[] power = new float[fft.SpectrumLength];
        float[] signal = new float[16000 + 512];
        Random random = new(1);
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = (float)((random.NextDouble() * 2.0) - 1.0);
        }

        // Warm up so the measurement is the steady state rather than the compiler.
        for (int i = 0; i < 20000; i++)
        {
            fft.PowerSpectrum(signal.AsSpan(0, 512), power, scratch);
        }

        ThroughputClock clock = new();
        double checksum = 0;
        for (int i = 0; i < frames; i++)
        {
            fft.PowerSpectrum(signal.AsSpan((i * 160) % 16000, 512), power, scratch);
            checksum += power[1];
        }

        clock.Stop();
        _output.WriteLine($"{frames} power spectra of 512 samples in {clock}, {clock.ProcessorSeconds * 1e6 / frames:F2} us each (checksum {checksum:E3})");
        Assert.True(clock.ProcessorSeconds < 5, $"got {clock} for {frames} frames, wanted under 5 s of processor time");
    }
}
