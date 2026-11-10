// The shipped service end to end over the real models: recognition, speakers and the three files per language, noise, the containers, cancellation and model reuse.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Pipeline;

/// <summary>Runs the real-service classes one after another, so their timings and cores do not overlap.</summary>
[CollectionDefinition(Name)]
public sealed class RealServiceSequence
{
    /// <summary>The collection's name.</summary>
    public const string Name = "real service";
}

[Collection(RealServiceSequence.Name)]
[Trait("Category", "transcript")]
[Trait("Tier", "models")]
public sealed class RealServiceTests
{
    /// <summary>
    /// The word error rate a short clip must stay under. Measured at four threads: en-multi-01 4.7 %,
    /// fr-multi-01 0.0 %, de-multi-01 8.8 % (raw); the floor leaves room for a different processor's
    /// arithmetic without letting a broken recogniser through.
    /// </summary>
    private const double ClipWerFloor = 0.15;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, int> Clips => new()
    {
        { "clips/en/en-multi-01", "en", 3 },
        { "clips/fr/fr-multi-01", "fr", 2 },
        { "clips/de/de-multi-01", "de", 2 },
    };

    public static TheoryData<string> NoiseFiles => ["noise/silence-60s.wav", "noise/noise-white-60s.wav", "noise/noise-pink-60s.wav", "noise/noise-office-60s.wav"];

    [Theory]
    [MemberData(nameof(Clips))]
    public async Task AClipIsTranscribedWithItsSpeakersIntoThreeValidFiles(string clip, string language, int speakers)
    {
        string input = TestPaths.RequireBenchFile(clip + ".wav");
        string reference = File.ReadAllText(TestPaths.RequireBenchFile(clip + ".ref.txt"));
        using TranscriptionService service = RequireService();
        using Scratch output = new();

        TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = language, OutputFolder = output.Path, Threads = 4 }, null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.Succeeded, $"got {outcome.Kind}: {outcome.Message}");
        TranscriptDocument document = ReadJson(output.File(Path.GetFileName(clip) + ".json"));
        double wer = WordErrorRate.Compute(TextNormalizer.Normalize(reference), TextNormalizer.Normalize(RawText(document))).Wer;
        Assert.True(wer < ClipWerFloor, $"got a word error rate of {wer:P1} on {clip}, wanted under {ClipWerFloor:P0}");

        // Speakers: the clip's count, every segment one of them, numbered by first appearance.
        Assert.True(document.Speakers.Count == speakers && outcome.SpeakerCount == speakers, $"got {document.Speakers.Count} speakers on {clip}, wanted {speakers}");
        Assert.True(document.Segments.All(segment => segment.Speaker is not null && document.Speakers.Any(speaker => speaker.Id == segment.Speaker)), "a segment names no speaker or an unknown one");
        string[] firstAppearance = document.Segments.Select(segment => segment.Speaker!).Distinct().ToArray();
        Assert.True(firstAppearance.SequenceEqual(document.Speakers.Select(speaker => speaker.Id)) && firstAppearance[0] == "S1", $"got speakers in order {string.Join(",", firstAppearance)}, wanted S1, S2 ... by first appearance");
        foreach (TranscriptSpeaker speaker in document.Speakers)
        {
            double summed = document.Segments.Where(segment => segment.Speaker == speaker.Id).Sum(segment => segment.End - segment.Start);
            Assert.True(Math.Abs(summed - speaker.SpeakingSeconds) < 0.01, $"got {speaker.SpeakingSeconds} s for {speaker.Id}, wanted the {summed:F2} s of their segments");
        }

        Assert.True(document.Metadata.Models.Select(model => model.Role).Order().SequenceEqual(["recognition", "speaker-embedding", "voice-activity"]), $"got models {string.Join(", ", document.Metadata.Models.Select(model => model.Role + "=" + model.Name))}");

        string vtt = File.ReadAllText(output.File(Path.GetFileName(clip) + ".vtt"));
        string txt = File.ReadAllText(output.File(Path.GetFileName(clip) + ".txt"));
        Assert.True(vtt.StartsWith("WEBVTT", StringComparison.Ordinal) && vtt.Contains(" --> ", StringComparison.Ordinal) && vtt.Contains("Speaker 1", StringComparison.Ordinal), "the vtt should hold timed cues with speakers");
        Assert.True(txt.Contains("Speaker 1:", StringComparison.Ordinal) && txt.Contains("Speaker " + speakers + ":", StringComparison.Ordinal), "the txt should name every speaker");
    }

    [Theory]
    [MemberData(nameof(NoiseFiles))]
    public async Task NoiseGivesNoWordAndNoSpeech(string relativePath)
    {
        string input = TestPaths.RequireBenchFile(relativePath);
        using TranscriptionService service = RequireService();
        using Scratch output = new();

        TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = output.Path }, null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.NoSpeech && outcome.WordCount == 0 && outcome.SegmentCount == 0, $"got {outcome.Kind} with {outcome.WordCount} words: {outcome.Message}");
        Assert.True(outcome.OutputFiles.Count == 3 && outcome.OutputFiles.All(File.Exists), $"got {outcome.OutputFiles.Count} files, wanted the three empty transcripts");
    }

    [Fact]
    public async Task TheContainersReadAsTheWav()
    {
        string wav = TestPaths.RequireBenchFile("long/en-10min.wav");
        using TranscriptionService service = RequireService();
        using Scratch output = new();

        string reference = RawText(await Transcribe(service, wav, output.File("wav")));
        foreach (string container in new[] { "long/en-10min.m4a", "long/en-10min.mp4" })
        {
            string input = TestPaths.RequireBenchFile(container);
            TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = output.File(Path.GetExtension(container)), Formats = [Engine.Writers.TranscriptFormat.Json] }, null, Token);
            Assert.SkipWhen(outcome.Kind == TranscriptionOutcomeKind.InputUnsupported, $"needs a decoder for {container}: {outcome.Message}");
            Assert.True(outcome.Succeeded, $"got {outcome.Kind}: {outcome.Message}");

            // The AAC copy differs from the PCM by its coding loss only, which moves a word here and there.
            double difference = WordErrorRate.Compute(TextNormalizer.Normalize(reference), TextNormalizer.Normalize(RawText(outcome.Pipeline!.Document))).Wer;
            Assert.True(difference < 0.02, $"got {difference:P2} of words differing between {container} and the wav, wanted under 2 %");
        }
    }

    [Fact]
    public async Task CancellingMidRunLeavesNoFile()
    {
        string input = TestPaths.RequireBenchFile("long/en-10min.wav");
        using TranscriptionService service = RequireService();
        using Scratch output = new();
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        CancelAt progress = new(cancel, 0.2);

        TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = output.Path, RawFiles = true }, progress, cancel.Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.Cancelled, $"got {outcome.Kind}: {outcome.Message}");
        string[] left = Directory.Exists(output.Path) ? Directory.GetFileSystemEntries(output.Path) : [];
        Assert.True(left.Length == 0, $"got {string.Join(", ", left)} left in the output folder, wanted nothing");
    }

    [Fact]
    public async Task ASecondRunReusesTheLoadedModels()
    {
        string input = TestPaths.RequireBenchFile("clips/en/en-single-01.wav");
        using TranscriptionService service = RequireService();
        using Scratch output = new();

        TranscriptionOutcome first = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = output.File("1") }, null, Token);
        TranscriptionOutcome second = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = output.File("2") }, null, Token);

        Assert.True(first.Succeeded && second.Succeeded, $"got {first.Kind} and {second.Kind}");
        Assert.True(first.ModelLoadSeconds > 0 && second.ModelLoadSeconds == 0, $"got model loads of {first.ModelLoadSeconds:F2} s and {second.ModelLoadSeconds:F2} s, wanted the second run to load nothing");
        Assert.True(second.ProcessingSeconds < first.ProcessingSeconds, $"got the second run at {second.ProcessingSeconds:F2} s and the first at {first.ProcessingSeconds:F2} s, wanted the second faster without the load");
        Assert.True(RawText(first.Pipeline!.Document) == RawText(second.Pipeline!.Document), "the two runs should give the same transcript");
    }

    /// <summary>The shipped service over the installed models; skips unless the recogniser, the VAD and the speaker model are there.</summary>
    internal static TranscriptionService RequireService()
    {
        string root = ModelCatalog.ResolveRoot();
        TranscriptionService service = new(root);
        ModelStatus status = service.Check();
        if (!status.Ready || !status.SpeakerSeparationAvailable)
        {
            service.Dispose();
            Assert.Skip($"needs the default models in {root}: {status.Message}{status.SpeakerSeparationMessage}; run node build/fetch-models.mjs --into {root}");
        }

        return service;
    }

    private static async Task<TranscriptDocument> Transcribe(TranscriptionService service, string input, string folder)
    {
        TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = folder, Formats = [Engine.Writers.TranscriptFormat.Json] }, null, Token);
        Assert.True(outcome.Succeeded, $"got {outcome.Kind}: {outcome.Message}");
        return outcome.Pipeline!.Document;
    }

    private static string RawText(TranscriptDocument document) => string.Join(' ', document.Segments.Select(segment => segment.RawText));

    private static TranscriptDocument ReadJson(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return TranscriptJson.Deserialize(stream);
    }

    /// <summary>Cancels the run once the overall fraction passes a mark.</summary>
    private sealed class CancelAt : IProgress<TranscriptionProgress>
    {
        private readonly CancellationTokenSource _source;
        private readonly double _mark;

        public CancelAt(CancellationTokenSource source, double mark)
        {
            _source = source;
            _mark = mark;
        }

        public void Report(TranscriptionProgress value)
        {
            if (value.Fraction >= _mark && !_source.IsCancellationRequested)
            {
                _source.Cancel();
            }
        }
    }

    /// <summary>A temporary folder deleted at the end of a test.</summary>
    private sealed class Scratch : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mat-real-" + Guid.NewGuid().ToString("N"));

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
