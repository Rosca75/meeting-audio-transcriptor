// Models-tier tests of the VAD chunking stage on the benchmark audio: noise yields nothing, reference speech is kept, and every chunk is well formed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Vad;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests;

/// <remarks>
/// Every test runs the stage with its defaults and a 30 s maximum, typical of the recognisers
/// considered. A result line goes to the test output, and is appended to the file MAT_VAD_REPORT
/// names when that variable is set.
/// </remarks>
[Trait("Category", "audio")]
[Trait("Tier", "models")]
public sealed class VadChunkingTests
{
    private const double MaxChunkSeconds = 30;

    private sealed record Outcome(List<SpeechChunk> Chunks, VadChunkingStage Stage, double CpuSeconds);

    private static Outcome Chunk(float[] audio)
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(TestPaths.RequireSileroModel(), new RunnerOptions());
        VadChunkingStage stage = new(new SileroVadModel(runner), MaxChunkSeconds);
        TimeSpan before = Process.GetCurrentProcess().TotalProcessorTime;
        List<SpeechChunk> chunks = stage.Run(new ArrayAudioSource(audio, 16384), null, TestContext.Current.CancellationToken).ToList();
        return new Outcome(chunks, stage, (Process.GetCurrentProcess().TotalProcessorTime - before).TotalSeconds);
    }

    [Theory]
    [InlineData("silence-60s.wav")]
    [InlineData("noise-white-60s.wav")]
    [InlineData("noise-pink-60s.wav")]
    [InlineData("noise-office-60s.wav")]
    public void NoiseWithoutSpeechYieldsNoChunk(string file)
    {
        float[] audio = TestAudio.ReadWav(TestPaths.RequireBenchFile(Path.Combine("noise", file)));

        Outcome outcome = Chunk(audio);

        string found = string.Join(", ", outcome.Chunks.Select(chunk => $"{chunk.StartSeconds:F2}+{chunk.DurationSeconds:F2} s"));
        Report($"{file}: {outcome.Chunks.Count} chunks");
        Assert.True(outcome.Chunks.Count == 0, $"got {outcome.Chunks.Count} chunk(s) from {file} ({found}), wanted none: the recogniser must not see noise");
    }

    [Theory]
    [InlineData("en", "en-multi-01")]
    [InlineData("en", "en-ami-01")]
    [InlineData("en", "en-ami-02")]
    [InlineData("en", "en-ami-sdm-01")]
    [InlineData("fr", "fr-multi-01")]
    [InlineData("fr", "fr-multi-02")]
    [InlineData("fr", "fr-multi-03")]
    [InlineData("de", "de-multi-01")]
    [InlineData("de", "de-multi-02")]
    [InlineData("de", "de-multi-03")]
    public void ChunksKeepTheReferenceSpeechOfAClip(string language, string clip)
    {
        string wav = TestPaths.RequireBenchFile(Path.Combine("clips", language, clip + ".wav"));
        float[] audio = TestAudio.ReadWav(wav);
        List<(double Start, double End)> turns = TestAudio.ReadRttm(Path.ChangeExtension(wav, ".rttm"));

        Outcome outcome = Chunk(audio);

        AssertWellFormed(outcome.Chunks, audio);
        double coverage = ReferenceCoverage(outcome.Chunks, turns, audio, audibleOnly: true);
        Report(string.Create(CultureInfo.InvariantCulture,
            $"{clip}: {audio.Length / 16000.0:F1} s, {outcome.Chunks.Count} chunks, audible reference covered {coverage:P2}, whole turns covered {ReferenceCoverage(outcome.Chunks, turns, audio, audibleOnly: false):P2}, kept {(double)outcome.Stage.ChunkedSamples / audio.Length:P1}, forced cuts {outcome.Stage.ForcedCuts}"));
        Assert.True(coverage >= 0.97, $"the chunks cover {coverage:P2} of the audible reference speech of {clip}, wanted at least 97 %");
    }

    [Fact]
    public void TenMinutesOfMeetingAreChunkedWithoutAForcedCut()
    {
        string wav = TestPaths.RequireBenchFile(Path.Combine("long", "en-10min.wav"));
        float[] audio = TestAudio.ReadWav(wav);
        List<(double Start, double End)> turns = TestAudio.ReadRttm(Path.ChangeExtension(wav, ".rttm"));

        // A first, untimed run warms the runtime, so the timed one measures the steady state.
        Chunk(audio[..(16000 * 20)]);
        Outcome outcome = Chunk(audio);

        AssertWellFormed(outcome.Chunks, audio);
        double coverage = ReferenceCoverage(outcome.Chunks, turns, audio, audibleOnly: true);
        double audioSeconds = audio.Length / 16000.0;
        double[] lengths = outcome.Chunks.Select(chunk => chunk.DurationSeconds).ToArray();
        Report(string.Create(CultureInfo.InvariantCulture,
            $"en-10min: {audioSeconds:F1} s, {outcome.Chunks.Count} chunks, mean {lengths.Average():F2} s, max {lengths.Max():F2} s, min {lengths.Min():F2} s, "
            + $"kept {(double)outcome.Stage.ChunkedSamples / audio.Length:P1}, speech regions {(double)outcome.Stage.SpeechSamples / audio.Length:P1}, "
            + $"audible reference covered {coverage:P2}, whole turns covered {ReferenceCoverage(outcome.Chunks, turns, audio, audibleOnly: false):P2}, forced cuts {outcome.Stage.ForcedCuts}, "
            + $"VAD and chunking {outcome.CpuSeconds:F2} CPU s ({audioSeconds / outcome.CpuSeconds:F0} x real time, 2 h in {7200 * outcome.CpuSeconds / audioSeconds:F1} CPU s)"));

        Assert.True(outcome.Stage.ForcedCuts == 0, $"got {outcome.Stage.ForcedCuts} forced cuts, wanted none on read speech with pauses");
        Assert.True(coverage >= 0.97, $"the chunks cover {coverage:P2} of the audible reference speech, wanted at least 97 %");
    }

    /// <summary>Writes a result line to the test output, and appends it to the file MAT_VAD_REPORT names when set.</summary>
    private static void Report(string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        string? reportPath = Environment.GetEnvironmentVariable("MAT_VAD_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.AppendAllText(reportPath, line + Environment.NewLine);
        }
    }

    /// <summary>Checks order, contiguity, the maximum length and that each chunk holds the source's samples.</summary>
    private static void AssertWellFormed(List<SpeechChunk> chunks, float[] audio)
    {
        long previousEnd = 0;
        foreach (SpeechChunk chunk in chunks)
        {
            Assert.True(chunk.StartSample >= previousEnd, $"chunk {chunk.Index} starts at {chunk.StartSample}, before the previous end {previousEnd}");
            Assert.True(chunk.DurationSeconds <= MaxChunkSeconds, $"chunk {chunk.Index} lasts {chunk.DurationSeconds:F2} s, over the {MaxChunkSeconds} s maximum");
            Assert.True(chunk.Samples.AsSpan().SequenceEqual(audio.AsSpan((int)chunk.StartSample, chunk.Samples.Length)), $"chunk {chunk.Index} does not hold the source's samples");
            Assert.True(chunk.SpeechRegions.Count > 0 && chunk.SpeechRegions[0].Start == 0 && chunk.SpeechRegions[^1].End == chunk.Samples.Length, $"chunk {chunk.Index} does not start and end in speech");
            previousEnd = chunk.EndSample;
        }
    }

    /// <summary>
    /// The share of the reference's speech that lies inside a chunk, counted in 32 ms frames inside a
    /// reference turn: only those louder than -40 dBFS when <paramref name="audibleOnly"/> is true.
    /// </summary>
    /// <remarks>
    /// A turn of the constructed clips is a whole corpus utterance, which keeps the recording's
    /// silence before and after the words, and an AMI turn joins words up to 0.5 s apart. That
    /// silence is not speech a recogniser needs, so the 97 % applies to the audible frames; the
    /// whole-turn figure is reported beside it.
    /// </remarks>
    private static double ReferenceCoverage(List<SpeechChunk> chunks, List<(double Start, double End)> turns, float[] audio, bool audibleOnly)
    {
        bool[] kept = new bool[audio.Length];
        foreach (SpeechChunk chunk in chunks)
        {
            Array.Fill(kept, true, (int)chunk.StartSample, chunk.Samples.Length);
        }

        long speech = 0;
        long covered = 0;
        foreach ((double start, double end) in turns)
        {
            for (long frame = (long)Math.Ceiling(start * 16000 / 512); (frame + 1) * 512 <= Math.Min(audio.Length, end * 16000); frame++)
            {
                double energy = 0;
                for (long i = frame * 512; i < (frame + 1) * 512; i++)
                {
                    energy += audio[i] * audio[i];
                }

                if (!audibleOnly || 10 * Math.Log10((energy / 512) + 1e-12) > -40)
                {
                    speech++;
                    covered += kept[(frame * 512) + 256] ? 1 : 0;
                }
            }
        }

        return speech == 0 ? 1 : (double)covered / speech;
    }
}
