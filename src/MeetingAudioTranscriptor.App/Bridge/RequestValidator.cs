// Checks a transcription request before a run starts, so a bad request is refused with a sentence
// the user can act on instead of failing half way through.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Validates and normalises a <see cref="TranscribeRequest"/>.</summary>
internal static class RequestValidator
{
    private const int MinSpeakers = 1;
    private const int MaxSpeakers = 10;
    private const int MaxNameLength = 40;

    /// <summary>
    /// Returns the request the transcriber receives: language and formats lower case and distinct,
    /// the speaker count and the speaker names dropped when separation is off, the names trimmed, and
    /// the output folder always set.
    /// </summary>
    /// <exception cref="ArgumentException">The request is invalid; the message says what to change.</exception>
    public static TranscribeRequest Validate(TranscribeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new ArgumentException("No recording is chosen. Choose a file, or drop one onto the page, before starting.");
        }

        if (!SupportedFiles.IsSupported(request.InputPath))
        {
            throw new ArgumentException(SupportedFiles.Refusal([request.InputPath]));
        }

        if (!File.Exists(request.InputPath))
        {
            throw new ArgumentException(
                $"\"{Path.GetFileName(request.InputPath)}\" could not be found. Check that the file still exists and choose it again.");
        }

        string language = (request.Language ?? "").Trim().ToLowerInvariant();
        if (!TranscribeLanguages.All.Contains(language))
        {
            throw new ArgumentException($"The language \"{request.Language}\" is not supported. Choose English, French or German.");
        }

        List<string> formats = (request.Formats ?? [])
            .Select(format => (format ?? "").Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        if (formats.Count == 0)
        {
            throw new ArgumentException("No output format is chosen. Tick at least one of VTT, JSON and TXT.");
        }

        string? unknown = formats.FirstOrDefault(format => !TranscribeFormats.All.Contains(format));
        if (unknown is not null)
        {
            throw new ArgumentException($"The output format \"{unknown}\" is not supported. Use vtt, json or txt.");
        }

        if (request.SpeakerCount is { } count && (count < MinSpeakers || count > MaxSpeakers))
        {
            throw new ArgumentException($"The speaker count {count} is not valid. Enter a number from {MinSpeakers} to {MaxSpeakers}, or leave it empty for automatic.");
        }

        // Names given while separation is off name nobody, so they are dropped unread, like the count.
        IReadOnlyList<string>? names = request.SpeakerSeparation ? SpeakerNames(request.SpeakerNames, request.SpeakerCount) : null;

        string folder = ResolveOutputFolder(request);
        return request with
        {
            Language = language,
            Formats = formats,
            SpeakerCount = request.SpeakerSeparation ? request.SpeakerCount : null,
            SpeakerNames = names,
            OutputFolder = folder,
        };
    }

    // The names trimmed, without the blank entries at the end, or null when none is left. A blank entry
    // before a name stays, because the position of an entry is the speaker it names. A name is written
    // into a WebVTT voice tag and on a heading line of the text transcript, so it holds no angle
    // bracket and no control character; two speakers with one label cannot be told apart in a
    // transcript, so names differ from each other and from the default label of another speaker.
    private static List<string>? SpeakerNames(IReadOnlyList<string>? given, int? count)
    {
        if (given is null || given.Count == 0)
        {
            return null;
        }

        if (count is not { } speakers)
        {
            throw new ArgumentException("Speaker names need a speaker count. Enter the number of speakers, or clear the names.");
        }

        if (given.Count > speakers)
        {
            throw new ArgumentException(
                $"There are {given.Count} speaker names for {speakers} {(speakers == 1 ? "speaker" : "speakers")}. Remove the extra names, or raise the speaker count.");
        }

        List<string> names = given.Select(name => (name ?? "").Trim()).ToList();
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (name.Length == 0)
            {
                continue;
            }

            if (name.Length > MaxNameLength)
            {
                throw new ArgumentException($"The name of speaker {i + 1} has {name.Length} characters. Shorten it to {MaxNameLength} characters or fewer.");
            }

            if (name.Any(c => c is '<' or '>' || char.IsControl(c)))
            {
                throw new ArgumentException($"The name of speaker {i + 1} holds a character a transcript cannot carry. Remove any <, > and line break from it.");
            }

            for (int earlier = 0; earlier < i; earlier++)
            {
                if (string.Equals(names[earlier], name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Speakers {earlier + 1} and {i + 1} are both named \"{names[earlier]}\". Give each speaker a different name.");
                }
            }

            for (int other = 1; other <= MaxSpeakers; other++)
            {
                if (other != i + 1 && string.Equals(name, "Speaker " + other.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"The name \"{name}\" of speaker {i + 1} is the label of another speaker. Choose a different name.");
                }
            }
        }

        while (names.Count > 0 && names[^1].Length == 0)
        {
            names.RemoveAt(names.Count - 1);
        }

        return names.Count > 0 ? names : null;
    }

    // The default is the input file's folder. A chosen folder must exist or be creatable: its nearest
    // existing ancestor must be a folder. Nothing is created here, so validating has no side effect.
    private static string ResolveOutputFolder(TranscribeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputFolder))
        {
            return Path.GetDirectoryName(Path.GetFullPath(request.InputPath)) ?? Path.GetPathRoot(request.InputPath) ?? "";
        }

        string folder = request.OutputFolder;
        if (!Path.IsPathRooted(folder))
        {
            throw new ArgumentException($"The output folder \"{folder}\" is not a full path. Use Change... to choose a folder.");
        }

        if (File.Exists(folder))
        {
            throw new ArgumentException($"The output folder \"{folder}\" is a file. Use Change... to choose a folder.");
        }

        string? ancestor = folder;
        while (ancestor is not null && !Directory.Exists(ancestor))
        {
            ancestor = Path.GetDirectoryName(ancestor);
        }

        if (ancestor is null)
        {
            throw new ArgumentException($"The output folder \"{folder}\" does not exist and cannot be created. Use Change... to choose another folder.");
        }

        return folder;
    }
}
