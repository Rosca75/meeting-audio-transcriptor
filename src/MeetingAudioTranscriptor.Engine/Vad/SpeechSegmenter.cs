// Turns a stream of speech probabilities into padded speech regions with the reference hysteresis, one frame at a time.
using System;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>One stretch of speech, in absolute samples.</summary>
/// <param name="Start">The padded start, inclusive.</param>
/// <param name="End">The padded end, exclusive.</param>
/// <param name="SpeechStart">The first sample of the first speech frame (a multiple of 512).</param>
/// <param name="SpeechEnd">The first sample of the silence that ended the region (a multiple of 512, or the stream's end).</param>
internal readonly record struct SpeechRegion(long Start, long End, long SpeechStart, long SpeechEnd)
{
    /// <summary>The padded length in samples.</summary>
    public long Length => End - Start;
}

/// <summary>The hysteresis of Silero's <c>get_speech_timestamps</c>, streamed.</summary>
/// <remarks>
/// <para>
/// Frame by frame, as the reference does: a frame at or above the threshold opens a region and
/// forgets any silence seen inside it; the first frame below the negative threshold marks a possible
/// end; the region closes at that mark once a later frame below the negative threshold lies at
/// least the minimum silence after it. A region no longer than the minimum speech is dropped. Raw
/// boundaries are on the 512-sample grid.
/// </para>
/// <para>
/// A closed region is reported at once, already padded: <see cref="VadOptions"/> holds the padding to
/// at most half the minimum silence, so the next region (which starts after that silence) can
/// never reach into this one's padding, and the reference's later step of splitting the gap between
/// two close regions is never needed. The padding is clipped at the stream's start and end and,
/// defensively, at the previous region's end. At the end of the stream an open region ends at its
/// possible end when one was marked (the reference ends it at the stream's end; trailing silence
/// is then trimmed rather than kept).
/// </para>
/// </remarks>
internal sealed class SpeechSegmenter
{
    private readonly float _threshold;
    private readonly float _negativeThreshold;
    private readonly long _minSpeech;
    private readonly long _minSilence;
    private readonly long _pad;

    private long _frames;
    private bool _open;
    private long _speechStart;
    private long _possibleEnd = -1;
    private long _previousEnd;
    private bool _completed;

    public SpeechSegmenter(VadOptions options)
    {
        options.Validate();
        _threshold = options.Threshold;
        _negativeThreshold = options.NegativeThreshold;
        _minSpeech = Samples(options.MinSpeechSeconds);
        _minSilence = Samples(options.MinSilenceSeconds);
        _pad = Samples(options.PadSeconds);
    }

    /// <summary>The padding in samples.</summary>
    public long Pad => _pad;

    /// <summary>The frames consumed so far.</summary>
    public long Frames => _frames;

    /// <summary>True while a region has opened and not yet closed.</summary>
    public bool IsOpen => _open;

    /// <summary>The raw start of the open region.</summary>
    public long OpenSpeechStart => _speechStart;

    /// <summary>The padded start of the open region; final, because no earlier region can move it.</summary>
    public long OpenStart => Math.Max(_previousEnd, Math.Max(0, _speechStart - _pad));

    /// <summary>The possible end of the open region (the first sample of the silence it may end at), or -1 while speech continues.</summary>
    public long OpenPossibleEnd => _open ? _possibleEnd : -1;

    /// <summary>Consumes the next frame's probability.</summary>
    /// <param name="probability">The speech probability of frame <see cref="Frames"/>.</param>
    /// <param name="region">The region this frame closed, when it closed one that is kept.</param>
    /// <returns>True when <paramref name="region"/> is set.</returns>
    public bool Push(float probability, out SpeechRegion region)
    {
        if (_completed)
        {
            throw new InvalidOperationException("the segmenter was completed; create a new one for the next stream.");
        }

        long current = _frames * SileroVadModel.FrameSamples;
        _frames++;
        region = default;

        if (probability >= _threshold)
        {
            // Speech again: a pause seen so far was only a pause.
            _possibleEnd = -1;
            if (!_open)
            {
                _open = true;
                _speechStart = current;
            }

            return false;
        }

        if (!_open || probability >= _negativeThreshold)
        {
            return false;
        }

        if (_possibleEnd < 0)
        {
            _possibleEnd = current;
        }

        if (current - _possibleEnd < _minSilence)
        {
            return false;
        }

        return Close(_possibleEnd, long.MaxValue, out region);
    }

    /// <summary>Ends the stream and closes a region still open.</summary>
    /// <param name="totalSamples">The stream's length in samples; padding never passes it.</param>
    /// <param name="region">The last region, when one was open and is kept.</param>
    /// <returns>True when <paramref name="region"/> is set.</returns>
    public bool Complete(long totalSamples, out SpeechRegion region)
    {
        region = default;
        if (_completed)
        {
            return false;
        }

        _completed = true;
        if (!_open)
        {
            return false;
        }

        long end = _possibleEnd >= 0 ? _possibleEnd : totalSamples;
        return Close(Math.Min(end, totalSamples), totalSamples, out region);
    }

    private bool Close(long speechEnd, long totalSamples, out SpeechRegion region)
    {
        long speechStart = _speechStart;
        long start = OpenStart;
        _open = false;
        _possibleEnd = -1;
        if (speechEnd - speechStart <= _minSpeech)
        {
            region = default;
            return false;
        }

        long end = Math.Min(totalSamples, speechEnd + _pad);
        _previousEnd = end;
        region = new SpeechRegion(start, end, speechStart, speechEnd);
        return true;
    }

    private static long Samples(double seconds) => (long)Math.Round(seconds * 16000);
}
