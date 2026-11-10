// The seam where speaker separation joins the pipeline: audio blocks in while recognition runs, one speaker per word out at the end.
using System;
using System.Collections.Generic;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>Tells the speakers of one recording apart.</summary>
/// <remarks>
/// <para>
/// One instance serves one recording. The recognition workers hand it every chunk as soon as the
/// chunk is recognised, so it can compute its embeddings while recognition is still running and the
/// recording is never held whole. Several workers call <see cref="AddBlock"/> at once and in any
/// order, so an implementation is thread-safe and orders the blocks by their start sample itself,
/// which keeps its answer the same whatever the thread count.
/// </para>
/// <para>
/// <see cref="Finish"/> is called once, after the last block, from one thread.
/// </para>
/// </remarks>
public interface ISpeakerSeparator
{
    /// <summary>Receives one chunk of 16 kHz audio; the span is only valid during the call.</summary>
    /// <param name="samples">The chunk's samples.</param>
    /// <param name="startSample">The absolute sample at which the chunk starts.</param>
    /// <param name="speechRegions">The padded speech regions inside the chunk, as [Start, End) offsets from its start.</param>
    void AddBlock(ReadOnlySpan<float> samples, long startSample, IReadOnlyList<(int Start, int End)> speechRegions);

    /// <summary>Assigns a speaker to every word.</summary>
    /// <param name="words">Every recognised word of the recording, in time order, with times in seconds from its start.</param>
    /// <param name="speakerCountHint">The number of speakers the user stated; null to let the separator decide.</param>
    /// <param name="cancellation">Stops the clustering when the user cancels.</param>
    /// <returns>One speaker index per word and the seconds each speaker spoke.</returns>
    SpeakerAssignment Finish(IReadOnlyList<RecognizedWord> words, int? speakerCountHint, CancellationToken cancellation);
}

/// <summary>The answer of a speaker separator.</summary>
/// <param name="SpeakerOfWord">One 0-based speaker index per word, numbered in order of first appearance.</param>
/// <param name="SpeakingSeconds">The seconds each speaker spoke, by speaker index.</param>
public sealed record SpeakerAssignment(IReadOnlyList<int> SpeakerOfWord, IReadOnlyList<double> SpeakingSeconds);
