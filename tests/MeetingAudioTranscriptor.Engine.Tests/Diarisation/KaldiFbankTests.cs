// Tests of the Kaldi filterbank against reference values from kaldi-native-fbank, of its framing and of the mean normalisation.
using System;
using MeetingAudioTranscriptor.Engine.Diarisation;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Diarisation;

/// <remarks>
/// The reference values come from kaldi-native-fbank 1.23.0 (dither 0, 80 bins, low 20 Hz, high
/// Nyquist, snip_edges, no energy, the samples multiplied by 32768) over the signal of
/// <see cref="Signal"/>, which the oracle script fbank_oracle.py beside this file generates the same way.
/// </remarks>
[Trait("Category", "diarisation")]
public sealed class KaldiFbankTests
{
    private const float Tolerance = 1e-3f;

    private static readonly (int Frame, int Bin, float Value)[] Hamming =
    [
        (0, 0, 12.83131f), (0, 1, 14.23929f), (0, 10, 13.28504f), (0, 39, 17.76796f), (0, 79, 22.45371f),
        (5, 3, 13.19753f), (5, 20, 16.26758f), (5, 47, 19.62727f), (5, 60, 21.10612f),
        (10, 0, 13.31632f), (10, 15, 23.12767f), (10, 33, 19.10101f), (10, 55, 18.77463f), (10, 79, 22.02016f),
        (17, 7, 15.56263f), (17, 28, 17.75813f), (17, 44, 20.13657f), (17, 71, 21.78018f),
        (22, 2, 12.72871f), (22, 25, 15.91678f), (22, 50, 19.51918f), (22, 78, 21.91852f),
        (21, 12, 20.10459f), (21, 66, 21.10141f),
    ];

    private static readonly (int Frame, int Bin, float Value)[] Povey =
    [
        (0, 0, 11.52490f), (0, 1, 11.70722f), (0, 10, 15.63219f), (0, 39, 17.78912f), (0, 79, 22.46103f),
        (5, 3, 13.14005f), (5, 20, 16.21992f), (5, 47, 19.65130f), (5, 60, 21.10975f),
        (10, 0, 13.02108f), (10, 15, 23.17549f), (10, 33, 19.13533f), (10, 55, 18.73414f), (10, 79, 22.02214f),
        (17, 7, 13.17900f), (17, 28, 17.83142f), (17, 44, 20.14267f), (17, 71, 21.79389f),
        (22, 2, 13.31516f), (22, 25, 15.60244f), (22, 50, 19.51797f), (22, 78, 21.92233f),
        (21, 12, 20.17555f), (21, 66, 21.10443f),
    ];

    // The Hamming features over all 23 frames with each bin's mean subtracted.
    private static readonly (int Frame, int Bin, float Value)[] HammingNormalised =
    [
        (0, 0, -0.30041f), (0, 1, 0.66626f), (0, 10, -0.62593f), (0, 39, -1.04588f), (0, 79, 0.25076f),
        (5, 3, -0.55897f), (5, 20, 0.07231f), (5, 47, 0.26220f),
    ];

    /// <summary>Two tones, a DC offset and uniform noise from a linear congruential generator, cast to float.</summary>
    internal static float[] Signal(int length)
    {
        float[] samples = new float[length];
        uint state = 12345;
        for (int i = 0; i < length; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            double noise = (state / 4294967296.0 * 2.0) - 1.0;
            double t = i / 16000.0;
            samples[i] = (float)((0.3 * Math.Sin(2 * Math.PI * 440.0 * t)) + (0.2 * Math.Sin(2 * Math.PI * 1234.5 * t)) + (0.05 * noise) + 0.01);
        }

        return samples;
    }

    private static float[] Features(FbankWindow window, float[] samples)
    {
        KaldiFbank fbank = new(window);
        float[] features = new float[KaldiFbank.FrameCount(samples.Length) * KaldiFbank.BinCount];
        fbank.Compute(samples, features, new KaldiFbank.Scratch());
        return features;
    }

    private static void AssertMatches((int Frame, int Bin, float Value)[] expected, float[] features, string what)
    {
        foreach ((int frame, int bin, float value) in expected)
        {
            float got = features[(frame * KaldiFbank.BinCount) + bin];
            Assert.True(
                Math.Abs(got - value) <= Tolerance,
                $"{what}: frame {frame} bin {bin} got {got}, wanted {value} within {Tolerance}");
        }
    }

    [Fact]
    public void HammingFeaturesMatchKaldi()
    {
        AssertMatches(Hamming, Features(FbankWindow.Hamming, Signal(4000)), "hamming");
    }

    [Fact]
    public void PoveyFeaturesMatchKaldi()
    {
        AssertMatches(Povey, Features(FbankWindow.Povey, Signal(4000)), "povey");
    }

    [Fact]
    public void MeanNormalisationMatchesKaldiFeaturesMinusTheirMean()
    {
        float[] features = Features(FbankWindow.Hamming, Signal(4000));
        KaldiFbank.SubtractMean(features);

        AssertMatches(HammingNormalised, features, "normalised");
        for (int bin = 0; bin < KaldiFbank.BinCount; bin++)
        {
            double sum = 0;
            for (int frame = 0; frame < 23; frame++)
            {
                sum += features[(frame * KaldiFbank.BinCount) + bin];
            }

            Assert.True(Math.Abs(sum / 23) < 1e-4, $"bin {bin}: got mean {sum / 23} after normalisation, wanted 0");
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(399, 0)]
    [InlineData(400, 1)]
    [InlineData(559, 1)]
    [InlineData(560, 2)]
    [InlineData(4000, 23)]
    [InlineData(24000, 148)]
    public void FrameCountFollowsSnipEdges(int samples, int frames)
    {
        int got = KaldiFbank.FrameCount(samples);

        Assert.True(got == frames, $"got {got} frames for {samples} samples, wanted {frames}");
    }

    [Fact]
    public void AFrameDependsOnlyOnItsOwnSamples()
    {
        // The features of a window cut from a region are the region's features sliced, which is what
        // lets the embedder compute a region's features once for all of its windows.
        float[] signal = Signal(8000);
        float[] whole = Features(FbankWindow.Hamming, signal);
        float[] tail = Features(FbankWindow.Hamming, signal[1600..]);

        for (int i = 0; i < tail.Length; i++)
        {
            float wholeValue = whole[(10 * KaldiFbank.BinCount) + i];
            Assert.True(wholeValue == tail[i], $"value {i} of the slice got {tail[i]}, wanted the region's {wholeValue}");
        }
    }

    [Fact]
    public void SilenceIsFlooredRatherThanMinusInfinity()
    {
        float[] features = Features(FbankWindow.Hamming, new float[800]);

        float floor = MathF.Log(1.19209290e-07f);
        foreach (float value in features)
        {
            Assert.True(value == floor, $"got {value} for digital silence, wanted the log floor {floor}");
        }
    }
}
