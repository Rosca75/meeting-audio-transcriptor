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
internal sealed record PlannedFile(TranscriptFormat Format, TranscriptVariant Variant, string Path)
{
    /// <summary>The temporary file it is written to first, in the same folder so the move is a rename.</summary>
    public string TemporaryPath => Path + ".tmp";
}

/// <summary>Plans and writes the transcript files.</summary>
/// <remarks>
/// A file is named after the recording: <c>meeting.m4a</c> gives <c>meeting.vtt</c>,
/// <c>meeting.json</c> and <c>meeting.txt</c>, and with raw files <c>meeting.raw.vtt</c> and
/// <c>meeting.raw.txt</c> for the text formats chosen (the JSON holds both texts). Every file is
/// written to <c>name.tmp</c> first, and the temporaries are moved into place only once all are
/// complete, so a failure or a cancellation leaves the old files as they were and no partial file.
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
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                PlannedFile file = files[i];
                using (FileStream stream = new(file.TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    TranscriptWriters.Write(file.Format, file.Variant, document, stream);
                    stream.Flush(flushToDisk: true);
                }

                onFile?.Invoke((i + 1) / (double)(files.Count + 1));
            }

            cancellation.ThrowIfCancellationRequested();
            foreach (PlannedFile file in files)
            {
                File.Move(file.TemporaryPath, file.Path, overwrite: true);
            }
        }
        catch
        {
            foreach (PlannedFile file in files)
            {
                TryDelete(file.TemporaryPath);
            }

            throw;
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
