// Tests of the Silero VAD driver with a fake graph: frame assembly, context and state carry-over, lane bookkeeping, flushing and the silence shortcut.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Vad;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Vad;

[Trait("Category", "audio")]
public sealed class SileroVadSessionTests
{
    /// <summary>32 frames per block and a 4-frame warm-up keep the layouts small enough to check by hand.</summary>
    private static SileroVadOptions Small(int lanes, bool skip = true) =>
        new() { Lanes = lanes, BlockSeconds = 1.024, WarmUpSeconds = 0.128, SkipDigitalSilence = skip, DigitalSilenceSeconds = 0.992 };

    /// <summary>Samples that are all distinct and all louder than the digital-silence level.</summary>
    private static float[] Ramp(int length)
    {
        float[] samples = new float[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = 0.001f + (i * 1e-6f);
        }

        return samples;
    }

    private static List<float> Run(FakeSileroRunner runner, SileroVadOptions options, float[] samples, int appendSize = int.MaxValue)
    {
        SileroVadSession session = new SileroVadModel(runner, options).Create();
        List<float> probabilities = [];
        for (int offset = 0; offset < samples.Length; offset += appendSize)
        {
            session.Append(samples.AsSpan(offset, Math.Min(appendSize, samples.Length - offset)), probabilities);
        }

        session.Flush(probabilities);
        return probabilities;
    }

    [Theory]
    [InlineData(1, 5000)]
    [InlineData(1, 512 * 100)]
    [InlineData(4, 512 * 100)]
    [InlineData(4, (512 * 77) + 300)]
    [InlineData(8, 512 * 200)]
    public void EveryFrameIsAnsweredOnceWithItsOwnSamples(int lanes, int length)
    {
        float[] samples = Ramp(length);

        List<float> got = Run(FakeSileroRunner.FirstNewSample(), Small(lanes), samples);

        int frames = (length + 511) / 512;
        Assert.True(got.Count == frames, $"got {got.Count} probabilities, wanted {frames} for {length} samples and {lanes} lane(s)");
        for (int k = 0; k < frames; k++)
        {
            Assert.True(got[k] == samples[512 * k], $"got {got[k]} for frame {k}, wanted its first sample {samples[512 * k]} ({lanes} lane(s))");
        }
    }

    [Fact]
    public void EachCallReadsThePreceding64SamplesAsContext()
    {
        float[] samples = Ramp(512 * 40);
        FakeSileroRunner runner = FakeSileroRunner.FirstNewSample();

        Run(runner, Small(1), samples);

        Assert.True(runner.Calls.Count == 40, $"got {runner.Calls.Count} calls, wanted 40");
        for (int k = 0; k < 40; k++)
        {
            float[] expected = new float[576];
            int from = (512 * k) - 64;
            for (int i = 0; i < 576; i++)
            {
                expected[i] = from + i >= 0 ? samples[from + i] : 0f;
            }

            Assert.True(runner.Calls[k].Row(0).SequenceEqual(expected), $"call {k} did not read samples [{from}, {from + 576}) with zeros before the stream");
        }
    }

    [Fact]
    public void OneLaneCarriesItsStateAcrossBlocks()
    {
        List<float> got = Run(FakeSileroRunner.StateCounter(), Small(1), Ramp(512 * 100));

        Assert.Equal(Enumerable.Range(0, 100).Select(k => (float)k), got);
    }

    [Fact]
    public void LanesStartEmptyAfterTheirWarmUpAndLaneZeroContinuesTheLastLane()
    {
        const int blockFrames = 32;
        const int warmUp = 4;
        float[] samples = Ramp(512 * blockFrames * 3);
        FakeSileroRunner runner = FakeSileroRunner.StateCounter();

        List<float> got = Run(runner, Small(4), samples);

        LanePlan plan = LanePlan.For(blockFrames, 4, warmUp);
        Assert.True(plan.Lanes == 4 && plan.OwnFrames == 7 && plan.Steps == 11, $"got plan {plan}, wanted 4 lanes of 7 frames and 11 steps");
        float carried = 0;
        for (int block = 0; block < 3; block++)
        {
            for (int lane = 0; lane < plan.Lanes; lane++)
            {
                int ownFrames = lane == 0 ? plan.Steps : plan.OwnFrames;
                for (int i = 0; i < ownFrames; i++)
                {
                    int frame = (block * blockFrames) + plan.FirstOwnFrame(lane) + i;
                    float expected = lane == 0 ? carried + i : warmUp + i;
                    Assert.True(got[frame] == expected, $"got counter {got[frame]} at frame {frame} (block {block}, lane {lane}), wanted {expected}");
                }
            }

            carried = warmUp + plan.OwnFrames;
        }

        Assert.True(runner.Calls.All(call => call.Lanes == 4), "every call of a full block should carry the 4 lanes as one batch");
        Assert.True(runner.Calls.Count == 3 * plan.Steps, $"got {runner.Calls.Count} calls, wanted {3 * plan.Steps}");
    }

    [Fact]
    public void ALaneReadsItsWarmUpFramesWithTheTrueContext()
    {
        float[] samples = Ramp(512 * 32);
        FakeSileroRunner runner = FakeSileroRunner.FirstNewSample();

        Run(runner, Small(4), samples);

        LanePlan plan = LanePlan.For(32, 4, 4);
        for (int lane = 0; lane < plan.Lanes; lane++)
        {
            for (int step = 0; step < plan.LaneSteps(lane); step++)
            {
                int frame = plan.FirstFrame(lane) + step;
                ReadOnlySpan<float> row = runner.Calls[step].Row(lane);
                bool matches = frame == 0
                    ? row[..64].IndexOfAnyExcept(0f) < 0 && row[64..].SequenceEqual(samples.AsSpan(0, 512))
                    : row.SequenceEqual(samples.AsSpan((512 * frame) - 64, 576));
                Assert.True(matches, $"lane {lane} at step {step} did not read frame {frame} with its 64 preceding samples");
            }
        }
    }

    [Theory]
    [InlineData(32, 4, 8)]
    [InlineData(1000, 32, 16)]
    [InlineData(4000, 125, 8)]
    [InlineData(37, 4, 8)]
    [InlineData(9, 4, 8)]
    [InlineData(1, 4, 8)]
    [InlineData(500, 0, 8)]
    public void ALanePlanCoversEveryFrameExactlyOnce(int frames, int warmUp, int maxLanes)
    {
        LanePlan plan = LanePlan.For(frames, maxLanes, warmUp);

        int[] answered = new int[frames];
        for (int lane = 0; lane < plan.Lanes; lane++)
        {
            Assert.True(plan.FirstFrame(lane) >= 0 && plan.LaneSteps(lane) <= plan.Steps, $"lane {lane} of {plan} reads outside the block or longer than lane 0");
            for (int step = plan.WarmUpSteps(lane); step < plan.LaneSteps(lane); step++)
            {
                answered[plan.FirstFrame(lane) + step]++;
            }
        }

        Assert.True(answered.All(count => count == 1), $"plan {plan} for {frames} frames answers some frame other than once: {string.Join(",", answered)}");
        Assert.True(plan.Lanes == 1 || plan.OwnFrames >= warmUp, $"plan {plan} has lanes shorter than their warm-up");
    }

    [Fact]
    public void FlushPadsThePartialLastFrameWithZeros()
    {
        float[] samples = Ramp(1000);
        FakeSileroRunner runner = FakeSileroRunner.FirstNewSample();

        List<float> got = Run(runner, Small(1), samples);

        Assert.True(got.Count == 2, $"got {got.Count} probabilities for 1000 samples, wanted 2");
        ReadOnlySpan<float> row = runner.Calls[1].Row(0);
        Assert.True(row[..552].SequenceEqual(samples.AsSpan(448, 552)), "the last frame did not read samples 448 to 1000");
        Assert.True(row[552..].IndexOfAnyExcept(0f) < 0, "the last frame was not padded with zeros");
    }

    [Fact]
    public void AnEmptyStreamYieldsNothing()
    {
        FakeSileroRunner runner = FakeSileroRunner.FirstNewSample();

        List<float> got = Run(runner, Small(4), []);

        Assert.True(got.Count == 0 && runner.Calls.Count == 0, $"got {got.Count} probabilities and {runner.Calls.Count} calls for no samples, wanted none");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(5000)]
    [InlineData(16384)]
    public void TheAnswerDoesNotDependOnHowSamplesAreAppended(int appendSize)
    {
        float[] samples = Ramp((512 * 90) + 17);

        List<float> whole = Run(FakeSileroRunner.StateCounter(), Small(4), samples);
        List<float> pieces = Run(FakeSileroRunner.StateCounter(), Small(4), samples, appendSize);

        Assert.Equal(whole, pieces);
    }

    [Fact]
    public void ADigitallySilentBlockIsZeroWithoutACallAndResetsTheState()
    {
        float[] samples = new float[512 * 32 * 3];
        Ramp(512 * 32).CopyTo(samples, 0);
        Ramp(512 * 32).CopyTo(samples, 512 * 64);
        FakeSileroRunner runner = FakeSileroRunner.StateCounter();

        SileroVadSession session = new SileroVadModel(runner, Small(1)).Create();
        List<float> got = [];
        session.Append(samples, got, TestContext.Current.CancellationToken);
        session.Flush(got, TestContext.Current.CancellationToken);

        Assert.True(runner.Calls.Count == 64, $"got {runner.Calls.Count} calls, wanted 64 (the silent middle block skipped)");
        Assert.True(session.SilentFramesSkipped == 32, $"got {session.SilentFramesSkipped} skipped frames, wanted 32");
        Assert.True(got.Skip(32).Take(32).All(p => p == 0), "the silent block's probabilities should all be 0");
        Assert.True(got[64] == 0 && got[95] == 31, $"got counters {got[64]} and {got[95]} after the silent block, wanted 0 and 31 (state reset)");
    }

    [Fact]
    public void ASilentRunInsideABlockIsSkippedAndAShorterOneIsRead()
    {
        // One block of 32 frames: 6 loud, 4 silent (the 0.128 s minimum, skipped), 2 loud, 3 silent (shorter, read), 17 loud.
        float[] samples = Ramp(512 * 32);
        Array.Clear(samples, 512 * 6, 512 * 4);
        Array.Clear(samples, 512 * 12, 512 * 3);
        FakeSileroRunner runner = FakeSileroRunner.StateCounter();
        SileroVadOptions options = Small(1) with { DigitalSilenceSeconds = 0.128 };

        List<float> got = Run(runner, options, samples);

        float[] expected = [0, 1, 2, 3, 4, 5, 0, 0, 0, 0, .. Enumerable.Range(0, 22).Select(k => (float)k)];
        Assert.Equal(expected, got);
        Assert.True(runner.Calls.Count == 28, $"got {runner.Calls.Count} calls, wanted 28 (only the 4-frame run skipped)");
    }

    [Fact]
    public void WithoutTheShortcutASilentBlockIsRun()
    {
        FakeSileroRunner runner = FakeSileroRunner.StateCounter();

        List<float> got = Run(runner, Small(1, skip: false), new float[512 * 32]);

        Assert.True(runner.Calls.Count == 32 && got[31] == 31, $"got {runner.Calls.Count} calls and last counter {got[31]}, wanted 32 calls and 31");
    }

    [Fact]
    public void AppendingAfterFlushIsRefused()
    {
        SileroVadSession session = new SileroVadModel(FakeSileroRunner.FirstNewSample(), Small(1)).Create();
        List<float> got = [];
        session.Flush(got, TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => session.Append(new float[10], got, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0, 32, 1)]
    [InlineData(65, 32, 1)]
    [InlineData(8, 0.5, 1)]
    [InlineData(8, 32, 20)]
    public void SettingsOutsideTheirRangeAreRefused(int lanes, double blockSeconds, double warmUpSeconds)
    {
        SileroVadOptions options = new() { Lanes = lanes, BlockSeconds = blockSeconds, WarmUpSeconds = warmUpSeconds };

        Assert.Throws<ArgumentOutOfRangeException>(() => new SileroVadModel(FakeSileroRunner.FirstNewSample(), options));
    }

    [Fact]
    public void TheDefaultsAreValid()
    {
        SileroVadModel model = new(FakeSileroRunner.FirstNewSample());

        Assert.True(model.Options.Lanes >= 1, "the default options should construct a model");
    }
}
