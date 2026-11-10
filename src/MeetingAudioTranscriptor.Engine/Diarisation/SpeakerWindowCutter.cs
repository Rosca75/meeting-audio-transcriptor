// Cuts one speech region into the sliding windows a speaker embedding is computed for.
using System;
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>Places embedding windows over a speech region.</summary>
/// <remarks>
/// <para>
/// Windows start every hop from the region's start. When the last full window stops short of the
/// region's end, one more window is placed flush with the end, so the end of every utterance is
/// heard by a window of full length rather than by a short one whose embedding is noisier.
/// A region shorter than the window but at least the minimum gets one window covering all of it; a
/// shorter one gets none (it is too short for a reliable voice print, and the timeline gives it the
/// speaker nearest in time).
/// </para>
/// <para>
/// Every start and length is a multiple of the 10 ms feature shift, so the features of a window are
/// exactly a slice of the features of its region and are computed once per region.
/// </para>
/// </remarks>
internal static class SpeakerWindowCutter
{
    /// <summary>The window placements in a region, as (start, length) in samples relative to the region.</summary>
    /// <param name="regionLength">The region's length in samples.</param>
    /// <param name="windowSamples">The full window length in samples.</param>
    /// <param name="hopSamples">The step between window starts in samples.</param>
    /// <param name="minimumSamples">The shortest region that still gets a window.</param>
    public static List<(int Start, int Length)> Cut(int regionLength, int windowSamples, int hopSamples, int minimumSamples)
    {
        int shift = KaldiFbank.FrameShift;
        int window = Align(windowSamples);
        int hop = Math.Max(shift, Align(hopSamples));
        List<(int Start, int Length)> windows = [];
        if (regionLength < Math.Max(minimumSamples, KaldiFbank.FrameLength))
        {
            return windows;
        }

        if (regionLength <= window)
        {
            windows.Add((0, Align(regionLength)));
            return windows;
        }

        int start = 0;
        for (; start + window <= regionLength; start += hop)
        {
            windows.Add((start, window));
        }

        int lastEnd = windows[^1].Start + window;
        int flushStart = Align(regionLength - window);
        if (regionLength - lastEnd >= shift && flushStart > windows[^1].Start)
        {
            windows.Add((flushStart, window));
        }

        return windows;
    }

    // Rounds down to the feature shift, so a window holds whole frames from a frame boundary.
    private static int Align(int samples)
    {
        return samples / KaldiFbank.FrameShift * KaldiFbank.FrameShift;
    }
}
