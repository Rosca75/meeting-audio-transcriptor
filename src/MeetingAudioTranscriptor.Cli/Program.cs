// The command-line entry point: reads the command line, wires Ctrl+C to cancellation and runs the command.
using System;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Cli.Benchmark;
using MeetingAudioTranscriptor.Contracts;

namespace MeetingAudioTranscriptor.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancel = new();
        int presses = 0;
        Console.CancelKeyPress += (_, e) =>
        {
            // The first Ctrl+C cancels cleanly; a second one ends the process at once.
            if (Interlocked.Increment(ref presses) == 1)
            {
                e.Cancel = true;
                cancel.Cancel();
            }
        };

        return await Run(args, cancel.Token).ConfigureAwait(false);
    }

    /// <summary>Runs one command line and returns the exit code.</summary>
    internal static async Task<int> Run(string[] args, CancellationToken cancellation)
    {
        ParsedCommandLine parsed = CommandLine.Parse(args);
        switch (parsed.Kind)
        {
            case CommandKind.Help:
                Console.Out.Write(HelpText.For(parsed.Topic));
                return ExitCodes.Success;
            case CommandKind.Version:
                Console.Out.WriteLine(AppInfo.Name + " " + AppVersion.Current);
                return ExitCodes.Success;
            case CommandKind.UsageError:
                Console.Error.WriteLine("error: " + parsed.Error);
                Console.Error.WriteLine("Run " + HelpText.Executable + (parsed.Topic is null ? "" : " " + parsed.Topic) + " --help for the options.");
                return ExitCodes.Usage;
            case CommandKind.Transcribe:
                return await TranscribeCommand.Run(parsed.Transcribe!, Console.Out, Console.Error, !Console.IsErrorRedirected, cancellation).ConfigureAwait(false);
            case CommandKind.Models:
                return ModelsCommand.Run(parsed.Models!, Console.Out, Console.Error);
            default:
                return await BenchmarkCommand.Run(parsed.Benchmark!, Console.Out, Console.Error, cancellation).ConfigureAwait(false);
        }
    }
}
