// Tests the ffmpeg lookup order (the override, the bundled copy, the PATH) over an injected environment and file system.
using System;
using System.Collections.Generic;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[Trait("Category", "config")]
public sealed class FfmpegLocatorTests
{
    private const string AppDirectory = "/opt/mat";

    [Fact]
    public void TheEnvironmentVariableWinsWhenItsFileExists()
    {
        FfmpegLocator locator = Locator(
            new() { ["MAT_FFMPEG"] = " /custom/ffmpeg ", ["PATH"] = "/usr/bin" },
            "/custom/ffmpeg", "/opt/mat/ffmpeg/ffmpeg", "/usr/bin/ffmpeg");

        FfmpegLocation? found = locator.Locate();

        Assert.True(
            found == new FfmpegLocation("/custom/ffmpeg", FfmpegOrigin.EnvironmentVariable),
            $"got {found}, wanted the MAT_FFMPEG path");
    }

    [Fact]
    public void AMissingOverrideFallsThroughToTheBundledCopyAndIsNamedInTheSearch()
    {
        FfmpegLocator locator = Locator(new() { ["MAT_FFMPEG"] = "/nowhere/ffmpeg" }, "/opt/mat/ffmpeg/ffmpeg");

        FfmpegLocation? found = locator.Locate();

        Assert.True(found == new FfmpegLocation("/opt/mat/ffmpeg/ffmpeg", FfmpegOrigin.Bundled), $"got {found}, wanted the bundled copy");
        Assert.True(
            locator.DescribeSearch().Contains("MAT_FFMPEG=/nowhere/ffmpeg (no such file)", StringComparison.Ordinal),
            $"got '{locator.DescribeSearch()}', wanted the missing override named");
    }

    [Fact]
    public void TheFirstPathEntryHoldingFfmpegIsChosen()
    {
        FfmpegLocator locator = Locator(new() { ["PATH"] = "/a::/b:/c" }, "/b/ffmpeg", "/c/ffmpeg");

        FfmpegLocation? found = locator.Locate();

        Assert.True(found == new FfmpegLocation("/b/ffmpeg", FfmpegOrigin.SearchPath), $"got {found}, wanted /b/ffmpeg from the PATH");
        Assert.True(found!.ToString() == "ffmpeg (PATH) at /b/ffmpeg", $"got '{found}', wanted the origin named");
    }

    [Fact]
    public void OnWindowsTheExecutableIsFfmpegExeAndThePathIsSplitOnSemicolons()
    {
        FfmpegLocator locator = Locator(new() { ["PATH"] = "\"/tools/x\";/tools/y" }, isWindows: true, "/tools/x/ffmpeg.exe");

        FfmpegLocation? found = locator.Locate();

        Assert.True(found == new FfmpegLocation("/tools/x/ffmpeg.exe", FfmpegOrigin.SearchPath), $"got {found}, wanted the quoted PATH entry unquoted");
        Assert.True(locator.BundledPath == "/opt/mat/ffmpeg/ffmpeg.exe", $"got {locator.BundledPath}, wanted ffmpeg.exe in the ffmpeg folder");
    }

    [Fact]
    public void NothingFoundIsNullAndTheSearchNamesEveryPlace()
    {
        FfmpegLocator locator = Locator(new() { ["PATH"] = "/usr/bin" });

        FfmpegLocation? found = locator.Locate();
        string search = locator.DescribeSearch();

        Assert.True(found is null, $"got {found}, wanted none");
        Assert.True(
            search.Contains("/opt/mat/ffmpeg/ffmpeg", StringComparison.Ordinal) && search.Contains("PATH", StringComparison.Ordinal),
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
