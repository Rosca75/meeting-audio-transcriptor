// Which input files the application accepts and the sentences used when one is refused, shared by
// the file dialog result, the drop protocol and the request validation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>The supported input extensions and the wording of a refusal.</summary>
internal static class SupportedFiles
{
    /// <summary>The supported extensions, lower case with the dot.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".m4a", ".mp4", ".wav"];

    /// <summary>Whether the file name ends in a supported extension, ignoring case.</summary>
    public static bool IsSupported(string pathOrName)
    {
        string extension = Path.GetExtension(pathOrName);
        return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The sentence for one or more refused names: what was refused and what is supported.</summary>
    public static string Refusal(IReadOnlyList<string> names)
    {
        string quoted = string.Join(", ", names.Select(name => "\"" + Path.GetFileName(name) + "\""));
        return $"{quoted} cannot be used: only .m4a, .mp4 and .wav files are supported. Choose a recording in one of those formats.";
    }

    /// <summary>
    /// Turns one chosen or dropped set of paths into a selection or a refusal sentence. When several
    /// supported files were dropped the first is taken and the note says so.
    /// </summary>
    /// <param name="paths">The full paths, in the order chosen.</param>
    /// <param name="selection">The chosen file when the call returns null.</param>
    /// <returns>Null when a file was selected; otherwise the refusal sentence.</returns>
    public static string? Select(IReadOnlyList<string> paths, out FileSelection? selection)
    {
        selection = null;
        if (paths.Count == 0)
        {
            return "No file was received. Choose a file with the button, or drop one onto the page.";
        }

        string? first = paths.FirstOrDefault(IsSupported);
        if (first is null)
        {
            return Refusal(paths);
        }

        if (!File.Exists(first))
        {
            return $"\"{Path.GetFileName(first)}\" could not be found. Check that the file still exists and choose it again.";
        }

        string name = Path.GetFileName(first);
        string? note = paths.Count > 1
            ? $"{paths.Count} files were dropped. Using \"{name}\", the first supported one. Drop one file at a time to choose another."
            : null;
        selection = new FileSelection { Path = first, Name = name, Note = note };
        return null;
    }
}
