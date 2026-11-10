// The thresholds and durations that turn speech probabilities into speech regions.
using System;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>The hysteresis of Silero's reference <c>get_speech_timestamps</c>, tuned for meetings.</summary>
/// <remarks>
/// <para>
/// A region opens on a frame whose probability reaches <see cref="Threshold"/> and closes once the
/// probability has stayed below <see cref="NegativeThreshold"/> for <see cref="MinSilenceSeconds"/>;
/// a frame between the two thresholds neither opens nor closes. The reference's own defaults (100 ms
/// silence, 30 ms padding) cut between words of fluent speech, so the silence and the padding are
/// longer here, as the Silero authors advise for long recordings.
/// </para>
/// <para>
/// The thresholds and the minimum speech are lower than the reference's (0.5, 0.35 and 0.25 s),
/// because a word the VAD drops is lost to the transcript while a stretch of noise it keeps only
/// costs recognition time: on the AMI meeting excerpts the reference settings miss quiet, short
/// backchannels ("mm-hmm", "yeah") and keep 96.5 % of the audible reference speech of one excerpt,
/// these keep at least 99.1 % of every multi-speaker clip, and white, pink and office noise and
/// digital silence still yield no region.
/// </para>
/// </remarks>
public sealed record VadOptions
{
    /// <summary>The probability at or above which a frame is speech.</summary>
    public float Threshold { get; init; } = 0.4f;

    /// <summary>The probability below which a frame counts towards ending a region; the threshold minus 0.15, as in the reference.</summary>
    public float NegativeThreshold { get; init; } = 0.25f;

    /// <summary>A region must be longer than this to be kept, in seconds.</summary>
    public double MinSpeechSeconds { get; init; } = 0.1;

    /// <summary>The silence that ends a region, in seconds; shorter pauses stay inside it.</summary>
    public double MinSilenceSeconds { get; init; } = 0.3;

    /// <summary>
    /// The audio added before and after each region, in seconds, so a soft onset or a trailing
    /// consonant reaches the recogniser. At most half of <see cref="MinSilenceSeconds"/>, so two padded
    /// regions never overlap.
    /// </summary>
    public double PadSeconds { get; init; } = 0.12;

    /// <summary>Throws when the settings cannot describe a valid segmentation.</summary>
    internal void Validate()
    {
        if (!(Threshold > 0 && Threshold < 1) || !(NegativeThreshold > 0 && NegativeThreshold <= Threshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(NegativeThreshold),
                $"the VAD thresholds must satisfy 0 < negative ({NegativeThreshold}) <= threshold ({Threshold}) < 1; the defaults are 0.35 and 0.5.");
        }

        if (!(MinSpeechSeconds >= 0) || !(MinSilenceSeconds > 0) || !(PadSeconds >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(MinSilenceSeconds), "the VAD durations must be zero or more, and the minimum silence more than zero.");
        }

        if (PadSeconds * 2 > MinSilenceSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PadSeconds),
                $"the padding ({PadSeconds} s) must be at most half the minimum silence ({MinSilenceSeconds} s), so padded regions cannot overlap; lower the padding or raise the silence.");
        }
    }
}
