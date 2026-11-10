// Deep-tier measurements of speaker separation through the full pipeline (real VAD, real recogniser):
// the size of every estimated speaker, where the small ones come from, a sweep of the speaker floor,
// the two-stage check and the hint; each writes a table beside the test output.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Scoring;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

[Trait("Category", "diarisation")]
[Trait("Tier", "deep")]
[Collection(DiarisationSequence.Name)]
public sealed class PipelineDiarisationTuningTests
{
    /// <summary>Every file with reference speakers, the clips first and the two-hour file last.</summary>
    public static readonly string[] Ids =
    [
        "en-multi-01", "fr-multi-01", "fr-multi-02", "fr-multi-03", "de-multi-01", "de-multi-02", "de-multi-03",
        "en-ami-01", "en-ami-02", "en-ami-sdm-01",
        "en-10min", "fr-10min", "de-10min", "en-30min", "en-120min",
    ];

    private static List<PipelineRun> Runs()
    {
        string? skip = PipelineDiarisation.SkipReason();
        Assert.SkipWhen(skip is not null, skip ?? "");
        List<PipelineRun> runs = Ids.Select(PipelineDiarisation.Run).Where(r => r is not null).Select(r => r!).ToList();
        Assert.SkipWhen(runs.Count == 0, $"needs the benchmark audio in {DiarisationBench.DataDirectory}; run: node bench/testset/fetch-testset.mjs");
        return runs;
    }

    [Fact]
    public void SpeakerSizesAndTheirOrigin()
    {
        StringBuilder report = new();
        foreach (PipelineRun run in Runs())
        {
            // The floor is off, so the speakers it would fold are listed with their origin.
            PipelineScore score = PipelineDiarisation.Score(run, new DiarisationOptions { SpeakerFloorSeconds = 0, SpeakerFloorShare = 0 });
            report.AppendLine($"## {run.Id}: {score.EstimatedSpeakers} for {run.TrueSpeakers}, attribution {DiarisationBench.F(score.Attribution)}, speech {DiarisationBench.F(run.SpeechSeconds, 1)} s, {score.Assignment.WindowCount} windows");
            for (int s = 0; s < score.SpeakerSeconds.Count; s++)
            {
                report.AppendLine($"  speaker {s + 1}: {DiarisationBench.F(score.SpeakerSeconds[s].Seconds, 1)} s of words, {score.SpeakerSeconds[s].Runs} runs, {DiarisationBench.F(score.Assignment.SpeakingSeconds[s], 1)} s of turns");
            }

            // The turns of every timeline speaker whose words total under 15 s, and what the reference says there.
            SpeakerTimeline timeline = score.Assignment.Timeline;
            double[] wordSeconds = new double[timeline.SpeakerCount];
            List<int> timelineOfWord = [.. timeline.AssignWords(run.Words)];
            for (int i = 0; i < run.Words.Count; i++)
            {
                wordSeconds[timelineOfWord[i]] += run.Words[i].End - run.Words[i].Start;
            }

            for (int t = 0; t < timeline.SpeakerCount; t++)
            {
                if (wordSeconds[t] >= 15)
                {
                    continue;
                }

                report.AppendLine($"  small timeline speaker {t}: {DiarisationBench.F(wordSeconds[t], 1)} s of words");
                foreach (SpeakerTurn turn in timeline.Turns.Where(x => x.Speaker == t))
                {
                    (double Start, double End, double ChunkStart, double ChunkEnd) region = run.Regions.FirstOrDefault(r => r.Start <= turn.Start + 1e-6 && turn.End <= r.End + 1e-6);
                    string reference = string.Join(" ", run.Reference.Where(r => r.End > turn.Start && r.Start < turn.End).Select(r => $"{r.Speaker}[{DiarisationBench.F(r.Start, 2)}-{DiarisationBench.F(r.End, 2)}]"));
                    string words = string.Join(" ", run.Words.Where((w, i) => timelineOfWord[i] == t && w.Start >= turn.Start - 0.5 && w.End <= turn.End + 0.5).Select(w => w.Text));
                    report.AppendLine(
                        $"    turn {DiarisationBench.F(turn.Start, 2)}-{DiarisationBench.F(turn.End, 2)} ({DiarisationBench.F(turn.End - turn.Start, 2)} s) in region {DiarisationBench.F(region.Start, 2)}-{DiarisationBench.F(region.End, 2)} "
                        + $"({DiarisationBench.F(region.End - region.Start, 2)} s) of chunk {DiarisationBench.F(region.ChunkStart, 1)}-{DiarisationBench.F(region.ChunkEnd, 1)}; reference {reference}; words: {words}");
                }
            }
        }

        Write("diarisation-pipeline-sizes.txt", report.ToString());
    }

    [Fact]
    public void SpeakerFloorSweep()
    {
        List<PipelineRun> runs = Runs();
        StringBuilder report = new();
        report.AppendLine("floor s | share | " + string.Join(" | ", runs.Select(r => r.Id)) + " | exact | attribution S/A/L");
        foreach (double seconds in new[] { 0.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 12.0, 15.0 })
        {
            foreach (double share in new[] { 0.0, 0.005, 0.01, 0.015, 0.02, 0.03 })
            {
                DiarisationOptions options = new() { SpeakerFloorSeconds = seconds, SpeakerFloorShare = share };
                List<PipelineScore> scores = runs.Select(r => PipelineDiarisation.Score(r, options)).ToList();
                int exact = scores.Count(x => x.EstimatedSpeakers == x.Run.TrueSpeakers);
                string groups = string.Join(" / ", new[] { BenchGroup.Synthetic, BenchGroup.Ami, BenchGroup.Long }
                    .Select(g => scores.Where(x => x.Run.Group == g).ToList())
                    .Select(list => list.Count == 0 ? "-" : DiarisationBench.F(list.Average(x => x.Attribution))));
                report.AppendLine($"{seconds} | {share} | " + string.Join(" | ", scores.Select(x => $"{x.EstimatedSpeakers}/{x.Run.TrueSpeakers} {DiarisationBench.F(x.Attribution)}")) + $" | {exact} of {scores.Count} | {groups}");
            }
        }

        report.AppendLine();
        report.AppendLine("Direct clustering up to 4000 windows (the two-stage check), without and with the share:");
        foreach (PipelineRun run in runs)
        {
            foreach (double share in new[] { 0.0, 0.015 })
            {
                PipelineScore direct = PipelineDiarisation.Score(run, new DiarisationOptions { MaxDirectWindows = 4000, SpeakerFloorShare = share });
                report.AppendLine($"{run.Id} share {share}: {direct.EstimatedSpeakers}/{run.TrueSpeakers} {DiarisationBench.F(direct.Attribution)} sizes {string.Join(", ", direct.Assignment.SpeakingSeconds.Select(x => DiarisationBench.F(x, 1)))}");
            }
        }

        report.AppendLine();
        report.AppendLine("With the hint:");
        foreach (PipelineRun run in runs)
        {
            PipelineScore hinted = PipelineDiarisation.Score(run, new DiarisationOptions { SpeakerCountHint = run.TrueSpeakers });
            report.AppendLine($"{run.Id}: {hinted.EstimatedSpeakers}/{run.TrueSpeakers} {DiarisationBench.F(hinted.Attribution)}");
        }

        Write("diarisation-pipeline-floor.txt", report.ToString());
    }

    private static void Write(string name, string text)
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, name), text);
        TestContext.Current.TestOutputHelper?.WriteLine(text);
    }
}
