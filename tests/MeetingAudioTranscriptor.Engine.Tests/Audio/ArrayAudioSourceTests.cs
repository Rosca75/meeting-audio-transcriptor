// Tests the in-memory audio source and the reading helpers every consumer leans on.
using System;
using MeetingAudioTranscriptor.Engine.Audio;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Audio;

[Trait("Category", "audio")]
public sealed class ArrayAudioSourceTests
{
    [Fact]
    public void ReadsAreCappedByTheMaximumBlockSize()
    {
        using ArrayAudioSource source = new(new float[] { 1, 2, 3, 4, 5 }, 16000, maxBlockSize: 2);
        float[] buffer = new float[10];

        int first = source.Read(buffer);
        int second = source.Read(buffer);
        int third = source.Read(buffer);
        int fourth = source.Read(buffer);

        Assert.True((first, second, third, fourth) == (2, 2, 1, 0), $"got reads of {first}, {second}, {third}, {fourth}, wanted 2, 2, 1, 0");
    }

    [Fact]
    public void ReadAllReturnsEverySampleInOrder()
    {
        float[] samples = ResamplingAudioSourceTests.Noise(70001, seed: 1);
        using ArrayAudioSource source = new(samples, 8000, maxBlockSize: 999);

        float[] all = source.ReadAll();

        Assert.True(all.AsSpan().SequenceEqual(samples), $"got {all.Length} samples differing from the {samples.Length} given, wanted the same samples");
        Assert.True(source.DurationSeconds == 70001.0 / 8000, $"got {source.DurationSeconds} s, wanted {70001.0 / 8000}");
    }

    [Fact]
    public void ReadBlockFillsAcrossShortReadsAndStopsAtTheEnd()
    {
        using ArrayAudioSource source = new(new float[] { 1, 2, 3, 4, 5, 6, 7 }, 16000, maxBlockSize: 2);
        float[] buffer = new float[5];

        int full = source.ReadBlock(buffer);
        int rest = source.ReadBlock(buffer);
        int none = source.ReadBlock(buffer);

        Assert.True((full, rest, none) == (5, 2, 0), $"got {full}, {rest}, {none}, wanted 5, 2, 0");
        Assert.True(buffer[0] == 6 && buffer[1] == 7, $"got [{buffer[0]}, {buffer[1]}] from the second block, wanted [6, 7]");
    }

    [Fact]
    public void InvalidArgumentsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayAudioSource(new float[1], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayAudioSource(new float[1], 16000, 0));
    }
}
