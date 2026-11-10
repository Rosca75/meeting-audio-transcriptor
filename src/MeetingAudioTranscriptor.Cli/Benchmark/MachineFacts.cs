// The facts about the machine a benchmark ran on, so a result is never read without them.
using System;
using System.IO;
using System.Runtime.InteropServices;
using MeetingAudioTranscriptor.App;

namespace MeetingAudioTranscriptor.Cli.Benchmark;

/// <summary>What the result file records about the machine.</summary>
/// <param name="CpuModel">The processor's name from /proc/cpuinfo, or the architecture where that file does not exist.</param>
/// <param name="LogicalCores">The logical processors the process may use.</param>
/// <param name="OperatingSystem">The operating system's description.</param>
/// <param name="Framework">The .NET runtime's description.</param>
/// <param name="OnnxRuntime">The native ONNX Runtime's version.</param>
/// <param name="CpuIdMask">
/// The value of the <c>CPUIDMASK</c> environment variable, or null: a benchmark may run under a
/// mask that hides processor features to stand for a laptop, and its numbers mean nothing without it.
/// </param>
internal sealed record MachineFacts(string CpuModel, int LogicalCores, string OperatingSystem, string Framework, string OnnxRuntime, string? CpuIdMask)
{
    public static MachineFacts Read() => new(
        ReadCpuModel(),
        Environment.ProcessorCount,
        RuntimeInformation.OSDescription,
        RuntimeInformation.FrameworkDescription,
        TranscriptionService.RuntimeVersion,
        Environment.GetEnvironmentVariable("CPUIDMASK") is { Length: > 0 } mask ? mask : null);

    private static string ReadCpuModel()
    {
        const string cpuinfo = "/proc/cpuinfo";
        try
        {
            if (File.Exists(cpuinfo))
            {
                foreach (string line in File.ReadLines(cpuinfo))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal))
                    {
                        int colon = line.IndexOf(':', StringComparison.Ordinal);
                        return colon < 0 ? line : line[(colon + 1)..].Trim();
                    }
                }
            }
        }
        catch (IOException)
        {
            // The architecture below is still a fact worth recording.
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }
}
