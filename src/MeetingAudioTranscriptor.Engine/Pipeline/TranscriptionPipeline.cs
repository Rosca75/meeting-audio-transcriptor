// The streaming transcription pipeline: one producer cuts the audio into chunks, several workers recognise them, one thread assembles the transcript.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Vad;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>Transcribes one recording, streaming, from samples to a <see cref="Contracts.Transcripts.TranscriptDocument"/>.</summary>
/// <remarks>
/// <para>
/// No stage waits for the whole file and the recording is never held whole. A producer thread
/// reads the source, resamples it to 16 kHz, runs the VAD and cuts chunks at silences; the chunks
/// go into a bounded queue of <see cref="PipelineOptions.QueueCapacity"/> slots, so a producer that
/// runs ahead waits for a free slot and memory stays flat whatever the length of the recording.
/// <see cref="PipelineOptions.Threads"/> workers take chunks from the queue, recognise them with
/// the one shared recogniser (with the chunk's speech regions when it is an
/// <see cref="IRegionAwareRecognizer"/>), shift the word times by the chunk's start and hand the audio to the
/// speaker separator; a chunk's samples are dropped once its worker is done. Only the words are
/// kept, by chunk index, and once the last chunk is in they are put in chunk order, assigned their
/// speakers, cut into segments and cleaned on the calling thread.
/// </para>
/// <para>
/// A failure on any thread cancels the others, and <see cref="Run"/> rethrows the original
/// exception once every thread has stopped. A cancelled run throws
/// <see cref="OperationCanceledException"/>; the workers notice at their next chunk or inside the
/// recogniser, and the producer at its next read. The source stays the caller's to dispose.
/// </para>
/// <para>
/// One pipeline instance holds no per-run state and may run several recordings, one after the other
/// or at once; a speaker separator, which does hold per-run state, is created per run by the factory.
/// </para>
/// </remarks>
public sealed class TranscriptionPipeline
{
    private readonly Func<double, IChunkSource> _chunkSources;
    private readonly ISpeechRecognizer _recognizer;
    private readonly Func<ISpeakerSeparator?> _separators;
    private readonly PipelineOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Creates a pipeline over loaded models.</summary>
    /// <param name="vad">The voice-activity model, shared between runs.</param>
    /// <param name="recognizer">The recogniser, shared between workers and runs.</param>
    /// <param name="separators">Creates the speaker separator of one run; null, or a factory returning null, runs without speakers.</param>
    /// <param name="options">The threads, the chunking and the segmentation; null for the defaults.</param>
    /// <param name="time">The clock of the timings and the metadata; null for the system clock.</param>
    public TranscriptionPipeline(
        SileroVadModel vad,
        ISpeechRecognizer recognizer,
        Func<ISpeakerSeparator?>? separators = null,
        PipelineOptions? options = null,
        TimeProvider? time = null)
        : this(MakeVadSources(vad, options), recognizer, separators, options, time)
    {
    }

    /// <summary>Creates a pipeline over any chunk source; the tests script the chunks this way.</summary>
    internal TranscriptionPipeline(
        Func<double, IChunkSource> chunkSources,
        ISpeechRecognizer recognizer,
        Func<ISpeakerSeparator?>? separators,
        PipelineOptions? options,
        TimeProvider? time)
    {
        ArgumentNullException.ThrowIfNull(chunkSources);
        ArgumentNullException.ThrowIfNull(recognizer);
        _options = options ?? new PipelineOptions();
        _options.Validate();
        _chunkSources = chunkSources;
        _recognizer = recognizer;
        _separators = separators ?? (() => null);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The settings every run uses.</summary>
    public PipelineOptions Options => _options;

    /// <summary>Transcribes one recording.</summary>
    /// <param name="source">The audio at its own sample rate, mono; the caller disposes it.</param>
    /// <param name="request">The language and the metadata facts.</param>
    /// <param name="progress">Receives the stage and the overall fraction, on the pipeline's threads; null for none.</param>
    /// <param name="cancellation">Stops the run; it then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The transcript and the run's measurements.</returns>
    public PipelineResult Run(IAudioSource source, PipelineRequest request, IProgress<PipelineProgress>? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        cancellation.ThrowIfCancellationRequested();

        long started = _time.GetTimestamp();
        ProgressTracker tracker = new(progress, _time, _options.ProgressInterval);
        tracker.Report(TranscriptionStage.Transcribing, 0, force: true);

        ISpeakerSeparator? separator = _separators();
        IChunkSource chunks = _chunkSources(_recognizer.MaxChunkSeconds);

        // The resampler is not disposed: disposing it would dispose the caller's source, and it holds only arrays.
        IAudioSource resampled = new ResamplingAudioSource(source, AudioFormat.ModelSampleRate);
        double? totalSamples = source.DurationSeconds is double seconds && seconds > 0 ? seconds * AudioFormat.ModelSampleRate : null;

        using Streaming streaming = new(this, chunks, separator, tracker, totalSamples, cancellation);
        List<RecognizedWord> words = streaming.Run(resampled);
        long streamed = _time.GetTimestamp();

        // Speakers, once every chunk has been heard.
        IReadOnlyList<int>? speakers = null;
        if (separator is not null && words.Count > 0)
        {
            tracker.Report(TranscriptionStage.IdentifyingSpeakers, 0, force: true);
            SpeakerAssignment assignment = separator.Finish(words, request.Settings.SpeakerCountHint, cancellation);
            if (assignment.SpeakerOfWord.Count != words.Count)
            {
                throw new InvalidOperationException(
                    $"the speaker separator assigned {assignment.SpeakerOfWord.Count} speakers for {words.Count} words; it must return exactly one speaker per word");
            }

            speakers = assignment.SpeakerOfWord;
            tracker.Report(TranscriptionStage.IdentifyingSpeakers, 1, force: true);
        }

        long identified = _time.GetTimestamp();
        cancellation.ThrowIfCancellationRequested();
        tracker.Report(TranscriptionStage.Cleaning, 0, force: true);

        double audioSeconds = chunks.TotalSamples / (double)AudioFormat.ModelSampleRate;
        TranscriptAssembler.Facts facts = new(
            request,
            audioSeconds,
            chunks.SpeechSamples / (double)AudioFormat.ModelSampleRate,
            request.SecondsBeforeRun + _time.GetElapsedTime(started).TotalSeconds,
            _time.GetUtcNow(),
            _options.Threads,
            separator is not null);
        Contracts.Transcripts.TranscriptDocument document = TranscriptAssembler.Build(
            words,
            speakers,
            _options.Segmentation,
            new SegmentCleaner(request.Language),
            facts,
            fraction => tracker.Report(TranscriptionStage.Cleaning, fraction * 0.99),
            cancellation);
        long finished = _time.GetTimestamp();
        tracker.Report(TranscriptionStage.Cleaning, 1, force: true);

        return new PipelineResult
        {
            Document = document,
            Timings = new PipelineTimings(
                _time.GetElapsedTime(started, finished).TotalSeconds,
                _time.GetElapsedTime(started, streamed).TotalSeconds,
                streaming.ProducerSeconds,
                streaming.RecognitionSeconds,
                _time.GetElapsedTime(streamed, identified).TotalSeconds,
                _time.GetElapsedTime(identified, finished).TotalSeconds),
            ChunkCount = streaming.ChunkCount,
            ForcedCuts = chunks.ForcedCuts,
            AudioSeconds = audioSeconds,
            SpeechSeconds = facts.SpeechSeconds,
            RecognizedWords = words.Count,
            Threads = _options.Threads,
        };
    }

    private static Func<double, IChunkSource> MakeVadSources(SileroVadModel vad, PipelineOptions? options)
    {
        ArgumentNullException.ThrowIfNull(vad);
        SpeechChunkerOptions chunker = (options ?? new PipelineOptions()).Chunker;
        return maxChunkSeconds => new VadChunkSource(vad, maxChunkSeconds, chunker);
    }

    /// <summary>The producer, the workers and their shared state for one run.</summary>
    private sealed class Streaming : IDisposable
    {
        private readonly TranscriptionPipeline _pipeline;
        private readonly IChunkSource _chunks;
        private readonly ISpeakerSeparator? _separator;
        private readonly ProgressTracker _tracker;
        private readonly double? _totalSamples;
        private readonly CancellationToken _callerToken;
        private readonly CancellationTokenSource _stop;
        private readonly BlockingCollection<SpeechChunk> _queue;

        // Guards the results and the progress bookkeeping below.
        private readonly Lock _lock = new();
        private readonly Dictionary<int, RecognizedWord[]> _results = [];
        private readonly SortedSet<long> _unfinishedStarts = [];
        private long _samplesRead;
        private int _chunksSeen;
        private int _chunksDone;

        private Exception? _failure;
        private long _producerTicks;
        private long _recognitionTicks;

        public Streaming(
            TranscriptionPipeline pipeline,
            IChunkSource chunks,
            ISpeakerSeparator? separator,
            ProgressTracker tracker,
            double? totalSamples,
            CancellationToken cancellation)
        {
            _pipeline = pipeline;
            _chunks = chunks;
            _separator = separator;
            _tracker = tracker;
            _totalSamples = totalSamples;
            _callerToken = cancellation;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            _queue = new BlockingCollection<SpeechChunk>(new ConcurrentQueue<SpeechChunk>(), pipeline._options.QueueCapacity);
        }

        public double ProducerSeconds => _producerTicks / (double)_pipeline._time.TimestampFrequency;

        public double RecognitionSeconds => Interlocked.Read(ref _recognitionTicks) / (double)_pipeline._time.TimestampFrequency;

        public int ChunkCount => _chunksSeen;

        /// <summary>Runs the producer and the workers to the end and returns the words in time order.</summary>
        public List<RecognizedWord> Run(IAudioSource resampled)
        {
            int workers = _pipeline._options.Threads;
            Thread producer = new(() => Produce(resampled)) { IsBackground = true, Name = "transcription producer" };
            Thread[] threads = new Thread[workers];
            for (int i = 0; i < workers; i++)
            {
                threads[i] = new Thread(Work) { IsBackground = true, Name = "recognition worker " + (i + 1) };
            }

            producer.Start();
            foreach (Thread thread in threads)
            {
                thread.Start();
            }

            // Every thread stops before the run returns or throws, so none outlives it.
            producer.Join();
            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            if (_failure is not null)
            {
                ExceptionDispatchInfo.Throw(_failure);
            }

            _callerToken.ThrowIfCancellationRequested();

            List<RecognizedWord> words = [];
            for (int index = 0; index < _chunksSeen; index++)
            {
                if (_results.TryGetValue(index, out RecognizedWord[]? chunkWords))
                {
                    words.AddRange(chunkWords);
                }
            }

            _tracker.Report(TranscriptionStage.Transcribing, 1, force: true);
            return words;
        }

        /// <summary>Releases the queue and the stop signal; called once every thread has stopped.</summary>
        public void Dispose()
        {
            _stop.Dispose();
            _queue.Dispose();
        }

        private void Produce(IAudioSource resampled)
        {
            CancellationToken token = _stop.Token;
            TimeProvider time = _pipeline._time;
            try
            {
                InlineProgress<long> reads = new(samples =>
                {
                    lock (_lock)
                    {
                        _samplesRead = samples;
                    }

                    ReportTranscribing();
                });
                using IEnumerator<SpeechChunk> enumerator = _chunks.Run(resampled, reads, token).GetEnumerator();
                while (true)
                {
                    // Only the time spent producing counts, never the time waiting for a free slot.
                    long before = time.GetTimestamp();
                    bool more = enumerator.MoveNext();
                    _producerTicks += time.GetTimestamp() - before;
                    if (!more)
                    {
                        break;
                    }

                    SpeechChunk chunk = enumerator.Current;
                    lock (_lock)
                    {
                        _unfinishedStarts.Add(chunk.StartSample);
                        _chunksSeen = Math.Max(_chunksSeen, chunk.Index + 1);
                    }

                    _queue.Add(chunk, token);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Fail(e);
            }
            finally
            {
                _queue.CompleteAdding();
            }
        }

        private void Work()
        {
            CancellationToken token = _stop.Token;
            TimeProvider time = _pipeline._time;
            try
            {
                foreach (SpeechChunk chunk in _queue.GetConsumingEnumerable(token))
                {
                    long before = time.GetTimestamp();
                    // A recogniser that levels each speech region on its own is handed the chunk's regions.
                    IReadOnlyList<RecognizedWord> found = _pipeline._recognizer is IRegionAwareRecognizer regionAware
                        ? regionAware.Recognize(chunk.Samples, chunk.SpeechRegions, token)
                        : _pipeline._recognizer.Recognize(chunk.Samples, token);
                    RecognizedWord[] shifted = Shift(found, chunk.StartSeconds);
                    _separator?.AddBlock(chunk.Samples, chunk.StartSample, chunk.SpeechRegions, token);
                    Interlocked.Add(ref _recognitionTicks, time.GetTimestamp() - before);

                    lock (_lock)
                    {
                        _results[chunk.Index] = shifted;
                        _unfinishedStarts.Remove(chunk.StartSample);
                        _chunksDone++;
                    }

                    ReportTranscribing();
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Fail(e);
            }
        }

        /// <summary>Records the first real failure and stops every thread; a cancellation it caused is not a failure.</summary>
        private void Fail(Exception e)
        {
            if (e is OperationCanceledException && _stop.IsCancellationRequested)
            {
                return;
            }

            Interlocked.CompareExchange(ref _failure, e, null);
            try
            {
                _stop.Cancel();
            }
            catch (AggregateException)
            {
                // A callback registered on the caller's token failed; the original failure is what the caller needs.
            }
        }

        /// <summary>
        /// Reports how far recognition has come: up to the earliest chunk not yet recognised, or
        /// everything read when no chunk is waiting, as a share of the recording's length; by chunk
        /// count when the length is unknown.
        /// </summary>
        private void ReportTranscribing()
        {
            double fraction;
            lock (_lock)
            {
                if (_totalSamples is double total)
                {
                    long position = _unfinishedStarts.Count > 0 ? Math.Min(_unfinishedStarts.Min, _samplesRead) : _samplesRead;
                    fraction = position / total;
                }
                else
                {
                    fraction = _chunksSeen == 0 ? 0 : _chunksDone / (double)(_chunksSeen + 1);
                }
            }

            // The run is not done until the last chunk is in, so the stage stops short of its end.
            _tracker.Report(TranscriptionStage.Transcribing, Math.Min(fraction, 0.999));
        }

        private static RecognizedWord[] Shift(IReadOnlyList<RecognizedWord> found, double offsetSeconds)
        {
            List<RecognizedWord> shifted = new(found.Count);
            foreach (RecognizedWord word in found)
            {
                // A word with no text has nothing to show and nothing to clean.
                if (string.IsNullOrWhiteSpace(word.Text))
                {
                    continue;
                }

                shifted.Add(word with { Start = word.Start + offsetSeconds, End = word.End + offsetSeconds });
            }

            return shifted.ToArray();
        }
    }
}
