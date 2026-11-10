// Exercises the Media Foundation interop on Windows: a WAV written by the test, a generated M4A and MP4
// compared with ffmpeg's decode of the same file, and the failure kinds. Every test skips elsewhere.
using System;
using System.IO;
using System.Runtime.Versioning;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[SupportedOSPlatform("windows")]
[Trait("Category", "audio")]
public sealed class MediaFoundationAudioSourceTests : IClassFixture<MediaFixtures>
{
    private const string NeedsWindows = "needs Windows: Media Foundation exists there alone";

    private readonly MediaFixtures _media;

    public MediaFoundationAudioSourceTests(MediaFixtures media)
    {
        _media = media;
    }

    [Theory]
    [InlineData(44100, 2)]
    [InlineData(48000, 1)]
    [InlineData(16000, 6)]
    public void AWavDecodesAtItsRateAsTheChannelAverage(int rate, int channels)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NeedsWindows);
        string path = Path.Combine(_media.Directory, $"tone-{rate}-{channels}.wav");
        File.WriteAllBytes(path, MediaFixtures.ToneWav(rate, channels, 1));

        using MediaFoundationAudioSource source = new(path);
        float[] samples = MediaFixtures.ReadAll(source);

        double rms = MediaFixtures.Rms(samples);
        double hz = MediaFixtures.ZeroCrossingHz(samples, source.SampleRate);
        Assert.True(source.SampleRate == rate, $"got {source.SampleRate} Hz, wanted {rate}");
        Assert.True(samples.Length == rate, $"got {samples.Length} samples, wanted {rate} for one second");
        Assert.True(source.DurationSeconds is double d && Math.Abs(d - 1) < 0.01, $"got a stated {source.DurationSeconds} s, wanted 1 s");
        Assert.True(Math.Abs(rms - (MediaFixtures.ToneAmplitude / Math.Sqrt(2))) < 0.005, $"got an RMS of {rms:0.0000}, wanted the tone's, channels averaged");
        Assert.True(Math.Abs(hz - MediaFixtures.ToneHz) < 5, $"got {hz:0.0} Hz, wanted {MediaFixtures.ToneHz}");
    }

    [Theory]
    [Trait("Tier", "integration")]
    [InlineData("mf-stereo-44k.m4a", false, 44100, 2)]
    [InlineData("mf-video+audio.mp4", true, 44100, 2)]
    // 16 kHz mono is what a meeting recorder writes, and the rate at which Media Foundation's AAC
    // decoder first states twice the rate (implicit SBR) and corrects it with the first block.
    [InlineData("mf-mono-16k.m4a", false, 16000, 1)]
    public void AnAacFileMatchesFfmpegsDecode(string name, bool withVideo, int rate, int channels)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NeedsWindows);
        _media.SkipWithoutFfmpeg();
        string sine = MediaFixtures.Sine(rate, 1);
        string stereo = MediaFixtures.CopyToChannels(channels);

        // The video is H.264 through OpenH264, the one H.264 encoder the pinned LGPL ffmpeg carries, in
        // 4:2:0, the chroma format of real recordings and the one Windows' H.264 decoder reads.
        string path = withVideo
            ? _media.Generate(name, "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=25", "-f", "lavfi", "-i", sine, "-filter:a", stereo, "-c:v", "libopenh264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest")
            : _media.Generate(name, "-f", "lavfi", "-i", sine, "-af", stereo, "-c:a", "aac");

        using MediaFoundationAudioSource source = new(path);
        using FfmpegAudioSource reference = new(path, _media.Ffmpeg!);
        float[] samples = MediaFixtures.ReadAll(source);
        float[] expected = MediaFixtures.ReadAll(reference);

        double seconds = samples.Length / (double)source.SampleRate;
        double expectedSeconds = expected.Length / (double)reference.SampleRate;
        // ffmpeg drops the encoder's priming frame the container names (1,024 samples of AAC) and
        // Media Foundation keeps it, so the two may differ by that frame plus 50 ms: 23 ms of it at
        // 44.1 kHz, 64 ms at 16 kHz, measured on the Windows runner.
        double tolerance = 0.05 + (1024.0 / source.SampleRate);
        Assert.True(source.SampleRate == reference.SampleRate, $"got {source.SampleRate} Hz, wanted ffmpeg's {reference.SampleRate}");
        Assert.True(Math.Abs(seconds - expectedSeconds) < tolerance, $"got {seconds:0.000} s, wanted ffmpeg's {expectedSeconds:0.000} s within {tolerance * 1000:0} ms");
        Assert.True(Math.Abs(MediaFixtures.Rms(samples) - MediaFixtures.Rms(expected)) < 0.01, $"got an RMS of {MediaFixtures.Rms(samples):0.0000}, wanted ffmpeg's {MediaFixtures.Rms(expected):0.0000}");
        Assert.True(Math.Abs(MediaFixtures.ZeroCrossingHz(samples, source.SampleRate) - MediaFixtures.ToneHz) < 5, "got another frequency than the tone's, wanted 440 Hz");
    }

    [Fact]
    public void AMissingFileIsReportedAsNotFound()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NeedsWindows);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => new MediaFoundationAudioSource(Path.Combine(_media.Directory, "missing.m4a")));

        Assert.True(e.Failure == AudioDecodeFailure.FileNotFound, $"got {e.Failure} '{e.Message}', wanted file not found");
    }

    [Fact]
    public void RandomBytesAreReportedAsCorrupt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NeedsWindows);
        string path = Path.Combine(_media.Directory, "random.m4a");
        byte[] bytes = new byte[4096];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => new MediaFoundationAudioSource(path));

        Assert.True(e.Failure == AudioDecodeFailure.CorruptOrUnsupportedData, $"got {e.Failure} '{e.Message}', wanted corrupt data");
    }

    [Fact]
    [Trait("Tier", "integration")]
    public void AVideoWithoutSoundSaysItHasNoAudioStream()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NeedsWindows);
        _media.SkipWithoutFfmpeg();
        string path = _media.Generate("mf-video-only.mp4", "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=25", "-c:v", "libopenh264", "-pix_fmt", "yuv420p");

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => new MediaFoundationAudioSource(path));

        Assert.True(e.Failure == AudioDecodeFailure.NoAudioStream, $"got {e.Failure} '{e.Message}', wanted no audio stream");
    }
}
