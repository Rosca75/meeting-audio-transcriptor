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

    [Fact]
    public void ADevelopmentBuildNeverReportsTheSdkDefault()
    {
        string version = AppVersion.Current;

        Assert.True(version != "1.0.0", $"got '{version}', which is the SDK default for a build with no version; Directory.Build.props sets 0.0.0-dev and a release passes -p:Version");
    }
}
