// Sweeps of the diarisation settings over the benchmark files: linkage, threshold, refinement, minimum
// speaker size, window and hop, and the model's batch size; each writes a table beside the test output.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

[Trait("Category", "diarisation")]
[Trait("Tier", "deep")]
[Collection(DiarisationSequence.Name)]
public sealed class DiarisationTuningTests
{
    private static readonly BenchGroup[] Groups = [BenchGroup.Synthetic, BenchGroup.Ami, BenchGroup.Long];

    [Fact]
    public void ClusteringSettingsSweep()
    {
        Assert.SkipWhen(DiarisationEvaluation.SkipReason() is not null, DiarisationEvaluation.SkipReason() ?? "");
        List<EmbeddedFile> files = DiarisationEvaluation.Embedded();
        StringBuilder report = new();
        report.AppendLine("linkage | threshold | refine | minSpk | count exact (S/A/L) | attribution (S/A/L) | DER excl (S/A/L)");
        foreach (SpeakerLinkage linkage in new[] { SpeakerLinkage.Average, SpeakerLinkage.Complete })
        {
            foreach (int refine in new[] { 0, 2 })
            {
                foreach (double minimum in new[] { 0.0, 2.0, 4.0, 8.0 })
                {
                    for (double threshold = 0.4; threshold <= 0.901; threshold += 0.025)
                    {
                        DiarisationOptions options = new() { Linkage = linkage, Threshold = threshold, RefinementPasses = refine, MinimumSpeakerSeconds = minimum };
                        List<EvaluationRow> rows = files.Select(f => DiarisationEvaluation.Evaluate(f, options, withHint: false)).ToList();
                        report.AppendLine($"{linkage} | {DiarisationBench.F(threshold, 3)} | {refine} | {minimum} | {Summary(rows)}");
                    }
                }

                DiarisationOptions hinted = new() { Linkage = linkage, RefinementPasses = refine };
                List<EvaluationRow> hintRows = files.Select(f => DiarisationEvaluation.Evaluate(f, hinted, withHint: true)).ToList();
                report.AppendLine($"{linkage} | hint | {refine} | 4 | {Summary(hintRows)}");
            }
        }

        Write("diarisation-sweep-clustering.txt", report.ToString());
    }

    [Fact]
    public void WindowAndHopSweep()
    {
        Assert.SkipWhen(DiarisationEvaluation.SkipReason() is not null, DiarisationEvaluation.SkipReason() ?? "");
        StringBuilder report = new();
        report.AppendLine("window | hop | cpu s per audio hour | thr | count exact (S/A/L) | attribution (S/A/L) | DER excl (S/A/L)");
        (double Window, double Hop)[] settings =
        [
            (1.0, 0.5), (1.5, 0.75), (1.5, 1.0), (1.5, 1.5), (2.0, 1.0), (2.0, 1.5), (2.0, 2.0),
            (2.5, 1.5), (2.5, 2.0), (3.0, 1.5), (3.0, 2.0), (3.0, 3.0), (4.0, 2.0),
        ];
        foreach ((double window, double hop) in settings)
        {
            List<EmbeddedFile> files = DiarisationEvaluation.Embedded(window, hop);
            double cpuPerHour = files.Sum(f => f.EmbeddingCpuSeconds) / files.Sum(f => f.AudioSeconds) * 3600;
            foreach (double threshold in new[] { 0.45, 0.5, 0.55, 0.6, 0.65, 0.7 })
            {
                DiarisationOptions options = new() { WindowSeconds = window, HopSeconds = hop, Threshold = threshold };
                List<EvaluationRow> rows = files.Select(f => DiarisationEvaluation.Evaluate(f, options, withHint: false)).ToList();
                report.AppendLine($"{window} | {hop} | {DiarisationBench.F(cpuPerHour, 1)} | {threshold} | {Summary(rows)}");
            }

            DiarisationOptions hinted = new() { WindowSeconds = window, HopSeconds = hop };
            List<EvaluationRow> hintRows = files.Select(f => DiarisationEvaluation.Evaluate(f, hinted, withHint: true)).ToList();
            report.AppendLine($"{window} | {hop} | {DiarisationBench.F(cpuPerHour, 1)} | hint | {Summary(hintRows)}");
        }

        Write("diarisation-sweep-window.txt", report.ToString());
    }

    [Fact]
    public void TwoStageClusteringOnTheThirtyMinuteFile()
    {
        Assert.SkipWhen(DiarisationEvaluation.SkipReason() is not null, DiarisationEvaluation.SkipReason() ?? "");
        string wav = Path.Combine(DiarisationBench.DataDirectory, "long", "en-30min.wav");
        Assert.SkipUnless(File.Exists(wav), $"needs {wav}; run: node bench/testset/fetch-testset.mjs");
        BenchFile file = new("en-30min", BenchGroup.Long, wav, Path.ChangeExtension(wav, ".rttm"));
        DiarisationOptions defaults = new();
        EmbeddedFile embedded = DiarisationEvaluation.Embed(file, defaults);

        StringBuilder report = new();
        report.AppendLine("max direct windows | windows | speakers est/true | DER excl | attribution | clustering CPU s");
        foreach (int maxDirect in new[] { 8000, 2000, 1000, 500, 250 })
        {
            foreach (bool hint in new[] { false, true })
            {
                EvaluationRow row = DiarisationEvaluation.Evaluate(embedded, defaults with { MaxDirectWindows = maxDirect }, hint);
                report.AppendLine($"{maxDirect}{(hint ? " (hint)" : "")} | {embedded.Collector.WindowCount} | {row.EstimatedSpeakers}/{row.TrueSpeakers} | {DiarisationBench.F(row.DerOverlapExcluded)} | {DiarisationBench.F(row.Attribution)} | {DiarisationBench.F(row.ClusteringCpuSeconds)}");
            }
        }

        report.AppendLine($"embedding CPU {DiarisationBench.F(embedded.EmbeddingCpuSeconds, 1)} s for {DiarisationBench.F(embedded.AudioSeconds, 0)} s of audio");
        Write("diarisation-two-stage.txt", report.ToString());
    }

    [Fact]
    public void BatchSizeCost()
    {
        string? model = DiarisationBench.EmbeddingModel();
        Assert.SkipWhen(model is null, "needs the wespeaker-campplus-lm model; run node build/fetch-models.mjs and set MAT_MODELS_DIR");
        using ITensorRunner runner = OrtRunnerFactory.Open(model!, new RunnerOptions());
        float[] samples = new float[16000 * 60];
        uint state = 7;
        for (int i = 0; i < samples.Length; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            samples[i] = (float)((0.2 * Math.Sin(i * 0.05)) + (((state >> 8) / 16777216.0) - 0.5) * 0.1);
        }

        StringBuilder report = new();
        report.AppendLine("batch | ms per window of the default length (thread CPU, best of 6)");
        foreach (int batch in new[] { 1, 4, 8, 16 })
        {
            SpeakerEmbedder embedder = new(runner, batch);
            DiarisationOptions options = new() { BatchSize = batch };
            double best = double.MaxValue;
            int windows = 0;
            for (int repeat = 0; repeat < 7; repeat++)
            {
                DiarisationCollector collector = new(embedder, options);
                double before = ThreadClock.Seconds();
                collector.AddBlock(samples, 0, [(0, samples.Length)], CancellationToken.None);
                double cpu = ThreadClock.Seconds() - before;
                windows = collector.WindowCount;
                if (repeat > 0)
                {
                    best = Math.Min(best, cpu);
                }
            }

            report.AppendLine($"{batch} | {DiarisationBench.F(best / windows * 1000, 2)}");
        }

        Write("diarisation-batch.txt", report.ToString());
    }

    private static string Summary(List<EvaluationRow> rows)
    {
        string exact = string.Join("/", Groups.Select(g => $"{rows.Count(r => r.File.Group == g && r.EstimatedSpeakers == r.TrueSpeakers)}of{rows.Count(r => r.File.Group == g)}"));
        string attribution = string.Join("/", Groups.Select(g => DiarisationBench.F(Mean(rows, g, r => r.Attribution))));
        string der = string.Join("/", Groups.Select(g => DiarisationBench.F(Mean(rows, g, r => r.DerOverlapExcluded))));
        string counts = string.Join(" ", rows.Select(r => $"{r.EstimatedSpeakers}/{r.TrueSpeakers}"));
        return $"{exact} | {attribution} | {der} | {counts}";
    }

    private static double Mean(List<EvaluationRow> rows, BenchGroup group, Func<EvaluationRow, double> value)
    {
        List<EvaluationRow> selected = rows.Where(r => r.File.Group == group).ToList();
        return selected.Count == 0 ? double.NaN : selected.Average(value);
    }

    private static void Write(string name, string text)
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, name), text);
        TestContext.Current.TestOutputHelper?.WriteLine(text);
    }
}
