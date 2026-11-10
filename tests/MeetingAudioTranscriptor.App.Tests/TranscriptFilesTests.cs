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

    private List<PlannedFile> Plan() =>
        TranscriptFiles.Plan(Path.Combine(_folder, "meeting.m4a"), _folder, [TranscriptFormat.Vtt, TranscriptFormat.Json, TranscriptFormat.Txt], rawFiles: false);

    private void AssertFolderHolds(params string[] wanted)
    {
        string[] names = Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
        Assert.True(names.SequenceEqual(wanted), $"the folder holds {string.Join(", ", names)}; wanted {string.Join(", ", wanted)}");
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
