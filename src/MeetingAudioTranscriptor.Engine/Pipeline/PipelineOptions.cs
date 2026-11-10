// The settings of a transcription run: how many workers recognise, how the audio is chunked and how words become segments.
using System;
using MeetingAudioTranscriptor.Engine.Vad;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>How the pipeline runs; the defaults are those of the shipped application.</summary>
public sealed record PipelineOptions
{
    /// <summary>The most workers the default picks, a guess at the physical cores of a laptop.</summary>
    public const int MaxDefaultThreads = 4;

    /// <summary>
    /// The number of recognition workers, each calling the shared model with one intra-op thread.
    /// </summary>
    /// <remarks>
    /// The recognition model scales poorly with intra-op threads, so several workers sharing one
    /// loaded model, each on one thread, is the fastest arrangement measured. The default is the
    /// logical core count capped at four: the framework does not report physical cores, and the
    /// model's matrix products keep a core's vector units busy, so the second hyper-thread of a core
    /// adds little while still costing memory bandwidth and a chunk's worth of memory.
    /// </remarks>
    public int Threads { get; init; } = DefaultThreads;

    /// <summary>The default worker count: the logical cores, at most <see cref="MaxDefaultThreads"/>.</summary>
    public static int DefaultThreads => Math.Clamp(Environment.ProcessorCount, 1, MaxDefaultThreads);

    /// <summary>Where the audio is cut into chunks; the target length is <see cref="SpeechChunkerOptions.TargetChunkSeconds"/>.</summary>
    public SpeechChunkerOptions Chunker { get; init; } = new();

    /// <summary>How words become segments, and which segments the hallucination guard removes.</summary>
    public SegmentationOptions Segmentation { get; init; } = new();

    /// <summary>The shortest time between two progress reports of one stage; a stage change is always reported.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The chunks waiting for a worker, at most: two per worker, so a worker never waits for the
    /// producer while the recording is never read far ahead of recognition.
    /// </summary>
    public int QueueCapacity => 2 * Threads;

    /// <summary>Throws when a setting is outside its range.</summary>
    internal void Validate()
    {
        if (Threads < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Threads), Threads, "the pipeline needs at least one recognition thread; pass 1 or more");
        }

        ArgumentNullException.ThrowIfNull(Chunker);
        ArgumentNullException.ThrowIfNull(Segmentation);
        Segmentation.Validate();
    }
}

/// <summary>Where the segment builder cuts the word stream, and the guard against invented words.</summary>
/// <remarks>
/// A segment is a sentence-like stretch by one speaker: the unit the cleaner reads, a subtitle cue
/// and a line of the text transcript. The recogniser sometimes omits punctuation, which is why a
/// pause also ends a segment.
/// </remarks>
public sealed record SegmentationOptions
{
    /// <summary>A gap between two words at least this long ends a segment, in seconds.</summary>
    public double PauseSeconds { get; init; } = 1.0;

    /// <summary>A number followed by a full stop ends a segment only before a gap this long, in seconds, because "am 3. Mai" is a German date.</summary>
    public double OrdinalPauseSeconds { get; init; } = 0.3;

    /// <summary>A segment longer than this many words is split at its largest gap.</summary>
    public int MaxWords { get; init; } = 40;

    /// <summary>A segment longer than this many seconds is split at its largest gap.</summary>
    public double MaxSeconds { get; init; } = 30;

    /// <summary>
    /// A segment of at most <see cref="LowConfidenceMaxWords"/> words whose words all have a
    /// confidence below this is removed with the reason <c>low-confidence</c>; 0 switches the guard off.
    /// </summary>
    /// <remarks>
    /// A recogniser hearing noise tends to emit one or two short words it is unsure of. The guard is
    /// conservative: a longer segment, a segment holding one confident word, and a segment holding
    /// a digit are always kept, because a number in a meeting is content too precious to risk.
    /// </remarks>
    public float LowConfidenceThreshold { get; init; } = 0.3f;

    /// <summary>The most words a segment the guard may remove holds.</summary>
    public int LowConfidenceMaxWords { get; init; } = 2;

    /// <summary>Throws when a setting is outside its range.</summary>
    internal void Validate()
    {
        if (!(PauseSeconds > 0) || !(OrdinalPauseSeconds >= 0) || !(MaxSeconds > 0) || MaxWords < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxWords),
                "the segmentation needs a pause over 0 s, an ordinal pause of 0 s or more, a maximum over 0 s and a maximum of at least 2 words");
        }

        if (!(LowConfidenceThreshold >= 0 && LowConfidenceThreshold <= 1) || LowConfidenceMaxWords < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LowConfidenceThreshold),
                "the low-confidence guard needs a threshold between 0 and 1 and a word count of 0 or more; a threshold of 0 switches it off");
        }
    }
}
