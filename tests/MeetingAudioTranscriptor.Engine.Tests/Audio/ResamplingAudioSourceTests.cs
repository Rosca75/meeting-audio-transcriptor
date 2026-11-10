// Tests that the resampler delivers exactly the right number of samples, keeps speech-band tones at
// their amplitude and phase, rejects what would alias, and gives the same output however it is read.
using System;
using MeetingAudioTranscriptor.Engine.Audio;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Audio;

[Trait("Category", "audio")]
public sealed class ResamplingAudioSourceTests
{
    private readonly ITestOutputHelper _output;

    public ResamplingAudioSourceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<int, int> RatePairs => new()
    {
        { 44100, 16000 },
        { 48000, 16000 },
        { 8000, 16000 },
        { 22050, 16000 },
        { 32000, 16000 },
        { 96000, 16000 },
        { 11025, 16000 },
        { 192000, 16000 },
        { 47999, 16000 },
        { 16000, 44100 },
        { 16000, 8000 },
    };

    [Theory]
    [MemberData(nameof(RatePairs))]
    public void TheOutputHoldsExactlyTheScaledLengthRoundedUp(int sourceRate, int targetRate)
    {
        foreach (int length in new[] { 0, 1, 2, 5, 17, 160, 1001, 44101 })
        {
            float[] input = Tone(length, sourceRate, 440.0, 0.5);
            using ResamplingAudioSource resampler = new(new ArrayAudioSource(input, sourceRate), targetRate);

            float[] output = resampler.ReadAll();

            long expected = ((long)length * targetRate + sourceRate - 1) / sourceRate;
            Assert.True(output.Length == expected, $"{sourceRate} to {targetRate} Hz over {length} samples: got {output.Length} outputs, wanted {expected}");
            Assert.True(resampler.Read(new float[8]) == 0, $"{sourceRate} to {targetRate} Hz over {length} samples: got more samples after the end, wanted none");
        }
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(8000)]
    [InlineData(22050)]
    [InlineData(96000)]
    [InlineData(47999)]
    public void AOneKilohertzToneKeepsItsAmplitudeAndPhase(int sourceRate)
    {
        const double frequency = 1000.0;
        const double amplitude = 0.8;
        float[] input = Tone(sourceRate / 2, sourceRate, frequency, amplitude);

        float[] output = Resample(input, sourceRate, 16000);

        // Away from the edges, where the filter sees the silence before and after the input, the
        // output is the same tone sampled at 16 kHz with no delay.
        const int edge = 400;
        double worst = 0;
        for (int n = edge; n < output.Length - edge; n++)
        {
            double wanted = amplitude * Math.Sin(2.0 * Math.PI * frequency * n / 16000.0);
            worst = Math.Max(worst, Math.Abs(output[n] - wanted));
        }

        double gain = Decibels(MeasureAmplitude(output, edge, output.Length - edge, frequency, 16000) / amplitude);
        _output.WriteLine($"{sourceRate} Hz: largest error {worst:E2}, gain {gain:F4} dB");
        Assert.True(worst < 1e-3, $"{sourceRate} to 16000 Hz: got a largest error of {worst:E3} against the ideal tone, wanted below 1e-3");
        Assert.True(Math.Abs(gain) < 0.1, $"{sourceRate} to 16000 Hz: got a 1 kHz gain of {gain:F4} dB, wanted within 0.1 dB");
    }

    [Theory]
    [InlineData(48000, 100.0)]
    [InlineData(48000, 3400.0)]
    [InlineData(48000, 7200.0)]
    [InlineData(44100, 7200.0)]
    [InlineData(22050, 7200.0)]
    [InlineData(8000, 3600.0)]
    public void ThePassBandIsFlatToNineTenthsOfTheLowerNyquist(int sourceRate, double frequency)
    {
        float[] input = Tone(sourceRate, sourceRate, frequency, 0.5);

        float[] output = Resample(input, sourceRate, 16000);

        double gain = Decibels(MeasureAmplitude(output, 1000, output.Length - 1000, frequency, 16000) / 0.5);
        Assert.True(Math.Abs(gain) < 0.1, $"{sourceRate} to 16000 Hz at {frequency} Hz: got a gain of {gain:F4} dB, wanted within 0.1 dB");
    }

    [Fact]
    public void ATenKilohertzToneIsRejectedBeforeItCanAlias()
    {
        float[] input = Tone(48000, 48000, 10000.0, 0.9);

        float[] output = Resample(input, 48000, 16000);

        double attenuation = -Decibels(Rms(output, 1000, output.Length - 1000) / Rms(input, 3000, input.Length - 3000));
        _output.WriteLine($"10 kHz at 48 kHz to 16 kHz: {attenuation:F1} dB below the input");
        Assert.True(attenuation >= 80, $"got the 10 kHz tone {attenuation:F1} dB down after 48 to 16 kHz, wanted at least 80 dB");
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(96000)]
    [InlineData(22050)]
    [InlineData(47999)]
    public void EveryToneAboveTheOutputNyquistIsAttenuatedByEightyDecibels(int sourceRate)
    {
        // A sweep across the stop band, from just past the lower Nyquist frequency to the input's.
        double worst = double.MaxValue;
        double worstFrequency = 0;
        for (double frequency = 8000.0; frequency < sourceRate / 2.0; frequency += sourceRate / 2.0 > 30000 ? 1531.0 : 377.0)
        {
            float[] input = Tone(sourceRate / 4, sourceRate, frequency, 0.9);
            float[] output = Resample(input, sourceRate, 16000);
            double attenuation = -Decibels(Rms(output, 600, output.Length - 600) / Rms(input, 2000, input.Length - 2000));
            if (attenuation < worst)
            {
                worst = attenuation;
                worstFrequency = frequency;
            }
        }

        _output.WriteLine($"{sourceRate} Hz to 16 kHz: worst stop-band attenuation {worst:F1} dB at {worstFrequency} Hz");
        Assert.True(worst >= 80, $"{sourceRate} to 16000 Hz: got {worst:F1} dB of attenuation at {worstFrequency} Hz, wanted at least 80 dB across the stop band");
    }

    [Fact]
    public void UpsamplingLeavesNoImageAboveTheInputNyquist()
    {
        // Upsampling a 3 kHz tone from 8 kHz must add no image at 5 kHz or 13 kHz: the residual
        // against the ideal 16 kHz tone carries any image there is.
        float[] input = Tone(8000, 8000, 3000.0, 0.9);

        float[] output = Resample(input, 8000, 16000);

        double sumSquares = 0;
        int count = 0;
        for (int n = 1000; n < output.Length - 1000; n++)
        {
            double residual = output[n] - (0.9 * Math.Sin(2.0 * Math.PI * 3000.0 * n / 16000.0));
            sumSquares += residual * residual;
            count++;
        }

        double level = -Decibels(Math.Sqrt(sumSquares / count) / (0.9 / Math.Sqrt(2)));
        _output.WriteLine($"8 kHz to 16 kHz: residual {level:F1} dB below the tone");
        Assert.True(level >= 80, $"got a residual {level:F1} dB below the tone after 8 to 16 kHz, wanted at least 80 dB");
    }

    [Fact]
    public void AConstantStaysConstant()
    {
        float[] input = new float[4410];
        Array.Fill(input, 0.25f);

        float[] output = Resample(input, 44100, 16000);

        for (int n = 200; n < output.Length - 200; n++)
        {
            Assert.True(Math.Abs(output[n] - 0.25f) < 1e-5, $"got {output[n]} at output {n}, wanted 0.25 for a constant input");
        }
    }

    [Theory]
    [InlineData(44100, 16000)]
    [InlineData(48000, 16000)]
    [InlineData(8000, 16000)]
    [InlineData(47999, 16000)]
    public void TheOutputIsIdenticalWhateverTheReadSizes(int sourceRate, int targetRate)
    {
        float[] input = Noise(sourceRate / 3, seed: 7);
        float[] reference = Resample(input, sourceRate, targetRate);

        foreach (int inputBlock in new[] { 1, 7, 160, 4096 })
        {
            foreach (int outputBlock in new[] { 1, 7, 160, 4096 })
            {
                using ResamplingAudioSource resampler = new(new ArrayAudioSource(input, sourceRate, inputBlock), targetRate);
                float[] output = new float[reference.Length + 10];
                float[] block = new float[outputBlock];
                int filled = 0;
                int read;
                while ((read = resampler.Read(block)) > 0)
                {
                    block.AsSpan(0, read).CopyTo(output.AsSpan(filled));
                    filled += read;
                }

                Assert.True(filled == reference.Length, $"input blocks of {inputBlock}, output blocks of {outputBlock}: got {filled} samples, wanted {reference.Length}");
                Assert.True(
                    output.AsSpan(0, filled).SequenceEqual(reference),
                    $"input blocks of {inputBlock}, output blocks of {outputBlock}: got samples that differ from one large read, wanted identical");
            }
        }
    }

    [Fact]
    public void EqualRatesPassTheSamplesThroughUnchanged()
    {
        float[] input = Noise(5000, seed: 3);
        using ResamplingAudioSource resampler = new(new ArrayAudioSource(input, 16000, 333));

        float[] output = resampler.ReadAll();

        Assert.True(resampler.TapsPerOutput == 0, $"got {resampler.TapsPerOutput} taps, wanted none for equal rates");
        Assert.True(output.AsSpan().SequenceEqual(input), "got samples changed by a pass-through, wanted them unchanged");
    }

    [Fact]
    public void TheRateAndDurationAreReported()
    {
        using ResamplingAudioSource resampler = new(new ArrayAudioSource(new float[44100], 44100));

        Assert.True(resampler.SampleRate == AudioFormat.ModelSampleRate, $"got {resampler.SampleRate} Hz, wanted the model rate");
        Assert.True(resampler.DurationSeconds == 1.0, $"got {resampler.DurationSeconds} s, wanted the inner source's 1 s");
    }

    internal static float[] Tone(int length, int sampleRate, double frequency, double amplitude)
    {
        float[] samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / sampleRate));
        }

        return samples;
    }

    internal static float[] Noise(int length, int seed)
    {
        Random random = new(seed);
        float[] samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)((random.NextDouble() * 2.0) - 1.0);
        }

        return samples;
    }

    private static float[] Resample(float[] input, int sourceRate, int targetRate)
    {
        using ResamplingAudioSource resampler = new(new ArrayAudioSource(input, sourceRate), targetRate);
        return resampler.ReadAll();
    }

    // The amplitude of the component at one frequency, by projecting on its sine and cosine.
    private static double MeasureAmplitude(float[] samples, int from, int to, double frequency, int sampleRate)
    {
        double s = 0;
        double c = 0;
        for (int n = from; n < to; n++)
        {
            double angle = 2.0 * Math.PI * frequency * n / sampleRate;
            s += samples[n] * Math.Sin(angle);
            c += samples[n] * Math.Cos(angle);
        }

        return 2.0 * Math.Sqrt((s * s) + (c * c)) / (to - from);
    }

    private static double Rms(float[] samples, int from, int to)
    {
        double sum = 0;
        for (int n = from; n < to; n++)
        {
            sum += (double)samples[n] * samples[n];
        }

        return Math.Sqrt(sum / (to - from));
    }

    private static double Decibels(double ratio) => 20.0 * Math.Log10(ratio);
}
