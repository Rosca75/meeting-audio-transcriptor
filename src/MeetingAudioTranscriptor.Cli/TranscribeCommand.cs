// The transcribe command: runs the service on one recording, shows progress on stderr and prints the summary.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App;

namespace MeetingAudioTranscriptor.Cli;

/// <summary>Runs <c>transcribe</c>.</summary>
internal static class TranscribeCommand
{
    /// <summary>The request the arguments describe.</summary>
    public static TranscriptionRequest Request(TranscribeArguments arguments) => new()
    {
        InputPath = arguments.File,
        Language = arguments.Language,
        Formats = arguments.Formats,
        OutputFolder = arguments.Output,
        SpeakerSeparation = arguments.NoSpeakers ? false : null,
        SpeakerCountHint = arguments.Speakers,
        Threads = arguments.Threads,
        RawFiles = arguments.Raw,
    };

    public static async Task<int> Run(TranscribeArguments arguments, TextWriter output, TextWriter errors, bool terminal, CancellationToken cancellation)
    {
        using TranscriptionService service = new(arguments.Models);
        ProgressDisplay? display = arguments.Quiet ? null : new ProgressDisplay(errors, terminal);
        TranscriptionOutcome outcome = await service.RunAsync(Request(arguments), display, cancellation).ConfigureAwait(false);
        display?.Finish();

        if (outcome.Succeeded)
        {
            // A notice says the run gave less than was asked (no speakers); it goes with the errors, so a script reading stdout is unaffected.
            foreach (string notice in outcome.Notices)
            {
                errors.WriteLine("note: " + notice);
            }

            if (!arguments.Quiet)
            {
                output.Write(ConsoleText.Summary(outcome, !arguments.NoSpeakers));
            }
        }
        else
        {
            errors.WriteLine("error: " + outcome.Message);
        }

        return ExitCodes.Of(outcome.Kind);
    }
}

/// <summary>Runs <c>models</c>.</summary>
internal static class ModelsCommand
{
    public static int Run(ModelsArguments arguments, TextWriter output, TextWriter errors)
    {
        using TranscriptionService service = new(arguments.Models);
        ModelStatus status = service.Check();
        output.WriteLine("Models folder: " + status.ModelsRoot);
        foreach (string file in status.RequiredFiles)
        {
            output.WriteLine((status.MissingFiles.Contains(file) ? "  missing  " : "  found    ") + file);
        }

        // Speaker separation is optional: its model is listed apart, and its absence is not an error.
        foreach (string file in status.SpeakerSeparationFiles)
        {
            output.WriteLine((status.SpeakerSeparationAvailable ? "  found    " : "  missing  ") + file + " (optional: speaker separation)");
        }

        output.WriteLine(status.SpeakerSeparationAvailable ? "Speaker separation: available" : "Speaker separation: not installed; " + status.SpeakerSeparationMessage);
        if (!status.Ready)
        {
            errors.WriteLine("error: " + status.Message);
            return ExitCodes.Models;
        }

        if (!arguments.Verify)
        {
            return ExitCodes.Success;
        }

        try
        {
            System.Collections.Generic.IReadOnlyList<string> problems = service.Verify();
            foreach (string problem in problems)
            {
                errors.WriteLine("error: " + problem);
            }

            output.WriteLine(problems.Count == 0 ? "Every file matches its size and SHA-256 in the lock." : $"{problems.Count} file(s) failed verification.");
            return problems.Count == 0 ? ExitCodes.Success : ExitCodes.Models;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            errors.WriteLine("error: " + e.Message);
            return ExitCodes.Models;
        }
    }
}
