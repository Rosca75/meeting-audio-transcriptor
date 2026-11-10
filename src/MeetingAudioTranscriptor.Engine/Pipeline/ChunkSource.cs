// The producer's source of chunks: the VAD chunking stage in the application, a scripted list in the tests.
using System;
using System.Collections.Generic;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Vad;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>Cuts one recording into speech chunks and counts what it read.</summary>
/// <remarks>A seam so the pipeline's threading is tested with chunks chosen by the test, without a VAD in between.</remarks>
internal interface IChunkSource
{
    /// <summary>The samples read so far.</summary>
    long TotalSamples { get; }

    /// <summary>The samples inside padded speech regions so far.</summary>
    long SpeechSamples { get; }

    /// <summary>The chunks yielded so far that end at a forced cut.</summary>
    int ForcedCuts { get; }

    /// <summary>Reads the whole 16 kHz source and yields its chunks in order.</summary>
    IEnumerable<SpeechChunk> Run(IAudioSource source, IProgress<long>? samplesRead, CancellationToken cancellation);
}

/// <summary>The chunk source of the application: <see cref="VadChunkingStage"/> over the Silero model.</summary>
internal sealed class VadChunkSource : IChunkSource
{
    private readonly VadChunkingStage _stage;

    public VadChunkSource(SileroVadModel model, double maxChunkSeconds, SpeechChunkerOptions options)
    {
        _stage = new VadChunkingStage(model, maxChunkSeconds, options);
    }

    public long TotalSamples => _stage.TotalSamples;

    public long SpeechSamples => _stage.SpeechSamples;

    public int ForcedCuts => _stage.ForcedCuts;

    public IEnumerable<SpeechChunk> Run(IAudioSource source, IProgress<long>? samplesRead, CancellationToken cancellation) =>
        _stage.Run(source, samplesRead, cancellation);
}
