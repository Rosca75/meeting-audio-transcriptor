// Checks a transcription request before a run starts, so a bad request is refused with a sentence
// the user can act on instead of failing half way through.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Validates and normalises a <see cref="TranscribeRequest"/>.</summary>
internal static class RequestValidator
{
    private const int MinSpeakers = 1;
    private const int MaxSpeakers = 10;

    /// <summary>
    /// Returns the request the transcriber receives: language and formats lower case and distinct,
    /// the speaker count dropped when separation is off, and the output folder always set.
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

        string folder = ResolveOutputFolder(request);
        return request with
        {
            Language = language,
            Formats = formats,
            SpeakerCount = request.SpeakerSeparation ? request.SpeakerCount : null,
            OutputFolder = folder,
        };
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
