// Tests that the per-user data folder follows each platform's rules.
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class DataFolderTests
{
    private const string Home = "/home/ana";

    [Fact]
    public void WindowsUsesLocalAppData()
    {
        string got = DataFolder.Resolve(true, Environment(("LOCALAPPDATA", "C:/Users/Ana/AppData/Local")), Home);
        string wanted = Path.Combine("C:/Users/Ana/AppData/Local", "meeting-audio-transcriptor");

        Assert.True(got == wanted, $"got '{got}', wanted '{wanted}'");
    }

    [Fact]
    public void WindowsWithoutLocalAppDataFallsBackUnderTheProfile()
    {
        string got = DataFolder.Resolve(true, Environment(), Home);
        string wanted = Path.Combine(Home, "AppData", "Local", "meeting-audio-transcriptor");

        Assert.True(got == wanted, $"got '{got}', wanted '{wanted}'");
    }

    [Fact]
    public void LinuxUsesXdgDataHome()
    {
        string got = DataFolder.Resolve(false, Environment(("XDG_DATA_HOME", "/data/ana")), Home);
        string wanted = Path.Combine("/data/ana", "meeting-audio-transcriptor");

        Assert.True(got == wanted, $"got '{got}', wanted '{wanted}'");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/path")]
    public void LinuxFallsBackToLocalShare(string? dataHome)
    {
        string got = DataFolder.Resolve(false, Environment(("XDG_DATA_HOME", dataHome)), Home);
        string wanted = Path.Combine(Home, ".local", "share", "meeting-audio-transcriptor");

        Assert.True(got == wanted, $"got '{got}' for XDG_DATA_HOME '{dataHome}', wanted '{wanted}'");
    }

    [Fact]
    public void TheWebViewProfileIsUnderTheDataFolder()
    {
        string got = DataFolder.WebViewProfile("/data/app");
        string wanted = Path.Combine("/data/app", "webview");

        Assert.True(got == wanted, $"got '{got}', wanted '{wanted}'");
    }

    private static System.Func<string, string?> Environment(params (string Name, string? Value)[] variables)
    {
        Dictionary<string, string?> values = [];
        foreach ((string name, string? value) in variables)
        {
            values[name] = value;
        }

        return name => values.GetValueOrDefault(name);
    }
}
