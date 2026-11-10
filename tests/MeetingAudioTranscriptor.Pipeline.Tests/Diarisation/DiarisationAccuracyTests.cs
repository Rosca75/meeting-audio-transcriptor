// Models-tier evaluation of speaker separation on the benchmark files with the reference speech
// regions: speaker count, DER and attribution accuracy per group, with and without the count hint,
// and the single-core cost per hour of audio; the full table is written beside the test output.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MeetingAudioTranscriptor.Engine.Diarisation;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

[Trait("Category", "diarisation")]
[Trait("Tier", "models")]
[Collection(DiarisationSequence.Name)]
public sealed class DiarisationAccuracyTests
{
    private static List<EmbeddedFile> Files()
    {
        string? skip = DiarisationEvaluation.SkipReason();
        Assert.SkipWhen(skip is not null, skip ?? "");
        return DiarisationEvaluation.Embedded();
    }

    private static List<EvaluationRow> Rows(BenchGroup group, bool withHint)
    {
        List<EvaluationRow> rows = Files().Where(f => f.File.Group == group).Select(f => DiarisationEvaluation.Evaluate(f, new DiarisationOptions(), withHint)).ToList();
        Assert.SkipWhen(rows.Count == 0, $"needs the {group} files of the benchmark set in {DiarisationBench.DataDirectory}");
        return rows;
    }

    private static string Show(EvaluationRow r) =>
        $"{r.File.Id}: {r.EstimatedSpeakers}/{r.TrueSpeakers} speakers, attribution {DiarisationBench.F(r.Attribution)}, DER {DiarisationBench.F(r.DerOverlapExcluded)}";

    [Fact]
    public void SyntheticClipsWithTheHintFindEverySpeaker()
    {
        foreach (EvaluationRow row in Rows(BenchGroup.Synthetic, withHint: true))
        {
            Assert.True(row.EstimatedSpeakers == row.TrueSpeakers && row.Attribution >= 0.9, $"got {Show(row)}; wanted the true count and attribution at least 0.9");
        }
    }

    [Fact]
    public void SyntheticClipsWithoutTheHintFindEverySpeaker()
    {
        foreach (EvaluationRow row in Rows(BenchGroup.Synthetic, withHint: false))
        {
            Assert.True(row.EstimatedSpeakers == row.TrueSpeakers && row.Attribution >= 0.9, $"got {Show(row)}; wanted the true count and attribution at least 0.9");
        }
    }

    [Fact]
    public void LongFilesKeepTheirSpeakersApart()
    {
        foreach (bool hint in new[] { true, false })
        {
            foreach (EvaluationRow row in Rows(BenchGroup.Long, hint))
            {
                Assert.True(
                    Math.Abs(row.EstimatedSpeakers - row.TrueSpeakers) <= (hint ? 0 : 1) && row.Attribution >= 0.95,
                    $"got {Show(row)} (hint {hint}); wanted the count within {(hint ? 0 : 1)} and attribution at least 0.95");
            }
        }
    }

    [Fact]
    public void AmiMeetingsAreMostlyAttributedToTheRightSpeaker()
    {
        foreach (bool hint in new[] { true, false })
        {
            List<EvaluationRow> rows = Rows(BenchGroup.Ami, hint);
            double mean = rows.Average(r => r.Attribution);
            Assert.True(mean >= 0.8, $"got mean attribution {DiarisationBench.F(mean)} over [{string.Join("; ", rows.Select(Show))}] (hint {hint}); wanted at least 0.8");
        }
    }

    [Fact]
    public void TheFullTableIsWritten()
    {
        List<EmbeddedFile> files = Files();
        StringBuilder table = new();
        table.AppendLine("| File | Group | Audio s | Speech s | Speakers true | est. (no hint) | DER excl. overlap | DER incl. overlap | Attribution | est. (hint) | DER excl. (hint) | DER incl. (hint) | Attribution (hint) | Embedding CPU s | Clustering CPU s |");
        table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (EmbeddedFile file in files)
        {
            EvaluationRow free = DiarisationEvaluation.Evaluate(file, new DiarisationOptions(), withHint: false);
            EvaluationRow hinted = DiarisationEvaluation.Evaluate(file, new DiarisationOptions(), withHint: true);
            table.AppendLine(
                $"| {file.File.Id} | {file.File.Group} | {DiarisationBench.F(file.AudioSeconds, 1)} | {DiarisationBench.F(file.SpeechSeconds, 1)} | {free.TrueSpeakers} | {free.EstimatedSpeakers} | "
                + $"{DiarisationBench.F(free.DerOverlapExcluded)} | {DiarisationBench.F(free.DerOverlapIncluded)} | {DiarisationBench.F(free.Attribution)} | {hinted.EstimatedSpeakers} | "
                + $"{DiarisationBench.F(hinted.DerOverlapExcluded)} | {DiarisationBench.F(hinted.DerOverlapIncluded)} | {DiarisationBench.F(hinted.Attribution)} | "
                + $"{DiarisationBench.F(file.EmbeddingCpuSeconds, 2)} | {DiarisationBench.F(free.ClusteringCpuSeconds, 3)} |");
        }

        double audio = files.Sum(f => f.AudioSeconds);
        double speech = files.Sum(f => f.SpeechSeconds);
        double embedding = files.Sum(f => f.EmbeddingCpuSeconds);
        table.AppendLine();
        table.AppendLine($"Embedding CPU: {DiarisationBench.F(embedding / audio * 3600, 1)} s per hour of audio, {DiarisationBench.F(embedding / speech * 3600, 1)} s per hour of speech ({DiarisationBench.F(speech / audio * 100, 1)} % speech).");
        string text = table.ToString();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diarisation-table.md"), text);
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        Assert.True(files.Count > 0, "no benchmark file was evaluated");
    }
}
