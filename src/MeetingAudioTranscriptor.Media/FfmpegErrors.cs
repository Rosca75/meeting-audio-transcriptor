// Turns what ffmpeg wrote to stderr into an AudioDecodeException of the right kind with an actionable message.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MeetingAudioTranscriptor.Media;

/// <summary>Classifies ffmpeg's error text and words the message a user reads.</summary>
/// <remarks>
/// ffmpeg reports failures as text, not codes, and its exit codes vary by version, so the kind is read
/// from phrases that have been stable for many releases (the operating system's own "No such file or
/// directory", ffmpeg's "Invalid data found when processing input"). The message always carries the
/// last few lines ffmpeg wrote, trimmed, because those name the codec or atom that failed.
/// </remarks>
internal static partial class FfmpegErrors
{
    /// <summary>How many of ffmpeg's last lines a message quotes.</summary>
    private const int QuotedLines = 4;

    /// <summary>The longest single quoted line; a longer one is cut, since a line can carry a whole path.</summary>
    private const int MaxLineLength = 240;

    /// <summary>Builds the exception for a failed probe or decode of the given file.</summary>
    /// <param name="path">The file as the user named it.</param>
    /// <param name="stderrLines">What ffmpeg wrote to stderr, in order.</param>
    /// <param name="exitCode">ffmpeg's exit code, or null when it was not waited for.</param>
    public static AudioDecodeException Classify(string path, IReadOnlyList<string> stderrLines, int? exitCode)
    {
        ArgumentNullException.ThrowIfNull(stderrLines);
        AudioDecodeFailure kind = ClassifyKind(stderrLines);
        string quoted = Quote(stderrLines);
        string said = quoted.Length > 0 ? $" (ffmpeg said: {quoted})" : exitCode is int code ? $" (ffmpeg exited with code {code} and printed nothing)" : "";
        string message = kind switch
        {
            AudioDecodeFailure.FileNotFound =>
                $"the audio file '{path}' was not found{said}; check the path is right and the file still exists",
            AudioDecodeFailure.AccessDenied =>
                $"the audio file '{path}' could not be read: permission denied{said}; copy the file to a folder you own, or ask its owner for read access",
            AudioDecodeFailure.NoAudioStream => NoAudioStreamMessage(path),
            AudioDecodeFailure.CorruptOrUnsupportedData =>
                $"the audio file '{path}' could not be decoded: its data is damaged or in a format ffmpeg does not read{said}; check that it plays in a media player, or export the recording again as .m4a, .mp4 or .wav",
            _ =>
                $"ffmpeg could not decode '{path}'{said}; check that it plays in a media player, and reinstall the application if every file fails the same way",
        };
        return new AudioDecodeException(kind, message);
    }

    /// <summary>The message for a container that holds video or data and no sound.</summary>
    public static string NoAudioStreamMessage(string path) =>
        $"the file '{path}' holds no audio stream (only video or data); export the meeting recording with its sound track, or choose the audio file itself";

    /// <summary>The message for an ffmpeg that exists and cannot be started.</summary>
    public static AudioDecodeException NotStartable(FfmpegLocation ffmpeg, Exception cause) =>
        new(
            AudioDecodeFailure.DecoderMissing,
            $"{ffmpeg} could not be started ({cause.Message}); reinstall the application, or set {FfmpegLocator.EnvironmentVariable} to the path of a working ffmpeg",
            cause);

    /// <summary>The message for a machine where no ffmpeg was found at all.</summary>
    public static AudioDecodeException NotFound(FfmpegLocator locator) =>
        new(
            AudioDecodeFailure.DecoderMissing,
            $"ffmpeg was not found beside the application nor on the PATH (looked in: {locator.DescribeSearch()}); reinstall the package or install ffmpeg");

    /// <summary>Reads the failure kind from the phrases ffmpeg and the operating system use.</summary>
    public static AudioDecodeFailure ClassifyKind(IReadOnlyList<string> stderrLines)
    {
        ArgumentNullException.ThrowIfNull(stderrLines);
        bool Says(string phrase) => stderrLines.Any(line => line.Contains(phrase, StringComparison.OrdinalIgnoreCase));

        if (Says("No such file or directory") || Says("The system cannot find the"))
        {
            return AudioDecodeFailure.FileNotFound;
        }

        if (Says("Permission denied") || Says("Access is denied"))
        {
            return AudioDecodeFailure.AccessDenied;
        }

        if (Says("matches no streams") || Says("does not contain any stream") || Says("Output file is empty"))
        {
            return AudioDecodeFailure.NoAudioStream;
        }

        if (Says("Invalid data found") || Says("moov atom not found") || Says("could not find codec parameters")
            || Says("Unknown format") || Says("not supported") || Says("Unsupported codec") || Says("Decoder") || Says("Invalid argument")
            || Says("Error while decoding") || Says("error reading header"))
        {
            return AudioDecodeFailure.CorruptOrUnsupportedData;
        }

        return AudioDecodeFailure.DecoderFailed;
    }

    /// <summary>The last lines ffmpeg wrote, without the memory addresses it prints, cut and joined into one sentence.</summary>
    public static string Quote(IReadOnlyList<string> stderrLines)
    {
        ArgumentNullException.ThrowIfNull(stderrLines);
        IEnumerable<string> lines = stderrLines
            .Select(Clean)
            .Where(line => line.Length > 0 && !line.StartsWith("To ignore this", StringComparison.Ordinal))
            .TakeLast(QuotedLines)
            .Select(line => line.Length > MaxLineLength ? line[..MaxLineLength] + "..." : line);
        return string.Join(" | ", lines);
    }

    /// <summary>Removes the context address ffmpeg prints (<c>[aac @ 0x55d0c2a8]</c> becomes <c>[aac]</c>) and the surrounding space.</summary>
    public static string Clean(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return ContextAddress().Replace(line, "").Trim();
    }

    /// <summary>Matches the <c> @ 0x...</c> address inside an ffmpeg log context; it does not match an at sign elsewhere.</summary>
    [GeneratedRegex(@" @ 0x[0-9a-fA-F]+(?=\])", RegexOptions.CultureInvariant)]
    private static partial Regex ContextAddress();
}
