// The one failure every audio decoder raises, with a kind a front end can act on and a message a user can.
using System;

namespace MeetingAudioTranscriptor.Media;

/// <summary>Why an audio file could not be decoded.</summary>
/// <remarks>
/// The kinds are the distinctions a user acts on differently: a wrong path is fixed in the dialog, a
/// video without sound needs another export, a damaged file needs another copy, and a missing decoder
/// needs a reinstall.
/// </remarks>
public enum AudioDecodeFailure
{
    /// <summary>The file does not exist at the path given.</summary>
    FileNotFound,

    /// <summary>The file exists and the process may not read it.</summary>
    AccessDenied,

    /// <summary>The file is empty.</summary>
    EmptyFile,

    /// <summary>The file is not one of the supported types (.wav, .m4a, .mp4).</summary>
    UnsupportedType,

    /// <summary>The container holds no audio stream, such as a screen recording without sound.</summary>
    NoAudioStream,

    /// <summary>The data is damaged, or encoded in a way no available decoder reads.</summary>
    CorruptOrUnsupportedData,

    /// <summary>No decoder could be found or loaded on this machine.</summary>
    DecoderMissing,

    /// <summary>A decoder started and failed for a reason none of the other kinds names.</summary>
    DecoderFailed,
}

/// <summary>An audio file could not be opened or decoded; the message says what to do about it.</summary>
/// <remarks>
/// Public because the front ends catch it to show the message as it stands: every message names the
/// file, what was expected and how to fix it, and carries the decoder's own last error lines when
/// there are any.
/// </remarks>
public sealed class AudioDecodeException : Exception
{
    /// <summary>Creates a failure of the given kind with an actionable message.</summary>
    public AudioDecodeException(AudioDecodeFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>Creates a failure of the given kind that wraps the exception it came from.</summary>
    public AudioDecodeException(AudioDecodeFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>Creates a decoder failure with the given message.</summary>
    public AudioDecodeException()
        : this(AudioDecodeFailure.DecoderFailed, "the audio file could not be decoded")
    {
    }

    /// <summary>Creates a decoder failure with the given message.</summary>
    public AudioDecodeException(string message)
        : this(AudioDecodeFailure.DecoderFailed, message)
    {
    }

    /// <summary>Creates a decoder failure with the given message and cause.</summary>
    public AudioDecodeException(string message, Exception innerException)
        : this(AudioDecodeFailure.DecoderFailed, message, innerException)
    {
    }

    /// <summary>Which of the failure kinds this is.</summary>
    public AudioDecodeFailure Failure { get; }
}
