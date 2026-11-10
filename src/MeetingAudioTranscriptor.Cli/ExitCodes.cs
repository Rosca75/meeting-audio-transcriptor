// The exit codes of the executable, and the one mapping from a run's outcome to them.
using MeetingAudioTranscriptor.App;

namespace MeetingAudioTranscriptor.Cli;

/// <summary>What the process returns, so a script can tell the failures apart.</summary>
internal static class ExitCodes
{
    /// <summary>Done; also when the recording held no speech and an empty transcript was written.</summary>
    public const int Success = 0;

    /// <summary>The command line could not be read.</summary>
    public const int Usage = 1;

    /// <summary>The recording is missing, unreadable, unsupported or damaged.</summary>
    public const int Input = 2;

    /// <summary>A model file is missing or does not load.</summary>
    public const int Models = 3;

    /// <summary>The transcripts could not be written.</summary>
    public const int Output = 4;

    /// <summary>Anything else.</summary>
    public const int Unexpected = 5;

    /// <summary>The user pressed Ctrl+C: 128 plus SIGINT, as shells report it.</summary>
    public const int Cancelled = 130;

    /// <summary>The exit code of an outcome.</summary>
    public static int Of(TranscriptionOutcomeKind kind) => kind switch
    {
        TranscriptionOutcomeKind.Succeeded or TranscriptionOutcomeKind.NoSpeech => Success,
        TranscriptionOutcomeKind.InputNotFound or TranscriptionOutcomeKind.InputUnreadable
            or TranscriptionOutcomeKind.InputUnsupported or TranscriptionOutcomeKind.InputCorrupt => Input,
        TranscriptionOutcomeKind.ModelsMissing => Models,
        TranscriptionOutcomeKind.OutputNotWritable or TranscriptionOutcomeKind.DiskFull => Output,
        TranscriptionOutcomeKind.Cancelled => Cancelled,
        _ => Unexpected,
    };
}
