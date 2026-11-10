// Deep-tier measurement of the Silero VAD batch layouts and the silence shortcut: single-core cost and agreement with the frame-by-frame answer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Vad;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests;

/// <remarks>
/// Prints markdown tables to the test output, and writes them to the file MAT_VAD_REPORT names when
/// that variable is set. The runner has one intra-op thread, so every call runs on the test's
/// thread, and the cost is the process's CPU time, which a shared machine disturbs far less than
/// wall time: it is the figure for one core. Each file is timed three times and the cheapest kept.
/// </remarks>
[Trait("Category", "audio")]
[Trait("Tier", "deep")]
public sealed class VadMeasurementTests
{
    private static readonly string[] Files =
    [
        Path.Combine("long", "en-10min.wav"),
        Path.Combine("clips", "en", "en-ami-01.wav"),
        Path.Combine("clips", "fr", "fr-multi-01.wav"),
    ];

    [Fact]
    public void BatchLayoutsAreMeasuredAgainstTheFrameByFrameAnswer()
    {
        string modelPath = TestPaths.RequireSileroModel();
        List<float[]> audio = Files.Select(file => TestAudio.ReadWav(TestPaths.RequireBenchFile(file))).ToList();
        double audioSeconds = audio.Sum(samples => samples.Length / 16000.0);

        using ITensorRunner runner = OrtRunnerFactory.Open(modelPath, new RunnerOptions());
        SileroVadOptions sequential = new() { Lanes = 1, SkipDigitalSilence = false };

        // One untimed pass warms the runtime's memory arena and the code paths.
        Probabilities(runner, sequential, audio[1], out _);
        List<float[]> reference = audio.Select(samples => Probabilities(runner, sequential, samples, out _)).ToList();

        SileroVadOptions[] layouts =
        [
            sequential,
            new() { Lanes = 4, BlockSeconds = 32, WarmUpSeconds = 1, SkipDigitalSilence = false },
            new() { Lanes = 8, BlockSeconds = 32, WarmUpSeconds = 1, SkipDigitalSilence = false },
            new() { Lanes = 16, BlockSeconds = 32, WarmUpSeconds = 1, SkipDigitalSilence = false },
            new() { Lanes = 32, BlockSeconds = 32, WarmUpSeconds = 0.5, SkipDigitalSilence = false },
            new() { Lanes = 4, BlockSeconds = 64, WarmUpSeconds = 4, SkipDigitalSilence = false },
            new() { Lanes = 4, BlockSeconds = 96, WarmUpSeconds = 4, SkipDigitalSilence = false },
            new() { Lanes = 8, BlockSeconds = 128, WarmUpSeconds = 2, SkipDigitalSilence = false },
            new() { Lanes = 8, BlockSeconds = 128, WarmUpSeconds = 4, SkipDigitalSilence = false },
            new() { Lanes = 8, BlockSeconds = 192, WarmUpSeconds = 4, SkipDigitalSilence = false },
            new() { Lanes = 16, BlockSeconds = 256, WarmUpSeconds = 4, SkipDigitalSilence = false },
        ];

        StringBuilder table = new();
        table.AppendLine(CultureInfo.InvariantCulture, $"Audio: {audioSeconds:F1} s in {Files.Length} files ({string.Join(", ", Files.Select(file => file.Replace('\\', '/')))}); agreement against one lane without the shortcut.");
        table.AppendLine();
        table.AppendLine("| Lanes | Block s | Warm-up s | Calls | CPU s | x real time | ms per call | 2 h in CPU s | Max abs diff | Mean abs diff | Decisions differing at 0.5 |");
        table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (SileroVadOptions layout in layouts)
        {
            (double seconds, long calls, Agreement agreement) = Measure(runner, layout, audio, reference);
            double speed = audioSeconds / seconds;
            table.AppendLine(CultureInfo.InvariantCulture,
                $"| {layout.Lanes} | {layout.BlockSeconds:G} | {layout.WarmUpSeconds:G} | {calls} | {seconds:F2} | {speed:F0} | {seconds * 1000 / calls:F3} | {7200 / speed:F1} | {agreement.Max:F4} | {agreement.Mean:F5} | {agreement.DifferingPercent:F3} % |");
        }

        // The digital-silence shortcut acts on runs of exact zeros: the corpus files carry gaps of
        // 0.3 to 1.5 s between utterances, and a padded recording adds long stretches, as a recorder
        // that pads its file writes them.
        float[] clip = audio[1];
        float[] padded = new float[(40 * 16000) + clip.Length + (130 * 16000) + clip.Length + (45 * 16000)];
        clip.CopyTo(padded, 40 * 16000);
        clip.CopyTo(padded, (40 * 16000) + clip.Length + (130 * 16000));
        List<float[]> withPadded = [.. audio, padded];
        double withPaddedSeconds = audioSeconds + (padded.Length / 16000.0);
        SileroVadOptions chosen = new();
        table.AppendLine();
        table.AppendLine(CultureInfo.InvariantCulture,
            $"Digital-silence shortcut over the same files plus a padded recording ({padded.Length / 16000.0:F1} s: en-ami-01 twice between 40 s, 130 s and 45 s of exact zeros), {withPaddedSeconds:F1} s in all; agreement against the same layout without the shortcut.");
        table.AppendLine();
        table.AppendLine("| Lanes | Block s | Warm-up s | Shortest skipped run s | Frames skipped | Calls | CPU s | Decisions differing |");
        table.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (SileroVadOptions layout in (SileroVadOptions[])[sequential with { BlockSeconds = 128 }, chosen])
        {
            SileroVadOptions without = layout with { SkipDigitalSilence = false };
            List<float[]> baseline = withPadded.Select(samples => Probabilities(runner, without, samples, out _)).ToList();
            foreach (double? run in (double?[])[null, 2.0, 1.0, 0.5])
            {
                SileroVadOptions variant = run is null ? without : layout with { SkipDigitalSilence = true, DigitalSilenceSeconds = run.Value };
                (double seconds, long calls, Agreement agreement) = Measure(runner, variant, withPadded, baseline);
                long skipped = withPadded.Sum(samples => SkippedFrames(runner, variant, samples));
                table.AppendLine(CultureInfo.InvariantCulture,
                    $"| {variant.Lanes} | {variant.BlockSeconds:G} | {variant.WarmUpSeconds:G} | {(run is null ? "off" : run.Value.ToString("G", CultureInfo.InvariantCulture))} | {skipped} | {calls} | {seconds:F2} | {agreement.Differing} of {agreement.Frames} |");
            }
        }

        string report = table.ToString();
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        string? reportPath = Environment.GetEnvironmentVariable("MAT_VAD_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.WriteAllText(reportPath, report);
        }
    }

    private sealed record Agreement(double Max, double Mean, long Differing, long Frames)
    {
        public double DifferingPercent => 100.0 * Differing / Frames;
    }

    /// <summary>Runs a layout over every file, timing each three times, and compares its answer to the reference.</summary>
    private static (double CpuSeconds, long Calls, Agreement Agreement) Measure(ITensorRunner runner, SileroVadOptions layout, List<float[]> audio, List<float[]> reference)
    {
        double seconds = 0;
        long calls = 0;
        double max = 0;
        double sum = 0;
        long differing = 0;
        long frames = 0;
        for (int f = 0; f < audio.Count; f++)
        {
            double cheapest = double.MaxValue;
            float[] got = [];
            long fileCalls = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                TimeSpan before = Process.GetCurrentProcess().TotalProcessorTime;
                got = Probabilities(runner, layout, audio[f], out fileCalls);
                cheapest = Math.Min(cheapest, (Process.GetCurrentProcess().TotalProcessorTime - before).TotalSeconds);
            }

            seconds += cheapest;
            calls += fileCalls;
            Assert.True(got.Length == reference[f].Length, $"got {got.Length} probabilities, wanted {reference[f].Length}, for file {f}");
            for (int i = 0; i < got.Length; i++)
            {
                double difference = Math.Abs(got[i] - reference[f][i]);
                max = Math.Max(max, difference);
                sum += difference;
                differing += (got[i] >= 0.5f) != (reference[f][i] >= 0.5f) ? 1 : 0;
            }

            frames += got.Length;
        }

        return (seconds, calls, new Agreement(max, sum / frames, differing, frames));
    }

    private static long SkippedFrames(ITensorRunner runner, SileroVadOptions options, float[] samples)
    {
        SileroVadSession session = Run(runner, options, samples, []);
        return session.SilentFramesSkipped;
    }

    private static float[] Probabilities(ITensorRunner runner, SileroVadOptions options, float[] samples, out long calls)
    {
        List<float> probabilities = new((samples.Length / 512) + 1);
        calls = Run(runner, options, samples, probabilities).ModelCalls;
        return probabilities.ToArray();
    }

    private static SileroVadSession Run(ITensorRunner runner, SileroVadOptions options, float[] samples, List<float> probabilities)
    {
        SileroVadSession session = new SileroVadModel(runner, options).Create();
        for (int offset = 0; offset < samples.Length; offset += 16384)
        {
            session.Append(samples.AsSpan(offset, Math.Min(16384, samples.Length - offset)), probabilities);
        }

        session.Flush(probabilities);
        return session;
    }
}
