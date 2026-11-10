// Tests of how the service is wired to its models and to the page: the model files the lock pins, the models a run names, the notice of a run without speakers, and the bridge adapter's mapping.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Contracts.Bridge;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Writers;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests;

[Trait("Category", "config")]
public sealed class ServiceWiringTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheFactoriesNameFilesTheLockShipsInTheDefaultSet()
    {
        string root = RepositoryRoot();
        ModelCatalog catalog = ModelCatalog.FromLockFile(Path.Combine(root, "models"), Path.Combine(root, "build", "models.lock"));

        foreach ((string model, string file) in new[]
        {
            (RecognizerFactory.ModelName, RecognizerFactory.ModelFile),
            (RecognizerFactory.ModelName, RecognizerFactory.TokenizerFile),
            (LoadedModels.VadModelName, LoadedModels.VadFile),
            (SpeakerSeparatorFactory.ModelName, SpeakerSeparatorFactory.ModelFile),
        })
        {
            // ResolveFile throws ArgumentException when the lock does not pin the file; a file missing on disk is fine here.
            try
            {
                catalog.ResolveFile(model, file);
            }
            catch (FileNotFoundException)
            {
                // The models are not installed on this machine; the lock is what this test reads.
            }
        }

        Assert.True(SpeakerSeparatorFactory.SessionOptions.IntraOpThreads == 1 && !SpeakerSeparatorFactory.SessionOptions.AllowSpinning, "the embedding session should run one intra-op thread without spinning");
    }

    [Fact]
    public async Task ARunWithoutTheSpeakerModelGoesOnAndSaysSo()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("meeting.wav"), 12, 2, 7);
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en" }, null, Token);
        TranscriptionOutcome off = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", SpeakerSeparation = false }, null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.Succeeded && outcome.SpeakerCount == 0, $"got {outcome.Kind} with {outcome.SpeakerCount} speakers: {outcome.Message}");
        Assert.True(outcome.Notices.Count == 1 && outcome.Notices[0].Contains(SpeakerSeparatorFactory.ModelFile, StringComparison.Ordinal) && outcome.Notices[0].Contains("fetch-models.mjs", StringComparison.Ordinal), $"got notices {string.Join(" | ", outcome.Notices)}");
        Assert.True(off.Notices.Count == 0, $"got notices {string.Join(" | ", off.Notices)} with separation switched off, wanted none");
        Assert.True(!outcome.Pipeline!.Document.Metadata.Models.Any(model => model.Role == LoadedModels.SpeakerEmbeddingRole), "a run without speakers named the speaker-embedding model");
    }

    [Fact]
    public async Task ARunWithSpeakersNamesTheEmbeddingModelAndPassesTheHint()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("meeting.wav"), 12, 2, 7);
        AlternatingSeparator separator = new();
        TranscriptionService service = ServiceKit.Service(new FakeRecognizer(), load: _ => ServiceKit.Models(new FakeRecognizer(), () => separator));
        using (service)
        {
            TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", SpeakerCountHint = 2 }, null, Token);

            Assert.True(outcome.Kind == TranscriptionOutcomeKind.Succeeded && outcome.SpeakerCount == 2 && outcome.Notices.Count == 0, $"got {outcome.Kind}, {outcome.SpeakerCount} speakers, notices {string.Join(" | ", outcome.Notices)}");
            TranscriptDocument document = outcome.Pipeline!.Document;
            Assert.True(document.Metadata.Models.Any(model => model.Role == LoadedModels.SpeakerEmbeddingRole && model.Name == SpeakerSeparatorFactory.ModelName), $"got models {string.Join(", ", document.Metadata.Models.Select(model => model.Role + "=" + model.Name))}");
            Assert.True(separator.Hint == 2 && document.Metadata.Settings.SpeakerCountHint == 2 && document.Metadata.Settings.SpeakerSeparation, $"got hint {separator.Hint} and settings {document.Metadata.Settings}");
        }
    }

    [Theory]
    [InlineData(TranscriptionOutcomeKind.Succeeded, TranscribeOutcomeKinds.Completed)]
    [InlineData(TranscriptionOutcomeKind.NoSpeech, TranscribeOutcomeKinds.NoSpeech)]
    [InlineData(TranscriptionOutcomeKind.Cancelled, TranscribeOutcomeKinds.Cancelled)]
    [InlineData(TranscriptionOutcomeKind.InputCorrupt, TranscribeOutcomeKinds.Failed)]
    [InlineData(TranscriptionOutcomeKind.ModelsMissing, TranscribeOutcomeKinds.Failed)]
    [InlineData(TranscriptionOutcomeKind.DiskFull, TranscribeOutcomeKinds.Failed)]
    [InlineData(TranscriptionOutcomeKind.AlreadyRunning, TranscribeOutcomeKinds.Failed)]
    [InlineData(TranscriptionOutcomeKind.Unexpected, TranscribeOutcomeKinds.Failed)]
    public void EveryServiceOutcomeBecomesOneOfThePageKinds(TranscriptionOutcomeKind kind, string expected)
    {
        TranscribeRequest request = new() { InputPath = "/a/b.wav", Language = "en", Formats = ["json"], OutputFolder = "/out" };
        TranscriptionOutcome outcome = new()
        {
            Kind = kind,
            Message = "the thing failed; do this",
            OutputFiles = ["/out/b.json"],
            AudioSeconds = 100,
            ProcessingSeconds = 10,
            SpeakerCount = 3,
            CleanedWordCount = 42,
            Notices = ["speakers were not told apart"],
        };

        TranscribeOutcome page = ServiceTranscriber.ToPageOutcome(outcome, request);

        Assert.True(page.Kind == expected, $"got {page.Kind}, wanted {expected}");
        Assert.True(page.Message == "The thing failed; do this. Speakers were not told apart.", $"got message '{page.Message}'");
        Assert.True(page.OutputFolder == "/out" && page.OutputFiles.SequenceEqual(["/out/b.json"]) && page.DurationSeconds == 100 && page.ProcessingSeconds == 10 && page.SpeedFactor == 10 && page.Speakers == 3 && page.Words == 42, $"got {page}");
    }

    [Fact]
    public void ThePageRequestBecomesTheServiceRequest()
    {
        TranscribeRequest on = new() { InputPath = "/a/b.m4a", Language = "de", Formats = ["vtt", "txt"], SpeakerSeparation = true, SpeakerCount = 4, RawFiles = true, OutputFolder = "/out" };
        TranscribeRequest off = on with { SpeakerSeparation = false };

        TranscriptionRequest service = ServiceTranscriber.ToServiceRequest(on);
        TranscriptionRequest serviceOff = ServiceTranscriber.ToServiceRequest(off);

        Assert.True(service.InputPath == "/a/b.m4a" && service.Language == "de" && service.OutputFolder == "/out" && service.RawFiles && service.Overwrite == OverwritePolicy.Overwrite, $"got {service}");
        Assert.True(service.Formats.SequenceEqual([TranscriptFormat.Vtt, TranscriptFormat.Txt]), $"got formats {string.Join(", ", service.Formats)}");
        Assert.True(service.SpeakerSeparation == true && service.SpeakerCountHint == 4, $"got separation {service.SpeakerSeparation} hint {service.SpeakerCountHint}");
        Assert.True(serviceOff.SpeakerSeparation == false && serviceOff.SpeakerCountHint is null, $"got separation {serviceOff.SpeakerSeparation} hint {serviceOff.SpeakerCountHint}");
    }

    [Fact]
    public void EveryPipelineStageHasAPageStage()
    {
        foreach (TranscriptionStage stage in Enum.GetValues<TranscriptionStage>())
        {
            Assert.True(BridgeStages.All.Contains(BridgeStages.Of(stage)), $"stage {stage} maps to '{BridgeStages.Of(stage)}', which is not a page stage");
        }

        Assert.True(BridgeStages.Of(TranscriptionStage.Transcribing) == BridgeStages.RecognisingWords && BridgeStages.Of(TranscriptionStage.IdentifyingSpeakers) == BridgeStages.TellingSpeakersApart, "got the wrong stage words");
        Assert.True(BridgeStages.All.All(text => !text.Contains('—', StringComparison.Ordinal)), "a stage name holds an em dash");
    }

    [Fact]
    public async Task TheAdapterRunsTheServiceAndReportsInPageStages()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("meeting.wav"), 12, 2, 7);
        using ServiceTranscriber transcriber = new(ServiceKit.Service(new FakeRecognizer()));
        List<TranscribeProgress> reports = [];
        PageProgress progress = new(report =>
        {
            lock (reports)
            {
                reports.Add(report);
            }
        });

        ModelCheckResult check = transcriber.CheckModels();
        TranscribeOutcome outcome = await transcriber.RunAsync(
            new TranscribeRequest { InputPath = input, Language = "en", Formats = ["json", "txt"], SpeakerSeparation = false, OutputFolder = folder.Path },
            progress,
            Token);

        Assert.True(check.Ready && check.Message is null, $"got ready {check.Ready} message {check.Message}");
        Assert.True(outcome.Kind == TranscribeOutcomeKinds.Completed && outcome.OutputFiles.Count == 2 && outcome.OutputFiles.All(File.Exists), $"got {outcome.Kind}: {outcome.Message}");
        Assert.True(outcome.Message!.StartsWith("The transcript of", StringComparison.Ordinal) && outcome.Message.EndsWith('.'), $"got message '{outcome.Message}'");
        Assert.True(reports.Count > 0 && reports.All(report => BridgeStages.All.Contains(report.Stage)) && reports[^1].Fraction == 1, $"got stages {string.Join(", ", reports.Select(report => report.Stage).Distinct())}");
    }

    [Fact]
    public void MissingModelsReadAsASentence()
    {
        using TempFolder folder = new();
        using ServiceTranscriber transcriber = new(folder.Path);

        ModelCheckResult check = transcriber.CheckModels();

        Assert.True(!check.Ready && check.Message is not null && char.IsUpper(check.Message[0]) && check.Message.EndsWith('.') && check.Message.Contains("fetch-models.mjs", StringComparison.Ordinal), $"got '{check.Message}'");
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MeetingAudioTranscriptor.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("the repository root was not found above the test output");
    }

    /// <summary>Gives alternate segments to two speakers and records the hint.</summary>
    private sealed class AlternatingSeparator : ISpeakerSeparator
    {
        public int? Hint { get; private set; }

        public void AddBlock(ReadOnlySpan<float> samples, long startSample, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
        {
        }

        public SpeakerAssignment Finish(IReadOnlyList<RecognizedWord> words, int? speakerCountHint, CancellationToken cancellation)
        {
            Hint = speakerCountHint;
            int[] speakers = new int[words.Count];
            for (int i = 0; i < words.Count; i++)
            {
                speakers[i] = words[i].Start < 5 ? 0 : 1;
            }

            return new SpeakerAssignment(speakers, [1, 1]);
        }
    }

    /// <summary>Calls back on the reporting thread.</summary>
    private sealed class PageProgress : IProgress<TranscribeProgress>
    {
        private readonly Action<TranscribeProgress> _callback;

        public PageProgress(Action<TranscribeProgress> callback)
        {
            _callback = callback;
        }

        public void Report(TranscribeProgress value) => _callback(value);
    }
}
