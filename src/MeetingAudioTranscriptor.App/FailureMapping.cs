// Maps every exception a run can raise to one outcome kind and one sentence the user can act on.
using System;
using System.IO;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Inference;
using MeetingAudioTranscriptor.Media;

namespace MeetingAudioTranscriptor.App;

/// <summary>Where in the run an exception was raised, which decides how a plain I/O failure reads.</summary>
internal enum RunPhase
{
    /// <summary>Opening and reading the recording.</summary>
    Input,

    /// <summary>Loading the models.</summary>
    Models,

    /// <summary>Running the pipeline.</summary>
    Pipeline,

    /// <summary>Creating the output folder or writing the files.</summary>
    Output,
}

/// <summary>The one place a failure becomes an outcome kind.</summary>
internal static class FailureMapping
{
    /// <summary>The Windows error for a full disk, the low 16 bits of the HResult.</summary>
    private const int WindowsDiskFull = 0x70;

    /// <summary>The Windows error for a full disk reached through a handle.</summary>
    private const int WindowsHandleDiskFull = 0x27;

    /// <summary>ENOSPC, which .NET on Linux and macOS reports as the HResult.</summary>
    private const int UnixNoSpace = 28;

    /// <summary>The kind and the message of a failure.</summary>
    public static (TranscriptionOutcomeKind Kind, string Message) Map(Exception error, RunPhase phase, string inputPath, string outputFolder)
    {
        switch (error)
        {
            case OperationCanceledException:
                return (TranscriptionOutcomeKind.Cancelled, "the transcription was cancelled; no transcript file was written");
            case AudioDecodeException decode:
                return (KindOf(decode.Failure), decode.Message);
            case AudioFormatException format:
                return (TranscriptionOutcomeKind.InputUnsupported, $"'{inputPath}' could not be read: {format.Message}");
            case ModelLoadException model:
                return (TranscriptionOutcomeKind.ModelsMissing, model.Message);
            case IOException io when IsDiskFull(io):
                return (TranscriptionOutcomeKind.DiskFull, $"the disk holding {outputFolder} is full; free some space or choose another output folder, then run the transcription again");
            case UnauthorizedAccessException or IOException or System.Security.SecurityException when phase == RunPhase.Output:
                return (TranscriptionOutcomeKind.OutputNotWritable, $"the transcripts could not be written to {outputFolder} ({error.Message}); choose a folder you can write to with the output option, or fix its permissions");
            case FileNotFoundException or DirectoryNotFoundException when phase == RunPhase.Input:
                return (TranscriptionOutcomeKind.InputNotFound, $"'{inputPath}' does not exist; check the path and the file name");
            case UnauthorizedAccessException when phase == RunPhase.Input:
                return (TranscriptionOutcomeKind.InputUnreadable, $"'{inputPath}' cannot be read ({error.Message}); check its permissions or copy it somewhere you can read");
            // The engine reads no file, so an I/O failure while the pipeline runs is the recording's.
            case IOException when phase is RunPhase.Input or RunPhase.Pipeline:
                return (TranscriptionOutcomeKind.InputCorrupt, $"'{inputPath}' could not be read ({error.Message}); check that the file is complete and not in use, or export the recording again");
            default:
                return (TranscriptionOutcomeKind.Unexpected, $"the transcription failed unexpectedly ({error.GetType().Name}: {error.Message}); try again, and if it keeps failing report the message");
        }
    }

    /// <summary>True when an I/O failure means the disk is full, on Windows or on Unix.</summary>
    public static bool IsDiskFull(IOException error)
    {
        int code = error.HResult & 0xFFFF;
        return code == WindowsDiskFull || code == WindowsHandleDiskFull || error.HResult == UnixNoSpace;
    }

    private static TranscriptionOutcomeKind KindOf(AudioDecodeFailure failure) => failure switch
    {
        AudioDecodeFailure.FileNotFound => TranscriptionOutcomeKind.InputNotFound,
        AudioDecodeFailure.AccessDenied => TranscriptionOutcomeKind.InputUnreadable,
        AudioDecodeFailure.UnsupportedType or AudioDecodeFailure.DecoderMissing => TranscriptionOutcomeKind.InputUnsupported,
        _ => TranscriptionOutcomeKind.InputCorrupt,
    };
}
