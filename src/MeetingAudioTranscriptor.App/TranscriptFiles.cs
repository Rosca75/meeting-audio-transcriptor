// Names the transcript files of a run and writes them atomically: each to a temporary file, moved into place only when all are complete.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;

namespace MeetingAudioTranscriptor.App;

/// <summary>One file a run writes.</summary>
/// <param name="Format">Its format.</param>
/// <param name="Variant">Cleaned or raw text.</param>
/// <param name="Path">Where it ends up.</param>
internal sealed record PlannedFile(TranscriptFormat Format, TranscriptVariant Variant, string Path);

/// <summary>Plans and writes the transcript files.</summary>
/// <remarks>
/// A file is named after the recording: <c>meeting.m4a</c> gives <c>meeting.vtt</c>,
/// <c>meeting.json</c> and <c>meeting.txt</c>, and with raw files <c>meeting.raw.vtt</c> and
/// <c>meeting.raw.txt</c> for the text formats chosen (the JSON holds both texts). Every file is
/// written to a temporary of its own first, <c>.meeting.vtt.&lt;random&gt;.mat-tmp</c> in the same
/// folder, created only where no file exists, so a user's file is never overwritten or deleted; the
/// temporaries are moved into place only once all are complete, so a failure or a cancellation leaves
/// the old files as they were and no partial file.
/// </remarks>
internal static class TranscriptFiles
{
    /// <summary>The files a request asks for, in a fixed order: vtt, json, txt, then the raw ones.</summary>
    public static List<PlannedFile> Plan(string inputPath, string outputFolder, IReadOnlyCollection<TranscriptFormat> formats, bool rawFiles)
    {
        string name = Path.GetFileNameWithoutExtension(inputPath);
        List<PlannedFile> files = [];
        foreach (TranscriptFormat format in (TranscriptFormat[])[TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Txt])
        {
            if (Contains(formats, format))
            {
                files.Add(new PlannedFile(format, TranscriptVariant.Cleaned, Path.Combine(outputFolder, name + TranscriptWriters.FileExtension(format))));
            }
        }

        if (rawFiles)
        {
            foreach (TranscriptFormat format in (TranscriptFormat[])[TranscriptFormat.Vtt, TranscriptFormat.Txt])
            {
                if (Contains(formats, format))
                {
                    files.Add(new PlannedFile(format, TranscriptVariant.Raw, Path.Combine(outputFolder, name + ".raw" + TranscriptWriters.FileExtension(format))));
                }
            }
        }

        return files;
    }

    /// <summary>Checks before the long run that the folder exists (creating it) and takes a file; throws what writing would.</summary>
    public static void ProbeFolder(string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        string probe = Path.Combine(outputFolder, "." + Guid.NewGuid().ToString("N") + ".probe.tmp");
        using (FileStream stream = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
            stream.WriteByte(0);
        }
    }

    /// <summary>Writes every file atomically; on any failure or cancellation removes every temporary file and rethrows.</summary>
    /// <param name="files">The planned files.</param>
    /// <param name="document">The transcript.</param>
    /// <param name="onFile">Called after each file is written, with the fraction done.</param>
    /// <param name="cancellation">Checked between files; once the moves start they finish.</param>
    public static void Write(IReadOnlyList<PlannedFile> files, TranscriptDocument document, Action<double>? onFile, CancellationToken cancellation)
    {
        List<string> temporaries = [];
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                PlannedFile file = files[i];
                using (FileStream stream = CreateUnique(file.Path, TemporarySuffix, out string temporary))
                {
                    temporaries.Add(temporary);
                    TranscriptWriters.Write(file.Format, file.Variant, document, stream);
                    stream.Flush(flushToDisk: true);
                }

                onFile?.Invoke((i + 1) / (double)(files.Count + 1));
            }

            cancellation.ThrowIfCancellationRequested();
            for (int i = 0; i < files.Count; i++)
            {
                File.Move(temporaries[i], files[i].Path, overwrite: true);
            }
        }
        catch
        {
            foreach (string temporary in temporaries)
            {
                TryDelete(temporary);
            }

            throw;
        }
    }

    /// <summary>The ending of a temporary file, which no transcript and no common program writes.</summary>
    internal const string TemporarySuffix = ".mat-tmp";

    /// <summary>How many random names are tried before a collision is reported.</summary>
    private const int UniqueNameAttempts = 16;

    /// <summary>
    /// Creates a new file beside <paramref name="path"/> named <c>.&lt;file name&gt;.&lt;random&gt;&lt;suffix&gt;</c>,
    /// only where no file exists (a collision draws another name), so an existing file is never touched.
    /// </summary>
    internal static FileStream CreateUnique(string path, string suffix, out string created)
    {
        string folder = Path.GetDirectoryName(path) ?? ".";
        string name = Path.GetFileName(path);
        for (int attempt = 0; ; attempt++)
        {
            string candidate = Path.Combine(folder, "." + name + "." + Guid.NewGuid().ToString("N")[..12] + suffix);
            try
            {
                FileStream stream = new(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created = candidate;
                return stream;
            }
            catch (IOException) when (attempt < UniqueNameAttempts && File.Exists(candidate))
            {
                // Another file took this name first: draw another.
            }
        }
    }

    private static bool Contains(IReadOnlyCollection<TranscriptFormat> formats, TranscriptFormat format)
    {
        foreach (TranscriptFormat candidate in formats)
        {
            if (candidate == format)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The original failure is what the user needs; a temporary that cannot be deleted is left.
        }
    }
}
