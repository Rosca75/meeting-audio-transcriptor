// Tests of what the command line prints and returns: exit codes per outcome, the progress line, durations and the summary.
using System;
using System.IO;
using System.Linq;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Cli.Benchmark;
using MeetingAudioTranscriptor.Engine.Pipeline;
using Xunit;

namespace MeetingAudioTranscriptor.Cli.Tests;

[Trait("Category", "config")]
public sealed class ConsoleOutputTests
{
    [Theory]
    [InlineData(TranscriptionOutcomeKind.Succeeded, 0)]
    [InlineData(TranscriptionOutcomeKind.NoSpeech, 0)]
    [InlineData(TranscriptionOutcomeKind.InputNotFound, 2)]
    [InlineData(TranscriptionOutcomeKind.InputUnreadable, 2)]
    [InlineData(TranscriptionOutcomeKind.InputUnsupported, 2)]
    [InlineData(TranscriptionOutcomeKind.InputCorrupt, 2)]
    [InlineData(TranscriptionOutcomeKind.ModelsMissing, 3)]
    [InlineData(TranscriptionOutcomeKind.OutputNotWritable, 4)]
    [InlineData(TranscriptionOutcomeKind.DiskFull, 4)]
    [InlineData(TranscriptionOutcomeKind.Unexpected, 5)]
    [InlineData(TranscriptionOutcomeKind.AlreadyRunning, 5)]
    [InlineData(TranscriptionOutcomeKind.Cancelled, 130)]
    public void EveryOutcomeHasItsExitCode(TranscriptionOutcomeKind kind, int code)
    {
        int got = ExitCodes.Of(kind);

        Assert.True(got == code, $"got exit code {got} for {kind}, wanted {code}");
    }

    [Fact]
    public void EveryOutcomeKindIsMapped()
    {
        foreach (TranscriptionOutcomeKind kind in Enum.GetValues<TranscriptionOutcomeKind>())
        {
            int code = ExitCodes.Of(kind);
            Assert.True(code is 0 or 2 or 3 or 4 or 5 or 130, $"got exit code {code} for {kind}");
        }
    }

    [Theory]
    [InlineData(TranscriptionStage.LoadingModels, 0.0, "Loading models         0%")]
    [InlineData(TranscriptionStage.Transcribing, 0.4267, "Transcribing          42%")]
    [InlineData(TranscriptionStage.IdentifyingSpeakers, 0.93, "Identifying speakers  93%")]
    [InlineData(TranscriptionStage.Writing, 1.0, "Writing              100%")]
    public void TheProgressLineShowsTheStageAndTheWholeRunsPercentage(TranscriptionStage stage, double fraction, string expected)
    {
        string got = ConsoleText.ProgressLine(new TranscriptionProgress(stage, 0.5, fraction));

        Assert.True(got == expected, $"got '{got}', wanted '{expected}'");
    }

    [Fact]
    public void ATerminalGetsOneUpdatingLine()
    {
        StringWriter writer = new();
        ProgressDisplay display = new(writer, terminal: true);

        display.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, 0, 0.10));
        display.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, 0, 0.10));
        display.Report(new TranscriptionProgress(TranscriptionStage.Cleaning, 0, 0.975));
        display.Finish();

        string text = writer.ToString();
        Assert.True(text.Count(c => c == '\r') == 2 && text.Count(c => c == '\n') == 1 && text.EndsWith("97%\n", StringComparison.Ordinal), $"got {text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}");
    }

    [Fact]
    public void ALogGetsALineEveryTenthAndEveryStage()
    {
        StringWriter writer = new();
        ProgressDisplay display = new(writer, terminal: false);

        for (int i = 0; i <= 100; i++)
        {
            display.Report(new TranscriptionProgress(TranscriptionStage.Transcribing, i / 100.0, i / 100.0 * 0.9));
        }

        display.Report(new TranscriptionProgress(TranscriptionStage.Writing, 0, 0.98));
        display.Finish();

        string[] lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length == 11 && lines[^1].StartsWith("Writing", StringComparison.Ordinal) && !writer.ToString().Contains('\r', StringComparison.Ordinal), $"got {lines.Length} lines: {string.Join(" | ", lines)}");
    }

    [Theory]
    [InlineData(45.24, "45.2 s")]
    [InlineData(185, "3 min 05 s")]
    [InlineData(3723, "1 h 02 min 03 s")]
    [InlineData(7200, "2 h 00 min 00 s")]
    [InlineData(-1, "unknown")]
    public void DurationsReadAsAPersonSaysThem(double seconds, string expected)
    {
        string got = ConsoleText.Duration(seconds);

        Assert.True(got == expected, $"got '{got}' for {seconds}, wanted '{expected}'");
    }

    [Fact]
    public void TheSummaryNamesTheFilesTheSpeedAndTheWords()
    {
        TranscriptionOutcome outcome = new()
        {
            Kind = TranscriptionOutcomeKind.Succeeded,
            Message = "",
            OutputFiles = ["/out/m.vtt", "/out/m.json"],
            AudioSeconds = 600,
            ProcessingSeconds = 60,
            SpeakerCount = 0,
            WordCount = 1500,
            CleanedWordCount = 1400,
            SegmentCount = 120,
        };

        string text = ConsoleText.Summary(outcome, speakersRequested: true);

        string[] expected = ["Transcript written:", "  /out/m.vtt", "  /out/m.json", "10 min 00 s", "1 min 00 s", "10.0 x real time", "not separated", "1500 recognised, 1400 after cleaning, in 120 segments"];
        foreach (string part in expected)
        {
            Assert.True(text.Contains(part, StringComparison.Ordinal), $"got summary:\n{text}\nwanted it to contain '{part}'");
        }

        Assert.True(!text.Contains('—', StringComparison.Ordinal), "the summary must not carry an em dash");
    }

    [Theory]
    [InlineData("en-ami-01", "clip", "en", "en-ami")]
    [InlineData("en-single-01", "clip", "en", "en-read")]
    [InlineData("en-10min", "long", "en", "en-read")]
    [InlineData("fr-multi-01", "clip", "fr", "fr")]
    [InlineData("de-10min", "long", "de", "de")]
    [InlineData("en-10min-m4a", "container", "en", "container")]
    [InlineData("noise-pink-60s", "noise", null, "noise")]
    public void BenchmarkFilesFallIntoTheirGroups(string id, string type, string? language, string group)
    {
        BenchEntry entry = new(id, type, language, "/x", null, null, 0);

        Assert.True(entry.Group == group, $"got {entry.Group} for {id}, wanted {group}");
    }

    [Fact]
    public void BenchmarkSetsSelectFromTheManifest()
    {
        BenchEntry[] entries =
        [
            new("en-single-01", "clip", "en", "/a", null, null, 95),
            new("en-10min", "long", "en", "/b", null, null, 603),
            new("en-30min", "long", "en", "/c", null, null, 1800),
            new("en-10min-m4a", "container", "en", "/d", null, null, 603),
            new("silence-60s", "noise", null, "/e", null, null, 60),
        ];

        string Ids(params string[] sets) => string.Join(",", BenchmarkManifest.Select(entries, sets).Select(e => e.Id));

        Assert.True(Ids("long") == "en-10min", $"got '{Ids("long")}' for long, wanted the ten-minute file only");
        Assert.True(Ids("noise", "clips") == "en-single-01,silence-60s", $"got '{Ids("noise", "clips")}', wanted manifest order");
        Assert.True(Ids("all").Split(',').Length == 5 && Ids("en-30min") == "en-30min", "all and an id should select");
        Exception? unknown = Record.Exception(() => BenchmarkManifest.Select(entries, ["bogus"]));
        Assert.True(unknown is ArgumentException && unknown.Message.Contains("matches nothing", StringComparison.Ordinal), $"got {unknown?.Message}");
    }
}
