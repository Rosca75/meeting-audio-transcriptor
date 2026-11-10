// Table tests of the command-line parser: every option, bad values, a missing file, unknown options and help.
using System;
using System.Linq;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;

namespace MeetingAudioTranscriptor.Cli.Tests;

[Trait("Category", "config")]
public sealed class CommandLineTests
{
    private static ParsedCommandLine Parse(string line) => CommandLine.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void TheSpeakerCountRunsFromOneToTen(int count)
    {
        ParsedCommandLine parsed = Parse($"transcribe a.wav --language en --speakers {count}");

        Assert.True(parsed.Kind == CommandKind.Transcribe && parsed.Transcribe!.Speakers == count, $"got {parsed.Kind}: {parsed.Error}, wanted a count of {count}");
    }

    [Fact]
    public void TranscribeReadsEveryOption()
    {
        ParsedCommandLine parsed = Parse("transcribe meeting.m4a --language FR --formats json,txt --output out --speakers 3 --threads 2 --raw --models m --quiet");

        TranscribeArguments? t = parsed.Transcribe;
        Assert.True(parsed.Kind == CommandKind.Transcribe && t is not null, $"got {parsed.Kind}: {parsed.Error}");
        Assert.True(t.File == "meeting.m4a" && t.Language == "fr" && t.Output == "out" && t.Models == "m", $"got {t}");
        Assert.True(t.Formats.SequenceEqual([TranscriptFormat.Json, TranscriptFormat.Txt]) && t.Speakers == 3 && t.Threads == 2 && t.Raw && t.Quiet && !t.NoSpeakers, $"got {t}");
    }

    [Fact]
    public void TranscribeDefaultsToAllFormatsAndNoOptions()
    {
        ParsedCommandLine parsed = Parse("transcribe a.wav --language=de");

        TranscribeArguments t = parsed.Transcribe!;
        Assert.True(t.Formats.SequenceEqual([TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Txt]), $"got formats {string.Join(",", t.Formats)}");
        Assert.True(t.Language == "de" && t.Output is null && t.Threads is null && t.Speakers is null && !t.Raw && !t.Quiet, $"got {t}");
        Assert.True(TranscribeCommand.Request(t).SpeakerSeparation is null, "speaker separation should default to the service's choice");
    }

    [Fact]
    public void NoSpeakersSwitchesSeparationOff()
    {
        TranscribeArguments t = Parse("transcribe a.wav --no-speakers --language en").Transcribe!;

        Assert.True(t.NoSpeakers && TranscribeCommand.Request(t).SpeakerSeparation == false, $"got {t}");
    }

    [Theory]
    [InlineData("transcribe --language en", "needs the recording")]
    [InlineData("transcribe a.wav", "--language is required")]
    [InlineData("transcribe a.wav --language", "--language is required")]
    [InlineData("transcribe a.wav --language it", "use en, fr or de")]
    [InlineData("transcribe a.wav --language en --formats pdf", "use any of vtt, json and txt")]
    [InlineData("transcribe a.wav --language en --formats ,", "names no format")]
    [InlineData("transcribe a.wav --language en --threads 0", "--threads '0' is not a whole number from 1 to 256")]
    [InlineData("transcribe a.wav --language en --threads two", "--threads 'two'")]
    [InlineData("transcribe a.wav --language en --speakers -2", "--speakers '-2' is not a whole number from 1 to 10")]
    [InlineData("transcribe a.wav --language en --speakers 11", "--speakers '11' is not a whole number from 1 to 10; the separator tells at most 10 speakers apart")]
    [InlineData("transcribe a.wav --language en --speakers 0", "or leave --speakers out to let it count them")]
    [InlineData("transcribe a.wav --language en --speakers 2 --no-speakers", "pass one of the two")]
    [InlineData("transcribe a.wav --language en --colour", "'--colour' is not an option of transcribe; its options are --language")]
    [InlineData("transcribe a.wav --language en -q", "'-q' is not an option")]
    [InlineData("transcribe a.wav b.wav --language en", "reads one recording, got 2")]
    [InlineData("transcribe a.wav --language en --raw=yes", "--raw is a switch")]
    [InlineData("transcribe a.wav --language en --output", "--output needs a value")]
    [InlineData("transcrib a.wav", "'transcrib' is not a command; use one of transcribe, benchmark, models")]
    [InlineData("benchmark --set", "--set needs a value")]
    [InlineData("benchmark --long-file x.wav", "needs its language right after it")]
    [InlineData("benchmark --long-file x.wav --threads 2 --language en", "needs its language right after it")]
    [InlineData("benchmark --language en", "gives the language of the --long-file")]
    [InlineData("benchmark --repeat 0", "--repeat '0'")]
    [InlineData("benchmark --label a/b", "use letters, digits")]
    [InlineData("benchmark clips", "takes no file argument")]
    [InlineData("models --verify --fast", "'--fast' is not an option of models")]
    public void BadCommandLinesNameTheProblem(string line, string expected)
    {
        ParsedCommandLine parsed = Parse(line);

        Assert.True(parsed.Kind == CommandKind.UsageError && parsed.Error!.Contains(expected, StringComparison.Ordinal), $"got {parsed.Kind}: '{parsed.Error}', wanted a usage error containing '{expected}' for '{line}'");
    }

    [Theory]
    [InlineData("", "Help", null)]
    [InlineData("--help", "Help", null)]
    [InlineData("help benchmark", "Help", "benchmark")]
    [InlineData("transcribe --help", "Help", "transcribe")]
    [InlineData("transcribe a.wav --language xx -h", "Help", "transcribe")]
    [InlineData("models --help", "Help", "models")]
    [InlineData("--version", "Version", null)]
    public void HelpAndVersionAreRecognised(string line, string expectedKind, string? topic)
    {
        CommandKind kind = Enum.Parse<CommandKind>(expectedKind);
        ParsedCommandLine parsed = Parse(line);

        Assert.True(parsed.Kind == kind && parsed.Topic == topic, $"got {parsed.Kind} on {parsed.Topic}, wanted {kind} on {topic}");
        Assert.True(HelpText.For(topic).Contains("Usage", StringComparison.Ordinal), "every help text should show its usage");
    }

    [Fact]
    public void BenchmarkReadsEveryOption()
    {
        ParsedCommandLine parsed = Parse("benchmark --data d --set clips,noise --set en-ami-01 --long-file a.wav --language EN --long-file b.m4a --language fr --threads 1 --no-speakers --repeat 3 --out o --label quiet-run --models m");

        BenchmarkArguments? b = parsed.Benchmark;
        Assert.True(parsed.Kind == CommandKind.Benchmark && b is not null, $"got {parsed.Kind}: {parsed.Error}");
        Assert.True(b.Data == "d" && b.Sets.SequenceEqual(["clips", "noise", "en-ami-01"]) && b.Threads == 1 && b.NoSpeakers && b.Repeat == 3 && b.Out == "o" && b.Label == "quiet-run" && b.Models == "m", $"got {b}");
        Assert.True(b.ExtraFiles.SequenceEqual([new ExtraFile("a.wav", "en"), new ExtraFile("b.m4a", "fr")]), $"got extras {string.Join(", ", b.ExtraFiles)}");
    }

    [Fact]
    public void BenchmarkDefaultsToTheClips()
    {
        BenchmarkArguments b = Parse("benchmark").Benchmark!;

        Assert.True(b.Sets.SequenceEqual(["clips"]) && b.Repeat == 1 && b.ExtraFiles.Count == 0, $"got {b}");
    }

    [Fact]
    public void ModelsReadsItsOptions()
    {
        ModelsArguments m = Parse("models --models /x --verify").Models!;

        Assert.True(m.Models == "/x" && m.Verify, $"got {m}");
    }

    [Fact]
    public void ADoubleDashEndsTheOptions()
    {
        TranscribeArguments t = CommandLine.Parse(["transcribe", "--language", "en", "--", "--odd-name.wav"]).Transcribe!;

        Assert.True(t.File == "--odd-name.wav", $"got file {t.File}");
    }
}
