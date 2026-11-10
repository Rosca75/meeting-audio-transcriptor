// The wording of the console output: the progress line, durations, the speed and the summary after a run.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Engine.Pipeline;

namespace MeetingAudioTranscriptor.Cli;

/// <summary>Formats what the command line prints.</summary>
internal static class ConsoleText
{
    /// <summary>The name a user reads for a stage.</summary>
    public static string StageName(TranscriptionStage stage) => stage switch
    {
        TranscriptionStage.LoadingModels => "Loading models",
        TranscriptionStage.Transcribing => "Transcribing",
        TranscriptionStage.IdentifyingSpeakers => "Identifying speakers",
        TranscriptionStage.Cleaning => "Cleaning",
        TranscriptionStage.Writing => "Writing",
        _ => stage.ToString(),
    };

    /// <summary>One progress line: the stage and the whole run's percentage, such as "Transcribing  42%".</summary>
    public static string ProgressLine(TranscriptionProgress progress)
    {
        int percent = (int)Math.Floor(Math.Clamp(progress.Fraction, 0, 1) * 100);
        return string.Create(CultureInfo.InvariantCulture, $"{StageName(progress.Stage),-20} {percent,3}%");
    }

    /// <summary>A duration a person reads: "45.2 s", "3 min 05 s", "1 h 02 min 03 s".</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return "unknown";
        }

        if (seconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds:0.0} s");
        }

        long whole = (long)Math.Round(seconds);
        long hours = whole / 3600;
        long minutes = whole % 3600 / 60;
        long rest = whole % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours} h {minutes:00} min {rest:00} s")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes} min {rest:00} s");
    }

    /// <summary>The speed as a person reads it: "12.3 x real time".</summary>
    public static string Speed(double audioSeconds, double processingSeconds) =>
        audioSeconds > 0 && processingSeconds > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{audioSeconds / processingSeconds:0.0} x real time")
            : "not measured";

    /// <summary>The lines printed after a successful run.</summary>
    public static string Summary(TranscriptionOutcome outcome, bool speakersRequested)
    {
        StringBuilder text = new();
        text.AppendLine(outcome.Kind == TranscriptionOutcomeKind.NoSpeech ? "No speech was found; an empty transcript was written:" : "Transcript written:");
        foreach (string file in outcome.OutputFiles)
        {
            text.Append("  ").AppendLine(file);
        }

        text.Append("Audio:      ").AppendLine(Duration(outcome.AudioSeconds));
        text.Append("Processing: ").AppendLine(Duration(outcome.ProcessingSeconds));
        text.Append("Speed:      ").AppendLine(Speed(outcome.AudioSeconds, outcome.ProcessingSeconds));
        if (!string.IsNullOrEmpty(outcome.DecoderName))
        {
            text.Append("Decoder:    ").AppendLine(outcome.DecoderName);
        }

        text.Append("Speakers:   ").AppendLine(outcome.SpeakerCount > 0
            ? outcome.SpeakerCount.ToString(CultureInfo.InvariantCulture)
            : speakersRequested ? "not separated (no speaker model is installed)" : "not separated");
        text.Append("Words:      ").AppendLine(string.Create(CultureInfo.InvariantCulture, $"{outcome.WordCount} recognised, {outcome.CleanedWordCount} after cleaning, in {outcome.SegmentCount} segments"));
        return text.ToString();
    }
}

/// <summary>Shows progress: one updating line on a terminal, a plain line every 10 % otherwise.</summary>
/// <remarks>
/// A terminal gets the line rewritten in place with a carriage return; a log file or a pipe gets
/// a new line at each tenth and at each stage change, so a captured log stays readable.
/// </remarks>
internal sealed class ProgressDisplay : IProgress<TranscriptionProgress>
{
    private readonly TextWriter _writer;
    private readonly bool _terminal;
    private readonly object _gate = new();
    private string _lastLine = "";
    private int _lastTenth = -1;
    private TranscriptionStage? _lastStage;
    private bool _lineOpen;

    public ProgressDisplay(TextWriter writer, bool terminal)
    {
        _writer = writer;
        _terminal = terminal;
    }

    public void Report(TranscriptionProgress value)
    {
        string line = ConsoleText.ProgressLine(value);
        lock (_gate)
        {
            if (_terminal)
            {
                if (line != _lastLine)
                {
                    _writer.Write("\r" + line.PadRight(Math.Max(line.Length, _lastLine.Length)));
                    _writer.Flush();
                    _lastLine = line;
                    _lineOpen = true;
                }

                return;
            }

            int tenth = (int)Math.Floor(Math.Clamp(value.Fraction, 0, 1) * 10);
            if (tenth != _lastTenth || value.Stage != _lastStage)
            {
                _writer.WriteLine(line);
                _lastTenth = tenth;
                _lastStage = value.Stage;
            }
        }
    }

    /// <summary>Ends the updating line, so what follows starts on a line of its own.</summary>
    public void Finish()
    {
        lock (_gate)
        {
            if (_lineOpen)
            {
                _writer.WriteLine();
                _writer.Flush();
                _lineOpen = false;
            }
        }
    }
}
