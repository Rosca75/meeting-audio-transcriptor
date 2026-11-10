// The command-line entry point: for now it prints the application's name and version.
using System;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Contracts;

namespace MeetingAudioTranscriptor.Cli;

internal static class Program
{
    private static int Main()
    {
        Console.Out.WriteLine(AppInfo.Name + " " + AppVersion.Current);
        return 0;
    }
}
