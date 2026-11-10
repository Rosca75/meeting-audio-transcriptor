// What a running transcription reports: the stage it is in and how far the whole run has come.
namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>The stages a front end names while a transcription runs.</summary>
/// <remarks>
/// Decoding and voice activity run inside <see cref="Transcribing"/>, because the pipeline streams:
/// the file is still being read while the first chunks are recognised, so no separate decoding
/// stage could honestly be shown.
/// </remarks>
public enum TranscriptionStage
{
    /// <summary>The models are being loaded; reported by the application service.</summary>
    LoadingModels,

    /// <summary>The audio is decoded, cut at silences and recognised.</summary>
    Transcribing,

    /// <summary>The speakers are told apart.</summary>
    IdentifyingSpeakers,

    /// <summary>The words are cut into segments and cleaned.</summary>
    Cleaning,

    /// <summary>The transcript files are written; reported by the application service.</summary>
    Writing,
}

/// <summary>One progress report of the pipeline.</summary>
/// <param name="Stage">The stage running.</param>
/// <param name="StageFraction">How far the stage has come, from 0 to 1.</param>
/// <param name="Fraction">How far the pipeline has come, from 0 to 1; it never decreases and reaches 1 when the transcript is assembled.</param>
public readonly record struct PipelineProgress(TranscriptionStage Stage, double StageFraction, double Fraction);
