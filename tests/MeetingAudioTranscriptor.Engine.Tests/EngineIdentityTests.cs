// Tests that the contracts' application name reaches the engine unchanged.
using MeetingAudioTranscriptor.Contracts;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests;

[Trait("Category", "config")]
public sealed class EngineIdentityTests
{
    [Fact]
    public void ApplicationNameIsTheShippedCommandName()
    {
        Assert.True(
            AppInfo.Name == "meeting-audio-transcriptor",
            $"got '{AppInfo.Name}', wanted 'meeting-audio-transcriptor' (AppInfo.Name)");
    }

    [Fact]
    public void EngineNameIsBuiltFromTheApplicationName()
    {
        Assert.True(
            EngineIdentity.Name == "meeting-audio-transcriptor engine",
            $"got '{EngineIdentity.Name}', wanted 'meeting-audio-transcriptor engine' (EngineIdentity.Name)");
    }
}
