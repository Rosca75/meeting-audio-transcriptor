// Tests that the WebView2 browser switches are the exact documented list and that none of them turns a
// network service on.
using System;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class BrowserArgumentsTests
{
    [Fact]
    public void TheCommandLineIsTheExactList()
    {
        const string Wanted =
            "--disable-background-networking --disable-component-update --disable-domain-reliability " +
            "--disable-sync --metrics-recording-only --no-pings --no-first-run " +
            "--disable-features=msSmartScreenProtection,OptimizationHints,MediaRouter";

        Assert.True(BrowserArguments.CommandLine == Wanted, $"got '{BrowserArguments.CommandLine}', wanted '{Wanted}'");
    }

    [Fact]
    public void NoSwitchEnablesAnything()
    {
        foreach (string entry in BrowserArguments.Switches)
        {
            Assert.True(entry.StartsWith("--disable-", StringComparison.Ordinal) || entry.StartsWith("--no-", StringComparison.Ordinal) || entry == "--metrics-recording-only", $"'{entry}' neither disables nor declines anything");
            Assert.DoesNotContain("enable-", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("remote-debugging", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("proxy", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("url", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(' ', entry);
        }
    }
}
