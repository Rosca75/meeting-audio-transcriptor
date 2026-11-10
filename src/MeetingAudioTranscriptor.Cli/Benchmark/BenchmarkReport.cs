// The benchmark's output: the machine-readable result file and the compact table printed at the end.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Scoring;

namespace MeetingAudioTranscriptor.Cli.Benchmark;

/// <summary>Writes the results of one benchmark run.</summary>
/// <remarks>
/// The result file is written with a hand-driven JSON writer in a fixed key order, so two files
/// compare line by line and no reflection-based serialisation is needed. Fractions are rounded to
/// four decimals and seconds to three.
/// </remarks>
internal sealed class BenchmarkReport
{
    /// <summary>The shape version of the result file.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The groups in the order the table lists them.</summary>
    private static readonly string[] GroupOrder = ["en-read", "en-ami", "fr", "de", "container", "noise", "extra"];

    private readonly DateTimeOffset _started;
    private readonly MachineFacts _machine;
    private readonly int _threads;
    private readonly bool _speakers;
    private readonly bool _hinted;
    private readonly int _repeat;
    private readonly string _data;
    private readonly IReadOnlyList<FileResult> _files;

    public BenchmarkReport(DateTimeOffset started, MachineFacts machine, int threads, bool speakers, int repeat, string data, IReadOnlyList<FileResult> files, bool hinted = false)
    {
        _hinted = hinted;
        _started = started;
        _machine = machine;
        _threads = threads;
        _speakers = speakers;
        _repeat = repeat;
        _data = data;
        _files = files;
    }

    /// <summary>The groups present, in table order.</summary>
    public IReadOnlyList<GroupResult> Groups() =>
        GroupOrder
            .Select(group => (Group: group, Files: _files.Where(file => file.Entry.Group == group).ToList()))
            .Where(pair => pair.Files.Count > 0)
            .Select(pair => GroupResult.Of(pair.Group, pair.Files))
            .ToList();

    /// <summary>Retention per language and over every language, from the files of the three languages that are not container copies.</summary>
    public IReadOnlyList<(string Language, ContentRetentionResult Retention)> RetentionByLanguage()
    {
        List<FileResult> counted = _files.Where(file => file.Retention is not null && file.Entry.Type is "clip" or "long" or "extra").ToList();
        List<(string, ContentRetentionResult)> rows = [];
        foreach (string language in (string[])["en", "fr", "de"])
        {
            List<FileResult> of = counted.Where(file => file.Entry.Language == language).ToList();
            if (of.Count > 0)
            {
                rows.Add((language, ContentRetention.Aggregate(of.Select(file => file.Retention!.Value))));
            }
        }

        if (counted.Count > 0)
        {
            rows.Add(("all", ContentRetention.Aggregate(counted.Select(file => file.Retention!.Value))));
        }

        return rows;
    }

    /// <summary>One line printed as a file finishes.</summary>
    public static string FileLine(FileResult file)
    {
        StringBuilder line = new();
        line.Append(CultureInfo.InvariantCulture, $"  {file.Entry.Id,-18} {file.Outcome,-10} {file.AudioSeconds,7:0.0} s audio {file.MedianWallSeconds,7:0.0} s wall {file.Throughput,6:0.0}x");
        if (file.RawWer is WordErrorRateResult raw && file.CleanedWer is WordErrorRateResult cleaned)
        {
            line.Append(CultureInfo.InvariantCulture, $"  WER raw {raw.Wer * 100:0.0}% cleaned {cleaned.Wer * 100:0.0}%");
        }

        if (file.SpeakerScore is SpeakerMetrics speakers)
        {
            line.Append(CultureInfo.InvariantCulture, $"  speakers {speakers.EstimatedSpeakers}/{speakers.ReferenceSpeakers} DER {speakers.Excluded.ErrorRate:0.000}/{speakers.Included.ErrorRate:0.000} attribution {speakers.Attribution:0.000}");
        }

        if (file.HintedSpeakers is SpeakerMetrics hinted)
        {
            line.Append(CultureInfo.InvariantCulture, $"  hinted {hinted.EstimatedSpeakers}/{hinted.ReferenceSpeakers} DER {hinted.Excluded.ErrorRate:0.000} attribution {hinted.Attribution:0.000}");
        }

        if (file.Entry.Type == "noise")
        {
            line.Append(CultureInfo.InvariantCulture, $"  {file.Words} word(s)");
        }

        if (file.Outcome is not (TranscriptionOutcomeKind.Succeeded or TranscriptionOutcomeKind.NoSpeech))
        {
            line.Append("  ").Append(file.Message);
        }

        return line.ToString();
    }

    /// <summary>The compact table: one row per group, then retention per language and the gates.</summary>
    public string Table()
    {
        StringBuilder text = new();
        text.AppendLine();
        text.AppendLine("group      files  audio s   wall s  x real  WER raw  WER clean  retention  removed  noise words");
        foreach (GroupResult group in Groups())
        {
            text.Append(CultureInfo.InvariantCulture, $"{group.Group,-10} {group.Files,5} {group.AudioSeconds,8:0.0} {group.WallSeconds,8:0.0} {group.Throughput,7:0.0}");
            text.Append(CultureInfo.InvariantCulture, $"  {Percent(group.RawWer?.Wer),7}  {Percent(group.CleanedWer?.Wer),9}  {Fraction(group.Retention?.Fraction),9}  {Percent(group.RemovedFraction),7}");
            text.AppendLine(group.Group == "noise" ? string.Create(CultureInfo.InvariantCulture, $"  {group.Words,11}") : "");
        }

        List<GroupResult> withSpeakers = Groups().Where(group => group.Speakers is not null).ToList();
        if (withSpeakers.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("speakers   files  DER excl  DER incl  confusion  attribution  count exact  count error" + (_hinted ? "  | hinted: DER excl  attribution  count exact" : ""));
            foreach (GroupResult group in withSpeakers)
            {
                SpeakerGroupMetrics speakers = group.Speakers!;
                text.Append(CultureInfo.InvariantCulture, $"{group.Group,-10} {speakers.Files,5} {speakers.ErrorRateExcluded,9:0.000} {speakers.ErrorRateIncluded,9:0.000} {speakers.ConfusionExcluded,10:0.000} {speakers.Attribution,12:0.000} {speakers.ExactCounts,7}/{speakers.Files,-4} {speakers.MeanAbsoluteCountError,11:0.00}");
                if (group.HintedSpeakers is SpeakerGroupMetrics hinted)
                {
                    text.Append(CultureInfo.InvariantCulture, $"  | {hinted.ErrorRateExcluded,15:0.000} {hinted.Attribution,12:0.000} {hinted.ExactCounts,7}/{hinted.Files}");
                }

                text.AppendLine();
            }
        }

        text.AppendLine();
        foreach ((string language, ContentRetentionResult retention) in RetentionByLanguage())
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"retention {language,-3} {retention.Fraction:0.0000} ({retention.Retained}/{retention.Total}) {(retention.Fraction >= BenchmarkCommand.RetentionGate ? "meets" : "MISSES")} the {BenchmarkCommand.RetentionGate:0.00} gate"));
        }

        List<FileResult> noise = _files.Where(file => file.Entry.Type == "noise").ToList();
        if (noise.Count > 0)
        {
            int words = noise.Sum(file => file.Words);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"noise: {words} word(s) over {noise.Count} file(s), {(words == 0 ? "meets" : "MISSES")} the gate of 0"));
        }

        long peak = _files.Count > 0 ? _files.Max(file => file.PeakWorkingSetBytes) : 0;
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"peak working set {peak / (1024.0 * 1024.0):0} MB, {_threads} thread(s)"));
        return text.ToString();
    }

    /// <summary>Writes the result file.</summary>
    public void WriteJson(string path)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter json = new(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json.WriteStartObject();
        json.WriteNumber("schemaVersion", SchemaVersion);
        json.WriteString("application", Contracts.AppInfo.Name);
        json.WriteString("applicationVersion", AppVersion.Current);
        json.WriteString("startedUtc", _started.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        json.WriteString("data", _data);

        json.WriteStartObject("machine");
        json.WriteString("cpuModel", _machine.CpuModel);
        json.WriteNumber("logicalCores", _machine.LogicalCores);
        json.WriteString("operatingSystem", _machine.OperatingSystem);
        json.WriteString("framework", _machine.Framework);
        json.WriteString("onnxRuntime", _machine.OnnxRuntime);
        if (_machine.CpuIdMask is null)
        {
            json.WriteNull("cpuIdMask");
        }
        else
        {
            json.WriteString("cpuIdMask", _machine.CpuIdMask);
        }

        json.WriteEndObject();

        json.WriteStartObject("settings");
        json.WriteNumber("threads", _threads);
        json.WriteBoolean("speakerSeparation", _speakers);
        json.WriteBoolean("speakersHintFromReference", _hinted);
        json.WriteNumber("diarisationCollarSeconds", SpeakerMetrics.CollarSeconds);
        json.WriteNumber("repeat", _repeat);
        json.WriteEndObject();

        json.WriteStartArray("groups");
        foreach (GroupResult group in Groups())
        {
            json.WriteStartObject();
            json.WriteString("group", group.Group);
            json.WriteNumber("files", group.Files);
            json.WriteNumber("failed", group.Failed);
            json.WriteNumber("audioSeconds", Seconds(group.AudioSeconds));
            json.WriteNumber("wallSeconds", Seconds(group.WallSeconds));
            json.WriteNumber("throughput", Round(group.Throughput, 2));
            WriteWer(json, "werRaw", group.RawWer);
            WriteWer(json, "werCleaned", group.CleanedWer);
            WriteRetention(json, "retention", group.Retention);
            json.WriteNumber("words", group.Words);
            json.WriteNumber("wordsRemoved", group.WordsRemoved);
            json.WriteNumber("removedFraction", Round(group.RemovedFraction, 4));
            WriteSpeakerGroup(json, "speakers", group.Speakers);
            WriteSpeakerGroup(json, "speakersHinted", group.HintedSpeakers);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartObject("retention");
        json.WriteNumber("gate", BenchmarkCommand.RetentionGate);
        foreach ((string language, ContentRetentionResult retention) in RetentionByLanguage())
        {
            WriteRetention(json, language, retention);
        }

        json.WriteEndObject();

        List<FileResult> noise = _files.Where(file => file.Entry.Type == "noise").ToList();
        json.WriteStartObject("noise");
        json.WriteNumber("files", noise.Count);
        json.WriteNumber("words", noise.Sum(file => file.Words));
        json.WriteNumber("gate", 0);
        json.WriteEndObject();

        json.WriteNumber("peakWorkingSetBytes", _files.Count > 0 ? _files.Max(file => file.PeakWorkingSetBytes) : 0);

        json.WriteStartArray("files");
        foreach (FileResult file in _files)
        {
            WriteFile(json, file);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteFile(Utf8JsonWriter json, FileResult file)
    {
        json.WriteStartObject();
        json.WriteString("id", file.Entry.Id);
        json.WriteString("group", file.Entry.Group);
        json.WriteString("type", file.Entry.Type);
        if (file.Entry.Language is null)
        {
            json.WriteNull("language");
        }
        else
        {
            json.WriteString("language", file.Entry.Language);
        }

        json.WriteString("outcome", file.Outcome.ToString());
        if (file.Outcome is not (TranscriptionOutcomeKind.Succeeded or TranscriptionOutcomeKind.NoSpeech))
        {
            json.WriteString("message", file.Message);
        }

        json.WriteNumber("audioSeconds", Seconds(file.AudioSeconds));
        json.WriteNumber("speechSeconds", Seconds(file.SpeechSeconds));
        json.WriteStartArray("wallSeconds");
        foreach (double wall in file.WallSeconds)
        {
            json.WriteNumberValue(Seconds(wall));
        }

        json.WriteEndArray();
        json.WriteNumber("throughput", Round(file.Throughput, 2));
        json.WriteNumber("chunks", file.Chunks);
        json.WriteNumber("forcedCuts", file.ForcedCuts);
        json.WriteNumber("segments", file.Segments);
        json.WriteNumber("speakers", file.Speakers);
        json.WriteNumber("words", file.Words);
        json.WriteNumber("wordsRemoved", file.WordsRemoved);
        json.WriteNumber("removedFraction", Round(file.RemovedFraction, 4));
        WriteWer(json, "werRaw", file.RawWer);
        WriteWer(json, "werCleaned", file.CleanedWer);
        WriteRetention(json, "retention", file.Retention);
        WriteSpeakerFile(json, "speakerScore", file.SpeakerScore);
        WriteSpeakerFile(json, "speakerScoreHinted", file.HintedSpeakers);

        json.WriteStartObject("stageSeconds");
        json.WriteNumber("modelLoad", Seconds(file.ModelLoadSeconds));
        if (file.Timings is PipelineTimings timings)
        {
            json.WriteNumber("pipeline", Seconds(timings.TotalSeconds));
            json.WriteNumber("streaming", Seconds(timings.StreamingSeconds));
            json.WriteNumber("decodeAndVad", Seconds(timings.ProducerSeconds));
            json.WriteNumber("recognitionWorkers", Seconds(timings.RecognitionSeconds));
            json.WriteNumber("speakers", Seconds(timings.SpeakerSeconds));
            json.WriteNumber("cleaning", Seconds(timings.CleaningSeconds));
        }

        json.WriteNumber("writing", Seconds(file.WriteSeconds));
        json.WriteEndObject();
        json.WriteNumber("peakWorkingSetBytes", file.PeakWorkingSetBytes);
        json.WriteEndObject();
    }

    private static void WriteWer(Utf8JsonWriter json, string name, WordErrorRateResult? result)
    {
        if (result is not WordErrorRateResult wer)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteNumber("wer", Round(wer.Wer, 4));
        json.WriteNumber("substitutions", wer.Substitutions);
        json.WriteNumber("deletions", wer.Deletions);
        json.WriteNumber("insertions", wer.Insertions);
        json.WriteNumber("referenceWords", wer.ReferenceLength);
        json.WriteEndObject();
    }

    private static void WriteSpeakerFile(Utf8JsonWriter json, string name, SpeakerMetrics? metrics)
    {
        if (metrics is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteNumber("referenceSpeakers", metrics.ReferenceSpeakers);
        json.WriteNumber("estimatedSpeakers", metrics.EstimatedSpeakers);
        json.WriteNumber("derOverlapExcluded", Round(metrics.Excluded.ErrorRate, 4));
        json.WriteNumber("derOverlapIncluded", Round(metrics.Included.ErrorRate, 4));
        json.WriteNumber("confusionOverlapExcluded", Round(metrics.Excluded.ReferenceSeconds > 0 ? metrics.Excluded.ConfusionSeconds / metrics.Excluded.ReferenceSeconds : 0, 4));
        json.WriteNumber("attributionAccuracy", Round(metrics.Attribution, 4));
        json.WriteNumber("referenceSeconds", Seconds(metrics.Included.ReferenceSeconds));
        json.WriteNumber("missedSeconds", Seconds(metrics.Included.MissedSeconds));
        json.WriteNumber("falseAlarmSeconds", Seconds(metrics.Included.FalseAlarmSeconds));
        json.WriteNumber("confusionSeconds", Seconds(metrics.Included.ConfusionSeconds));
        json.WriteEndObject();
    }

    private static void WriteSpeakerGroup(Utf8JsonWriter json, string name, SpeakerGroupMetrics? metrics)
    {
        if (metrics is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteNumber("files", metrics.Files);
        json.WriteNumber("derOverlapExcluded", Round(metrics.ErrorRateExcluded, 4));
        json.WriteNumber("derOverlapIncluded", Round(metrics.ErrorRateIncluded, 4));
        json.WriteNumber("confusionOverlapExcluded", Round(metrics.ConfusionExcluded, 4));
        json.WriteNumber("attributionAccuracy", Round(metrics.Attribution, 4));
        json.WriteNumber("speakerCountExact", metrics.ExactCounts);
        json.WriteNumber("speakerCountMeanAbsoluteError", Round(metrics.MeanAbsoluteCountError, 3));
        json.WriteEndObject();
    }

    private static void WriteRetention(Utf8JsonWriter json, string name, ContentRetentionResult? result)
    {
        if (result is not ContentRetentionResult retention)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteNumber("fraction", Round(retention.Fraction, 4));
        json.WriteNumber("retained", retention.Retained);
        json.WriteNumber("total", retention.Total);
        json.WriteEndObject();
    }

    private static double Seconds(double value) => Round(value, 3);

    private static double Round(double value, int decimals) => double.IsFinite(value) ? Math.Round(value, decimals, MidpointRounding.AwayFromZero) : 0;

    private static string Percent(double? value) => value is double v ? string.Create(CultureInfo.InvariantCulture, $"{v * 100:0.0}%") : "-";

    private static string Fraction(double? value) => value is double v ? string.Create(CultureInfo.InvariantCulture, $"{v:0.0000}") : "-";
}
