// Reads the test set's manifest.json and picks the files a benchmark run covers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MeetingAudioTranscriptor.Cli.Benchmark;

/// <summary>One file of the test set, as the manifest lists it.</summary>
/// <param name="Id">The file's id, such as <c>en-ami-01</c>.</param>
/// <param name="Type">clip, long, container or noise; extra for a file given on the command line.</param>
/// <param name="Language">en, fr or de; null for noise.</param>
/// <param name="Path">The absolute path of the audio.</param>
/// <param name="Reference">The absolute path of the reference text; null for noise and extra files.</param>
/// <param name="Rttm">The absolute path of the speaker turns; null when the file has none.</param>
/// <param name="DurationSeconds">The duration the manifest states; 0 for an extra file.</param>
internal sealed record BenchEntry(string Id, string Type, string? Language, string Path, string? Reference, string? Rttm, double DurationSeconds)
{
    /// <summary>
    /// The group a file is reported in: <c>en-ami</c> for the AMI meeting excerpts, <c>en-read</c>
    /// for the other English, <c>fr</c>, <c>de</c>, <c>noise</c>, <c>container</c> for the m4a and
    /// mp4 copies (which would count en-10min twice) and <c>extra</c>.
    /// </summary>
    public string Group => Type switch
    {
        "noise" => "noise",
        "container" => "container",
        "extra" => "extra",
        _ when Id.Contains("ami", StringComparison.Ordinal) => "en-ami",
        _ when Language == "en" => "en-read",
        _ => Language ?? "other",
    };
}

/// <summary>The test set's manifest.</summary>
internal static class BenchmarkManifest
{
    /// <summary>The long files that the <c>long</c> set holds: the ten-minute ones, so the 30-minute loop runs only when asked by id or with all.</summary>
    private const double LongSetMaxSeconds = 15 * 60;

    /// <summary>Reads every entry of <c>manifest.json</c> in the data folder.</summary>
    /// <exception cref="FileNotFoundException">The manifest is missing; the message names the builder.</exception>
    public static List<BenchEntry> Read(string dataFolder)
    {
        string path = System.IO.Path.Combine(dataFolder, "manifest.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"the test set manifest {path} does not exist; build the test set with node bench/testset/fetch-testset.mjs, or pass its folder with --data or MAT_BENCH_DATA");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        List<BenchEntry> entries = [];
        foreach (JsonElement entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new BenchEntry(
                entry.GetProperty("id").GetString()!,
                entry.GetProperty("type").GetString()!,
                StringOrNull(entry, "language"),
                Absolute(dataFolder, entry.GetProperty("path").GetString()!)!,
                Absolute(dataFolder, StringOrNull(entry, "reference")),
                Absolute(dataFolder, StringOrNull(entry, "rttm")),
                entry.TryGetProperty("durationSeconds", out JsonElement duration) ? duration.GetDouble() : 0));
        }

        return entries;
    }

    /// <summary>The entries the sets name, in manifest order and without repeats.</summary>
    /// <exception cref="ArgumentException">A set is neither a known name nor a file id; the message lists both.</exception>
    public static List<BenchEntry> Select(IReadOnlyList<BenchEntry> entries, IReadOnlyList<string> sets)
    {
        HashSet<string> chosen = new(StringComparer.Ordinal);
        foreach (string set in sets)
        {
            IEnumerable<BenchEntry> matched = set switch
            {
                "all" => entries,
                "clips" => entries.Where(e => e.Type == "clip"),
                "long" => entries.Where(e => e.Type == "long" && e.DurationSeconds <= LongSetMaxSeconds),
                "containers" => entries.Where(e => e.Type == "container"),
                "noise" => entries.Where(e => e.Type == "noise"),
                _ => entries.Where(e => e.Id == set),
            };
            List<BenchEntry> list = matched.ToList();
            if (list.Count == 0)
            {
                throw new ArgumentException(
                    $"--set '{set}' matches nothing; use clips, long, containers, noise, all or a file id such as {string.Join(", ", entries.Take(3).Select(e => e.Id))}");
            }

            foreach (BenchEntry entry in list)
            {
                chosen.Add(entry.Id);
            }
        }

        return entries.Where(e => chosen.Contains(e.Id)).ToList();
    }

    private static string? StringOrNull(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Absolute(string folder, string? relative) =>
        relative is null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, relative));
}
