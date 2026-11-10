// The recogniser as the pipeline sees it: a chunk of speech in, timed words out.
using System;
using System.Collections.Generic;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Recognition;

/// <summary>Turns one chunk of 16 kHz mono speech into timed words.</summary>
/// <remarks>
/// The pipeline cuts audio into chunks at silences and gives each chunk to a recogniser, so a
/// recogniser never sees a whole meeting and never needs to carry state between chunks. An
/// implementation is safe to call from several threads at once.
/// </remarks>
public interface ISpeechRecognizer
{
    /// <summary>The longest chunk, in seconds, the model reads well; the chunker never exceeds it.</summary>
    double MaxChunkSeconds { get; }

    /// <summary>Recognises one chunk.</summary>
    /// <param name="samples">The chunk's samples at the model sample rate.</param>
    /// <param name="cancellation">Stops the recognition when the user cancels.</param>
    /// <returns>The words in time order, with times in seconds from the start of the chunk.</returns>
    IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation);
}
