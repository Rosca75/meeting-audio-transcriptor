// Tests the ffmpeg lookup order (the override, the bundled copy, the PATH) over an injected environment and file system.
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[Trait("Category", "config")]
public sealed class FfmpegLocatorTests
{
    private const string AppDirectory = "/opt/mat";

    // The locator joins folders with the platform's separator, as a real path needs: the expected
    // paths are joined the same way, so the tests read alike on Windows and elsewhere.
    private static readonly string Bundled = Path.Combine(AppDirectory, "ffmpeg", "ffmpeg");

    [Fact]
    public void TheEnvironmentVariableWinsWhenItsFileExists()
    {
        FfmpegLocator locator = Locator(
            new() { ["MAT_FFMPEG"] = " /custom/ffmpeg ", ["PATH"] = "/usr/bin" },
            "/custom/ffmpeg", Bundled, Path.Combine("/usr/bin", "ffmpeg"));

        FfmpegLocation? found = locator.Locate();

        Assert.True(
            found == new FfmpegLocation("/custom/ffmpeg", FfmpegOrigin.EnvironmentVariable),
            $"got {found}, wanted the MAT_FFMPEG path");
    }

    [Fact]
    public void AMissingOverrideFallsThroughToTheBundledCopyAndIsNamedInTheSearch()
    {
        FfmpegLocator locator = Locator(new() { ["MAT_FFMPEG"] = "/nowhere/ffmpeg" }, Bundled);

        FfmpegLocation? found = locator.Locate();

        Assert.True(found == new FfmpegLocation(Bundled, FfmpegOrigin.Bundled), $"got {found}, wanted the bundled copy");
        Assert.True(
            locator.DescribeSearch().Contains("MAT_FFMPEG=/nowhere/ffmpeg (no such file)", StringComparison.Ordinal),
            $"got '{locator.DescribeSearch()}', wanted the missing override named");
    }

    [Fact]
    public void TheFirstPathEntryHoldingFfmpegIsChosen()
    {
        string inB = Path.Combine("/b", "ffmpeg");
        FfmpegLocator locator = Locator(new() { ["PATH"] = "/a::/b:/c" }, inB, Path.Combine("/c", "ffmpeg"));

        FfmpegLocation? found = locator.Locate();

        Assert.True(found == new FfmpegLocation(inB, FfmpegOrigin.SearchPath), $"got {found}, wanted {inB} from the PATH");
        Assert.True(found!.ToString() == $"ffmpeg (PATH) at {inB}", $"got '{found}', wanted the origin named");
    }

    [Fact]
    public void OnWindowsTheExecutableIsFfmpegExeAndThePathIsSplitOnSemicolons()
    {
        string inX = Path.Combine("/tools/x", "ffmpeg.exe");
        FfmpegLocator locator = Locator(new() { ["PATH"] = "\"/tools/x\";/tools/y" }, isWindows: true, inX);

        FfmpegLocation? found = locator.Locate();

        Assert.True(found == new FfmpegLocation(inX, FfmpegOrigin.SearchPath), $"got {found}, wanted the quoted PATH entry unquoted");
        Assert.True(locator.BundledPath == Path.Combine(AppDirectory, "ffmpeg", "ffmpeg.exe"), $"got {locator.BundledPath}, wanted ffmpeg.exe in the ffmpeg folder");
    }

    [Fact]
    public void NothingFoundIsNullAndTheSearchNamesEveryPlace()
    {
        FfmpegLocator locator = Locator(new() { ["PATH"] = "/usr/bin" });

        FfmpegLocation? found = locator.Locate();
        string search = locator.DescribeSearch();

        Assert.True(found is null, $"got {found}, wanted none");
        Assert.True(
            search.Contains(Bundled, StringComparison.Ordinal) && search.Contains("PATH", StringComparison.Ordinal),
            $"got '{search}', wanted the bundled path and the PATH named");
    }

    private static FfmpegLocator Locator(Dictionary<string, string> environment, params string[] existing) =>
        Locator(environment, isWindows: false, existing);

    private static FfmpegLocator Locator(Dictionary<string, string> environment, bool isWindows, params string[] existing)
    {
        HashSet<string> files = [.. existing];
        return new FfmpegLocator(name => environment.GetValueOrDefault(name), AppDirectory, isWindows, files.Contains);
    }
}
