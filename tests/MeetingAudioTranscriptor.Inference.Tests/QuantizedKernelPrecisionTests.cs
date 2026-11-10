// Tests the rule that turns the exact 8-bit kernels on: an override wins, otherwise the lack of VNNI decides.
using Xunit;

namespace MeetingAudioTranscriptor.Inference.Tests;

/// <summary>The decision table of <see cref="QuantizedKernelPrecision"/>.</summary>
public sealed class QuantizedKernelPrecisionTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData("", false, true)]
    [InlineData("1", true, true)]
    [InlineData("0", false, false)]
    [InlineData("yes", true, false)]
    public void TheOverrideWinsAndOtherwiseMissingVnniTurnsTheExactKernelsOn(string? overrideText, bool hasVnni, bool expected)
    {
        Assert.Equal(expected, QuantizedKernelPrecision.Decide(overrideText, hasVnni));
    }

    [Fact]
    public void TheProcessorProbeAnswersWithoutThrowing()
    {
        // The value depends on the machine; the probe must simply be callable everywhere.
        _ = QuantizedKernelPrecision.HasVnni();
    }
}
