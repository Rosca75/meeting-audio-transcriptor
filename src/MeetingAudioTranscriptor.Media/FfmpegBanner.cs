// Reads what ffmpeg says about an input on stderr (its duration and its first audio stream's rate), and
// builds the argument lists of the probe and the decode, so both are tested without starting ffmpeg.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MeetingAudioTranscriptor.Media;

/// <summary>What the input section of an ffmpeg banner says about a file.</summary>
/// <param name="HasInput">Whether ffmpeg opened the file and printed an <c>Input #0</c> section.</param>
/// <param name="AudioSampleRate">The rate of the first audio stream, null when there is no audio stream.</param>
/// <param name="AudioStreamCount">How many audio streams the input holds.</param>
/// <param name="DurationSeconds">The container's stated duration, null when it says <c>N/A</c> or nothing.</param>
/// <param name="AudioChannels">The channel count of the first audio stream, null when its layout is not one ffmpeg names.</param>
internal sealed record FfmpegInputInfo(bool HasInput, int? AudioSampleRate, int AudioStreamCount, double? DurationSeconds, int? AudioChannels = null);

/// <summary>Parses the input section ffmpeg prints to stderr when it opens a file.</summary>
/// <remarks>
/// The section has kept one shape across ffmpeg 4 to 8: an <c>Input #0, ...</c> line, a
/// <c>Duration: HH:MM:SS.ss</c> line and one <c>Stream #0:N...: Kind: ...</c> line per stream, where
/// newer versions insert a container id (<c>[0x2]</c>) and a language (<c>(und)</c>) between the index
/// and the colon. The field after the rate is the channel layout (<c>mono</c>, <c>stereo</c>,
/// <c>5.1(side)</c>, <c>6 channels</c>). Only the first input is read, and parsing stops at the first output section, so a
/// stream line of an output never counts as the input's.
/// </remarks>
internal static partial class FfmpegBanner
{
    /// <summary>Parses the stderr text of an ffmpeg run that opened one input.</summary>
    public static FfmpegInputInfo Parse(string stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        bool hasInput = false;
        int? rate = null;
        int? channels = null;
        int audioStreams = 0;
        double? duration = null;

        foreach (string rawLine in stderr.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("Input #", StringComparison.Ordinal))
            {
                if (hasInput)
                {
                    // A second input is not this file's.
                    break;
                }

                hasInput = true;
                continue;
            }

            if (line.StartsWith("Output #", StringComparison.Ordinal) || line.StartsWith("Stream mapping:", StringComparison.Ordinal))
            {
                break;
            }

            if (!hasInput)
            {
                continue;
            }

            Match durationMatch = DurationLine().Match(line);
            if (durationMatch.Success && duration is null)
            {
                duration = ParseDuration(durationMatch);
                continue;
            }

            Match streamMatch = AudioStreamLine().Match(line);
            if (streamMatch.Success)
            {
                audioStreams++;
                if (rate is null)
                {
                    Match rateMatch = SampleRate().Match(streamMatch.Groups["details"].Value);
                    if (rateMatch.Success && int.TryParse(rateMatch.Groups["rate"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    {
                        rate = parsed;
                        channels = ChannelCount(rateMatch.Groups["layout"].Value);
                    }
                }
            }
        }

        return new FfmpegInputInfo(hasInput, rate, audioStreams, duration, channels);
    }

    /// <summary>The number of channels an ffmpeg layout name stands for; null for a name this table does not hold.</summary>
    /// <remarks>
    /// ffmpeg names a layout by a word (<c>mono</c>, <c>quad</c>), by a count (<c>6 channels</c>), or by
    /// its speaker groups (<c>5.1</c>, <c>7.1.4</c>, <c>3.0(back)</c>), whose numbers add up to the count.
    /// </remarks>
    public static int? ChannelCount(string layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string name = layout.Trim();
        int bracket = name.IndexOf('(', StringComparison.Ordinal);
        string stem = bracket > 0 ? name[..bracket] : name;
        switch (stem)
        {
            case "mono":
                return 1;
            case "stereo":
            case "downmix":
                return 2;
            case "quad":
                return 4;
            case "hexagonal":
                return 6;
            case "octagonal":
            case "cube":
                return 8;
            case "hexadecagonal":
                return 16;
        }

        Match counted = CountedLayout().Match(name);
        if (counted.Success)
        {
            return int.Parse(counted.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        Match grouped = GroupedLayout().Match(stem);
        if (grouped.Success)
        {
            int total = 0;
            foreach (Capture group in grouped.Groups["group"].Captures)
            {
                total += int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            }

            return total > 0 ? total : null;
        }

        return null;
    }

    /// <summary>The probe: opens the file and prints its input section, with no output.</summary>
    /// <remarks>ffmpeg exits non-zero ("At least one output file must be specified") by design; only the text is read.</remarks>
    public static IReadOnlyList<string> ProbeArguments(string path) =>
        ["-nostdin", "-hide_banner", "-i", InputUrl(path)];

    /// <summary>The decode: the first audio stream at its own channel count, as raw interleaved little-endian float on stdout.</summary>
    /// <remarks>
    /// The channels are averaged by the converter rather than by ffmpeg, because ffmpeg's own downmix
    /// (<c>-ac 1</c>) weights each channel of a stereo pair by 0.707 and sums, which is louder than the
    /// average every other decoder of the application delivers. The count is pinned with <c>-ac</c>, so
    /// the frames on stdout always have the width the converter expects. The rate is pinned to the one the probe read, so the samples on stdout are always at the rate the
    /// source announced before its first read, even for a stream whose decoder reports another rate than
    /// its container (HE-AAC); when the two agree, as they almost always do, ffmpeg does not resample.
    /// </remarks>
    public static IReadOnlyList<string> DecodeArguments(string path, int sampleRate, int channels) =>
    [
        "-nostdin", "-hide_banner", "-loglevel", "error",
        "-i", InputUrl(path),
        "-vn", "-map", "0:a:0",
        "-ac", channels.ToString(CultureInfo.InvariantCulture), "-ar", sampleRate.ToString(CultureInfo.InvariantCulture),
        "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1",
    ];

    /// <summary>The input as ffmpeg must read it: an absolute path behind the <c>file:</c> protocol.</summary>
    /// <remarks>
    /// The absolute path keeps a file named like an option (<c>-x.m4a</c>) from being read as one, and the
    /// protocol prefix keeps a name containing a colon (<c>concat:a.m4a</c>) from being read as another
    /// protocol. Each argument goes to the child as its own argv entry, so spaces and non-ASCII
    /// characters need no quoting.
    /// </remarks>
    public static string InputUrl(string path) => "file:" + Path.GetFullPath(path);

    private static double? ParseDuration(Match match)
    {
        if (!match.Groups["hours"].Success)
        {
            return null;
        }

        int hours = int.Parse(match.Groups["hours"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        int minutes = int.Parse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        double seconds = double.Parse(match.Groups["seconds"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return (hours * 3600d) + (minutes * 60d) + seconds;
    }

    /// <summary>
    /// Matches <c>  Duration: 00:01:02.50, start: ...</c> and <c>  Duration: N/A, ...</c>; the time
    /// groups are absent for N/A. It does not match a <c>DURATION</c> metadata tag, which is upper case.
    /// </summary>
    [GeneratedRegex(@"^\s+Duration:\s*(?:N/A|(?<hours>\d+):(?<minutes>\d{2}):(?<seconds>\d{2}(?:\.\d+)?))", RegexOptions.CultureInvariant)]
    private static partial Regex DurationLine();

    /// <summary>
    /// Matches an input's audio stream line, such as <c>  Stream #0:1[0x2](und): Audio: aac (LC) ...</c>
    /// or <c>  Stream #0:0: Audio: pcm_s16le ...</c>, capturing what follows <c>Audio:</c>. It does not
    /// match a video, subtitle or data stream line.
    /// </summary>
    [GeneratedRegex(@"^\s+Stream #0:\d+\S*: Audio: (?<details>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AudioStreamLine();

    /// <summary>
    /// Matches a sample rate field and the layout field after it, such as <c>, 44100 Hz, stereo,</c>; it
    /// does not match a <c>kHz</c> or bit-rate field. The layout group is empty when no field follows.
    /// </summary>
    [GeneratedRegex(@"(?:^|,\s*)(?<rate>\d+) Hz\b(?:,\s*(?<layout>[^,]+))?", RegexOptions.CultureInvariant)]
    private static partial Regex SampleRate();

    /// <summary>Matches a counted layout such as <c>6 channels</c> or <c>1 channels (FL)</c>; it does not match a named layout.</summary>
    [GeneratedRegex(@"^(?<count>\d+) channels?\b", RegexOptions.CultureInvariant)]
    private static partial Regex CountedLayout();

    /// <summary>Matches a speaker-group layout such as <c>5.1</c>, <c>3.0</c> or <c>7.1.4</c>; it does not match a bare number or a word.</summary>
    [GeneratedRegex(@"^(?<group>\d+)(?:\.(?<group>\d+)){1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupedLayout();
}
