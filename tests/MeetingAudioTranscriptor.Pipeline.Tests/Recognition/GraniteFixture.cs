// The Granite model, its vocabulary and the benchmark clips as the recognition tests share them: loaded once, skipping by name when absent.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Recognition.Granite;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Engine.Vad;
using MeetingAudioTranscriptor.Inference;
using Xunit;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Recognition;

/// <summary>One benchmark file with its reference: a group name, the language and the path relative to the data folder.</summary>
internal sealed record BenchClip(string Group, string Language, string RelativePath)
{
    /// <summary>The file name without extension, which names the clip in a report.</summary>
    public string Name => Path.GetFileNameWithoutExtension(RelativePath);
}

/// <summary>Loads the shared Granite graph, the VAD and the clips, and runs a clip through chunking and recognition.</summary>
/// <remarks>
/// The graph is loaded once per test process because it is half a gigabyte and every test reads
/// the same one; ONNX Runtime sessions are safe to share. Chunks are cached per clip and cutting
/// policy, because the VAD's cut does not depend on the recogniser's settings.
/// </remarks>
internal static class GraniteFixture
{
    public const string ModelName = "granite-speech-5.0-470m-turboctc";

    /// <summary>The read-speech clips per language and the AMI meeting clips.</summary>
    public static readonly BenchClip[] Clips =
    [
        new("en-read", "en", "clips/en/en-multi-01.wav"),
        new("en-read", "en", "clips/en/en-single-01.wav"),
        new("en-read", "en", "clips/en/en-single-02.wav"),
        new("en-ami", "en", "clips/en/en-ami-01.wav"),
        new("en-ami", "en", "clips/en/en-ami-02.wav"),
        new("en-ami", "en", "clips/en/en-ami-sdm-01.wav"),
        new("fr", "fr", "clips/fr/fr-multi-01.wav"),
        new("fr", "fr", "clips/fr/fr-multi-02.wav"),
        new("fr", "fr", "clips/fr/fr-multi-03.wav"),
        new("fr", "fr", "clips/fr/fr-single-01.wav"),
        new("fr", "fr", "clips/fr/fr-single-02.wav"),
        new("de", "de", "clips/de/de-multi-01.wav"),
        new("de", "de", "clips/de/de-multi-02.wav"),
        new("de", "de", "clips/de/de-multi-03.wav"),
        new("de", "de", "clips/de/de-single-01.wav"),
        new("de", "de", "clips/de/de-single-02.wav"),
    ];

    /// <summary>The three ten-minute meeting-style files.</summary>
    public static readonly BenchClip[] LongFiles =
    [
        new("en-10min", "en", "long/en-10min.wav"),
        new("fr-10min", "fr", "long/fr-10min.wav"),
        new("de-10min", "de", "long/de-10min.wav"),
    ];

    private static readonly Lock Gate = new();
    private static readonly ConcurrentDictionary<string, List<SpeechChunk>> ChunkCache = new();
    private static readonly ConcurrentDictionary<string, float[]> AudioCache = new();
    private static ModelCatalog? _catalog;
    private static ITensorRunner? _model;
    private static TimingRunner? _timed;
    private static BpeVocabulary? _vocabulary;
    private static ITensorRunner? _vad;

    /// <summary>The catalog over the models folder the suite reads.</summary>
    public static ModelCatalog Catalog
    {
        get
        {
            lock (Gate)
            {
                return _catalog ??= ModelCatalog.FromLockFile(ModelCatalog.ResolveRoot(), Path.Combine(TestPaths.RepositoryRoot, "build", "models.lock"));
            }
        }
    }

    /// <summary>The derived graph, timed; the test skips when it is not installed.</summary>
    public static TimingRunner Model
    {
        get
        {
            RequireGranite();
            lock (Gate)
            {
                _model ??= OrtRunnerFactory.Open(Catalog.ResolveRole(ModelName, "model"), new RunnerOptions());
                return _timed ??= new TimingRunner(_model);
            }
        }
    }

    /// <summary>The vocabulary of the graph's tokenizer.json.</summary>
    public static BpeVocabulary Vocabulary
    {
        get
        {
            RequireGranite();
            lock (Gate)
            {
                if (_vocabulary is null)
                {
                    using FileStream stream = File.OpenRead(Catalog.ResolveRole(ModelName, "tokenizer"));
                    _vocabulary = BpeVocabulary.Load(stream);
                }

                return _vocabulary;
            }
        }
    }

    /// <summary>A recogniser over the shared graph.</summary>
    public static GraniteCtcRecognizer Recognizer(GraniteOptions? options = null) => new(Model, Vocabulary, options);

    /// <summary>Skips the test unless the derived graph and the tokenizer are installed.</summary>
    public static void RequireGranite()
    {
        IReadOnlyList<string> missing = Catalog.MissingFiles(ModelName);
        Assert.SkipUnless(
            missing.Count == 0,
            $"needs the {ModelName} model in {Catalog.Root} (missing {string.Join(", ", missing)}); run: node build/fetch-models.mjs --into {Catalog.Root}");
    }

    /// <summary>A clip's samples, read once.</summary>
    public static float[] Audio(BenchClip clip) =>
        AudioCache.GetOrAdd(clip.RelativePath, relative => TestAudio.ReadWav(TestPaths.RequireBenchFile(relative)));

    /// <summary>A clip's reference text.</summary>
    public static string Reference(BenchClip clip) =>
        File.ReadAllText(Path.ChangeExtension(TestPaths.RequireBenchFile(clip.RelativePath), ".ref.txt"));

    /// <summary>The VAD stage's chunks of a clip for one cutting policy, computed once.</summary>
    public static List<SpeechChunk> Chunks(BenchClip clip, double maxChunkSeconds, double targetChunkSeconds)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"{clip.RelativePath}|{maxChunkSeconds}|{targetChunkSeconds}");
        float[] audio = Audio(clip);
        return ChunkCache.GetOrAdd(key, _ =>
        {
            ITensorRunner vad;
            lock (Gate)
            {
                vad = _vad ??= OrtRunnerFactory.Open(TestPaths.RequireSileroModel(), new RunnerOptions());
            }

            VadChunkingStage stage = new(new SileroVadModel(vad), maxChunkSeconds, new SpeechChunkerOptions { TargetChunkSeconds = targetChunkSeconds });
            return stage.Run(new ArrayAudioSource(audio, 16384), null, CancellationToken.None).ToList();
        });
    }

    /// <summary>Recognises every chunk of a clip and returns the words with times in the recording.</summary>
    public static List<RecognizedWord> Transcribe(IRegionAwareRecognizer recognizer, IReadOnlyList<SpeechChunk> chunks, CancellationToken cancellation)
    {
        List<RecognizedWord> words = [];
        foreach (SpeechChunk chunk in chunks)
        {
            foreach (RecognizedWord word in recognizer.Recognize(chunk.Samples, chunk.SpeechRegions, cancellation))
            {
                words.Add(word with { Start = word.Start + chunk.StartSeconds, End = word.End + chunk.StartSeconds });
            }
        }

        return words;
    }

    /// <summary>The word error rate of words against a reference, after the scoring normalisation.</summary>
    public static WordErrorRateResult Score(IEnumerable<RecognizedWord> words, string reference) =>
        WordErrorRate.Compute(reference, string.Join(' ', words.Select(word => word.Text)));

    /// <summary>Writes a line to the test output, and appends it to the file MAT_GRANITE_REPORT names when set.</summary>
    public static void Report(string line)
    {
        TestContext.Current.SendDiagnosticMessage(line);
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        string? path = Environment.GetEnvironmentVariable("MAT_GRANITE_REPORT");
        if (!string.IsNullOrWhiteSpace(path))
        {
            lock (Gate)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
    }
}

/// <summary>Wraps a runner and adds up the wall time spent inside it, so a measurement can split model time from decoding time.</summary>
internal sealed class TimingRunner : ITensorRunner
{
    private readonly ITensorRunner _inner;
    private long _ticks;
    private long _runs;

    public TimingRunner(ITensorRunner inner)
    {
        _inner = inner;
    }

    /// <summary>The time spent in the model since the last reset.</summary>
    public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _ticks) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

    /// <summary>The model runs since the last reset.</summary>
    public long Runs => Interlocked.Read(ref _runs);

    public void Reset()
    {
        Interlocked.Exchange(ref _ticks, 0);
        Interlocked.Exchange(ref _runs, 0);
    }

    public TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return _inner.Run(inputs, outputNames, cancellation);
        }
        finally
        {
            Interlocked.Add(ref _ticks, Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref _runs);
        }
    }

    public void Dispose()
    {
        // The shared graph lives for the test process.
    }
}
