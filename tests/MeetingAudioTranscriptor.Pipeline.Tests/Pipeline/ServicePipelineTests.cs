// The real VAD and the streaming pipeline, driven through the application service with a fake recogniser, over the benchmark's long files and noise.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Vad;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Pipeline;

[Trait("Category", "transcript")]
[Trait("Tier", "models")]
public sealed class ServicePipelineTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> LongFiles => ["long/en-10min.wav", "long/en-10min.m4a", "long/en-10min.mp4"];

    public static TheoryData<string> NoiseFiles => ["noise/silence-60s.wav", "noise/noise-white-60s.wav", "noise/noise-pink-60s.wav", "noise/noise-office-60s.wav"];

    [Theory]
    [MemberData(nameof(LongFiles))]
    public async Task ALongRecordingStreamsThroughTheRealVadIntoEveryFormat(string relativePath)
    {
        string input = TestPaths.RequireBenchFile(relativePath);
        string output = Path.Combine(Path.GetTempPath(), "mat-pipeline-" + Guid.NewGuid().ToString("N"));
        WordPerHalfSecond recognizer = new();
        using TranscriptionService service = Service(recognizer);
        List<TranscriptionProgress> reports = [];
        Progress progress = new(report =>
        {
            lock (reports)
            {
                reports.Add(report);
            }
        });

        try
        {
            TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = output, Threads = 1, RawFiles = true }, progress, Token);

            Assert.SkipWhen(
                outcome.Kind == TranscriptionOutcomeKind.InputUnsupported && relativePath.EndsWith(".wav", StringComparison.Ordinal) is false,
                $"needs a decoder for {relativePath}: {outcome.Message}");
            Assert.True(outcome.Kind == TranscriptionOutcomeKind.Succeeded, $"got {outcome.Kind}: {outcome.Message}");
            PipelineResult result = outcome.Pipeline!;

            // en-10min.wav is 603.7 s of read speech separated by pauses.
            Assert.True(Math.Abs(result.AudioSeconds - 603.66) < 0.2, $"got {result.AudioSeconds:F2} s of audio, wanted about 603.66");
            Assert.True(result.SpeechSeconds > 0.6 * result.AudioSeconds && result.SpeechSeconds < result.AudioSeconds, $"got {result.SpeechSeconds:F1} s of speech in {result.AudioSeconds:F1} s");
            Assert.True(result.ChunkCount >= result.AudioSeconds / 30 && result.ChunkCount < result.AudioSeconds / 2, $"got {result.ChunkCount} chunks, wanted between one per 30 s and one per 2 s");
            Assert.True(result.ForcedCuts <= result.ChunkCount / 10, $"got {result.ForcedCuts} forced cuts of {result.ChunkCount} chunks, wanted at most a tenth");
            Assert.True(recognizer.LongestChunkSeconds <= 30.001, $"got a chunk of {recognizer.LongestChunkSeconds:F2} s, wanted none over the recogniser's 30 s");
            Assert.True(result.RecognizedWords == recognizer.Words, $"got {result.RecognizedWords} words in the result, the recogniser returned {recognizer.Words}");

            Assert.True(reports.Count > 2 && reports.Zip(reports.Skip(1)).All(pair => pair.First.Fraction <= pair.Second.Fraction), "progress went backwards");
            Assert.True(reports[^1].Fraction == 1 && reports[^1].Stage == TranscriptionStage.Writing, $"got the last report {reports[^1]}");

            string name = Path.GetFileNameWithoutExtension(input);
            string[] files = Directory.GetFiles(output).Select(Path.GetFileName).Order().ToArray()!;
            string[] expected = [name + ".json", name + ".raw.txt", name + ".raw.vtt", name + ".txt", name + ".vtt"];
            Assert.True(files.SequenceEqual(expected), $"got {string.Join(", ", files)}, wanted {string.Join(", ", expected)}");
            Assert.True(File.ReadAllText(Path.Combine(output, name + ".vtt")).StartsWith("WEBVTT", StringComparison.Ordinal), "the vtt should open with WEBVTT");
            Assert.True(new FileInfo(Path.Combine(output, name + ".txt")).Length > 1000, "the txt should hold the words");
            using FileStream json = File.OpenRead(Path.Combine(output, name + ".json"));
            TranscriptDocument document = TranscriptJson.Deserialize(json);
            Assert.True(document.Segments.Count > 50 && document.Segments.Zip(document.Segments.Skip(1)).All(pair => pair.First.End <= pair.Second.Start + 1e-6), $"got {document.Segments.Count} segments, wanted many, in order and apart");
            Assert.True(document.Metadata.SourceFile == Path.GetFileName(input) && document.Metadata.Settings.Threads == 1, "got the wrong metadata");
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(NoiseFiles))]
    public async Task NoiseGivesAnEmptyTranscriptAndSaysNoSpeech(string relativePath)
    {
        string input = TestPaths.RequireBenchFile(relativePath);
        string output = Path.Combine(Path.GetTempPath(), "mat-pipeline-" + Guid.NewGuid().ToString("N"));
        WordPerHalfSecond recognizer = new();
        using TranscriptionService service = Service(recognizer);

        try
        {
            TranscriptionOutcome outcome = await service.RunAsync(new TranscriptionRequest { InputPath = input, Language = "en", OutputFolder = output, Threads = 1 }, null, Token);

            Assert.True(outcome.Kind == TranscriptionOutcomeKind.NoSpeech, $"got {outcome.Kind} with {outcome.SegmentCount} segments and {recognizer.Calls} recognitions: {outcome.Message}");
            Assert.True(recognizer.Calls == 0 && outcome.OutputFiles.Count == 3 && outcome.OutputFiles.All(File.Exists), $"got {recognizer.Calls} recognitions and {outcome.OutputFiles.Count} files");
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    /// <summary>The service over the real Silero model and the fake recogniser; the recogniser's files are not required.</summary>
    private static TranscriptionService Service(ISpeechRecognizer recognizer)
    {
        string silero = TestPaths.RequireSileroModel();
        ServiceSeams seams = ServiceSeams.Default with
        {
            MissingFiles = _ => [],
            LoadModels = _ =>
            {
                ITensorRunner runner = OrtRunnerFactory.Open(silero, new RunnerOptions(IntraOpThreads: 1, AllowSpinning: false));
                return new LoadedModels(new SileroVadModel(runner), recognizer, null, [new() { Role = "voice-activity", Name = "silero-vad" }], [runner]);
            },
        };
        return new TranscriptionService(Path.GetDirectoryName(silero)!, seams);
    }

    /// <summary>Returns one word per half second of chunk, every eighth ending a sentence, and records what it was given.</summary>
    private sealed class WordPerHalfSecond : ISpeechRecognizer
    {
        private readonly Lock _lock = new();
        private int _calls;
        private int _words;
        private double _longest;

        public double MaxChunkSeconds => 30;

        public int Calls => Volatile.Read(ref _calls);

        public int Words => Volatile.Read(ref _words);

        public double LongestChunkSeconds
        {
            get
            {
                lock (_lock)
                {
                    return _longest;
                }
            }
        }

        public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _calls);
            double seconds = samples.Length / 16000.0;
            lock (_lock)
            {
                _longest = Math.Max(_longest, seconds);
            }

            int count = Math.Max(1, (int)(seconds / 0.5));
            RecognizedWord[] words = new RecognizedWord[count];
            for (int i = 0; i < count; i++)
            {
                words[i] = new RecognizedWord(i % 8 == 7 ? "word." : "word", i * 0.5, (i * 0.5) + 0.4, 0.9f);
            }

            Interlocked.Add(ref _words, count);
            return words;
        }
    }

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
