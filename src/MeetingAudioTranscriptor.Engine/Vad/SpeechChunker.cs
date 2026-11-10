// Cuts a 16 kHz stream into recogniser-sized chunks at silences, from the samples and their speech probabilities, as both stream in.
using System;
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Audio;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>Turns samples and their frame probabilities into <see cref="SpeechChunk"/>s, never longer than the recogniser's maximum.</summary>
/// <remarks>
/// <para>
/// The chunker runs the <see cref="SpeechSegmenter"/> itself, because a cut must be decided while a
/// long region is still open (a monologue can outlast any chunk) and a forced cut needs every
/// frame's probability, neither of which a list of closed regions carries.
/// </para>
/// <para>
/// The policy, checked after every frame:
/// a chunk starts at the padded start of a speech region and grows across the silences between
/// regions; a silence of at least <see cref="SpeechChunkerOptions.SplitSilenceSeconds"/> ends it at
/// the padded end of the region before it, so leading and trailing silence beyond the padding never
/// reaches a recogniser. Once the chunk reaches <see cref="SpeechChunkerOptions.TargetChunkSeconds"/>
/// it is cut at the longest closed silence of at least
/// <see cref="SpeechChunkerOptions.MinCutSilenceSeconds"/> in its second half (a gap between regions,
/// cut at their padded edges, or a pause inside a region, cut in its middle). When the next frame
/// would take it past the maximum it is cut at the latest such silence, and when there is none, at
/// the frame with the lowest probability in the last quarter of the window, marked
/// <see cref="SpeechChunk.ForcedCut"/>. A region no longer than the minimum speech never opens a chunk.
/// </para>
/// <para>
/// Samples are appended before the probabilities that describe them (the VAD answers a block
/// later), and only the current chunk and the padding before a possible next region are held, so
/// the memory held is bounded by about the maximum chunk plus the VAD's block, whatever the
/// recording's length. A chunker serves one stream and is used by one thread at a time.
/// </para>
/// </remarks>
public sealed class SpeechChunker
{
    private const int Frame = SileroVadModel.FrameSamples;

    private readonly long _maxSamples;
    private readonly long _targetSamples;
    private readonly long _splitSilence;
    private readonly long _minCutSilence;
    private readonly float _negativeThreshold;
    private readonly SpeechSegmenter _segmenter;

    private readonly SlidingBuffer _samples;
    private readonly SlidingBuffer _probabilities;
    private readonly List<float> _waiting = [];
    private readonly List<SpeechRegion> _regions = [];

    /// <summary>Closed pauses below the negative threshold at least the shortest cut silence long, as [start, end) samples.</summary>
    private readonly List<(long Start, long End)> _pauses = [];

    private long _pauseStartFrame = -1;
    private long _chunkStart = -1;
    private long _frames;
    private int _index;
    private bool _completed;

    /// <summary>Creates a chunker for one stream.</summary>
    /// <param name="maxChunkSeconds">The longest chunk the recogniser reads (<c>ISpeechRecognizer.MaxChunkSeconds</c>); at least 2 seconds.</param>
    /// <param name="options">The cutting policy; null takes the defaults.</param>
    public SpeechChunker(double maxChunkSeconds, SpeechChunkerOptions? options = null)
    {
        if (!(maxChunkSeconds >= 2))
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkSeconds), maxChunkSeconds, "the maximum chunk must be at least 2 seconds; pass the recogniser's MaxChunkSeconds.");
        }

        Options = options ?? new SpeechChunkerOptions();
        Options.Validate();
        MaxChunkSeconds = maxChunkSeconds;
        _maxSamples = Samples(maxChunkSeconds);
        _targetSamples = Math.Min(_maxSamples, Samples(Options.TargetChunkSeconds));
        _splitSilence = Samples(Options.SplitSilenceSeconds);
        _minCutSilence = Samples(Options.MinCutSilenceSeconds);
        _negativeThreshold = Options.Vad.NegativeThreshold;
        _segmenter = new SpeechSegmenter(Options.Vad);
        _samples = new SlidingBuffer((int)_maxSamples * 2);
        _probabilities = new SlidingBuffer((int)(_maxSamples / Frame) * 2);
    }

    /// <summary>The cutting policy.</summary>
    public SpeechChunkerOptions Options { get; }

    /// <summary>The longest chunk, in seconds.</summary>
    public double MaxChunkSeconds { get; }

    /// <summary>The samples appended so far.</summary>
    public long TotalSamples => _samples.End;

    /// <summary>The samples inside padded speech regions of the chunks emitted so far.</summary>
    public long SpeechSamples { get; private set; }

    /// <summary>The samples of the chunks emitted so far, the silences inside them included.</summary>
    public long ChunkedSamples { get; private set; }

    /// <summary>The chunks emitted so far.</summary>
    public int ChunkCount => _index;

    /// <summary>The chunks emitted so far that end at a forced cut.</summary>
    public int ForcedCuts { get; private set; }

    /// <summary>The most samples held at once, for measurements and the memory bound's test.</summary>
    internal int PeakBufferedSamples { get; private set; }

    /// <summary>Appends the next samples of the stream; they must arrive no later than their probabilities.</summary>
    public void AppendSamples(ReadOnlySpan<float> samples)
    {
        ThrowIfCompleted();
        _samples.Append(samples);
        PeakBufferedSamples = Math.Max(PeakBufferedSamples, _samples.Count);
    }

    /// <summary>Appends the next frames' probabilities and adds every chunk they close to <paramref name="chunks"/>.</summary>
    /// <param name="probabilities">One probability per 512-sample frame, in frame order, continuing the previous call.</param>
    /// <param name="chunks">Receives the chunks closed, in order.</param>
    public void AppendProbabilities(ReadOnlySpan<float> probabilities, List<SpeechChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ThrowIfCompleted();
        ProcessWaiting(chunks, completing: false);
        foreach (float probability in probabilities)
        {
            // A frame is processed once all its samples are held; the last, partial frame waits for Complete.
            if (_waiting.Count > 0 || (_frames + 1) * Frame > _samples.End)
            {
                _waiting.Add(probability);
            }
            else
            {
                Step(probability, chunks);
            }
        }
    }

    /// <summary>Ends the stream: processes the last frame, closes an open region and emits the remaining chunks.</summary>
    /// <param name="chunks">Receives the remaining chunks, in order.</param>
    /// <exception cref="InvalidOperationException">The probabilities do not cover the samples, one per started frame.</exception>
    public void Complete(List<SpeechChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ThrowIfCompleted();
        ProcessWaiting(chunks, completing: true);
        long expectedFrames = (_samples.End + Frame - 1) / Frame;
        if (_frames != expectedFrames)
        {
            throw new InvalidOperationException(
                $"the chunker received {_frames} probabilities for {_samples.End} samples, which need {expectedFrames} (one per started 512-sample frame); flush the VAD stream and pass all its probabilities before Complete.");
        }

        _completed = true;
        if (_segmenter.Complete(_samples.End, out SpeechRegion last))
        {
            _regions.Add(last);
        }

        Decide(_frames * Frame, completing: true, chunks);
        _samples.DiscardBefore(_samples.End);
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("the chunker was completed; create a new SpeechChunker for the next stream.");
        }
    }

    private void ProcessWaiting(List<SpeechChunk> chunks, bool completing)
    {
        int used = 0;
        while (used < _waiting.Count)
        {
            bool full = (_frames + 1) * Frame <= _samples.End;
            bool started = _frames * Frame < _samples.End;
            if (!full && !(completing && started))
            {
                if (completing)
                {
                    throw new InvalidOperationException(
                        $"the chunker received more probabilities than the {_samples.End} samples have frames; pass one probability per 512-sample frame.");
                }

                break;
            }

            Step(_waiting[used], chunks);
            used++;
        }

        _waiting.RemoveRange(0, used);
    }

    /// <summary>Consumes one frame's probability and makes every decision it allows.</summary>
    private void Step(float probability, List<SpeechChunk> chunks)
    {
        long frame = _frames++;
        _probabilities.Append(probability);

        // Pauses inside speech are the preferred places to cut a long region.
        if (probability < _negativeThreshold)
        {
            if (_pauseStartFrame < 0)
            {
                _pauseStartFrame = frame;
            }
        }
        else if (_pauseStartFrame >= 0)
        {
            if ((frame - _pauseStartFrame) * Frame >= _minCutSilence)
            {
                _pauses.Add((_pauseStartFrame * Frame, frame * Frame));
            }

            _pauseStartFrame = -1;
        }

        if (_segmenter.Push(probability, out SpeechRegion region))
        {
            _regions.Add(region);
        }

        long position = (frame + 1) * Frame;
        Decide(position, completing: false, chunks);
        DiscardUnneeded(position);
    }

    /// <summary>Emits every chunk the frames up to <paramref name="position"/> decide.</summary>
    private void Decide(long position, bool completing, List<SpeechChunk> chunks)
    {
        while (true)
        {
            bool open = _segmenter.IsOpen;
            if (_regions.Count == 0 && !open)
            {
                // Nothing to emit; a chunk opened by a region that was then dropped as too short is forgotten.
                _chunkStart = -1;
                return;
            }

            if (_chunkStart < 0)
            {
                if (_regions.Count > 0)
                {
                    _chunkStart = _regions[0].Start;
                }
                else
                {
                    _chunkStart = _segmenter.OpenStart;
                }
            }

            // A long silence between two closed regions ends the chunk at the first one.
            int split = FirstLongGap();
            if (split >= 0)
            {
                chunks.Add(Emit(_regions[split].End, forced: false));
                continue;
            }

            if (_regions.Count > 0)
            {
                // The next speech starts no earlier than the open region, or than the next frame.
                long nextSpeech = open ? _segmenter.OpenSpeechStart : position;
                if (completing || nextSpeech - _regions[^1].SpeechEnd >= _splitSilence)
                {
                    chunks.Add(Emit(_regions[^1].End, forced: false));
                    continue;
                }
            }

            long extent = open ? position : _regions[^1].End;
            if (extent - _chunkStart >= _targetSamples && TryCutAtSilence(_chunkStart + (_targetSamples / 2), preferLongest: true, chunks))
            {
                continue;
            }

            if (!completing && position + Frame - _chunkStart > _maxSamples)
            {
                // The next frame could pass the maximum: cut now.
                if (!open)
                {
                    // The chunk already ends in a silence of at least the minimum silence.
                    chunks.Add(Emit(_regions[^1].End, forced: false));
                }
                else if (!TryCutAtSilence(_chunkStart + 1, preferLongest: false, chunks))
                {
                    chunks.Add(Emit(LeastSpeechLikeCut(position), forced: true));
                }

                continue;
            }

            return;
        }
    }

    /// <summary>The index of the first closed region followed by a gap of at least the split silence, or -1.</summary>
    private int FirstLongGap()
    {
        for (int i = 0; i + 1 < _regions.Count; i++)
        {
            if (_regions[i + 1].SpeechStart - _regions[i].SpeechEnd >= _splitSilence)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Cuts the chunk in a silence of at least the shortest cut silence whose cut point is at or after <paramref name="earliestCut"/>.</summary>
    /// <param name="earliestCut">The earliest absolute cut point allowed.</param>
    /// <param name="preferLongest">Picks the longest silence (then the latest) when true, and the latest when false.</param>
    /// <param name="chunks">Receives the chunk when one is cut.</param>
    /// <returns>True when a chunk was cut.</returns>
    private bool TryCutAtSilence(long earliestCut, bool preferLongest, List<SpeechChunk> chunks)
    {
        long bestCut = -1;
        long bestLength = -1;

        void Consider(long cut, long length)
        {
            if (cut < earliestCut || cut <= _chunkStart || length < _minCutSilence)
            {
                return;
            }

            bool better = preferLongest
                ? length > bestLength || (length == bestLength && cut > bestCut)
                : cut > bestCut;
            if (better)
            {
                bestCut = cut;
                bestLength = length;
            }
        }

        // Gaps between regions: the chunk ends at the earlier region's padded end.
        for (int i = 0; i + 1 < _regions.Count; i++)
        {
            Consider(_regions[i].End, _regions[i + 1].SpeechStart - _regions[i].SpeechEnd);
        }

        bool open = _segmenter.IsOpen;
        if (open && _regions.Count > 0)
        {
            Consider(_regions[^1].End, _segmenter.OpenSpeechStart - _regions[^1].SpeechEnd);
        }

        // Pauses inside a region: the cut lies in the pause's middle.
        foreach ((long start, long end) in _pauses)
        {
            if (start >= _chunkStart && IsInsideSpeech(start, end, open))
            {
                Consider((start + end) / 2, end - start);
            }
        }

        if (bestCut < 0)
        {
            return false;
        }

        chunks.Add(Emit(bestCut, forced: false));
        return true;
    }

    /// <summary>True when a pause lies inside one region's raw speech, so cutting in it splits no silence that ends a region.</summary>
    private bool IsInsideSpeech(long start, long end, bool open)
    {
        foreach (SpeechRegion region in _regions)
        {
            if (region.SpeechStart <= start && end <= region.SpeechEnd)
            {
                return true;
            }
        }

        if (open && start >= _segmenter.OpenSpeechStart)
        {
            // A pause after the open region's possible end may turn out to be the silence that closes it.
            long possibleEnd = _segmenter.OpenPossibleEnd;
            return possibleEnd < 0 || end <= possibleEnd;
        }

        return false;
    }

    /// <summary>The middle of the least speech-like frame in the last quarter of the window, where a forced cut goes.</summary>
    private long LeastSpeechLikeCut(long position)
    {
        long windowEnd = Math.Min(position, _chunkStart + _maxSamples);
        long firstFrame = Math.Max(_probabilities.Start, (_chunkStart + (_maxSamples * 3 / 4) + Frame - 1) / Frame);
        long lastFrame = (windowEnd / Frame) - 1;
        long best = -1;
        float bestProbability = float.MaxValue;
        for (long frame = firstFrame; frame <= lastFrame; frame++)
        {
            float probability = _probabilities[frame];

            // Ties go to the later frame, which keeps the chunk longer.
            if (probability <= bestProbability)
            {
                bestProbability = probability;
                best = frame;
            }
        }

        return best < 0 ? windowEnd : (best * Frame) + (Frame / 2);
    }

    /// <summary>Emits the chunk from the current start to <paramref name="end"/> and moves the start past it.</summary>
    private SpeechChunk Emit(long end, bool forced)
    {
        long start = _chunkStart;
        end = Math.Min(end, _samples.End);
        if (end <= start || end - start > _maxSamples)
        {
            throw new InvalidOperationException($"the chunker computed the chunk [{start}, {end}), which is empty or longer than {_maxSamples} samples; this is a defect in the chunker.");
        }

        bool open = _segmenter.IsOpen;
        List<(int Start, int End)> speech = [];
        long speechSamples = 0;
        foreach (SpeechRegion region in _regions)
        {
            AddClipped(region.Start, region.End);
        }

        if (open)
        {
            AddClipped(_segmenter.OpenStart, end);
        }

        float[] samples = _samples.Slice(start, end).ToArray();
        SpeechChunk chunk = new(_index++, start, samples, speech, forced);
        SpeechSamples += speechSamples;
        ChunkedSamples += samples.Length;
        if (forced)
        {
            ForcedCuts++;
        }

        // Regions wholly inside the chunk are done; a region the cut went through continues in the next chunk.
        _regions.RemoveAll(region => region.End <= end);
        bool continues = (_regions.Count > 0 && _regions[0].Start < end) || (open && _segmenter.OpenStart < end);
        _chunkStart = continues ? end : -1;
        return chunk;

        void AddClipped(long regionStart, long regionEnd)
        {
            long from = Math.Max(regionStart, start);
            long to = Math.Min(regionEnd, end);
            if (to > from)
            {
                speech.Add(((int)(from - start), (int)(to - start)));
                speechSamples += to - from;
            }
        }
    }

    /// <summary>Forgets the samples, probabilities and pauses no future chunk can contain.</summary>
    private void DiscardUnneeded(long position)
    {
        // Without a chunk in progress, the next region's padded start is at least one padding before
        // the next frame.
        long keepFrom = _chunkStart >= 0 ? _chunkStart : Math.Max(0, position - _segmenter.Pad);
        _samples.DiscardBefore(keepFrom);
        _probabilities.DiscardBefore(keepFrom / Frame);
        int stale = 0;
        while (stale < _pauses.Count && _pauses[stale].Start < keepFrom)
        {
            stale++;
        }

        _pauses.RemoveRange(0, stale);
    }

    private static long Samples(double seconds) => (long)Math.Round(seconds * AudioFormat.ModelSampleRate);
}
