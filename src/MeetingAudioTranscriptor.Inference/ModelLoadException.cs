// The one exception a caller sees when a model cannot be opened, whatever the runtime's own failure was.
using System;

namespace MeetingAudioTranscriptor.Inference;

/// <summary>A model file or the ONNX Runtime itself could not be loaded.</summary>
/// <remarks>
/// The message is a complete sentence saying what failed and what to do, so a front end can show
/// it as it is. The runtime's own exception types stay in the inner exception, because the callers
/// above this project do not reference the package.
/// </remarks>
public sealed class ModelLoadException : Exception
{
    /// <summary>Creates the exception with the sentence to show.</summary>
    public ModelLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the sentence to show and the failure behind it.</summary>
    public ModelLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
