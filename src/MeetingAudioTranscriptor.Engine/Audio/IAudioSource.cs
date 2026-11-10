// The one shape decoded audio takes on its way into the engine: a forward-only stream of mono samples.
using System;

namespace MeetingAudioTranscriptor.Engine.Audio;

/// <summary>A forward-only stream of mono audio samples in the range -1 to 1.</summary>
/// <remarks>
/// The engine receives samples and never a path, so every decoder (a WAV reader, an ffmpeg child
/// process, Media Foundation) looks the same to it and a test can feed it an array. A source is read
/// by one thread at a time, and a two-hour recording is never held whole: the reader pulls blocks.
/// </remarks>
public interface IAudioSource : IDisposable
{
    /// <summary>The number of samples per second this source delivers.</summary>
    int SampleRate { get; }

    /// <summary>The total length in seconds when the container states it, used for progress only.</summary>
    double? DurationSeconds { get; }

    /// <summary>Fills the buffer with the next samples and returns how many were written.</summary>
    /// <returns>Zero only at the end of the audio; otherwise at least one sample.</returns>
    int Read(Span<float> buffer);
}
