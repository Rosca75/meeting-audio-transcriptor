// The benchmark command: runs the real service over the test set, measures accuracy, retention, noise and speed, and writes the result file.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Engine.Writers;

namespace MeetingAudioTranscriptor.Cli.Benchmark;

/// <summary>Runs <c>benchmark</c>.</summary>
/// <remarks>
/// Every file goes through the same service a user's run goes through, decoding and writing
/// included: the transcripts are written to a temporary folder, deleted afterwards, so the
/// throughput is end to end. The models load once, before the first file, and the load time is
/// recorded on that file rather than spread over the set.
/// </remarks>
internal static class BenchmarkCommand
{
    /// <summary>The environment variable naming the test set folder.</summary>
    public const string BenchDataVariable = "MAT_BENCH_DATA";

    /// <summary>The gate every language's cleaned-versus-raw content retention must reach.</summary>
    public const double RetentionGate = 0.99;

    public static async Task<int> Run(BenchmarkArguments arguments, TextWriter output, TextWriter errors, CancellationToken cancellation)
    {
        string data = Path.GetFullPath(arguments.Data
            ?? (Environment.GetEnvironmentVariable(BenchDataVariable) is { Length: > 0 } fromEnvironment ? fromEnvironment : Path.Combine("bench", "data")));
        List<BenchEntry> entries;
        try
        {
            entries = arguments.Sets.Count == 0 ? [] : BenchmarkManifest.Select(BenchmarkManifest.Read(data), arguments.Sets);
        }
        catch (Exception e) when (e is FileNotFoundException or ArgumentException)
        {
            errors.WriteLine("error: " + e.Message);
            return e is ArgumentException ? ExitCodes.Usage : ExitCodes.Input;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            errors.WriteLine($"error: the manifest in {data} could not be read ({e.Message}); rebuild the test set with node bench/testset/fetch-testset.mjs");
            return ExitCodes.Input;
        }

        foreach (ExtraFile extra in arguments.ExtraFiles)
        {
            string path = Path.GetFullPath(extra.Path);
            entries.Add(new BenchEntry(Path.GetFileNameWithoutExtension(path), "extra", extra.Language, path, null, null, 0));
        }

        using TranscriptionService service = new(arguments.Models);
        ModelStatus status = service.Check();
        if (!status.Ready)
        {
            errors.WriteLine("error: " + status.Message);
            return ExitCodes.Models;
        }

        MachineFacts machine = MachineFacts.Read();
        int threads = arguments.Threads ?? Engine.Pipeline.PipelineOptions.DefaultThreads;
        output.WriteLine($"Benchmark of {entries.Count} file(s), {threads} thread(s), {arguments.Repeat} repeat(s) on {machine.CpuModel} ({machine.LogicalCores} logical cores{(machine.CpuIdMask is null ? "" : ", CPUIDMASK=" + machine.CpuIdMask)})");

        string scratch = Path.Combine(Path.GetTempPath(), "mat-benchmark-" + Guid.NewGuid().ToString("N"));
        List<FileResult> results = [];
        DateTimeOffset started = DateTimeOffset.UtcNow;
        try
        {
            foreach (BenchEntry entry in entries)
            {
                FileResult result = await RunFile(service, entry, arguments, scratch, cancellation).ConfigureAwait(false);
                results.Add(result);
                output.WriteLine(BenchmarkReport.FileLine(result));
                if (result.Outcome == TranscriptionOutcomeKind.Cancelled)
                {
                    errors.WriteLine("error: the benchmark was cancelled; no result file was written");
                    return ExitCodes.Cancelled;
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(scratch))
                {
                    Directory.Delete(scratch, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leftover temporary folder does not change the results.
            }
        }

        string label = arguments.Label ?? string.Join("-", arguments.Sets.Count > 0 ? arguments.Sets : ["extra"]);
        string outFolder = Path.GetFullPath(arguments.Out ?? Path.Combine("bench", "results"));
        string outFile = Path.Combine(outFolder, started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-" + label + ".json");
        BenchmarkReport report = new(started, machine, threads, !arguments.NoSpeakers && status.SpeakerSeparationAvailable, arguments.Repeat, data, results, arguments.SpeakersHintFromReference);
        try
        {
            Directory.CreateDirectory(outFolder);
            report.WriteJson(outFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors.WriteLine($"error: the result file {outFile} could not be written ({e.Message}); choose another folder with --out");
            return ExitCodes.Output;
        }

        output.Write(report.Table());
        output.WriteLine("Result file: " + outFile);
        return results.Any(result => result.Outcome is not (TranscriptionOutcomeKind.Succeeded or TranscriptionOutcomeKind.NoSpeech)) ? ExitCodes.Unexpected : ExitCodes.Success;
    }

    private static async Task<FileResult> RunFile(TranscriptionService service, BenchEntry entry, BenchmarkArguments arguments, string scratch, CancellationToken cancellation)
    {
        // Noise has no language; it is run as English, the cleaner that sees the most noise in meetings.
        string language = entry.Language ?? "en";
        List<double> walls = [];
        TranscriptionOutcome? first = null;
        double loadSeconds = 0;
        for (int repeat = 0; repeat < arguments.Repeat; repeat++)
        {
            string folder = Path.Combine(scratch, entry.Id + "-" + repeat.ToString(CultureInfo.InvariantCulture));
            TranscriptionRequest request = new()
            {
                InputPath = entry.Path,
                Language = language,
                Formats = [TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Txt],
                OutputFolder = folder,
                SpeakerSeparation = arguments.NoSpeakers ? false : null,
                Threads = arguments.Threads,
                RawFiles = true,
            };
            Stopwatch watch = Stopwatch.StartNew();
            TranscriptionOutcome outcome = await service.RunAsync(request, null, cancellation).ConfigureAwait(false);
            watch.Stop();

            // The models load during the first run only; that time is a start-up cost, not the file's.
            loadSeconds += outcome.ModelLoadSeconds;
            walls.Add(watch.Elapsed.TotalSeconds - outcome.ModelLoadSeconds);
            first ??= outcome;
            if (!outcome.Succeeded)
            {
                break;
            }

            Directory.Delete(folder, recursive: true);
        }

        string? reference = entry.Reference is not null && File.Exists(entry.Reference) ? File.ReadAllText(entry.Reference) : null;
        string? rttm = !arguments.NoSpeakers && entry.Rttm is not null && File.Exists(entry.Rttm) ? File.ReadAllText(entry.Rttm) : null;
        FileResult result = FileResult.From(entry, first!, walls, reference, rttm) with { ModelLoadSeconds = loadSeconds };
        if (arguments.SpeakersHintFromReference && rttm is not null && first!.Succeeded)
        {
            // A separate run told the reference's speaker count, reported beside the automatic one.
            int count = Engine.Scoring.DiarisationRttm.Parse(rttm).Select(segment => segment.Speaker).Distinct(StringComparer.Ordinal).Count();
            string folder = Path.Combine(scratch, entry.Id + "-hinted");
            TranscriptionRequest hinted = new()
            {
                InputPath = entry.Path,
                Language = language,
                Formats = [TranscriptFormat.Json],
                OutputFolder = folder,
                SpeakerSeparation = true,
                SpeakerCountHint = count,
                Threads = arguments.Threads,
            };
            TranscriptionOutcome outcome = await service.RunAsync(hinted, null, cancellation).ConfigureAwait(false);
            if (outcome.Succeeded && outcome.Pipeline?.Document is { } document)
            {
                result = result with { HintedSpeakers = SpeakerMetrics.Compute(rttm, document) };
                Directory.Delete(folder, recursive: true);
            }
        }

        return result;
    }
}
