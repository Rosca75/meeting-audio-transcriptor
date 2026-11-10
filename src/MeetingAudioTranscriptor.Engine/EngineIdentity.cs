// The engine's name as it appears in diagnostics, derived from the shared application name.
using MeetingAudioTranscriptor.Contracts;

namespace MeetingAudioTranscriptor.Engine;

/// <summary>The engine's identity, the first thing the engine owns.</summary>
internal static class EngineIdentity
{
    /// <summary>The name diagnostics use for the engine.</summary>
    public static string Name => AppInfo.Name + " engine";
}
