// Names the transcript files of a run and writes them as one set: each to a temporary file, the previous
// versions set aside, the new ones moved into place, and everything rolled back if one cannot be replaced.
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

    /// <summary>Writes the set of files as one: either every file is the new run's or every file is as it was.</summary>
    /// <remarks>
    /// The steps, each undone if a later one fails: every file is written to a temporary; every
    /// existing destination is opened for exclusive writing and closed again, so a file another
    /// program holds is found before anything moves; the previous versions are renamed aside to
    /// backups; the temporaries are renamed into place; only then are the backups deleted. A failure
    /// while replacing removes the new files already placed, renames the backups back and throws
    /// <see cref="TranscriptFileBusyException"/> naming the file. No temporary or backup is left,
    /// except a backup that could not be renamed back, which is kept because it is the user's
    /// previous transcript.
    /// </remarks>
    /// <param name="files">The planned files.</param>
    /// <param name="document">The transcript.</param>
    /// <param name="onFile">Called after each file is written, with the fraction done.</param>
    /// <param name="cancellation">Checked between files; once the replacing starts it finishes or rolls back.</param>
    /// <param name="fileSystem">The renames, checks and deletions of the replacing step; null for the real file system.</param>
    public static void Write(IReadOnlyList<PlannedFile> files, TranscriptDocument document, Action<double>? onFile, CancellationToken cancellation, IOutputFileSystem? fileSystem = null)
    {
        fileSystem ??= OutputFileSystem.Real;
        List<string> temporaries = [];
        List<(string Destination, string Backup)> backups = [];
        List<string> placed = [];
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

            // A previous transcript open in another program is found before anything moves.
            foreach (PlannedFile file in files)
            {
                if (fileSystem.Exists(file.Path))
                {
                    Replacing(file.Path, () => fileSystem.CheckReplaceable(file.Path));
                }
            }

            // The previous versions are set aside, not deleted, until the whole new set is in place.
            foreach (PlannedFile file in files)
            {
                if (fileSystem.Exists(file.Path))
                {
                    string backup = UnusedName(file.Path, BackupSuffix, fileSystem);
                    Replacing(file.Path, () => fileSystem.Move(file.Path, backup));
                    backups.Add((file.Path, backup));
                }
            }

            for (int i = 0; i < files.Count; i++)
            {
                Replacing(files[i].Path, () => fileSystem.Move(temporaries[i], files[i].Path));
                placed.Add(files[i].Path);
            }
        }
        catch
        {
            RollBack(placed, backups, fileSystem);
            foreach (string temporary in temporaries)
            {
                TryDelete(() => fileSystem.Delete(temporary));
            }

            throw;
        }

        foreach ((_, string backup) in backups)
        {
            TryDelete(() => fileSystem.Delete(backup));
        }
    }

    /// <summary>Removes the new files already placed and renames every previous version back.</summary>
    private static void RollBack(List<string> placed, List<(string Destination, string Backup)> backups, IOutputFileSystem fileSystem)
    {
        foreach (string destination in placed)
        {
            TryDelete(() => fileSystem.Delete(destination));
        }

        foreach ((string destination, string backup) in backups)
        {
            try
            {
                fileSystem.Move(backup, destination);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The backup is the user's previous transcript: it stays under its backup name rather than being lost.
            }
        }
    }

    /// <summary>Runs one replacing step, turning its failure into one that names the file.</summary>
    private static void Replacing(string path, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not TranscriptFileBusyException)
        {
            throw new TranscriptFileBusyException(path, e);
        }
    }

    /// <summary>A name beside <paramref name="path"/> that no file has: <c>.&lt;file name&gt;.&lt;random&gt;&lt;suffix&gt;</c>.</summary>
    private static string UnusedName(string path, string suffix, IOutputFileSystem fileSystem)
    {
        for (int attempt = 0; ; attempt++)
        {
            string candidate = SiblingName(path, suffix);
            if (!fileSystem.Exists(candidate) || attempt >= UniqueNameAttempts)
            {
                return candidate;
            }
        }
    }

    private static string SiblingName(string path, string suffix) =>
        Path.Combine(Path.GetDirectoryName(path) ?? ".", "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..12] + suffix);

    /// <summary>The ending of a temporary file, which no transcript and no common program writes.</summary>
    internal const string TemporarySuffix = ".mat-tmp";

    /// <summary>The ending of a previous version set aside while the new set is moved in.</summary>
    internal const string BackupSuffix = ".mat-bak";

    /// <summary>How many random names are tried before a collision is reported.</summary>
    private const int UniqueNameAttempts = 16;

    /// <summary>
    /// Creates a new file beside <paramref name="path"/> named <c>.&lt;file name&gt;.&lt;random&gt;&lt;suffix&gt;</c>,
    /// only where no file exists (a collision draws another name), so an existing file is never touched.
    /// </summary>
    internal static FileStream CreateUnique(string path, string suffix, out string created)
    {
        for (int attempt = 0; ; attempt++)
        {
            string candidate = SiblingName(path, suffix);
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

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The original failure is what the user needs; a file that cannot be deleted is left.
        }
    }
}

/// <summary>The file operations of replacing a transcript set, replaced by a fake that fails on cue in the tests.</summary>
internal interface IOutputFileSystem
{
    /// <summary>True when a file exists at the path.</summary>
    bool Exists(string path);

    /// <summary>Throws when the existing file cannot be replaced now, for example because another program holds it.</summary>
    void CheckReplaceable(string path);

    /// <summary>Renames a file; the destination must not exist.</summary>
    void Move(string from, string to);

    /// <summary>Deletes a file; nothing happens when it does not exist.</summary>
    void Delete(string path);
}

/// <summary>The real file system.</summary>
internal sealed class OutputFileSystem : IOutputFileSystem
{
    /// <summary>The one instance.</summary>
    public static OutputFileSystem Real { get; } = new();

    /// <inheritdoc />
    public bool Exists(string path) => File.Exists(path);

    /// <inheritdoc />
    /// <remarks>
    /// Opening for writing with no sharing fails on Windows while another program has the file open
    /// without sharing it, and everywhere for a read-only file; the file is closed again unchanged.
    /// </remarks>
    public void CheckReplaceable(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.None);
    }

    /// <inheritdoc />
    public void Move(string from, string to) => File.Move(from, to, overwrite: false);

    /// <inheritdoc />
    public void Delete(string path) => File.Delete(path);
}

/// <summary>A transcript file of the previous run could not be replaced; the previous set was restored.</summary>
internal sealed class TranscriptFileBusyException : IOException
{
    /// <summary>Creates the failure for one file.</summary>
    public TranscriptFileBusyException(string path, Exception inner)
        : base($"{path} could not be replaced ({inner.Message}), so the previous transcript files were left as they were; close any program that has it open and check that it is not read-only, then run the transcription again", inner)
    {
        FilePath = path;
    }

    /// <summary>The file that could not be replaced.</summary>
    public string FilePath { get; }
}
