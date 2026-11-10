// The application's version as both front ends show it, read from the assembly.
using System.Reflection;

namespace MeetingAudioTranscriptor.App;

/// <summary>Reports the version the build stamped on the application.</summary>
public static class AppVersion
{
    /// <summary>The informational version without build metadata, or 0.0.0 when none is stamped.</summary>
    public static string Current
    {
        get
        {
            string? stamped = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(stamped))
            {
                return "0.0.0";
            }

            int metadata = stamped.IndexOf('+', System.StringComparison.Ordinal);
            return metadata < 0 ? stamped : stamped[..metadata];
        }
    }
}
