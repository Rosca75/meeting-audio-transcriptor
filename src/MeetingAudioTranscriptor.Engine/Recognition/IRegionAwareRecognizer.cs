// A recogniser that can also use where the voice-activity detector found speech inside a chunk.
using System;
using System.Collections.Generic;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Recognition;

/// <summary>A recogniser that reads a chunk together with its speech regions.</summary>
/// <remarks>
/// The pipeline holds each chunk's speech regions (the VAD's padded regions, as sample offsets
/// from the chunk's start) and passes them when the recogniser implements this interface, so a
/// recogniser can level each speaker's stretch on its own or read a quiet region again. Calling
/// <see cref="ISpeechRecognizer.Recognize"/> stays valid and treats the whole chunk as speech.
/// </remarks>
public interface IRegionAwareRecognizer : ISpeechRecognizer
{
    /// <summary>Recognises one chunk, knowing where speech lies in it.</summary>
    /// <param name="samples">The chunk's samples at the model sample rate.</param>
    /// <param name="speechRegions">The speech regions as [Start, End) sample offsets from the chunk's start, in order; empty when unknown.</param>
    /// <param name="cancellation">Stops the recognition when the user cancels.</param>
    /// <returns>The words in time order, with times in seconds from the start of the chunk.</returns>
    IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation);
}
