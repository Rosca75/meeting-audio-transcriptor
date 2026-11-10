// The Silero VAD graph as the engine sees it: a shared runner and the settings every stream over it uses.
using System;
using MeetingAudioTranscriptor.Engine.Models;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>Computes speech probabilities for 16 kHz audio with the Silero VAD v6 graph.</summary>
/// <remarks>
/// <para>
/// The graph's contract: input <c>input</c> float [B, 576], the 64 samples preceding a frame followed
/// by the frame's 512 samples; <c>state</c> float [2, B, 128], empty at the start of a stream and the
/// previous call's <c>stateN</c> afterwards; <c>sr</c> int64 scalar 16000. Outputs <c>output</c> float
/// [B, 1], the probability that the frame holds speech, and <c>stateN</c>. Frame k covers samples
/// [512k, 512k + 512).
/// </para>
/// <para>
/// The model holds no per-stream state, so one instance serves every file; each file gets its own
/// <see cref="SileroVadSession"/> from <see cref="Create"/>. The runner belongs to the caller, which
/// disposes it.
/// </para>
/// <para>
/// Measured as CPU time on one core of a 2.1 GHz Xeon shared with other work, so approximate
/// (ONNX Runtime 1.31.1 with one intra-op thread; en-10min.wav, en-ami-01.wav and fr-multi-01.wav,
/// 785.5 s of audio; agreement against the one-lane answer):
/// one lane costs 0.121 ms per call, 264 times real time, 27.3 s of one core for two hours.
/// A call costs about 0.06 ms plus 0.055 ms per lane, so batching at most halves the cost per
/// frame. The state has a long memory: with a 1 s warm-up, 4 to 32 lanes in 32 s blocks run 290
/// to 450 times real time but decide 0.6 to 1.7 % of frames differently at 0.5, and even a 16 s
/// warm-up with one lane boundary per 64 s leaves 0.06 %. Few lane boundaries with a long warm-up are the
/// balance: 8 lanes in 128 s blocks with a 4 s warm-up, the default, run 498 times real time (two
/// hours in 14.5 s of one core) with a mean absolute probability difference of 0.0044 and 0.32 %
/// of frames deciding differently at 0.5 (4 lanes in 96 s blocks: 471 times, 0.22 %).
/// </para>
/// </remarks>
public sealed class SileroVadModel
{
    /// <summary>The samples of one frame, the unit one probability describes (32 ms).</summary>
    public const int FrameSamples = 512;

    /// <summary>The samples preceding a frame that the graph reads with it.</summary>
    public const int ContextSamples = 64;

    /// <summary>The width of the recurrent state per lane and layer.</summary>
    internal const int StateWidth = 128;

    /// <summary>The graph outputs read on every call, in this order.</summary>
    internal static readonly string[] OutputNames = ["output", "stateN"];

    /// <summary>Creates the model over a runner of the Silero VAD graph.</summary>
    /// <param name="runner">The loaded graph; it stays the caller's to dispose.</param>
    /// <param name="options">The batch layout; null takes the measured defaults.</param>
    public SileroVadModel(ITensorRunner runner, SileroVadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        Options = options ?? new SileroVadOptions();
        Options.Validate();
        Runner = runner;
    }

    /// <summary>The batch layout every stream of this model uses.</summary>
    public SileroVadOptions Options { get; }

    /// <summary>The loaded graph.</summary>
    internal ITensorRunner Runner { get; }

    /// <summary>Starts a stream for one file: empty state, empty context, frame 0 next.</summary>
    public SileroVadSession Create() => new(this);
}
