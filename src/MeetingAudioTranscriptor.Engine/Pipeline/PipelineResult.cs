// What a finished run returns: the transcript and the measurements a benchmark or a summary line reads.
using MeetingAudioTranscriptor.Contracts.Transcripts;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>The outcome of <see cref="TranscriptionPipeline.Run"/>.</summary>
public sealed record PipelineResult
{
    /// <summary>The transcript, ready for the writers.</summary>
    public required TranscriptDocument Document { get; init; }

    /// <summary>Where the time went.</summary>
    public required PipelineTimings Timings { get; init; }

    /// <summary>The chunks the recording was cut into.</summary>
    public required int ChunkCount { get; init; }

    /// <summary>The chunks that end at a cut through speech because no silence came in time.</summary>
    public required int ForcedCuts { get; init; }

    /// <summary>The length of the recording in seconds, from the samples actually read.</summary>
    public required double AudioSeconds { get; init; }

    /// <summary>The seconds inside padded speech regions.</summary>
    public required double SpeechSeconds { get; init; }

    /// <summary>The words the recogniser returned.</summary>
    public required int RecognizedWords { get; init; }

    /// <summary>The recognition workers the run used.</summary>
    public required int Threads { get; init; }
}

/// <summary>The seconds each part of a run took.</summary>
/// <remarks>
/// Decoding, voice activity and recognition overlap in time, so <see cref="StreamingSeconds"/> is
/// their shared wall time, and the producer and worker figures say which of them was busy: when
/// <see cref="ProducerSeconds"/> nears the streaming time the producer is the bottleneck, and when
/// <see cref="RecognitionSeconds"/> nears the streaming time multiplied by the thread count the
/// workers are.
/// </remarks>
/// <param name="TotalSeconds">The wall time of the whole run.</param>
/// <param name="StreamingSeconds">The wall time from the first read to the last chunk recognised.</param>
/// <param name="ProducerSeconds">The time the producer spent decoding, resampling and running the VAD (not waiting for a free slot).</param>
/// <param name="RecognitionSeconds">The time the workers spent recognising and handing audio to the separator, summed over workers.</param>
/// <param name="SpeakerSeconds">The wall time of the speaker assignment after the last chunk.</param>
/// <param name="CleaningSeconds">The wall time of segmentation, cleaning and assembly.</param>
public sealed record PipelineTimings(
    double TotalSeconds,
    double StreamingSeconds,
    double ProducerSeconds,
    double RecognitionSeconds,
    double SpeakerSeconds,
    double CleaningSeconds);
