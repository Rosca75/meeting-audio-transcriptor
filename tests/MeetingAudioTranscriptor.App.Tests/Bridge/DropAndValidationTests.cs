// Tests the drop protocol (names from the page, resolved paths from the host) and the validation
// messages a bad run request produces.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Contracts.Bridge;
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests.Bridge;

[Trait("Category", "config")]
public sealed class DropTests
{
    private static readonly string[] Unsupported = ["a.pdf", "b.docx"];
    private static readonly string[] Mixed = ["a.pdf", "b.wav"];

    private static string DropJson(params string[] paths)
    {
        return "{\"drop\":{\"paths\":" + JsonSerializer.Serialize(paths, BridgeJson.Context.StringArray) + "}}";
    }

    [Fact]
    public async Task ADroppedSupportedPathBecomesFileSelected()
    {
        using BridgeHarness h = new();
        string path = h.File("call.MP4");

        await h.Bridge.ReceiveAsync(DropJson(path));

        JsonElement selected = Assert.Single(h.Host.Events(BridgeEvents.FileSelected));
        Assert.Equal(path, selected.GetProperty("path").GetString());
        Assert.Equal("call.MP4", selected.GetProperty("name").GetString());
        Assert.False(selected.TryGetProperty("note", out _), "got a note for a single file, wanted none");
    }

    [Fact]
    public async Task ADroppedUnsupportedPathBecomesFileRefusedNamingItAndTheSupportedTypes()
    {
        using BridgeHarness h = new();

        await h.Bridge.ReceiveAsync(DropJson(h.File("slides.pptx")));

        string reason = Assert.Single(h.Host.Events(BridgeEvents.FileRefused)).GetProperty("reason").GetString()!;
        Assert.Contains("\"slides.pptx\"", reason);
        Assert.Contains(".m4a, .mp4 and .wav", reason);
        Assert.Empty(h.Host.Events(BridgeEvents.FileSelected));
    }

    [Fact]
    public async Task SeveralDroppedFilesTakeTheFirstSupportedOneAndSaySo()
    {

        using BridgeHarness h = new();
        string first = h.File("a.txt");
        string second = h.File("b.wav");
        string third = h.File("c.m4a");

        await h.Bridge.ReceiveAsync(DropJson(first, second, third));

        JsonElement selected = Assert.Single(h.Host.Events(BridgeEvents.FileSelected));
        Assert.Equal(second, selected.GetProperty("path").GetString());
        string note = selected.GetProperty("note").GetString()!;
        Assert.Contains("3 files were dropped", note);
        Assert.Contains("\"b.wav\"", note);
    }

    [Fact]
    public async Task ADroppedPathThatNoLongerExistsIsRefusedWithAWayOut()
    {
        using BridgeHarness h = new();

        await h.Bridge.ReceiveAsync(DropJson(Path.Combine(h.Folder, "gone.wav")));

        string reason = Assert.Single(h.Host.Events(BridgeEvents.FileRefused)).GetProperty("reason").GetString()!;
        Assert.Contains("could not be found", reason);
    }

    [Fact]
    public async Task AnEmptyDropIsRefused()
    {

        using BridgeHarness h = new();

        await h.Bridge.ReceiveAsync(DropJson());

        Assert.Single(h.Host.Events(BridgeEvents.FileRefused));
    }

    [Fact]
    public async Task ThePagesOwnDropMarkerIsIgnoredByTheBridge()
    {

        using BridgeHarness h = new();

        await h.Bridge.ReceiveAsync("{\"drop\":true}");

        Assert.Empty(h.Host.Messages());
    }

    [Fact]
    public async Task FilesDroppedWithNoSupportedNameRefusesAtOnceAndResolvesNull()
    {

        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.FilesDropped, (object)Unsupported);

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
        string reason = Assert.Single(h.Host.Events(BridgeEvents.FileRefused)).GetProperty("reason").GetString()!;
        Assert.Contains("\"a.pdf\", \"b.docx\"", reason);
    }

    [Fact]
    public async Task FilesDroppedWithASupportedNameEmitsNothingBecauseThePathFollows()
    {

        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.FilesDropped, (object)Mixed);

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
        Assert.Empty(h.Host.Events(BridgeEvents.FileRefused));
    }
}

[Trait("Category", "errors")]
public sealed class RequestValidationTests
{

    private static async Task<string> RejectionAsync(BridgeHarness h, TranscribeRequest request)
    {

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, request);
        return reply.GetProperty("error").GetString() ?? throw new Xunit.Sdk.XunitException("got a successful reply, wanted a rejection for " + request);
    }

    [Fact]
    public async Task AnEmptyInputPathAsksTheUserToChooseAFile()
    {
        using BridgeHarness h = new();

        Assert.Contains("Choose a file", await RejectionAsync(h, h.Request("")));
    }

    [Fact]
    public async Task AnUnsupportedExtensionNamesTheFileAndTheSupportedTypes()
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request(h.File("voice.ogg")));

        Assert.Contains("\"voice.ogg\"", error);
        Assert.Contains(".m4a, .mp4 and .wav", error);
    }

    [Fact]
    public async Task AMissingFileIsNamedWithAWayOut()
    {

        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request(Path.Combine(h.Folder, "gone.wav")));

        Assert.Contains("\"gone.wav\" could not be found", error);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("")]
    [InlineData("english")]
    public async Task AnUnsupportedLanguageNamesTheChoices(string language)
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { Language = language });

        Assert.Contains("English, French or German", error);
    }

    [Fact]
    public async Task NoFormatAsksForAtLeastOne()
    {
        using BridgeHarness h = new();

        Assert.Contains("at least one", await RejectionAsync(h, h.Request() with { Formats = [] }));
    }

    [Fact]
    public async Task AnUnknownFormatIsNamed()
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { Formats = ["vtt", "srt"] });

        Assert.Contains("\"srt\"", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public async Task ASpeakerCountOutsideOneToTenIsRefused(int count)
    {

        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { SpeakerCount = count });

        Assert.Contains("from 1 to 10", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task TheSpeakerCountBoundsAreAccepted(int count)
    {

        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request() with { SpeakerCount = count });
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.False(reply.TryGetProperty("error", out _), $"got an error for speaker count {count}, wanted acceptance");
        Assert.Equal(count, h.Transcriber.LastRequest!.SpeakerCount);
    }

    [Fact]
    public async Task SpeakerNamesAreAcceptedTrimmedWithBlanksKept()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request() with { SpeakerCount = 4, SpeakerNames = ["  Anna Keller ", "", "\tBjörn"] });
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.False(reply.TryGetProperty("error", out _), "got an error for three valid names of four speakers, wanted acceptance");
        Assert.Equal(["Anna Keller", "", "Björn"], h.Transcriber.LastRequest!.SpeakerNames);
    }

    [Fact]
    public async Task ANameMayHoldFortyCharactersAndAsManyNamesAsSpeakers()
    {
        using BridgeHarness h = new();
        const string forty = "Maximiliane Alexandra von Hohenstein-Ost";

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request() with { SpeakerCount = 2, SpeakerNames = [forty, "R&D"] });
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.False(reply.TryGetProperty("error", out _), "got an error for a name of 40 characters, wanted acceptance");
        Assert.Equal([forty, "R&D"], h.Transcriber.LastRequest!.SpeakerNames);
    }

    [Fact]
    public async Task TrailingBlankNamesAreDroppedAndAllBlankNamesAreNoNames()
    {
        using BridgeHarness h = new();
        using BridgeHarness second = new();

        await h.CallAsync(BridgeMethods.RunStart, h.Request() with { SpeakerCount = 3, SpeakerNames = ["", "Chloé", "  "] });
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);
        IReadOnlyList<string>? trimmed = h.Transcriber.LastRequest!.SpeakerNames;

        await second.CallAsync(BridgeMethods.RunStart, second.Request() with { SpeakerCount = 3, SpeakerNames = ["", " "] });
        await second.Host.WaitForEventAsync(BridgeEvents.RunFinished);
        IReadOnlyList<string>? blank = second.Transcriber.LastRequest!.SpeakerNames;

        Assert.Equal(["", "Chloé"], trimmed);
        Assert.Null(blank);
    }

    [Fact]
    public async Task NamesAreDroppedWhenSeparationIsOff()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request() with { SpeakerSeparation = false, SpeakerCount = 2, SpeakerNames = ["Anna", "Anna"] });
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.False(reply.TryGetProperty("error", out _), "got an error for names with separation off, wanted them ignored");
        Assert.Null(h.Transcriber.LastRequest!.SpeakerNames);
        Assert.Null(h.Transcriber.LastRequest!.SpeakerCount);
    }

    [Fact]
    public async Task NamesWithoutASpeakerCountAreRefused()
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { SpeakerNames = ["Anna"] });

        Assert.Equal("Speaker names need a speaker count. Enter the number of speakers, or clear the names.", error);
    }

    [Fact]
    public async Task MoreNamesThanSpeakersAreRefused()
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { SpeakerCount = 2, SpeakerNames = ["Anna", "Björn", ""] });

        Assert.Equal("There are 3 speaker names for 2 speakers. Remove the extra names, or raise the speaker count.", error);
    }

    [Fact]
    public async Task ANameOverFortyCharactersIsRefusedNamingTheSpeaker()
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { SpeakerCount = 2, SpeakerNames = ["Anna", "Maximiliane Alexandra von Hohenstein-Ost2"] });

        Assert.Equal("The name of speaker 2 has 41 characters. Shorten it to 40 characters or fewer.", error);
    }

    [Theory]
    [InlineData("Anna <Finance>")]
    [InlineData("Anna>")]
    [InlineData("<b")]
    [InlineData("Anna\nKeller")]
    [InlineData("Anna\tKeller")]
    [InlineData("Anna\u0000")]
    [InlineData("Anna\u0085Keller")]
    public async Task ANameWithAnAngleBracketOrAControlCharacterIsRefused(string name)
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { SpeakerCount = 3, SpeakerNames = ["Björn", "", name] });

        Assert.Equal("The name of speaker 3 holds a character a transcript cannot carry. Remove any <, > and line break from it.", error);
    }

    [Theory]
    [InlineData("Anna", "anna")]
    [InlineData("Élodie", " ÉLODIE ")]
    public async Task TwoSpeakersWithOneNameAreRefused(string first, string second)
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { SpeakerCount = 3, SpeakerNames = [first, "", second] });

        Assert.Equal($"Speakers 1 and 3 are both named \"{first}\". Give each speaker a different name.", error);
    }

    [Theory]
    [InlineData("Speaker 2")]
    [InlineData("speaker 10")]
    public async Task ANameThatIsAnotherSpeakersDefaultLabelIsRefused(string name)
    {
        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { SpeakerCount = 2, SpeakerNames = [name] });

        Assert.Equal($"The name \"{name}\" of speaker 1 is the label of another speaker. Choose a different name.", error);
    }

    [Fact]
    public async Task ASpeakersOwnDefaultLabelIsAccepted()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request() with { SpeakerCount = 2, SpeakerNames = ["Speaker 1", "Speaker 2"] });
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.False(reply.TryGetProperty("error", out _), "got an error for each speaker's own default label, wanted acceptance");
    }

    [Fact]
    public async Task ARelativeOutputFolderIsRefused()
    {

        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { OutputFolder = "relative/out" });

        Assert.Contains("full path", error);
    }

    [Fact]
    public async Task AnOutputFolderThatIsAFileIsRefused()
    {

        using BridgeHarness h = new();

        string error = await RejectionAsync(h, h.Request() with { OutputFolder = h.File("taken") });

        Assert.Contains("is a file", error);
    }

    [Fact]
    public async Task ANewOutputFolderUnderAnExistingOneIsAcceptedWithoutBeingCreated()
    {

        using BridgeHarness h = new();
        string folder = Path.Combine(h.Folder, "out", "deeper");

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request() with { OutputFolder = folder });
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.False(reply.TryGetProperty("error", out _), "got an error for a creatable folder, wanted acceptance");
        Assert.Equal(folder, h.Transcriber.LastRequest!.OutputFolder);
        Assert.False(Directory.Exists(folder), "got a created folder, wanted validation to have no side effect");
    }

    [Fact]
    public async Task AllRejectionsAreFreeOfEmDashes()
    {

        using BridgeHarness h = new();
        TranscribeRequest[] bad =
        [
            h.Request("") , h.Request(h.File("x.ogg")), h.Request() with { Language = "xx" },
            h.Request() with { Formats = [] }, h.Request() with { SpeakerCount = 99 },
            h.Request() with { SpeakerNames = ["Anna"] }, h.Request() with { SpeakerCount = 1, SpeakerNames = ["Anna", "Björn"] },
            h.Request() with { SpeakerCount = 2, SpeakerNames = [new string('x', 41)] }, h.Request() with { SpeakerCount = 2, SpeakerNames = ["<"] },
            h.Request() with { SpeakerCount = 2, SpeakerNames = ["Anna", "ANNA"] }, h.Request() with { SpeakerCount = 2, SpeakerNames = ["Speaker 2"] },
        ];

        foreach (TranscribeRequest request in bad)
        {
            Assert.DoesNotContain('\u2014', await RejectionAsync(h, request));
        }
    }
}
