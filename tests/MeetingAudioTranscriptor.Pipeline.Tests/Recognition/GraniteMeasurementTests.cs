// Deep-tier measurements of the Granite recogniser on the benchmark audio: the input-level schemes, the low-yield retry, the chunk length, noise and throughput, printed as markdown tables.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Engine.Vad;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Recognition;

/// <remarks>
/// Each measurement runs the real VAD stage and the real graph and reports word error rates per
/// group (micro-averaged over the group's clips) and the CPU time. Run them pinned to one core,
/// because they report single-core cost; the tables also go to the file MAT_GRANITE_REPORT names.
/// They assert only that something was measured: the floors live in the models tier.
/// </remarks>
[Trait("Category", "audio")]
[Trait("Tier", "deep")]
public sealed class GraniteMeasurementTests
{
    private const double DefaultMax = 30;
    private const double DefaultTarget = 20;
    private static readonly string[] Groups = ["en-read", "en-ami", "fr", "de"];
    private static readonly string[] AmiClips = ["en-ami-01", "en-ami-02", "en-ami-sdm-01"];
    private static readonly string[] LongGroups = ["en-10min", "fr-10min", "de-10min", "en-ami"];

    [Fact]
    public void GainSchemesOnAllClips()
    {
        List<(string Name, GainOptions Gain)> schemes = [("none", new GainOptions { Mode = GainMode.None })];
        foreach (double target in Targets())
        {
            schemes.Add(($"chunk rms {target}", new GainOptions { Mode = GainMode.ChunkRms, TargetRms = target }));
            schemes.Add(($"speech rms {target}", new GainOptions { Mode = GainMode.SpeechRms, TargetRms = target }));
            schemes.Add(($"region rms {target}", new GainOptions { Mode = GainMode.RegionRms, TargetRms = target }));
            schemes.Add(($"agc {target} 0.5 s 30x", new GainOptions { Mode = GainMode.SlowAgc, TargetRms = target }));
        }

        Table(schemes.Select(s => (s.Name, (IRegionAwareRecognizer)GraniteFixture.Recognizer(new GraniteOptions { Gain = s.Gain }))), GraniteFixture.Clips);
    }

    [Fact]
    public void SlowAgcSweepOnMeetings()
    {
        List<(string, IRegionAwareRecognizer)> schemes = [];
        foreach (double target in Targets())
        {
            foreach (double window in new[] { 0.25, 0.5, 1.0 })
            {
                foreach (double cap in new[] { 10.0, 30.0, 100.0 })
                {
                    GainOptions gain = new() { Mode = GainMode.SlowAgc, TargetRms = target, WindowSeconds = window, MaxGain = cap };
                    schemes.Add((string.Create(CultureInfo.InvariantCulture, $"agc {target} {window} s {cap}x"), GraniteFixture.Recognizer(new GraniteOptions { Gain = gain })));
                }
            }
        }

        Table(schemes, GraniteFixture.Clips.Where(clip => clip.Group == "en-ami").ToArray());
    }

    [Fact]
    public void CandidatesOnAllClips()
    {
        // The schemes the selected settings are compared with, named by MAT_GRANITE_CANDIDATES as
        // mode:target:window:cap separated by commas.
        string spec = Environment.GetEnvironmentVariable("MAT_GRANITE_CANDIDATES") ?? "SlowAgc:0.1:0.5:30";
        List<(string, IRegionAwareRecognizer)> schemes = [];
        foreach (string item in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = item.Split(':');
            GainOptions gain = new()
            {
                Mode = Enum.Parse<GainMode>(parts[0]),
                TargetRms = double.Parse(parts[1], CultureInfo.InvariantCulture),
                WindowSeconds = double.Parse(parts[2], CultureInfo.InvariantCulture),
                MaxGain = double.Parse(parts[3], CultureInfo.InvariantCulture),
            };
            schemes.Add((item, GraniteFixture.Recognizer(new GraniteOptions { Gain = gain })));
        }

        Table(schemes, GraniteFixture.Clips);
    }

    [Fact]
    public void LowYieldRetryOnAllClips()
    {
        GraniteCtcRecognizer plain = GraniteFixture.Recognizer();
        List<(string, IRegionAwareRecognizer)> schemes = [("default, no retry", plain)];
        List<LowYieldRetry> retries = [];
        foreach ((double minSeconds, double rate) in new[] { (1.5, 0.5), (1.5, 1.0), (1.0, 1.0), (2.0, 1.5) })
        {
            LowYieldRetry retry = new(plain, new LowYieldRetryOptions { MinRegionSeconds = minSeconds, MinWordsPerSecond = rate });
            retries.Add(retry);
            schemes.Add((string.Create(CultureInfo.InvariantCulture, $"retry >= {minSeconds} s, < {rate} words/s"), retry));
        }

        Table(schemes, GraniteFixture.Clips);
        double audioHours = GraniteFixture.Clips.Sum(clip => GraniteFixture.Chunks(clip, DefaultMax, DefaultTarget).Sum(chunk => chunk.DurationSeconds)) / 3600;
        foreach ((LowYieldRetry retry, int i) in retries.Select((r, i) => (r, i)))
        {
            GraniteFixture.Report(string.Create(CultureInfo.InvariantCulture,
                $"- {schemes[i + 1].Item1}: {retry.Chunks} chunks, {retry.Retries} extra runs ({retry.Retries / audioHours:F0} per hour of chunked audio), {retry.Replaced} replaced"));
        }
    }

    [Fact]
    public void ChunkLengthOnLongFilesAndMeetings()
    {
        BenchClip[] clips = [.. GraniteFixture.LongFiles, .. GraniteFixture.Clips.Where(clip => clip.Group == "en-ami")];
        GraniteFixture.Report("| chunker target / max | en-10min | fr-10min | de-10min | en-ami | chunks | CPU s |");
        GraniteFixture.Report("|---|---|---|---|---|---|---|");
        foreach ((double target, double max) in new[] { (10.0, 20.0), (20.0, 30.0), (30.0, 40.0) })
        {
            GraniteCtcRecognizer recognizer = GraniteFixture.Recognizer(new GraniteOptions { MaxChunkSeconds = max });
            Dictionary<string, List<WordErrorRateResult>> byGroup = [];
            int chunks = 0;
            TimeSpan before = Process.GetCurrentProcess().TotalProcessorTime;
            foreach (BenchClip clip in clips)
            {
                List<SpeechChunk> cut = GraniteFixture.Chunks(clip, max, target);
                chunks += cut.Count;
                WordErrorRateResult result = GraniteFixture.Score(GraniteFixture.Transcribe(recognizer, cut, TestContext.Current.CancellationToken), GraniteFixture.Reference(clip));
                (byGroup.TryGetValue(clip.Group, out List<WordErrorRateResult>? list) ? list : byGroup[clip.Group] = []).Add(result);
            }

            double cpu = (Process.GetCurrentProcess().TotalProcessorTime - before).TotalSeconds;
            string cells = string.Join(" | ", LongGroups.Select(g => Percent(WordErrorRate.Aggregate(byGroup[g]))));
            GraniteFixture.Report(string.Create(CultureInfo.InvariantCulture, $"| {target} / {max} s | {cells} | {chunks} | {cpu:F0} |"));
        }
    }

    [Fact]
    public void NoiseByScheme()
    {
        string[] files = ["silence-60s.wav", "noise-white-60s.wav", "noise-pink-60s.wav", "noise-office-60s.wav"];
        (string Name, GainOptions Gain)[] schemes =
        [
            ("none", new GainOptions { Mode = GainMode.None }),
            ("chunk rms 0.1", new GainOptions { Mode = GainMode.ChunkRms }),
            ("speech rms 0.1", new GainOptions { Mode = GainMode.SpeechRms }),
            ("region rms 0.1", new GainOptions { Mode = GainMode.RegionRms }),
            ("agc 0.1 0.5 s 30x", new GainOptions { Mode = GainMode.SlowAgc }),
            ("agc 0.1 0.5 s 100x", new GainOptions { Mode = GainMode.SlowAgc, MaxGain = 100 }),
        ];
        GraniteFixture.Report("| scheme | " + string.Join(" | ", files) + " |");
        GraniteFixture.Report("|---|" + string.Concat(Enumerable.Repeat("---|", files.Length)));
        foreach ((string name, GainOptions gain) in schemes)
        {
            GraniteCtcRecognizer recognizer = GraniteFixture.Recognizer(new GraniteOptions { Gain = gain });
            List<string> cells = [];
            foreach (string file in files)
            {
                float[] audio = TestAudio.ReadWav(TestPaths.RequireBenchFile(System.IO.Path.Combine("noise", file)));
                List<string> words = [];
                for (int start = 0; start < audio.Length; start += 30 * 16000)
                {
                    int end = Math.Min(audio.Length, start + (30 * 16000));
                    words.AddRange(recognizer.Recognize(audio.AsMemory(start, end - start), TestContext.Current.CancellationToken).Select(w => w.Text));
                }

                cells.Add(words.Count == 0 ? "0 words" : $"{words.Count} words: {string.Join(' ', words.Take(8))}");
            }

            GraniteFixture.Report($"| {name} | {string.Join(" | ", cells)} |");
        }
    }

    [Fact]
    public void ThroughputOnTenMinutes()
    {
        BenchClip clip = GraniteFixture.LongFiles[0];
        List<SpeechChunk> chunks = GraniteFixture.Chunks(clip, DefaultMax, DefaultTarget);
        GraniteCtcRecognizer recognizer = GraniteFixture.Recognizer();

        // A warm-up chunk, so the session's first-call costs are not counted.
        recognizer.Recognize(chunks[0].Samples, chunks[0].SpeechRegions, TestContext.Current.CancellationToken);
        GraniteFixture.Model.Reset();
        TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        Stopwatch wall = Stopwatch.StartNew();
        List<RecognizedWord> words = GraniteFixture.Transcribe(recognizer, chunks, TestContext.Current.CancellationToken);
        wall.Stop();
        double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
        double audio = GraniteFixture.Audio(clip).Length / 16000.0;
        double model = GraniteFixture.Model.Elapsed.TotalSeconds;
        GraniteFixture.Report(string.Create(CultureInfo.InvariantCulture,
            $"- {clip.Name}: {audio:F0} s of audio, {chunks.Count} chunks, {words.Count} words, CPU {cpu:F1} s ({audio / cpu:F1}x real time per core), wall {wall.Elapsed.TotalSeconds:F1} s, in the model {model:F1} s ({model / wall.Elapsed.TotalSeconds:P1} of the wall time), WER {Percent(GraniteFixture.Score(words, GraniteFixture.Reference(clip)))}"));
        Assert.True(words.Count > 0, $"got no words from {clip.Name}, wanted a transcript");
    }

    /// <summary>The target levels to sweep: MAT_GRANITE_TARGETS as a comma-separated list, else 0.05, 0.1 and 0.2.</summary>
    private static double[] Targets() =>
        (Environment.GetEnvironmentVariable("MAT_GRANITE_TARGETS") ?? "0.05,0.1,0.2")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(text => double.Parse(text, CultureInfo.InvariantCulture))
            .ToArray();

    /// <summary>Runs every scheme over the clips (only the groups MAT_GRANITE_GROUPS lists, when set) and reports one row per scheme.</summary>
    private static void Table(IEnumerable<(string Name, IRegionAwareRecognizer Recognizer)> schemes, IReadOnlyList<BenchClip> clips)
    {
        string? only = Environment.GetEnvironmentVariable("MAT_GRANITE_GROUPS");
        if (!string.IsNullOrWhiteSpace(only))
        {
            string[] wanted = only.Split(',');
            clips = clips.Where(clip => wanted.Contains(clip.Group)).ToArray();
        }

        string[] groups = Groups.Where(g => clips.Any(c => c.Group == g)).ToArray();
        bool meetings = clips.Any(c => c.Group == "en-ami");
        GraniteFixture.Report("| scheme | " + string.Join(" | ", groups) + (meetings ? " | " + string.Join(" | ", AmiClips) : "") + " | CPU s |");
        GraniteFixture.Report("|---|" + string.Concat(Enumerable.Repeat("---|", groups.Length + (meetings ? AmiClips.Length : 0) + 1)));
        int rows = 0;
        foreach ((string name, IRegionAwareRecognizer recognizer) in schemes)
        {
            Dictionary<string, WordErrorRateResult> perClip = [];
            TimeSpan before = Process.GetCurrentProcess().TotalProcessorTime;
            foreach (BenchClip clip in clips)
            {
                List<SpeechChunk> chunks = GraniteFixture.Chunks(clip, DefaultMax, DefaultTarget);
                perClip[clip.Name] = GraniteFixture.Score(GraniteFixture.Transcribe(recognizer, chunks, TestContext.Current.CancellationToken), GraniteFixture.Reference(clip));
            }

            double cpu = (Process.GetCurrentProcess().TotalProcessorTime - before).TotalSeconds;
            IEnumerable<string> cells = groups.Select(g => Percent(WordErrorRate.Aggregate(clips.Where(c => c.Group == g).Select(c => perClip[c.Name]))));
            if (meetings)
            {
                cells = cells.Concat(AmiClips.Select(name => Percent(perClip[name])));
            }

            GraniteFixture.Report(string.Create(CultureInfo.InvariantCulture, $"| {name} | {string.Join(" | ", cells)} | {cpu:F0} |"));
            rows++;
        }

        Assert.True(rows > 0, "got no scheme to measure, wanted at least one");
    }

    private static string Percent(WordErrorRateResult result) => string.Create(CultureInfo.InvariantCulture, $"{result.Wer * 100:F1}");
}
