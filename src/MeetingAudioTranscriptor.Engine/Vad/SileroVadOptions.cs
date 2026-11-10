// How the Silero VAD graph is driven: the batch layout that trades a little accuracy for speed, and the digital-silence shortcut.
using System;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>The batching and shortcut settings of <see cref="SileroVadModel"/>.</summary>
/// <remarks>
/// The graph reads one 32 ms frame per call and carries a recurrent state from frame to frame, so
/// the plain way to run it is one call per frame, which for two hours of audio is 225,000 calls
/// each dominated by per-call overhead. A block of audio is therefore split into several lanes run
/// side by side as one batch: every call then advances all lanes by one frame. A lane other than the
/// first starts from an empty state, so it first reads a warm-up stretch of the audio before its own
/// part and discards those answers; the first lane continues the state the previous block ended
/// with. The measured speed and agreement with the one-lane answer are recorded on
/// <see cref="SileroVadModel"/>.
/// </remarks>
public sealed record SileroVadOptions
{
    /// <summary>The number of lanes run as one batch; 1 runs the graph frame by frame, exactly as the reference does.</summary>
    public int Lanes { get; init; } = 8;

    /// <summary>
    /// The audio read before probabilities are produced, in seconds. Longer blocks spend a smaller
    /// share on warm-up but delay every probability by up to the block length, which the chunker
    /// must buffer.
    /// </summary>
    public double BlockSeconds { get; init; } = 128;

    /// <summary>The audio a lane reads before its own part to settle its state, in seconds.</summary>
    public double WarmUpSeconds { get; init; } = 4.0;

    /// <summary>
    /// Whether a run of at least <see cref="DigitalSilenceSeconds"/> of frames whose every sample is
    /// below <see cref="DigitalSilenceLevel"/> is given probability 0 without running the graph. The
    /// state is then reset to empty, which is the state a stream starts with, so the audio after the
    /// run is read the way a lane reads its start.
    /// </summary>
    public bool SkipDigitalSilence { get; init; } = true;

    /// <summary>The peak absolute sample value below which audio counts as digital silence (about -80 dBFS).</summary>
    public float DigitalSilenceLevel { get; init; } = 1e-4f;

    /// <summary>
    /// The shortest run of digital silence skipped, in seconds; a shorter one is read, so speech
    /// around a dropout keeps its state. Two seconds because a skip splits the block, and the short
    /// spans between frequent short gaps are too short for several lanes, which costs more calls
    /// than the skip saves.
    /// </summary>
    public double DigitalSilenceSeconds { get; init; } = 2.0;

    /// <summary>The shortest skipped run in 512-sample frames.</summary>
    internal int DigitalSilenceFrames => (int)Math.Round(DigitalSilenceSeconds * 16000 / SileroVadModel.FrameSamples);

    /// <summary>The block length in 512-sample frames.</summary>
    internal int BlockFrames => (int)Math.Round(BlockSeconds * 16000 / SileroVadModel.FrameSamples);

    /// <summary>The warm-up length in 512-sample frames.</summary>
    internal int WarmUpFrames => (int)Math.Round(WarmUpSeconds * 16000 / SileroVadModel.FrameSamples);

    /// <summary>Throws when a setting is outside the range the model can run with.</summary>
    internal void Validate()
    {
        if (Lanes is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(Lanes), Lanes, "the VAD lane count must be between 1 and 64; 8 is the measured default.");
        }

        if (!(BlockSeconds >= 1 && BlockSeconds <= 300))
        {
            throw new ArgumentOutOfRangeException(nameof(BlockSeconds), BlockSeconds, "the VAD block must be between 1 and 300 seconds; 128 is the measured default.");
        }

        if (!(WarmUpSeconds >= 0) || WarmUpFrames * 2 > BlockFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmUpSeconds), WarmUpSeconds, "the VAD warm-up must be zero or more and at most half the block length.");
        }

        if (!(DigitalSilenceLevel >= 0 && DigitalSilenceLevel < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(DigitalSilenceLevel), DigitalSilenceLevel, "the digital-silence level must be between 0 and 1; 0.0001 is about -80 dBFS.");
        }

        if (!(DigitalSilenceSeconds > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(DigitalSilenceSeconds), DigitalSilenceSeconds, "the shortest skipped digital silence must be more than zero seconds; 2 is the measured default.");
        }
    }
}
