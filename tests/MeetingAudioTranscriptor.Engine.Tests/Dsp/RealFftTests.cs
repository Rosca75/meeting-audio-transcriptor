// Tests the real FFT against a direct double-precision DFT and against the identities a spectrum obeys.
using System;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Engine.Dsp;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Dsp;

[Trait("Category", "audio")]
public sealed class RealFftTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void TheSpectrumMatchesADirectDft(int size)
    {
        float[] frame = Noise(size, seed: size);
        RealFft fft = new(size);
        float[] real = new float[fft.SpectrumLength];
        float[] imaginary = new float[fft.SpectrumLength];
        float[] power = new float[fft.SpectrumLength];

        fft.Forward(frame, real, imaginary, fft.CreateScratch());
        fft.PowerSpectrum(frame, power, fft.CreateScratch());

        (double[] wantedRe, double[] wantedIm) = Dft(frame);
        double peak = 0;
        double peakPower = 0;
        for (int k = 0; k <= size / 2; k++)
        {
            peak = Math.Max(peak, Math.Sqrt((wantedRe[k] * wantedRe[k]) + (wantedIm[k] * wantedIm[k])));
            peakPower = Math.Max(peakPower, (wantedRe[k] * wantedRe[k]) + (wantedIm[k] * wantedIm[k]));
        }

        for (int k = 0; k <= size / 2; k++)
        {
            double error = Math.Max(Math.Abs(real[k] - wantedRe[k]), Math.Abs(imaginary[k] - wantedIm[k])) / peak;
            Assert.True(error < 1e-5, $"size {size}, bin {k}: got {real[k]} {imaginary[k]}i, wanted {wantedRe[k]} {wantedIm[k]}i (relative error {error:E2})");
            double wantedPower = (wantedRe[k] * wantedRe[k]) + (wantedIm[k] * wantedIm[k]);
            double powerError = Math.Abs(power[k] - wantedPower) / peakPower;
            Assert.True(powerError < 1e-4, $"size {size}, bin {k}: got power {power[k]}, wanted {wantedPower} (relative error {powerError:E2})");
        }
    }

    [Theory]
    [InlineData(256, 0)]
    [InlineData(512, 3)]
    [InlineData(1024, 1023)]
    public void AnImpulseHasAFlatSpectrum(int size, int position)
    {
        float[] frame = new float[size];
        frame[position] = 1;

        float[] power = PowerOf(frame);

        for (int k = 0; k < power.Length; k++)
        {
            Assert.True(Math.Abs(power[k] - 1) < 1e-5, $"size {size}, impulse at {position}: got power {power[k]} in bin {k}, wanted 1");
        }
    }

    [Fact]
    public void AConstantHasAllItsPowerAtZeroHertz()
    {
        float[] frame = new float[512];
        Array.Fill(frame, 0.5f);

        float[] power = PowerOf(frame);

        Assert.True(Math.Abs(power[0] - (256.0 * 256.0)) < 1e-2, $"got {power[0]} at 0 Hz, wanted {256.0 * 256.0}");
        for (int k = 1; k < power.Length; k++)
        {
            Assert.True(power[k] < 1e-6, $"got power {power[k]} in bin {k}, wanted none away from 0 Hz");
        }
    }

    [Theory]
    [InlineData(512, 1)]
    [InlineData(512, 37)]
    [InlineData(512, 255)]
    [InlineData(256, 100)]
    public void ASineAtABinCentreFallsInThatBinAlone(int size, int bin)
    {
        float[] frame = new float[size];
        for (int n = 0; n < size; n++)
        {
            frame[n] = (float)Math.Sin(2.0 * Math.PI * bin * n / size);
        }

        float[] power = PowerOf(frame);

        double wanted = (size / 2.0) * (size / 2.0);
        Assert.True(Math.Abs(power[bin] - wanted) / wanted < 1e-4, $"got {power[bin]} in bin {bin}, wanted {wanted}");
        for (int k = 0; k < power.Length; k++)
        {
            if (k != bin)
            {
                Assert.True(power[k] / wanted < 1e-9, $"size {size}: got power {power[k]} in bin {k} for a tone in bin {bin}, wanted none");
            }
        }
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(2048)]
    public void ParsevalHolds(int size)
    {
        float[] frame = Noise(size, seed: 11);

        float[] power = PowerOf(frame);

        double timeEnergy = 0;
        foreach (float sample in frame)
        {
            timeEnergy += sample * sample;
        }

        double spectrumEnergy = power[0] + power[size / 2];
        for (int k = 1; k < size / 2; k++)
        {
            spectrumEnergy += 2.0 * power[k];
        }

        spectrumEnergy /= size;
        double error = Math.Abs(spectrumEnergy - timeEnergy) / timeEnergy;
        Assert.True(error < 1e-5, $"size {size}: got spectrum energy {spectrumEnergy}, wanted the frame's {timeEnergy} (relative error {error:E2})");
    }

    [Fact]
    public void TheFrameMayBeTheScratchBuffer()
    {
        RealFft fft = new(512);
        float[] frame = Noise(512, seed: 5);
        float[] separate = new float[fft.SpectrumLength];
        float[] inPlace = new float[fft.SpectrumLength];

        fft.PowerSpectrum(frame, separate, fft.CreateScratch());
        float[] scratch = (float[])frame.Clone();
        fft.PowerSpectrum(scratch, inPlace, scratch);

        Assert.True(inPlace.AsSpan().SequenceEqual(separate), "got a different spectrum when the frame is the scratch buffer, wanted the same");
    }

    [Fact]
    public void OneInstanceServesManyThreadsWithTheirOwnScratch()
    {
        RealFft fft = new(512);
        float[] frame = Noise(512, seed: 9);
        float[] reference = PowerOf(frame);
        bool[] same = new bool[8];

        Parallel.For(0, same.Length, worker =>
        {
            float[] scratch = fft.CreateScratch();
            float[] power = new float[fft.SpectrumLength];
            bool all = true;
            for (int round = 0; round < 500; round++)
            {
                fft.PowerSpectrum(frame, power, scratch);
                all &= power.AsSpan().SequenceEqual(reference);
            }

            same[worker] = all;
        });

        Assert.True(Array.TrueForAll(same, value => value), $"got [{string.Join(", ", same)}] per thread, wanted every thread to match the reference");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(500)]
    [InlineData(131072)]
    public void SizesThatAreNotSupportedPowersOfTwoAreRefused(int size)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new RealFft(size));

        Assert.True(error.Message.Contains("power of two", StringComparison.Ordinal), $"got '{error.Message}', wanted the rule named");
    }

    [Fact]
    public void ShortBuffersAreRefused()
    {
        RealFft fft = new(256);

        Assert.Throws<ArgumentException>(() => fft.PowerSpectrum(new float[255], new float[129], new float[256]));
        Assert.Throws<ArgumentException>(() => fft.PowerSpectrum(new float[256], new float[128], new float[256]));
        Assert.Throws<ArgumentException>(() => fft.PowerSpectrum(new float[256], new float[129], new float[255]));
    }

    private static float[] PowerOf(float[] frame)
    {
        RealFft fft = new(frame.Length);
        float[] power = new float[fft.SpectrumLength];
        fft.PowerSpectrum(frame, power, fft.CreateScratch());
        return power;
    }

    // The textbook DFT in double precision, bins 0 .. N/2.
    private static (double[] Re, double[] Im) Dft(float[] frame)
    {
        int n = frame.Length;
        double[] re = new double[(n / 2) + 1];
        double[] im = new double[(n / 2) + 1];
        for (int k = 0; k <= n / 2; k++)
        {
            for (int t = 0; t < n; t++)
            {
                // The angle is reduced modulo n first, so large k * t keeps full precision.
                double angle = -2.0 * Math.PI * ((long)k * t % n) / n;
                re[k] += frame[t] * Math.Cos(angle);
                im[k] += frame[t] * Math.Sin(angle);
            }
        }

        return (re, im);
    }

    private static float[] Noise(int length, int seed)
    {
        Random random = new(seed);
        float[] samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)((random.NextDouble() * 2.0) - 1.0);
        }

        return samples;
    }
}
