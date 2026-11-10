// The one failure a decoder raises when the audio it was given is not in a form the engine reads.
using System;

namespace MeetingAudioTranscriptor.Engine.Audio;

/// <summary>Raised when an audio container or encoding is malformed or not supported.</summary>
/// <remarks>
/// A dedicated type lets a front end tell "this file is not readable as it stands, convert it" apart
/// from a programming fault, and show the message as it is: every message names what was found,
/// what is supported and how to get there.
/// </remarks>
public sealed class AudioFormatException : Exception
{
    /// <summary>Creates the exception with a generic message.</summary>
    public AudioFormatException()
        : base("the audio is not in a supported format; convert it to a PCM or floating point WAV file")
    {
    }

    /// <summary>Creates the exception with an actionable message.</summary>
    public AudioFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an actionable message and the failure that caused it.</summary>
    public AudioFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
