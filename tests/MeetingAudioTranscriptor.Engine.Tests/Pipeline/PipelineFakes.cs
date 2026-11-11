// Stand-ins for the pipeline's collaborators: scripted chunks, a scripted recogniser and a scripted speaker separator.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Vad;

namespace MeetingAudioTranscriptor.Engine.Tests.Pipeline;

/// <summary>An audio source that states a duration and delivers nothing; the scripted chunk source ignores the audio.</summary>
internal sealed class SilentSource : IAudioSource
{
    public SilentSource(double? durationSeconds)
    {
        DurationSeconds = durationSeconds;
    }

    public int SampleRate => AudioFormat.ModelSampleRate;

    public double? DurationSeconds { get; }

    public int Read(Span<float> buffer) => 0;

    public void Dispose()
    {
    }
}

/// <summary>Yields chunks of one second every two seconds; each chunk's samples all hold its index plus one, so a fake recogniser knows which it got.</summary>
internal sealed class ScriptedChunkSource : IChunkSource
{
    private readonly int _count;
    private int _yielded;

    public ScriptedChunkSource(int count)
    {
        _count = count;
    }

    /// <summary>The chunk index at which the source throws, if any.</summary>
    public int? ThrowAt { get; init; }

    /// <summary>What it throws.</summary>
    public Func<Exception> Failure { get; init; } = () => new AudioFormatException("the scripted source failed");

    /// <summary>How many chunks the producer has pulled so far.</summary>
    public int Yielded => Volatile.Read(ref _yielded);

    public long TotalSamples { get; private set; }

    public long SpeechSamples { get; private set; }

    public int ForcedCuts => 0;

    public static double StartSeconds(int index) => index * 2.0;

    public IEnumerable<SpeechChunk> Run(IAudioSource source, IProgress<long>? samplesRead, CancellationToken cancellation)
    {
        for (int index = 0; index < _count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (ThrowAt == index)
            {
                throw Failure();
            }

            float[] samples = new float[16000];
            Array.Fill(samples, index + 1);
            long start = index * 32000L;
            TotalSamples = start + 32000;
            SpeechSamples += 16000;
            samplesRead?.Report(TotalSamples);
            Interlocked.Increment(ref _yielded);
            yield return new SpeechChunk(index, start, samples, [(0, 16000)], false);
        }
    }
}

/// <summary>Returns words from a script per chunk index and can be slowed, gated or made to fail.</summary>
internal sealed class ScriptedRecognizer : ISpeechRecognizer
{
    private readonly Func<int, IReadOnlyList<RecognizedWord>> _words;
    private int _started;

    public ScriptedRecognizer(Func<int, IReadOnlyList<RecognizedWord>>? words = null)
    {
        _words = words ?? TwoWords;
    }

    public double MaxChunkSeconds => 30;

    /// <summary>Milliseconds to spend on a chunk, by index.</summary>
    public Func<int, int> DelayMilliseconds { get; init; } = _ => 0;

    /// <summary>When set, every call waits for it (or for cancellation) before answering.</summary>
    public ManualResetEventSlim? Gate { get; init; }

    /// <summary>The chunk index at which it throws, if any.</summary>
    public int? ThrowAt { get; init; }

    /// <summary>How many calls have started.</summary>
    public int Started => Volatile.Read(ref _started);

    /// <summary>The chunk indexes in the order their calls finished.</summary>
    public ConcurrentQueue<int> Finished { get; } = new();

    /// <summary>"w{k}a" and "w{k}b." with confidence 0.9, at 0.1 to 0.3 s and 0.5 to 0.7 s of chunk k.</summary>
    public static IReadOnlyList<RecognizedWord> TwoWords(int index)
    {
        string k = index.ToString(CultureInfo.InvariantCulture);
        return [new("w" + k + "a", 0.1, 0.3, 0.9f), new("w" + k + "b.", 0.5, 0.7, 0.9f)];
    }

    public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation)
    {
        int index = (int)samples.Span[0] - 1;
        Interlocked.Increment(ref _started);
        Gate?.Wait(cancellation);
        if (ThrowAt == index)
        {
            throw new InvalidOperationException("the scripted recogniser failed on chunk " + index);
        }

        int delay = DelayMilliseconds(index);
        if (delay > 0)
        {
            cancellation.WaitHandle.WaitOne(delay);
            cancellation.ThrowIfCancellationRequested();
        }

        Finished.Enqueue(index);
        return _words(index);
    }
}

/// <summary>Assigns speakers by a rule over the word index and records the blocks it was handed.</summary>
internal sealed class ScriptedSeparator : ISpeakerSeparator
{
    private readonly Func<int, RecognizedWord, int> _rule;

    public ScriptedSeparator(Func<int, RecognizedWord, int> rule)
    {
        _rule = rule;
    }

    public ConcurrentBag<long> Blocks { get; } = [];

    public int? HintSeen { get; private set; }

    public int FinishCalls { get; private set; }

    public void AddBlock(ReadOnlySpan<float> samples, long startSample, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
    {
        Blocks.Add(startSample);
    }

    public SpeakerAssignment Finish(IReadOnlyList<RecognizedWord> words, int? speakerCountHint, CancellationToken cancellation)
    {
        FinishCalls++;
        HintSeen = speakerCountHint;
        int[] speakers = new int[words.Count];
        for (int i = 0; i < words.Count; i++)
        {
            speakers[i] = _rule(i, words[i]);
        }

        return new SpeakerAssignment(speakers, []);
    }
}

/// <summary>Collects progress reports on the reporting thread.</summary>
internal sealed class ProgressLog : IProgress<PipelineProgress>
{
    private readonly Lock _lock = new();

    public List<PipelineProgress> Reports { get; } = [];

    public void Report(PipelineProgress value)
    {
        lock (_lock)
        {
            Reports.Add(value);
        }
    }
}

/// <summary>Builders shared by the pipeline tests.</summary>
internal static class PipelineTestKit
{
    public static PipelineRequest Request(CleaningLanguage language = CleaningLanguage.English, int? hint = null) => new()
    {
        Language = language,
        SourceFileName = "meeting.wav",
        ApplicationVersion = "1.2.3",
        Models = [new TranscriptModel { Role = "recognition", Name = "fake" }],
        Settings = new TranscriptSettings
        {
            SpeakerSeparation = false,
            SpeakerCountHint = hint,
            Threads = 0,
            Formats = ["vtt", "json", "txt"],
            RawFiles = false,
        },
    };

    public static TranscriptionPipeline Pipeline(
        IChunkSource chunks,
        ISpeechRecognizer recognizer,
        int threads,
        Func<ISpeakerSeparator?>? separators = null,
        SegmentationOptions? segmentation = null,
        IReadOnlyList<string>? speakerNames = null) =>
        new(_ => chunks, recognizer, separators, new PipelineOptions { Threads = threads, Segmentation = segmentation ?? new SegmentationOptions(), ProgressInterval = TimeSpan.Zero, SpeakerNames = speakerNames }, null);

    public static RecognizedWord Word(string text, double start, double end, float confidence = 0.9f) => new(text, start, end, confidence);
}
