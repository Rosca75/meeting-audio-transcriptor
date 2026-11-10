// Reads the RTTM files that carry who-spoke-when references, the format every diarisation benchmark uses.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MeetingAudioTranscriptor.Engine.Scoring;

/// <summary>One stretch of speech by one speaker, in seconds.</summary>
/// <param name="Speaker">The speaker's name as the file writes it.</param>
/// <param name="Start">Where the speech starts.</param>
/// <param name="End">Where it ends.</param>
public readonly record struct SpeakerSegment(string Speaker, double Start, double End);

/// <summary>Parses the SPEAKER lines of an RTTM file.</summary>
/// <remarks>
/// A line is <c>SPEAKER file channel start duration &lt;NA&gt; &lt;NA&gt; speaker &lt;NA&gt; &lt;NA&gt;</c>, fields separated by
/// white space. Lines of other types and blank lines are skipped; a SPEAKER line whose numbers do
/// not parse is an error naming its line, because a silently dropped segment changes every score.
/// </remarks>
public static class DiarisationRttm
{
    /// <summary>The segments of every SPEAKER line, in file order.</summary>
    public static List<SpeakerSegment> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<SpeakerSegment> segments = [];
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string[] fields = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0 || !string.Equals(fields[0], "SPEAKER", StringComparison.Ordinal))
            {
                continue;
            }

            if (fields.Length < 8
                || !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double start)
                || !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double duration)
                || duration < 0)
            {
                throw new FormatException($"RTTM line {i + 1} ('{lines[i].Trim()}') is not SPEAKER file channel start duration <NA> <NA> speaker; fix or remove the line");
            }

            segments.Add(new SpeakerSegment(fields[7], start, start + duration));
        }

        return segments;
    }

    /// <summary>Writes segments as RTTM lines for the given file name, so a hypothesis can be inspected with standard tools.</summary>
    public static string Format(string fileName, IEnumerable<SpeakerSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        System.Text.StringBuilder text = new();
        foreach (SpeakerSegment segment in segments)
        {
            text.Append(CultureInfo.InvariantCulture, $"SPEAKER {fileName} 1 {segment.Start:0.000} {segment.End - segment.Start:0.000} <NA> <NA> {segment.Speaker} <NA> <NA>\n");
        }

        return text.ToString();
    }
}
