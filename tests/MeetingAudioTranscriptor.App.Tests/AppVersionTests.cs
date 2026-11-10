// Tests that the application reports a version both front ends can print.
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests;

[Trait("Category", "config")]
public sealed class AppVersionTests
{
    [Fact]
    public void CurrentVersionIsNeverEmptyAndCarriesNoBuildMetadata()
    {
        string version = AppVersion.Current;

        Assert.True(version.Length > 0, "got an empty version, wanted a dotted version such as 0.0.0");
        Assert.True(!version.Contains('+', System.StringComparison.Ordinal), $"got '{version}', wanted no '+' build metadata");
    }
}
