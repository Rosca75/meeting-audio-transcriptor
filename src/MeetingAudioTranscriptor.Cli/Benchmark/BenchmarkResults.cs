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

    /// <summary>The speakers against the entry's RTTM; null without an RTTM or without speaker separation.</summary>
    public SpeakerMetrics? SpeakerScore { get; init; }

    /// <summary>The speakers of an extra run given the reference's speaker count as the hint; null unless asked for.</summary>
    public SpeakerMetrics? HintedSpeakers { get; init; }

    /// <summary>The median wall time over the repeats.</summary>
    public double MedianWallSeconds => Median(WallSeconds);

    /// <summary>Audio seconds per wall second, end to end.</summary>
    public double Throughput => MedianWallSeconds > 0 ? AudioSeconds / MedianWallSeconds : 0;

    /// <summary>The share of the recognised words cleaning removed.</summary>
    public double RemovedFraction => Words > 0 ? WordsRemoved / (double)Words : 0;

    /// <summary>Measures one run's transcript against the entry's reference.</summary>
    public static FileResult From(BenchEntry entry, TranscriptionOutcome outcome, IReadOnlyList<double> wallSeconds, string? reference, string? rttm = null)
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
            SpeakerScore = outcome.Succeeded && rttm is not null && document is not null ? SpeakerMetrics.Compute(rttm, document) : null,
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
    int WordsRemoved,
    SpeakerGroupMetrics? Speakers = null,
    SpeakerGroupMetrics? HintedSpeakers = null)
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
            files.Sum(file => file.WordsRemoved),
            SpeakerGroupMetrics.Of(files.Select(file => file.SpeakerScore)),
            SpeakerGroupMetrics.Of(files.Select(file => file.HintedSpeakers)));
    }
}

/// <summary>How well one file's speakers were told apart, against its reference turns.</summary>
/// <param name="Excluded">The score with overlapped reference speech left out, collar 0.25 s.</param>
/// <param name="Included">The score with overlapped reference speech counted, collar 0.25 s.</param>
/// <param name="ReferenceSpeakers">The speakers the reference names.</param>
/// <param name="EstimatedSpeakers">The speakers the transcript names.</param>
internal sealed record SpeakerMetrics(DiarisationScore Excluded, DiarisationScore Included, int ReferenceSpeakers, int EstimatedSpeakers)
{
    /// <summary>The collar around every reference boundary that is not scored, in seconds.</summary>
    public const double CollarSeconds = 0.25;

    /// <summary>The fraction of single-speaker reference time shown with the right speaker.</summary>
    public double Attribution => Excluded.AttributionAccuracy;

    /// <summary>
    /// Scores a transcript's speakers: the hypothesis turns are the transcript's segments, each from
    /// its first word's start to its last word's end under its speaker, removed segments included
    /// because their words were spoken; null when the transcript names no speaker.
    /// </summary>
    public static SpeakerMetrics? Compute(string rttmText, TranscriptDocument document)
    {
        if (document.Speakers.Count == 0)
        {
            return null;
        }

        List<SpeakerSegment> reference = DiarisationRttm.Parse(rttmText);
        List<SpeakerSegment> hypothesis = document.Segments
            .Where(segment => segment.Speaker is not null && segment.End > segment.Start)
            .Select(segment => new SpeakerSegment(segment.Speaker!, segment.Start, segment.End))
            .ToList();
        DiarisationScore excluded = DiarisationErrorRate.Compute(reference, hypothesis, new DiarisationScoringOptions(CollarSeconds, ExcludeOverlap: true));
        DiarisationScore included = DiarisationErrorRate.Compute(reference, hypothesis, new DiarisationScoringOptions(CollarSeconds, ExcludeOverlap: false));
        return new SpeakerMetrics(excluded, included, excluded.ReferenceSpeakers, document.Speakers.Count);
    }
}

/// <summary>The speaker figures of a group: error rates over the summed times, attribution weighted by each file's scored time.</summary>
/// <remarks>
/// The hypothesis turns are segment spans, which stop at the first and last word and leave pauses
/// out, so most of the error rate is missed speech; <see cref="ConfusionExcluded"/> is the share of
/// the scored time given to the wrong speaker, which is what speaker separation alone decides.
/// </remarks>
internal sealed record SpeakerGroupMetrics(int Files, double ErrorRateExcluded, double ErrorRateIncluded, double ConfusionExcluded, double Attribution, int ExactCounts, double MeanAbsoluteCountError)
{
    /// <summary>The group's figures; null when no file of it was scored.</summary>
    public static SpeakerGroupMetrics? Of(IEnumerable<SpeakerMetrics?> metrics)
    {
        List<SpeakerMetrics> scored = metrics.Where(metric => metric is not null).Select(metric => metric!).ToList();
        if (scored.Count == 0)
        {
            return null;
        }

        static double Errors(DiarisationScore score) => score.MissedSeconds + score.FalseAlarmSeconds + score.ConfusionSeconds;
        double excludedReference = scored.Sum(metric => metric.Excluded.ReferenceSeconds);
        double includedReference = scored.Sum(metric => metric.Included.ReferenceSeconds);
        return new SpeakerGroupMetrics(
            scored.Count,
            excludedReference > 0 ? scored.Sum(metric => Errors(metric.Excluded)) / excludedReference : 0,
            includedReference > 0 ? scored.Sum(metric => Errors(metric.Included)) / includedReference : 0,
            excludedReference > 0 ? scored.Sum(metric => metric.Excluded.ConfusionSeconds) / excludedReference : 0,
            excludedReference > 0 ? scored.Sum(metric => metric.Attribution * metric.Excluded.ReferenceSeconds) / excludedReference : 0,
            scored.Count(metric => metric.EstimatedSpeakers == metric.ReferenceSpeakers),
            scored.Average(metric => (double)Math.Abs(metric.EstimatedSpeakers - metric.ReferenceSpeakers)));
    }
}
