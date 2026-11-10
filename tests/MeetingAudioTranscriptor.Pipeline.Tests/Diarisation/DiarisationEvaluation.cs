// The embeddings of every benchmark file, computed once per test run, and the scoring of one
// clustering setting over them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Inference;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

/// <summary>One benchmark file with its embeddings collected and what that cost.</summary>
internal sealed record EmbeddedFile(BenchFile File, double AudioSeconds, double SpeechSeconds, double EmbeddingCpuSeconds, DiarisationCollector Collector, List<SpeakerSegment> Reference);

/// <summary>The score of one file under one setting.</summary>
internal sealed record EvaluationRow(
    BenchFile File,
    int TrueSpeakers,
    int EstimatedSpeakers,
    double DerOverlapExcluded,
    double DerOverlapIncluded,
    double Attribution,
    double ClusteringCpuSeconds);

/// <summary>Embeds the benchmark files once and scores clustering settings over them.</summary>
/// <remarks>
/// The speech regions are the reference's own (the union of the RTTM segments), so the scores
/// measure speaker separation alone and not voice activity detection.
/// </remarks>
internal static class DiarisationEvaluation
{
    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<(double Window, double Hop, int Batch), List<EmbeddedFile>> Cache = [];

    /// <summary>Why the evaluation cannot run here, or null when it can.</summary>
    public static string? SkipReason()
    {
        if (DiarisationBench.EmbeddingModel() is null)
        {
            return $"needs the wespeaker-campplus-lm model in {ModelCatalog.ResolveRoot()}; run: node build/fetch-models.mjs --into <models folder> and set MAT_MODELS_DIR";
        }

        if (DiarisationBench.Files().Count == 0)
        {
            return $"needs the benchmark audio in {DiarisationBench.DataDirectory}; run: node bench/testset/fetch-testset.mjs, or set {DiarisationBench.DataVariable}";
        }

        return null;
    }
    /// <summary>The embedded files under the default window, hop and batch size.</summary>
    /// <summary>The embedded files for a window and hop, computed on first use and kept for the run.</summary>
    public static List<EmbeddedFile> Embedded() => Embedded(new DiarisationOptions().WindowSeconds, new DiarisationOptions().HopSeconds, new DiarisationOptions().BatchSize);

    /// <summary>The embedded files for a window, hop and batch size, computed on first use and kept for the run.</summary>
    public static List<EmbeddedFile> Embedded(double windowSeconds, double hopSeconds, int batchSize = 8)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue((windowSeconds, hopSeconds, batchSize), out List<EmbeddedFile>? cached))
            {
                return cached;
            }

            DiarisationOptions options = new() { WindowSeconds = windowSeconds, HopSeconds = hopSeconds, BatchSize = batchSize };
            List<EmbeddedFile> files = DiarisationBench.Files().Select(file => Embed(file, options)).ToList();
            Cache[(windowSeconds, hopSeconds, batchSize)] = files;
            return files;
        }
    }

    /// <summary>Embeds one file with the reference speech regions, timing the embedding on this thread.</summary>
    public static EmbeddedFile Embed(BenchFile file, DiarisationOptions options)
    {
        using ITensorRunner runner = OrtRunnerFactory.Open(DiarisationBench.EmbeddingModel()!, new RunnerOptions());
        SpeakerEmbedder embedder = new(runner, options.BatchSize);
        float[] samples = DiarisationBench.ReadWav(file.WavPath);
        List<SpeakerSegment> reference = DiarisationRttm.Parse(File.ReadAllText(file.RttmPath));
        List<(long Start, long End)> regions = DiarisationBench.SpeechRegions(reference, 16000, samples.Length);
        DiarisationCollector collector = new(embedder, options);

        // One untimed call first, so the model's first-run set-up is not counted as the file's cost.
        embedder.Embed(samples.AsSpan(0, Math.Min(samples.Length, 24000)), CancellationToken.None);
        double before = ThreadClock.Seconds();
        DiarisationBench.Feed(collector, samples, regions, CancellationToken.None);
        double cpu = ThreadClock.Seconds() - before;
        double speech = regions.Sum(r => (r.End - r.Start) / 16000.0);
        return new EmbeddedFile(file, samples.Length / 16000.0, speech, cpu, collector, reference);
    }

    /// <summary>Clusters one embedded file with the given options and scores it.</summary>
    public static EvaluationRow Evaluate(EmbeddedFile file, DiarisationOptions options, bool withHint)
    {
        int trueSpeakers = file.Reference.Select(s => s.Speaker).Distinct(StringComparer.Ordinal).Count();
        DiarisationOptions settings = options with { SpeakerCountHint = withHint ? trueSpeakers : null };
        double before = ThreadClock.Seconds();
        DiarisationResult result = file.Collector.Finish(settings, CancellationToken.None);
        double cpu = ThreadClock.Seconds() - before;
        List<SpeakerSegment> hypothesis = DiarisationBench.Hypothesis(result.Timeline);
        DiarisationScore excluded = DiarisationErrorRate.Compute(file.Reference, hypothesis, new DiarisationScoringOptions(0.25, ExcludeOverlap: true));
        DiarisationScore included = DiarisationErrorRate.Compute(file.Reference, hypothesis, new DiarisationScoringOptions(0.25, ExcludeOverlap: false));
        return new EvaluationRow(file.File, trueSpeakers, result.Timeline.SpeakerCount, excluded.ErrorRate, included.ErrorRate, excluded.AttributionAccuracy, cpu);
    }
}
