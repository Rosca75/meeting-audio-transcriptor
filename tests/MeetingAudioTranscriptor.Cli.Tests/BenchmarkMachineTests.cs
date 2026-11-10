// Tests that a benchmark records the facts a speed and an accuracy cannot be read without: the mask, whether the processor reports VNNI and whether the exact 8-bit kernels are on.
using System;
using System.IO;
using System.Text.Json;
using MeetingAudioTranscriptor.Cli.Benchmark;
using Xunit;

namespace MeetingAudioTranscriptor.Cli.Tests;

[Trait("Category", "config")]
public sealed class BenchmarkMachineTests
{
    private static MachineFacts Facts(string? mask, bool vnni, bool exact) => new("Test CPU", 4, "Linux", ".NET", "1.31.1", mask, vnni, exact);

    [Theory]
    [InlineData("avx2", false, true, "Test CPU (4 logical cores, CPUIDMASK=avx2, VNNI not reported, exact 8-bit kernels on)")]
    [InlineData(null, true, false, "Test CPU (4 logical cores, VNNI reported, exact 8-bit kernels off)")]
    public void TheHeaderNamesTheMaskTheVnniAndTheKernels(string? mask, bool vnni, bool exact, string expected)
    {
        string text = Facts(mask, vnni, exact).Describe();

        Assert.True(text == expected, $"got '{text}', wanted '{expected}'");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheResultFileRecordsTheVnniAndTheKernelNextToTheMask(bool vnni, bool exact)
    {
        string path = Path.Combine(Path.GetTempPath(), "mat-machine-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new BenchmarkReport(DateTimeOffset.UnixEpoch, Facts("avx2", vnni, exact), 4, true, 1, "data", []).WriteJson(path);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement machine = json.RootElement.GetProperty("machine");

            Assert.True(machine.GetProperty("cpuIdMask").GetString() == "avx2", $"got {machine}, wanted the mask avx2");
            Assert.True(machine.GetProperty("processorReportsVnni").GetBoolean() == vnni, $"got {machine}, wanted processorReportsVnni {vnni}");
            Assert.True(machine.GetProperty("exactQuantizedKernels").GetBoolean() == exact, $"got {machine}, wanted exactQuantizedKernels {exact}");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
