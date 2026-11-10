// One piece of a recording handed to a recogniser: contiguous audio cut at silences, with where the speech lies inside it.
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Audio;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>A contiguous stretch of 16 kHz audio holding speech, cut from the stream at silences.</summary>
/// <remarks>
/// The samples are the stream's own, uncut inside the chunk (short silences included), so a time
/// inside the chunk plus <see cref="StartSample"/> is the time in the recording. Chunks never
/// overlap and come in stream order.
/// </remarks>
public sealed class SpeechChunk
{
    internal SpeechChunk(int index, long startSample, float[] samples, IReadOnlyList<(int Start, int End)> speechRegions, bool forcedCut)
    {
        Index = index;
        StartSample = startSample;
        Samples = samples;
        SpeechRegions = speechRegions;
        ForcedCut = forcedCut;
    }

    /// <summary>The chunk's position in the stream's sequence of chunks, from 0.</summary>
    public int Index { get; }

    /// <summary>The absolute sample at which the chunk starts.</summary>
    public long StartSample { get; }

    /// <summary>The chunk's samples.</summary>
    public float[] Samples { get; }

    /// <summary>The padded speech regions inside the chunk, as [Start, End) sample offsets from the chunk's start, in order.</summary>
    public IReadOnlyList<(int Start, int End)> SpeechRegions { get; }

    /// <summary>
    /// True when the chunk ends at a cut through speech because no silence was found before the
    /// recogniser's maximum length; the cut lies on the least speech-like frame of the window's last quarter.
    /// </summary>
    public bool ForcedCut { get; }

    /// <summary>The absolute sample just after the chunk.</summary>
    public long EndSample => StartSample + Samples.Length;

    /// <summary>The start in seconds from the beginning of the recording.</summary>
    public double StartSeconds => StartSample / (double)AudioFormat.ModelSampleRate;

    /// <summary>The length in seconds.</summary>
    public double DurationSeconds => Samples.Length / (double)AudioFormat.ModelSampleRate;
}
