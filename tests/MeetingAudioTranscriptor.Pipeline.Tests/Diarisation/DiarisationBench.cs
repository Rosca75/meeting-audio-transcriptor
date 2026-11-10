// The benchmark files of the diarisation evaluation, how they are read and fed to the collector the
// way the recognition workers feed it, and the clock that measures what that costs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Inference;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

/// <summary>Which kind of recording a benchmark file is, because each kind gets its own floors.</summary>
internal enum BenchGroup
{
    /// <summary>Read speech of 2 to 4 speakers concatenated with silences: the easy case.</summary>
    Synthetic,

    /// <summary>Real four-person meeting excerpts with overlaps and backchannels.</summary>
    Ami,

    /// <summary>Ten-minute files of 5 or 6 speakers in alternating turns.</summary>
    Long,
}

/// <summary>One benchmark recording with its reference speakers.</summary>
internal sealed record BenchFile(string Id, BenchGroup Group, string WavPath, string RttmPath);

/// <summary>Locations and loaders the diarisation tests share.</summary>
internal static class DiarisationBench
{
    /// <summary>The environment variable that points the suite at the benchmark audio.</summary>
    public const string DataVariable = "MAT_BENCH_DATA";

    /// <summary>The block length the collector is fed with, as a recognition worker would.</summary>
    public const double BlockSeconds = 60;

    /// <summary>The repository root: the nearest folder above the test output holding the solution file.</summary>
    public static string RepositoryRoot { get; } = FindRoot();

    /// <summary>The benchmark data folder: MAT_BENCH_DATA, else the repository's bench/data.</summary>
    public static string DataDirectory
    {
        get
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(DataVariable);
            return string.IsNullOrWhiteSpace(fromEnvironment)
                ? Path.Combine(RepositoryRoot, "bench", "data")
                : Path.GetFullPath(fromEnvironment);
        }
    }

    /// <summary>The embedding model's path, or null when it is not installed.</summary>
    public static string? EmbeddingModel()
    {
        ModelCatalog catalog = ModelCatalog.FromLockFile(ModelCatalog.ResolveRoot(), Path.Combine(RepositoryRoot, "build", "models.lock"));
        return catalog.MissingFiles("wespeaker-campplus-lm").Count == 0 ? catalog.ResolveRole("wespeaker-campplus-lm", "model") : null;
    }

    /// <summary>Every multi-speaker clip and the three ten-minute files that are present, in a fixed order.</summary>
    public static List<BenchFile> Files()
    {
        List<BenchFile> files = [];
        string clips = Path.Combine(DataDirectory, "clips");
        foreach (string language in new[] { "en", "fr", "de" })
        {
            string folder = Path.Combine(clips, language);
            if (Directory.Exists(folder))
            {
                foreach (string rttm in Directory.GetFiles(folder, "*.rttm").Order(StringComparer.Ordinal))
                {
                    string id = Path.GetFileNameWithoutExtension(rttm);
                    BenchGroup? group = id.Contains("ami", StringComparison.Ordinal) ? BenchGroup.Ami
                        : id.Contains("multi", StringComparison.Ordinal) ? BenchGroup.Synthetic
                        : null;
                    string wav = Path.ChangeExtension(rttm, ".wav");
                    if (group is BenchGroup g && File.Exists(wav))
                    {
                        files.Add(new BenchFile(id, g, wav, rttm));
                    }
                }
            }

            string longWav = Path.Combine(DataDirectory, "long", language + "-10min.wav");
            string longRttm = Path.ChangeExtension(longWav, ".rttm");
            if (File.Exists(longWav) && File.Exists(longRttm))
            {
                files.Add(new BenchFile(language + "-10min", BenchGroup.Long, longWav, longRttm));
            }
        }

        return files;
    }

    /// <summary>Reads a 16 kHz mono WAV file whole.</summary>
    public static float[] ReadWav(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using WavAudioSource source = new(stream);
        if (source.SampleRate != 16000)
        {
            throw new InvalidDataException($"{path} is {source.SampleRate} Hz where the benchmark set is 16 kHz; rebuild it with bench/testset/fetch-testset.mjs");
        }

        return source.ReadAll();
    }

    /// <summary>The reference speech regions: the union of the RTTM segments, in samples.</summary>
    public static List<(long Start, long End)> SpeechRegions(IReadOnlyList<SpeakerSegment> reference, int sampleRate, long totalSamples)
    {
        List<(long Start, long End)> spans = reference
            .Select(s => ((long)Math.Round(s.Start * sampleRate), Math.Min(totalSamples, (long)Math.Round(s.End * sampleRate))))
            .Where(s => s.Item2 > s.Item1)
            .OrderBy(s => s.Item1)
            .ToList();
        List<(long Start, long End)> merged = [];
        foreach ((long start, long end) in spans)
        {
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            }
            else
            {
                merged.Add((start, end));
            }
        }

        return merged;
    }

    /// <summary>Feeds the audio to the collector in blocks, each with the speech regions inside it, as the recognition workers do.</summary>
    public static void Feed(DiarisationCollector collector, float[] samples, IReadOnlyList<(long Start, long End)> regions, CancellationToken cancellation)
    {
        int block = (int)(BlockSeconds * 16000);
        for (long blockStart = 0; blockStart < samples.Length; blockStart += block)
        {
            long blockEnd = Math.Min(samples.Length, blockStart + block);
            List<(int Start, int End)> inside = [];
            foreach ((long start, long end) in regions)
            {
                long from = Math.Max(start, blockStart);
                long to = Math.Min(end, blockEnd);
                if (to > from)
                {
                    inside.Add(((int)(from - blockStart), (int)(to - blockStart)));
                }
            }

            collector.AddBlock(samples.AsSpan((int)blockStart, (int)(blockEnd - blockStart)), blockStart, inside, cancellation);
        }
    }

    /// <summary>The timeline's turns as scorer segments.</summary>
    public static List<SpeakerSegment> Hypothesis(SpeakerTimeline timeline)
    {
        return timeline.Turns.Select(t => new SpeakerSegment(SpeakerTimeline.Label(t.Speaker), t.Start, t.End)).ToList();
    }

    /// <summary>Formats a number for the report with a fixed number of decimals.</summary>
    public static string F(double value, int decimals = 3)
    {
        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MeetingAudioTranscriptor.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("the repository root (the folder holding MeetingAudioTranscriptor.slnx) was not found above the test output");
    }
}

/// <summary>The processor time the calling thread has used, which is what a single-core cost is.</summary>
/// <remarks>
/// The models run with one intra-op thread, on the calling thread, so the thread's own time is the
/// work done, unaffected by other suites running in parallel. Linux reports it in
/// /proc/thread-self/schedstat; elsewhere the wall clock stands in for it.
/// </remarks>
internal static class ThreadClock
{
    /// <summary>Seconds of processor time used by this thread so far (wall-clock seconds where unavailable).</summary>
    public static double Seconds()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                string text = File.ReadAllText("/proc/thread-self/schedstat");
                return long.Parse(text.Split(' ')[0], CultureInfo.InvariantCulture) / 1e9;
            }
        }
        catch (IOException)
        {
        }

        return Environment.TickCount64 / 1000.0;
    }
}
