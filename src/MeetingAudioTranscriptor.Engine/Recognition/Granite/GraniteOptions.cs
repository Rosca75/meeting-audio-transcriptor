// The settings of the Granite recogniser: the longest chunk it is given and how the input level is evened out before the model.
using System;

namespace MeetingAudioTranscriptor.Engine.Recognition.Granite;

/// <summary>The settings of <see cref="GraniteCtcRecognizer"/>.</summary>
public sealed record GraniteOptions
{
    /// <summary>
    /// The longest chunk, in seconds, the recogniser is handed. What counts is the chunker's target:
    /// on the ten-minute files every target from 20 to 30 s reads alike (1.2, 2.4 and 8.2 % in
    /// English, French and German) and 10 s is worse in German (9.0 %), while the meetings read
    /// 26.4 % at a 20 s target and 24.1 % at 25 or 30 s, because fewer cuts fall inside overlapping
    /// speech. A 25 s target under this 30 s limit keeps that gain with room for the chunker to find
    /// a silence.
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
/// <para>
/// Measured on VAD chunks (target 20 s, maximum 30 s) of the benchmark clips, word error rate in
/// percent for read English, the three AMI meeting clips, French and German: no gain 2.2, 30.5,
/// 3.0, 7.7; one gain per chunk to RMS 0.1 gives 2.2, 31.2, 3.1, 7.7 and to 0.2 gives 2.2, 26.4,
/// 3.0, 7.4; the speech-region RMS to 0.2 gives 2.2, 26.4, 2.9, 7.3; the slow automatic gain
/// (0.5 s windows, 30x) to 0.1 gives 2.2, 27.1, 3.1, 8.0 and to 0.4 gives 2.0, 25.4, 3.6, 7.8.
/// Targets of 0.2 and above all bring the meetings to 25.4 to 26.4, a spread of a dozen words
/// over 828; the slow gain and targets above 0.2 cost French or German more than 0.2 points. The
/// default is therefore the speech-region RMS at 0.2: the meetings' gain without a read-speech
/// loss, and the least clipping of the schemes that reach it. Silence and the four noise files
/// yield no word under every scheme.
/// </para>
/// </remarks>
public sealed record GainOptions
{
    /// <summary>How the gain is chosen.</summary>
    public GainMode Mode { get; init; } = GainMode.SpeechRms;

    /// <summary>The RMS the gain aims at, on samples in [-1, 1].</summary>
    public double TargetRms { get; init; } = 0.2;

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
