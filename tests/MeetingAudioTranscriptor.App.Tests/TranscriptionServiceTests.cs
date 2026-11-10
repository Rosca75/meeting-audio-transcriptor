// Tests of the transcription service: the files it writes, how every failure maps to an outcome, cleanup, cancellation and model reuse.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Writers;
using MeetingAudioTranscriptor.Inference;
using MeetingAudioTranscriptor.Media;
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests;

[Trait("Category", "transcript")]
public sealed class TranscriptionServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TranscriptionRequest Request(string input, string language = "en") => new() { InputPath = input, Language = language };

    [Fact]
    public async Task ARunWritesTheThreeFormatsBesideTheRecording()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("meeting.wav"), 12, 2, 7);
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.Succeeded, $"got {outcome.Kind}: {outcome.Message}");
        string[] expected = ["meeting.json", "meeting.txt", "meeting.vtt", "meeting.wav"];
        Assert.True(folder.Names().Order().SequenceEqual(expected), $"got files {string.Join(", ", folder.Names().Order())}, wanted {string.Join(", ", expected)}");
        Assert.True(outcome.OutputFiles.Select(Path.GetFileName).SequenceEqual(["meeting.vtt", "meeting.json", "meeting.txt"]), $"got outputs {string.Join(", ", outcome.OutputFiles)}");
        Assert.True(Math.Abs(outcome.AudioSeconds - 12) < 0.01 && outcome.SegmentCount == 2 && outcome.WordCount == 4 && outcome.CleanedWordCount == 4, $"got {outcome.AudioSeconds} s, {outcome.SegmentCount} segments, {outcome.WordCount} words");
        Assert.True(outcome.ProcessingSeconds > 0 && outcome.RealTimeFactor > 0 && outcome.PeakWorkingSetBytes > 0 && outcome.DecoderName == AudioFileOpener.WavDecoderName, $"got processing {outcome.ProcessingSeconds}, rtf {outcome.RealTimeFactor}, peak {outcome.PeakWorkingSetBytes}, decoder {outcome.DecoderName}");

        using FileStream json = File.OpenRead(folder.File("meeting.json"));
        TranscriptDocument document = TranscriptJson.Deserialize(json);
        Assert.True(document.Metadata.SourceFile == "meeting.wav" && document.Metadata.Language == "en", $"got source {document.Metadata.SourceFile} language {document.Metadata.Language}");
        Assert.True(document.Metadata.Settings.Formats.SequenceEqual(["vtt", "json", "txt"]) && !document.Metadata.Settings.RawFiles, "got the wrong settings");
        Assert.True(document.Segments[0].Start > 1.8 && document.Segments[1].Start > 6.8, $"got segments at {document.Segments[0].Start} and {document.Segments[1].Start}, wanted after 2 s and 7 s");
    }

    [Theory]
    [InlineData("json", false, "out.json")]
    [InlineData("json", true, "out.json")]
    [InlineData("vtt", true, "out.raw.vtt,out.vtt")]
    [InlineData("txt,vtt", true, "out.raw.txt,out.raw.vtt,out.txt,out.vtt")]
    public async Task OnlyTheChosenFormatsAreWritten(string formats, bool raw, string expected)
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("out.wav"), 6, 1);
        string output = Path.Combine(folder.Path, "transcripts");
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());
        TranscriptionRequest request = Request(input) with
        {
            Formats = formats.Split(',').Select(TranscriptFormats.Parse).ToArray(),
            RawFiles = raw,
            OutputFolder = output,
        };

        TranscriptionOutcome outcome = await service.RunAsync(request, null, Token);

        string got = string.Join(",", Directory.GetFiles(output).Select(Path.GetFileName).Order());
        Assert.True(outcome.Succeeded && got == expected, $"got {outcome.Kind} and files '{got}' in the created output folder, wanted '{expected}'");
    }

    [Fact]
    public async Task AnExistingFileIsReplacedByDefaultAndKeptWhenOverwritingIsRefused()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("m.wav"), 6, 1);
        File.WriteAllText(folder.File("m.txt"), "old");
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        TranscriptionOutcome refused = await service.RunAsync(Request(input) with { Overwrite = OverwritePolicy.Refuse }, null, Token);
        string afterRefusal = File.ReadAllText(folder.File("m.txt"));
        bool vttAfterRefusal = File.Exists(folder.File("m.vtt"));
        TranscriptionOutcome replaced = await service.RunAsync(Request(input), null, Token);

        Assert.True(refused.Kind == TranscriptionOutcomeKind.OutputNotWritable && refused.Message.Contains("m.txt", StringComparison.Ordinal), $"got {refused.Kind}: {refused.Message}");
        Assert.True(afterRefusal == "old" && !vttAfterRefusal, "a refused run should leave the old file and write nothing");
        Assert.True(replaced.Succeeded && File.ReadAllText(folder.File("m.txt")).Contains("Hello world.", StringComparison.Ordinal), $"got {replaced.Kind}, wanted the old file replaced");
    }

    [Fact]
    public async Task SilenceWritesAnEmptyTranscriptAndSaysNoSpeech()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("quiet.wav"), 5);
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.NoSpeech && outcome.Succeeded && outcome.Message.Contains("no speech", StringComparison.Ordinal), $"got {outcome.Kind}: {outcome.Message}");
        Assert.True(outcome.OutputFiles.Count == 3 && outcome.OutputFiles.All(File.Exists), "the empty transcript should still be written");
    }

    [Fact]
    public async Task AnEmptyWavIsNoSpeechToo()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("empty.wav"), 0);
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.NoSpeech && outcome.AudioSeconds == 0, $"got {outcome.Kind}: {outcome.Message}");
    }

    public static TheoryData<string, TranscriptionOutcomeKind> OpenFailures => new()
    {
        { "not-found", TranscriptionOutcomeKind.InputNotFound },
        { "denied", TranscriptionOutcomeKind.InputUnreadable },
        { "unsupported", TranscriptionOutcomeKind.InputUnsupported },
        { "decoder-missing", TranscriptionOutcomeKind.InputUnsupported },
        { "corrupt", TranscriptionOutcomeKind.InputCorrupt },
        { "no-audio", TranscriptionOutcomeKind.InputCorrupt },
        { "format", TranscriptionOutcomeKind.InputUnsupported },
        { "format-while-reading", TranscriptionOutcomeKind.InputUnsupported },
        { "io-while-reading", TranscriptionOutcomeKind.InputCorrupt },
    };

    [Theory]
    [MemberData(nameof(OpenFailures))]
    public async Task InputFailuresMapToTheirKinds(string failure, TranscriptionOutcomeKind expected)
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 3, 0);
        (IAudioSource, string) Open(string path) => failure switch
        {
            "not-found" => throw new AudioDecodeException(AudioDecodeFailure.FileNotFound, "gone"),
            "denied" => throw new AudioDecodeException(AudioDecodeFailure.AccessDenied, "denied"),
            "unsupported" => throw new AudioDecodeException(AudioDecodeFailure.UnsupportedType, "not a recording"),
            "decoder-missing" => throw new AudioDecodeException(AudioDecodeFailure.DecoderMissing, "no ffmpeg"),
            "corrupt" => throw new AudioDecodeException(AudioDecodeFailure.CorruptOrUnsupportedData, "damaged"),
            "no-audio" => throw new AudioDecodeException(AudioDecodeFailure.NoAudioStream, "no audio stream"),
            "format" => throw new AudioFormatException("24-bit float in a strange header"),
            "format-while-reading" => (new FailingSource(() => new AudioFormatException("bad block")), "fake"),
            _ => (new FailingSource(() => new IOException("the pipe broke")), "fake"),
        };
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer(), open: Open);

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), null, Token);

        Assert.True(outcome.Kind == expected && outcome.Message.Length > 0, $"got {outcome.Kind}: {outcome.Message}, wanted {expected} for {failure}");
        Assert.True(outcome.OutputFiles.Count == 0 && folder.Names().SequenceEqual(["in.wav"]), $"got files {string.Join(", ", folder.Names())} after a failure, wanted none written");
    }

    [Fact]
    public async Task AMissingRecordingAndAnUnsupportedFileAreNamedByTheRealOpener()
    {
        using TempFolder folder = new();
        File.WriteAllText(folder.File("notes.docx"), "this is not audio at all");
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        TranscriptionOutcome missing = await service.RunAsync(Request(folder.File("absent.wav")), null, Token);
        TranscriptionOutcome unsupported = await service.RunAsync(Request(folder.File("notes.docx")), null, Token);

        Assert.True(missing.Kind == TranscriptionOutcomeKind.InputNotFound && missing.Message.Contains("absent.wav", StringComparison.Ordinal), $"got {missing.Kind}: {missing.Message}");
        Assert.True(unsupported.Kind == TranscriptionOutcomeKind.InputUnsupported, $"got {unsupported.Kind}: {unsupported.Message}");
    }

    [Fact]
    public async Task MissingModelsNameTheFetchCommand()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 3, 0);
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer(), missing: ["silero-vad/silero_vad.onnx"]);

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), null, Token);
        ModelStatus status = service.Check();

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.ModelsMissing && outcome.Message.Contains("fetch-models.mjs", StringComparison.Ordinal) && outcome.Message.Contains("silero_vad.onnx", StringComparison.Ordinal), $"got {outcome.Kind}: {outcome.Message}");
        Assert.True(!status.Ready && status.MissingFiles.Single() == "silero-vad/silero_vad.onnx", "the status should list the missing file");
    }

    [Fact]
    public async Task AModelThatFailsToLoadIsModelsMissingAndIsRetriedNextRun()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 3, 0);
        int attempts = 0;
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer(), load: _ =>
        {
            attempts++;
            return attempts == 1 ? throw new ModelLoadException("the model file is damaged; fetch it again") : ServiceKit.Models(new FakeRecognizer());
        });

        TranscriptionOutcome first = await service.RunAsync(Request(input), null, Token);
        TranscriptionOutcome second = await service.RunAsync(Request(input), null, Token);

        Assert.True(first.Kind == TranscriptionOutcomeKind.ModelsMissing && first.Message.Contains("damaged", StringComparison.Ordinal), $"got {first.Kind}: {first.Message}");
        Assert.True(second.Succeeded && attempts == 2, $"got {second.Kind} after {attempts} loads, wanted a retry that succeeds");
    }

    [Fact]
    public async Task ASecondRunReusesTheLoadedModels()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 4, 1);
        StrongBox<int> loads = new();
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer(), loads);

        TranscriptionOutcome first = await service.RunAsync(Request(input), null, Token);
        TranscriptionOutcome second = await service.RunAsync(Request(input, "fr"), null, Token);

        Assert.True(first.Succeeded && second.Succeeded && loads.Value == 1, $"got {first.Kind}, {second.Kind} and {loads.Value} loads, wanted one load for two runs");
        Assert.True(first.ModelLoadSeconds >= 0 && second.ModelLoadSeconds < 0.5, $"got load times {first.ModelLoadSeconds} and {second.ModelLoadSeconds}");
    }

    [Fact]
    public async Task AnOutputFolderThatIsAFileIsNotWritable()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 3, 0);
        File.WriteAllText(folder.File("blocker"), "a file where a folder should be");
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        TranscriptionOutcome outcome = await service.RunAsync(Request(input) with { OutputFolder = folder.File("blocker") }, null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.OutputNotWritable && outcome.Message.Contains("blocker", StringComparison.Ordinal), $"got {outcome.Kind}: {outcome.Message}");
    }

    [Fact]
    public async Task ATargetThatIsAFolderIsRefusedBeforeTheRun()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 3, 0);
        Directory.CreateDirectory(folder.File("in.txt"));
        FakeRecognizer recognizer = new();
        using TranscriptionService service = ServiceKit.Service(recognizer);

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.OutputNotWritable && recognizer.Calls == 0, $"got {outcome.Kind} after {recognizer.Calls} recognitions: {outcome.Message}");
    }

    [Theory]
    [InlineData(28, true)]
    [InlineData(unchecked((int)0x80070070), true)]
    [InlineData(unchecked((int)0x80070027), true)]
    [InlineData(unchecked((int)0x80070005), false)]
    [InlineData(13, false)]
    public void ADiskFullIsRecognisedOnEveryPlatform(int hresult, bool full)
    {
        IOException error = new("write failed", hresult);

        (TranscriptionOutcomeKind kind, string message) = FailureMapping.Map(error, RunPhase.Output, "/in.wav", "/out");

        TranscriptionOutcomeKind expected = full ? TranscriptionOutcomeKind.DiskFull : TranscriptionOutcomeKind.OutputNotWritable;
        Assert.True(kind == expected && message.Contains("/out", StringComparison.Ordinal), $"got {kind}: {message} for HResult 0x{hresult:X8}, wanted {expected}");
    }

    [Fact]
    public async Task ARecogniserFailureIsUnexpectedWithItsMessage()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 4, 1);
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer { Failure = () => new InvalidOperationException("tensor shape mismatch") });

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), null, Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.Unexpected && outcome.Message.Contains("tensor shape mismatch", StringComparison.Ordinal), $"got {outcome.Kind}: {outcome.Message}");
        Assert.True(folder.Names().SequenceEqual(["in.wav"]), $"got files {string.Join(", ", folder.Names())}, wanted none written");
    }

    [Fact]
    public async Task CancellingDuringRecognitionWritesNothing()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 8, 1, 5);
        using ManualResetEventSlim gate = new(false);
        FakeRecognizer recognizer = new() { Gate = gate };
        using TranscriptionService service = ServiceKit.Service(recognizer);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        Task<TranscriptionOutcome> run = service.RunAsync(Request(input), null, cancel.Token);
        SpinWait.SpinUntil(() => recognizer.Calls > 0, 5000);
        await cancel.CancelAsync();
        TranscriptionOutcome outcome = await run;

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.Cancelled, $"got {outcome.Kind}: {outcome.Message}");
        Assert.True(folder.Names().SequenceEqual(["in.wav"]), $"got files {string.Join(", ", folder.Names())}, wanted none");
    }

    [Fact]
    public async Task CancellingDuringWritingRemovesTheTemporaryFiles()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 4, 1);
        File.WriteAllText(folder.File("in.vtt"), "old");
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Progress progress = new(report =>
        {
            if (report.Stage == TranscriptionStage.Writing && report.StageFraction > 0)
            {
                cancel.Cancel();
            }
        });

        TranscriptionOutcome outcome = await service.RunAsync(Request(input), progress, cancel.Token);

        Assert.True(outcome.Kind == TranscriptionOutcomeKind.Cancelled, $"got {outcome.Kind}: {outcome.Message}");
        string[] names = folder.Names().Order().ToArray();
        Assert.True(names.SequenceEqual(["in.vtt", "in.wav"]) && File.ReadAllText(folder.File("in.vtt")) == "old", $"got files {string.Join(", ", names)}, wanted the old vtt untouched and no temporary file");
    }

    [Fact]
    public async Task ASecondRunWhileOneIsInFlightIsRefused()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 4, 1);
        using ManualResetEventSlim gate = new(false);
        FakeRecognizer recognizer = new() { Gate = gate };
        using TranscriptionService service = ServiceKit.Service(recognizer);

        Task<TranscriptionOutcome> first = service.RunAsync(Request(input), null, Token);
        SpinWait.SpinUntil(() => recognizer.Calls > 0, 5000);
        TranscriptionOutcome second = await service.RunAsync(Request(input), null, Token);
        gate.Set();
        TranscriptionOutcome finished = await first;

        Assert.True(second.Kind == TranscriptionOutcomeKind.AlreadyRunning && finished.Succeeded, $"got {second.Kind} and {finished.Kind}");
    }

    [Fact]
    public async Task ProgressNeverDecreasesAndEndsWritingAtOne()
    {
        using TempFolder folder = new();
        string input = ServiceKit.WriteWav(folder.File("in.wav"), 10, 1, 6);
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());
        List<TranscriptionProgress> reports = [];
        Progress progress = new(report =>
        {
            lock (reports)
            {
                reports.Add(report);
            }
        });

        await service.RunAsync(Request(input), progress, Token);

        Assert.True(reports.Zip(reports.Skip(1)).All(pair => pair.First.Fraction <= pair.Second.Fraction), "progress went backwards");
        Assert.True(reports[0].Stage == TranscriptionStage.LoadingModels && reports[^1].Stage == TranscriptionStage.Writing && reports[^1].Fraction == 1, $"got first {reports[0]} and last {reports[^1]}");
    }

    [Fact]
    public async Task AnInvalidRequestIsRefusedAtOnce()
    {
        using TranscriptionService service = ServiceKit.Service(new FakeRecognizer());

        Exception? language = await Record.ExceptionAsync(() => service.RunAsync(Request("/x.wav", "it"), null, Token));
        Exception? formats = await Record.ExceptionAsync(() => service.RunAsync(Request("/x.wav") with { Formats = [] }, null, Token));

        Assert.True(language is ArgumentException && language.Message.Contains("en, fr or de", StringComparison.Ordinal), $"got {language?.Message}");
        Assert.True(formats is ArgumentException, $"got {formats?.GetType().Name}");
    }

    [Fact]
    public void TheShippedServiceNeedsTheVadAndTheRecogniser()
    {
        using TempFolder folder = new();
        using TranscriptionService service = new(folder.Path);

        ModelStatus status = service.Check();

        Assert.True(!status.Ready && status.MissingFiles.Contains("silero-vad/silero_vad.onnx") && status.MissingFiles.Contains(RecognizerFactory.ModelFile), $"got missing {string.Join(", ", status.MissingFiles)}");
        Assert.True(RecognizerFactory.SessionOptions.IntraOpThreads == 1 && !RecognizerFactory.SessionOptions.AllowSpinning, "the recogniser session should run one intra-op thread without spinning");
    }

    /// <summary>Delivers no sample and fails on its first read.</summary>
    private sealed class FailingSource : IAudioSource
    {
        private readonly Func<Exception> _failure;

        public FailingSource(Func<Exception> failure)
        {
            _failure = failure;
        }

        public int SampleRate => 16000;

        public double? DurationSeconds => 10;

        public int Read(Span<float> buffer) => throw _failure();

        public void Dispose()
        {
        }
    }

    /// <summary>Calls back on the reporting thread.</summary>
    private sealed class Progress : IProgress<TranscriptionProgress>
    {
        private readonly Action<TranscriptionProgress> _callback;

        public Progress(Action<TranscriptionProgress> callback)
        {
            _callback = callback;
        }

        public void Report(TranscriptionProgress value) => _callback(value);
    }
}
