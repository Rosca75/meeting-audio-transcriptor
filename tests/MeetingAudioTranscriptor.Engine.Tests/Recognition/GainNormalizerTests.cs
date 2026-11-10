// Unit tests of the input gain: each scheme's target, the gain cap, clipping, silence left untouched, regions and the slow envelope.
using System;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Recognition;

[Trait("Category", "audio")]
public sealed class GainNormalizerTests
{
    /// <summary>A tone whose RMS is the given value (a sine's RMS is its amplitude over root two).</summary>
    private static float[] Tone(int samples, double rms) =>
        Enumerable.Range(0, samples).Select(i => (float)(rms * Math.Sqrt(2) * Math.Sin(i * 0.1))).ToArray();

    private static float[] Apply(float[] source, GainOptions options, params (int Start, int End)[] regions)
    {
        float[] output = new float[source.Length];
        GainNormalizer.Apply(source, output, regions, options);
        return output;
    }

    private static double Rms(ReadOnlySpan<float> samples) => GainNormalizer.Rms(samples);

    [Fact]
    public void NoneCopiesTheSamples()
    {
        float[] source = Tone(1000, 0.3);

        float[] output = Apply(source, new GainOptions { Mode = GainMode.None });

        Assert.True(output.SequenceEqual(source), "got changed samples, wanted an exact copy");
    }

    [Theory]
    [InlineData(0.01, 0.1)]
    [InlineData(0.01, 0.05)]
    [InlineData(0.3, 0.1)]
    public void ChunkRmsReachesTheTarget(double level, double target)
    {
        float[] output = Apply(Tone(16000, level), new GainOptions { Mode = GainMode.ChunkRms, TargetRms = target });

        Assert.True(Math.Abs(Rms(output) - target) < target * 0.01, $"got RMS {Rms(output):F5} from a level of {level}, wanted {target}");
    }

    [Fact]
    public void TheGainIsCapped()
    {
        float[] output = Apply(Tone(16000, 0.0005), new GainOptions { Mode = GainMode.ChunkRms, TargetRms = 0.1, MaxGain = 30 });

        Assert.True(Math.Abs(Rms(output) - 0.015) < 1e-4, $"got RMS {Rms(output):F5}, wanted 0.0005 x 30 = 0.015");
    }

    [Fact]
    public void LoudSamplesAreClippedToFullScale()
    {
        float[] source = Tone(16000, 0.02);
        source[100] = 0.9f;

        float[] output = Apply(source, new GainOptions { Mode = GainMode.ChunkRms, TargetRms = 0.2 });

        Assert.True(output.All(s => s is >= -1f and <= 1f), $"got a peak of {output.Max(Math.Abs)}, wanted every sample in [-1, 1]");
        Assert.True(output[100] == 1f, $"got {output[100]}, wanted the boosted peak clipped to 1");
    }

    [Theory]
    [InlineData(GainMode.ChunkRms)]
    [InlineData(GainMode.SpeechRms)]
    [InlineData(GainMode.SlowAgc)]
    [InlineData(GainMode.RegionRms)]
    public void SilenceIsLeftUntouched(GainMode mode)
    {
        float[] zeros = new float[16000];
        float[] hiss = Tone(16000, 5e-5);

        float[] fromZeros = Apply(zeros, new GainOptions { Mode = mode }, (0, 16000));
        float[] fromHiss = Apply(hiss, new GainOptions { Mode = mode }, (0, 16000));

        Assert.True(fromZeros.All(s => s == 0f), $"{mode}: got non-zero samples from digital silence");
        Assert.True(fromHiss.SequenceEqual(hiss), $"{mode}: got RMS {Rms(fromHiss):E2} from hiss at 5e-5, wanted it unchanged below the silence level");
    }

    [Fact]
    public void SpeechRmsMeasuresTheRegionsOnly()
    {
        // 0.5 s of a quiet region at 0.02 RMS between near silence: the chunk RMS would be far lower.
        float[] source = [.. new float[8000], .. Tone(8000, 0.02), .. new float[8000]];

        float[] output = Apply(source, new GainOptions { Mode = GainMode.SpeechRms, TargetRms = 0.1 }, (8000, 16000));

        double region = Rms(output.AsSpan(8000, 8000));
        Assert.True(Math.Abs(region - 0.1) < 1e-3, $"got region RMS {region:F4}, wanted 0.1 (gain 5, not the chunk's 8.7)");
    }

    [Fact]
    public void SpeechRmsWithoutRegionsFallsBackToTheChunk()
    {
        float[] output = Apply(Tone(16000, 0.02), new GainOptions { Mode = GainMode.SpeechRms, TargetRms = 0.1 });

        Assert.True(Math.Abs(Rms(output) - 0.1) < 1e-3, $"got RMS {Rms(output):F4}, wanted 0.1");
    }

    [Fact]
    public void RegionRmsLevelsEachRegionAndEasesTheGap()
    {
        float[] source = [.. Tone(8000, 0.2), .. Tone(8000, 0.001), .. Tone(8000, 0.01)];

        float[] output = Apply(source, new GainOptions { Mode = GainMode.RegionRms, TargetRms = 0.1 }, (0, 8000), (16000, 24000));

        double first = Rms(output.AsSpan(0, 8000));
        double second = Rms(output.AsSpan(16000, 8000));
        Assert.True(Math.Abs(first - 0.1) < 1e-3 && Math.Abs(second - 0.1) < 1e-3, $"got region RMS {first:F4} and {second:F4}, wanted 0.1 each");

        // Across the gap the gain moves from 0.5 to 10 in a straight line.
        double early = Rms(output.AsSpan(8000, 800)) / 0.001;
        double late = Rms(output.AsSpan(15200, 800)) / 0.001;
        Assert.True(early is > 0.5 and < 2 && late is > 8 and < 10, $"got gains {early:F2} near the first region and {late:F2} near the second, wanted a ramp from 0.5 to 10");
    }

    [Fact]
    public void SlowAgcLevelsAQuietAndALoudSpeaker()
    {
        // Within four times of each other, so neither level is under the envelope's floor.
        float[] source = [.. Tone(48000, 0.05), .. Tone(48000, 0.2)];

        float[] output = Apply(source, new GainOptions { Mode = GainMode.SlowAgc, TargetRms = 0.1, WindowSeconds = 0.5 });

        double quiet = Rms(output.AsSpan(8000, 32000));
        double loud = Rms(output.AsSpan(56000, 32000));
        Assert.True(Math.Abs(quiet - 0.1) < 5e-3 && Math.Abs(loud - 0.1) < 5e-3, $"got {quiet:F4} and {loud:F4} away from the change, wanted both near 0.1");
    }

    [Fact]
    public void SlowAgcRaisesAPauseNoMoreThanItsFloorAllows()
    {
        // Speech at 0.05 RMS with a 1 s pause of hiss at 0.001: the floor is a quarter of the median
        // window RMS (0.0125), so the pause's gain is at most 0.1 / 0.0125 = 8, never the 30x cap.
        float[] source = [.. Tone(32000, 0.05), .. Tone(16000, 0.001), .. Tone(32000, 0.05)];

        float[] output = Apply(source, new GainOptions { Mode = GainMode.SlowAgc, TargetRms = 0.1, MaxGain = 30 });

        double pauseGain = Rms(output.AsSpan(36000, 8000)) / 0.001;
        Assert.True(pauseGain <= 8.01, $"got a gain of {pauseGain:F2} inside the pause, wanted at most 8");
    }

    [Fact]
    public void ADestinationTooShortIsRefused()
    {
        Assert.Throws<ArgumentException>(() => GainNormalizer.Apply(new float[10], new float[5], [], new GainOptions()));
    }
}
