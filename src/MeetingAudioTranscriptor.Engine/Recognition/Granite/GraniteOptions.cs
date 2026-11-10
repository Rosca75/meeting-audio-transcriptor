// The settings of the Granite recogniser: the longest chunk it is given and how the input level is evened out before the model.
using System;

namespace MeetingAudioTranscriptor.Engine.Recognition.Granite;

/// <summary>The settings of <see cref="GraniteCtcRecognizer"/>.</summary>
public sealed record GraniteOptions
{
    /// <summary>
    /// The longest chunk, in seconds, the recogniser is handed. The model reads chunks of 10 to 30
    /// seconds equally well, and a longer chunk costs fewer cuts through speech.
    /// </summary>
    public double MaxChunkSeconds { get; init; } = 30;

    /// <summary>How the input level is evened out before the model reads it.</summary>
    public GainOptions Gain { get; init; } = new();

    /// <summary>Throws when a setting is outside its range.</summary>
    internal void Validate()
    {
        if (!(MaxChunkSeconds >= 1) || MaxChunkSeconds > 40)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxChunkSeconds),
                $"the Granite chunk limit is {MaxChunkSeconds} s; use 1 to 40 s, because the model's positions are trained up to about 41 s of audio");
        }

        ArgumentNullException.ThrowIfNull(Gain);
        Gain.Validate();
    }
}

/// <summary>How a gain is chosen for the samples the model reads.</summary>
public enum GainMode
{
    /// <summary>The samples are read as they are.</summary>
    None,

    /// <summary>One gain for the whole chunk, bringing its RMS to the target.</summary>
    ChunkRms,

    /// <summary>One gain for the whole chunk, bringing the RMS of its speech regions to the target.</summary>
    SpeechRms,

    /// <summary>A slowly varying gain following the RMS envelope over short windows.</summary>
    SlowAgc,

    /// <summary>One gain per speech region, eased linearly across the gaps between regions.</summary>
    RegionRms,
}

/// <summary>The input-level normalisation of the Granite recogniser.</summary>
/// <remarks>
/// The model computes its log-mel features inside the graph without normalising their mean or
/// variance, so it reads the absolute level: a quiet speaker or a distant microphone loses whole
/// stretches of speech. A gain before the model brings the level to where it was trained.
/// </remarks>
public sealed record GainOptions
{
    /// <summary>How the gain is chosen.</summary>
    public GainMode Mode { get; init; } = GainMode.SlowAgc;

    /// <summary>The RMS the gain aims at, on samples in [-1, 1].</summary>
    public double TargetRms { get; init; } = 0.1;

    /// <summary>The largest gain applied, so a near-silent stretch is not raised to speech level.</summary>
    public double MaxGain { get; init; } = 30;

    /// <summary>The window of the RMS envelope of <see cref="GainMode.SlowAgc"/>, in seconds.</summary>
    public double WindowSeconds { get; init; } = 0.5;

    /// <summary>
    /// The envelope of <see cref="GainMode.SlowAgc"/> is floored at this fraction of its median, so
    /// a pause inside speech is not raised more than the speech around it.
    /// </summary>
    public double EnvelopeFloor { get; init; } = 0.25;

    /// <summary>An RMS below this is silence and is left at gain one, about -80 dB below full scale.</summary>
    public double SilenceRms { get; init; } = 1e-4;

    /// <summary>Throws when a setting is outside its range.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "the gain mode is not one of None, ChunkRms, SpeechRms, SlowAgc or RegionRms");
        }

        if (!(TargetRms > 0 && TargetRms <= 1) || !(MaxGain >= 1) || !(WindowSeconds > 0) || !(EnvelopeFloor >= 0 && EnvelopeFloor <= 1) || !(SilenceRms >= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(TargetRms),
                $"the gain settings are out of range (target RMS {TargetRms}, maximum gain {MaxGain}, window {WindowSeconds} s, envelope floor {EnvelopeFloor}, silence RMS {SilenceRms}); "
                + "use a target RMS in (0, 1], a maximum gain of at least 1, a window above zero, an envelope floor in [0, 1] and a silence RMS of at least zero");
        }
    }
}
