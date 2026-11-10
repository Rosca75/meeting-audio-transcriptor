// Tests of the atomic writing of a run's transcript files: the temporary names, the replacement of a
// previous set, the roll-back when a file cannot be replaced, and that nothing temporary is left.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests;

[Trait("Category", "transcript")]
public sealed class TranscriptFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mat-files-" + Guid.NewGuid().ToString("N"));

    public TranscriptFilesTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AUserFileNamedLikeAnOldTemporarySurvivesUntouched()
    {
        string userFile = Path.Combine(_folder, "meeting.vtt.tmp");
        File.WriteAllText(userFile, "mine");
        List<PlannedFile> files = Plan();

        TranscriptFiles.Write(files, Document(), null, CancellationToken.None);

        Assert.True(File.ReadAllText(userFile) == "mine", "the user's meeting.vtt.tmp was changed");
        Assert.True(File.ReadAllText(Path.Combine(_folder, "meeting.vtt")).StartsWith("WEBVTT", StringComparison.Ordinal), "meeting.vtt is not the new transcript");
        AssertFolderHolds("meeting.json", "meeting.txt", "meeting.vtt", "meeting.vtt.tmp");
    }

    [Fact]
    public void ATemporaryIsHiddenUniqueAndNeverReplacesAFile()
    {
        string target = Path.Combine(_folder, "meeting.vtt");
        string first;
        string second;
        using (TranscriptFiles.CreateUnique(target, TranscriptFiles.TemporarySuffix, out first))
        using (TranscriptFiles.CreateUnique(target, TranscriptFiles.TemporarySuffix, out second))
        {
        }

        string name = Path.GetFileName(first);
        Assert.True(name.StartsWith(".meeting.vtt.", StringComparison.Ordinal) && name.EndsWith(".mat-tmp", StringComparison.Ordinal), $"got '{name}'");
        Assert.True(first != second, "two temporaries of one file got the same name");
        Assert.True(Path.GetDirectoryName(first) == _folder, "the temporary is not in the destination folder");
    }

    [Fact]
    public void ANewSetReplacesThePreviousOneAndLeavesNothingBehind()
    {
        WriteOld("meeting.vtt", "meeting.json", "meeting.txt");

        TranscriptFiles.Write(Plan(), Document(), null, CancellationToken.None);

        Assert.True(File.ReadAllText(Path.Combine(_folder, "meeting.json")).Contains("\"Hello.\"", StringComparison.Ordinal), "meeting.json is not the new transcript");
        Assert.True(File.ReadAllText(Path.Combine(_folder, "meeting.txt")).Contains("Hello.", StringComparison.Ordinal), "meeting.txt is not the new transcript");
        AssertFolderHolds("meeting.json", "meeting.txt", "meeting.vtt");
    }

    [Fact]
    public void AFileThatCannotBeReplacedStopsTheSetBeforeAnythingMoves()
    {
        WriteOld("meeting.vtt", "meeting.json", "meeting.txt");
        FailingFileSystem fileSystem = new() { FailCheckOf = "meeting.json" };

        TranscriptFileBusyException error = Assert.Throws<TranscriptFileBusyException>(() => TranscriptFiles.Write(Plan(), Document(), null, CancellationToken.None, fileSystem));

        Assert.True(error.FilePath == Path.Combine(_folder, "meeting.json"), $"the failure names {error.FilePath}");
        Assert.True(fileSystem.Moves == 0, $"{fileSystem.Moves} renames happened before the check failed");
        AssertOld("meeting.vtt", "meeting.json", "meeting.txt");
        AssertFolderHolds("meeting.json", "meeting.txt", "meeting.vtt");
    }

    [Fact]
    public void AMoveThatFailsAfterOthersRestoresThePreviousSet()
    {
        WriteOld("meeting.vtt", "meeting.json", "meeting.txt");
        FailingFileSystem fileSystem = new() { FailMoveTo = "meeting.txt" };

        TranscriptFileBusyException error = Assert.Throws<TranscriptFileBusyException>(() => TranscriptFiles.Write(Plan(), Document(), null, CancellationToken.None, fileSystem));

        Assert.True(error.FilePath == Path.Combine(_folder, "meeting.txt"), $"the failure names {error.FilePath}");
        Assert.True(error.Message.Contains("meeting.txt could not be replaced", StringComparison.Ordinal) && error.Message.Contains("close any program", StringComparison.Ordinal), $"got '{error.Message}'");
        AssertOld("meeting.vtt", "meeting.json", "meeting.txt");
        AssertFolderHolds("meeting.json", "meeting.txt", "meeting.vtt");
    }

    [Fact]
    public void ASetAsideThatFailsRestoresThePreviousSet()
    {
        WriteOld("meeting.vtt", "meeting.json", "meeting.txt");
        FailingFileSystem fileSystem = new() { FailMoveFrom = "meeting.json" };

        Assert.Throws<TranscriptFileBusyException>(() => TranscriptFiles.Write(Plan(), Document(), null, CancellationToken.None, fileSystem));

        AssertOld("meeting.vtt", "meeting.json", "meeting.txt");
        AssertFolderHolds("meeting.json", "meeting.txt", "meeting.vtt");
    }

    [Fact]
    public void AFailedFirstRunLeavesTheFolderAsItWas()
    {
        WriteOld("meeting.txt");
        FailingFileSystem fileSystem = new() { FailMoveTo = "meeting.txt" };

        Assert.Throws<TranscriptFileBusyException>(() => TranscriptFiles.Write(Plan(), Document(), null, CancellationToken.None, fileSystem));

        AssertOld("meeting.txt");
        AssertFolderHolds("meeting.txt");
    }

    [Fact]
    public void ACancelledWriteLeavesThePreviousSetAndNoTemporary()
    {
        WriteOld("meeting.vtt", "meeting.json", "meeting.txt");
        using CancellationTokenSource cancel = new();

        Assert.ThrowsAny<OperationCanceledException>(() => TranscriptFiles.Write(Plan(), Document(), fraction => cancel.Cancel(), cancel.Token));

        AssertOld("meeting.vtt", "meeting.json", "meeting.txt");
        AssertFolderHolds("meeting.json", "meeting.txt", "meeting.vtt");
    }

    [Fact]
    public void ABusyFileMapsToAnOutputOutcomeNamingIt()
    {
        string path = Path.Combine(_folder, "meeting.json");
        (TranscriptionOutcomeKind kind, string message) = FailureMapping.Map(new TranscriptFileBusyException(path, new IOException("in use")), RunPhase.Output, "in.wav", _folder);

        Assert.True(kind == TranscriptionOutcomeKind.OutputNotWritable, $"got {kind}");
        Assert.True(message == $"{path} could not be replaced (in use), so the previous transcript files were left as they were; close any program that has it open and check that it is not read-only, then run the transcription again", $"got '{message}'");
    }

    private void WriteOld(params string[] names)
    {
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(_folder, name), "old " + name);
        }
    }

    private void AssertOld(params string[] names)
    {
        foreach (string name in names)
        {
            string text = File.ReadAllText(Path.Combine(_folder, name));
            Assert.True(text == "old " + name, $"{name} holds '{text}', not its previous content");
        }
    }

    private List<PlannedFile> Plan() =>
        TranscriptFiles.Plan(Path.Combine(_folder, "meeting.m4a"), _folder, [TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Txt], rawFiles: false);

    private void AssertFolderHolds(params string[] wanted)
    {
        string[] names = Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
        Assert.True(names.SequenceEqual(wanted), $"the folder holds {string.Join(", ", names)}; wanted {string.Join(", ", wanted)}");
    }

    /// <summary>The real file system, failing one step on cue as a file held by another program would.</summary>
    private sealed class FailingFileSystem : IOutputFileSystem
    {
        /// <summary>The file name whose replaceability check fails.</summary>
        public string? FailCheckOf { get; init; }

        /// <summary>The file name a rename into fails.</summary>
        public string? FailMoveTo { get; init; }

        /// <summary>The file name a rename out of fails.</summary>
        public string? FailMoveFrom { get; init; }

        private bool _failed;

        public int Moves { get; private set; }

        public bool Exists(string path) => OutputFileSystem.Real.Exists(path);

        public void CheckReplaceable(string path)
        {
            if (Path.GetFileName(path) == FailCheckOf)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            OutputFileSystem.Real.CheckReplaceable(path);
        }

        public void Move(string from, string to)
        {
            // Only the first attempt fails, as a program closing the file would let the roll-back through.
            if (!_failed && (Path.GetFileName(to) == FailMoveTo || Path.GetFileName(from) == FailMoveFrom))
            {
                _failed = true;
                throw new UnauthorizedAccessException("Access to the path is denied.");
            }

            Moves++;
            OutputFileSystem.Real.Move(from, to);
        }

        public void Delete(string path) => OutputFileSystem.Real.Delete(path);
    }

    private static TranscriptDocument Document() => new()
    {
        SchemaVersion = "1",
        Metadata = new TranscriptMetadata
        {
            Application = "test",
            ApplicationVersion = "1",
            SourceFile = "meeting.m4a",
            DurationSeconds = 2,
            SpeechSeconds = 1,
            Language = "en",
            CreatedUtc = "2026-01-01T00:00:00Z",
            ProcessingSeconds = 1,
            Models = [],
            Settings = new TranscriptSettings { SpeakerSeparation = false, Threads = 1, Formats = ["vtt", "json", "txt"], RawFiles = false },
        },
        Speakers = [],
        Segments =
        [
            new TranscriptSegment { Id = 1, Start = 0, End = 1, Text = "Hello.", RawText = "Hello.", Confidence = 1 },
        ],
    };
}
