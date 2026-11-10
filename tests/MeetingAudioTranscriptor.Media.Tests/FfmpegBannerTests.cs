// Tests the parsing of ffmpeg's input banner (recorded from ffmpeg 6.1 and written in the 7.x and 8.x
// shapes) and the argument lists of the probe and the decode.
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[Trait("Category", "audio")]
public sealed class FfmpegBannerTests
{
    /// <summary>ffmpeg 6.1 on a stereo 16-bit WAV, as recorded.</summary>
    private const string Ffmpeg6Wav = """
        [aist#0:0/pcm_s16le @ 0x55a2fc4132c0] Guessed Channel Layout: stereo
        Input #0, wav, from 'file:/tmp/s.wav':
          Metadata:
            encoder         : Lavf60.16.100
          Duration: 00:00:01.00, bitrate: 1411 kb/s
          Stream #0:0: Audio: pcm_s16le ([1][0][0][0] / 0x0001), 44100 Hz, 2 channels, s16, 1411 kb/s
        At least one output file must be specified
        """;

    /// <summary>ffmpeg 6.1 on an MP4 with a video track before the audio track, as recorded.</summary>
    private const string Ffmpeg6VideoThenAudio = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'file:/tmp/v.mp4':
          Metadata:
            major_brand     : isom
            minor_version   : 512
            compatible_brands: isomiso2avc1mp41
            encoder         : Lavf60.16.100
          Duration: 00:00:01.00, start: 0.000000, bitrate: 129 kb/s
          Stream #0:0[0x1](und): Video: h264 (High 4:4:4 Predictive) (avc1 / 0x31637661), yuv444p(progressive), 160x120 [SAR 1:1 DAR 4:3], 41 kb/s, 25 fps, 25 tbr, 12800 tbn (default)
            Metadata:
              handler_name    : VideoHandler
              vendor_id       : [0][0][0][0]
              encoder         : Lavc60.31.102 libx264
          Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, mono, fltp, 70 kb/s (default)
            Metadata:
              handler_name    : SoundHandler
              vendor_id       : [0][0][0][0]
        At least one output file must be specified
        """;

    /// <summary>The ffmpeg 7.x shape: two audio tracks with languages, CRLF line ends as on Windows, and a 1:02:03 meeting.</summary>
    private const string Ffmpeg7TwoAudioTracks =
        "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:\\Users\\me\\Réunion 1.m4a':\r\n"
        + "  Metadata:\r\n"
        + "    major_brand     : M4A \r\n"
        + "    DURATION        : 00:00:09.000000000\r\n"
        + "  Duration: 01:02:03.45, start: 0.000000, bitrate: 96 kb/s\r\n"
        + "  Stream #0:0[0x1](fra): Audio: aac (HE-AAC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 64 kb/s (default)\r\n"
        + "      Metadata:\r\n"
        + "        handler_name    : Core Media Audio\r\n"
        + "  Stream #0:1[0x2](eng): Audio: aac (LC) (mp4a / 0x6134706D), 22050 Hz, mono, fltp, 32 kb/s\r\n"
        + "At least one output file must be specified\r\n";

    /// <summary>The ffmpeg 8.x shape: a start field after the bit rate, an unknown duration and a cover picture stream.</summary>
    private const string Ffmpeg8UnknownDuration = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'file:/tmp/live.mp4':
          Duration: N/A, start: 0.000000, bitrate: N/A
          Stream #0:0[0x1](und): Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 600x600, 90k tbr, 90k tbn (attached pic)
          Stream #0:1[0x2](und): Audio: opus (Opus / 0x7375704F), 16000 Hz, mono, fltp, 24 kb/s, start 0.006500 (default)
        Output #0, null, to 'pipe:':
          Stream #0:0: Audio: pcm_s16le, 8000 Hz, mono, s16, 128 kb/s
        """;

    /// <summary>A video-only file.</summary>
    private const string VideoOnly = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'file:/tmp/vo.mp4':
          Duration: 00:00:01.00, start: 0.000000, bitrate: 49 kb/s
          Stream #0:0[0x1](und): Video: h264 (High 4:4:4 Predictive) (avc1 / 0x31637661), yuv444p(progressive), 160x120, 41 kb/s, 25 fps, 25 tbr, 12800 tbn (default)
        At least one output file must be specified
        """;

    /// <summary>ffmpeg 6.1 on random bytes, as recorded.</summary>
    private const string Corrupt = """
        [in#0 @ 0x559b72139dc0] Error opening input: Invalid data found when processing input
        Error opening input file file:/tmp/bad.m4a.
        Error opening input files: Invalid data found when processing input
        """;

    [Fact]
    public void AWavBannerGivesItsRateAndDuration()
    {
        FfmpegInputInfo info = FfmpegBanner.Parse(Ffmpeg6Wav);

        Assert.True(info == new FfmpegInputInfo(true, 44100, 1, 1.0), $"got {info}, wanted 44100 Hz, one audio stream, 1 s");
    }

    [Fact]
    public void AVideoTrackBeforeTheAudioTrackIsSkipped()
    {
        FfmpegInputInfo info = FfmpegBanner.Parse(Ffmpeg6VideoThenAudio);

        Assert.True(info == new FfmpegInputInfo(true, 48000, 1, 1.0), $"got {info}, wanted the audio track's 48000 Hz");
    }

    [Fact]
    public void TheFirstOfSeveralAudioTracksGivesTheRateAndHoursAreRead()
    {
        FfmpegInputInfo info = FfmpegBanner.Parse(Ffmpeg7TwoAudioTracks);

        Assert.True(info.AudioSampleRate == 44100 && info.AudioStreamCount == 2, $"got {info}, wanted the first track's 44100 Hz of two");
        Assert.True(Math.Abs(info.DurationSeconds!.Value - 3723.45) < 1e-9, $"got {info.DurationSeconds}, wanted 3723.45 s from 01:02:03.45 and not the DURATION tag");
    }

    [Fact]
    public void AnUnknownDurationIsNullAndOutputStreamsAreIgnored()
    {
        FfmpegInputInfo info = FfmpegBanner.Parse(Ffmpeg8UnknownDuration);

        Assert.True(info == new FfmpegInputInfo(true, 16000, 1, null), $"got {info}, wanted 16000 Hz, one input audio stream and no duration");
    }

    [Fact]
    public void AVideoOnlyFileHasNoAudioStream()
    {
        FfmpegInputInfo info = FfmpegBanner.Parse(VideoOnly);

        Assert.True(info == new FfmpegInputInfo(true, null, 0, 1.0), $"got {info}, wanted an input without audio");
    }

    [Fact]
    public void AFileFfmpegCannotOpenHasNoInput()
    {
        FfmpegInputInfo info = FfmpegBanner.Parse(Corrupt);

        Assert.True(!info.HasInput && info.AudioStreamCount == 0, $"got {info}, wanted no input section");
    }

    [Fact]
    public void TheDecodeReadsTheFirstAudioStreamAsMonoFloatAtTheProbedRate()
    {
        string path = Path.Combine(Path.GetTempPath(), "a b", "réunion.m4a");
        IReadOnlyList<string> arguments = FfmpegBanner.DecodeArguments(path, 44100);

        string joined = string.Join(" ", arguments);
        Assert.True(
            joined == $"-nostdin -hide_banner -loglevel error -i file:{Path.GetFullPath(path)} -vn -map 0:a:0 -ac 1 -ar 44100 -f f32le -acodec pcm_f32le pipe:1",
            $"got '{joined}', wanted the documented decode command");
        Assert.True(arguments[5] == "file:" + Path.GetFullPath(path), $"got '{arguments[5]}', wanted the whole path as one argument");
    }

    [Fact]
    public void ARelativeNameThatLooksLikeAnOptionBecomesAnAbsoluteFileUrl()
    {
        string url = FfmpegBanner.InputUrl("-y.m4a");

        Assert.True(url == "file:" + Path.Combine(Environment.CurrentDirectory, "-y.m4a"), $"got '{url}', wanted an absolute file: url");
        Assert.True(string.Join(" ", FfmpegBanner.ProbeArguments("x.wav")) == "-nostdin -hide_banner -i " + FfmpegBanner.InputUrl("x.wav"), "got another probe command, wanted ffmpeg -i alone");
    }
}
