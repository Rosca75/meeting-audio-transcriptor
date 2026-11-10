// The benchmark's measurements per file and per group: word error rates, cleaning retention, noise words, speed and memory.
using System;
using System.Collections.Generic;
using System.Linq;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Scoring;

namespace MeetingAudioTranscriptor.Cli.Benchmark;

/// <summary>What one file measured.</summary>
internal sealed record FileResult
{
    public required BenchEntry Entry { get; init; }

    public required TranscriptionOutcomeKind Outcome { get; init; }

    public required string Message { get; init; }

    public double AudioSeconds { get; init; }

    public double SpeechSeconds { get; init; }

    /// <summary>The end-to-end wall time of each repeat: decoding, every stage and writing to a temporary folder.</summary>
    public IReadOnlyList<double> WallSeconds { get; init; } = [];

    public int Segments { get; init; }

    public int Speakers { get; init; }

    /// <summary>The words the recogniser produced.</summary>
    public int Words { get; init; }

    /// <summary>The words cleaning removed.</summary>
    public int WordsRemoved { get; init; }

    public int Chunks { get; init; }

    public int ForcedCuts { get; init; }

    /// <summary>The raw transcript against the reference; null without a reference.</summary>
    public WordErrorRateResult? RawWer { get; init; }

    /// <summary>The cleaned transcript against the reference; null without a reference.</summary>
    public WordErrorRateResult? CleanedWer { get; init; }

    /// <summary>The content words of the raw transcript that cleaning kept; null without a language.</summary>
    public ContentRetentionResult? Retention { get; init; }

    public PipelineTimings? Timings { get; init; }

    public double ModelLoadSeconds { get; init; }

    public double WriteSeconds { get; init; }

    /// <summary>The process's peak working set after this file, in bytes; the peak of the whole process so far.</summary>
    public long PeakWorkingSetBytes { get; init; }

    // Extension point: speaker metrics (the diarisation error rate against Entry.Rttm and the
    // speaker count error) are computed here once speaker separation is part of the pipeline.

    /// <summary>The median wall time over the repeats.</summary>
    public double MedianWallSeconds => Median(WallSeconds);

    /// <summary>Audio seconds per wall second, end to end.</summary>
    public double Throughput => MedianWallSeconds > 0 ? AudioSeconds / MedianWallSeconds : 0;

    /// <summary>The share of the recognised words cleaning removed.</summary>
    public double RemovedFraction => Words > 0 ? WordsRemoved / (double)Words : 0;

    /// <summary>Measures one run's transcript against the entry's reference.</summary>
    public static FileResult From(BenchEntry entry, TranscriptionOutcome outcome, IReadOnlyList<double> wallSeconds, string? reference)
    {
        TranscriptDocument? document = outcome.Pipeline?.Document;
        string raw = document is null ? "" : string.Join(' ', document.Segments.Select(segment => segment.RawText));
        string cleaned = document is null ? "" : string.Join(' ', document.Segments.Where(segment => segment.Text.Length > 0).Select(segment => segment.Text));
        int words = document?.Segments.Sum(segment => segment.Words?.Count ?? 0) ?? 0;
        int removed = document?.Segments.Sum(segment => segment.Words?.Count(word => word.Removed == true) ?? 0) ?? 0;
        bool scored = outcome.Succeeded && reference is not null;
        bool hasLanguage = outcome.Succeeded && entry.Language is not null && CleaningLanguages.TryParse(entry.Language, out _);
        return new FileResult
        {
            Entry = entry,
            Outcome = outcome.Kind,
            Message = outcome.Message,
            AudioSeconds = outcome.AudioSeconds,
            SpeechSeconds = outcome.SpeechSeconds,
            WallSeconds = wallSeconds,
            Segments = outcome.SegmentCount,
            Speakers = outcome.SpeakerCount,
            Words = words,
            WordsRemoved = removed,
            Chunks = outcome.Pipeline?.ChunkCount ?? 0,
            ForcedCuts = outcome.Pipeline?.ForcedCuts ?? 0,
            RawWer = scored ? WordErrorRate.Compute(TextNormalizer.Normalize(reference), TextNormalizer.Normalize(raw)) : null,
            CleanedWer = scored ? WordErrorRate.Compute(TextNormalizer.Normalize(reference), TextNormalizer.Normalize(cleaned)) : null,
            Retention = hasLanguage ? ContentRetention.Compute(CleaningLanguages.Parse(entry.Language!), raw, cleaned) : null,
            Timings = outcome.Pipeline?.Timings,
            ModelLoadSeconds = outcome.ModelLoadSeconds,
            WriteSeconds = outcome.WriteSeconds,
            PeakWorkingSetBytes = outcome.PeakWorkingSetBytes,
        };
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.Order().ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

/// <summary>The figures of a group of files, micro-averaged: every word counts once whichever file it is in.</summary>
internal sealed record GroupResult(
    string Group,
    int Files,
    int Failed,
    double AudioSeconds,
    double WallSeconds,
    WordErrorRateResult? RawWer,
    WordErrorRateResult? CleanedWer,
    ContentRetentionResult? Retention,
    int Words,
    int WordsRemoved)
{
    public double Throughput => WallSeconds > 0 ? AudioSeconds / WallSeconds : 0;

    public double RemovedFraction => Words > 0 ? WordsRemoved / (double)Words : 0;

    /// <summary>Sums the files' counts.</summary>
    public static GroupResult Of(string group, IReadOnlyList<FileResult> files)
    {
        List<FileResult> scored = files.Where(file => file.RawWer is not null).ToList();
        List<FileResult> retained = files.Where(file => file.Retention is not null).ToList();
        return new GroupResult(
            group,
            files.Count,
            files.Count(file => file.Outcome is not (TranscriptionOutcomeKind.Succeeded or TranscriptionOutcomeKind.NoSpeech)),
            files.Sum(file => file.AudioSeconds),
            files.Sum(file => file.MedianWallSeconds),
            scored.Count > 0 ? WordErrorRate.Aggregate(scored.Select(file => file.RawWer!.Value)) : null,
            scored.Count > 0 ? WordErrorRate.Aggregate(scored.Select(file => file.CleanedWer!.Value)) : null,
            retained.Count > 0 ? ContentRetention.Aggregate(retained.Select(file => file.Retention!.Value)) : null,
            files.Sum(file => file.Words),
            files.Sum(file => file.WordsRemoved));
    }
}
