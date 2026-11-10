// Where the chunker cuts: the silences that end a chunk and the length it aims for.
using System;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>The cutting policy of <see cref="SpeechChunker"/>; the hard limit is the recogniser's maximum, passed separately.</summary>
public sealed record SpeechChunkerOptions
{
    /// <summary>
    /// The length a chunk aims for, in seconds. Once a chunk reaches it, the chunk ends at the
    /// longest silence of at least <see cref="MinCutSilenceSeconds"/> in its second half, or at the
    /// next such silence. A parameter because the best length depends on the recogniser.
    /// </summary>
    public double TargetChunkSeconds { get; init; } = 20;

    /// <summary>
    /// A silence at least this long always ends a chunk and is never inside one, in seconds, so a
    /// recogniser is not handed long stretches of nothing.
    /// </summary>
    public double SplitSilenceSeconds { get; init; } = 0.6;

    /// <summary>The shortest silence a chunk may be cut in before it reaches the maximum, in seconds.</summary>
    public double MinCutSilenceSeconds { get; init; } = 0.2;

    /// <summary>The segmentation of probabilities into speech regions.</summary>
    public VadOptions Vad { get; init; } = new();

    /// <summary>Throws when a setting is outside its range.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Vad);
        Vad.Validate();
        if (!(TargetChunkSeconds > 0) || !(SplitSilenceSeconds > 0) || !(MinCutSilenceSeconds > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(TargetChunkSeconds), "the chunk target, the split silence and the shortest cut silence must all be more than zero seconds.");
        }

        if (MinCutSilenceSeconds > SplitSilenceSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinCutSilenceSeconds),
                $"the shortest cut silence ({MinCutSilenceSeconds} s) must not exceed the split silence ({SplitSilenceSeconds} s); a silence that always ends a chunk is also one it may be cut in.");
        }
    }
}
