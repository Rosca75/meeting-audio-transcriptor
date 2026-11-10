// Decodes real files through a real ffmpeg: rates, mono downmix, durations, the tone itself, every failure
// kind, awkward paths, a truncated file, and a child that must stop when the source is disposed.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Audio;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[Trait("Tier", "integration")]
[Trait("Category", "audio")]
public sealed class FfmpegAudioSourceTests : IClassFixture<MediaFixtures>
{
    private readonly MediaFixtures _media;

    public FfmpegAudioSourceTests(MediaFixtures media)
    {
        _media = media;
    }

    public static TheoryData<string, int, int, string> Tones() => new()
    {
        { "stereo-44k.wav", 44100, 2, "pcm_s16le" },
        { "mono-48k.wav", 48000, 1, "pcm_s16le" },
        { "stereo-44k.m4a", 44100, 2, "aac" },
        { "mono-48k.m4a", 48000, 1, "aac" },
    };

    [Theory]
    [MemberData(nameof(Tones))]
    public void AToneDecodesAtItsNativeRateAsMono(string name, int rate, int channels, string codec)
    {
        _media.SkipWithoutFfmpeg();
        string path = _media.Tone(name, rate, channels, 1, "-c:a", codec);

        using FfmpegAudioSource source = new(path, _media.Ffmpeg!);
        float[] samples = MediaFixtures.ReadAll(source);

        AssertTone(source, samples, rate, name);
    }

    [Fact]
    public void TheAudioTrackOfAVideoIsDecodedAfterItsVideoTrack()
    {
        _media.SkipWithoutFfmpeg();
        string path = _media.Generate(
            "video+audio.mp4",
            "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=25",
            "-f", "lavfi", "-i", MediaFixtures.Sine(44100, 1),
            "-filter:a", MediaFixtures.CopyToChannels(2), "-c:v", "libx264", "-c:a", "aac", "-shortest");

        using FfmpegAudioSource source = new(path, _media.Ffmpeg!);
        float[] samples = MediaFixtures.ReadAll(source);

        AssertTone(source, samples, 44100, "video+audio.mp4");
    }

    [Fact]
    public void StereoChannelsAreAveragedNotSummed()
    {
        _media.SkipWithoutFfmpeg();
        string path = _media.Generate("left-only.wav", "-f", "lavfi", "-i", MediaFixtures.Sine(44100, 1), "-af", "pan=stereo|c0=c0|c1=0*c0", "-c:a", "pcm_s16le");

        using FfmpegAudioSource source = new(path, _media.Ffmpeg!);
        double rms = MediaFixtures.Rms(MediaFixtures.ReadAll(source));

        double expected = MediaFixtures.ToneAmplitude / Math.Sqrt(2) / 2;
        Assert.True(Math.Abs(rms - expected) < 0.003, $"got an RMS of {rms:0.0000}, wanted {expected:0.0000}: the tone on the left and silence on the right average to half the tone");
    }

    [Fact]
    public void FiveOneChannelsAreAveraged()
    {
        _media.SkipWithoutFfmpeg();

        // PCM rather than AAC, because the AAC encoder low-passes the LFE channel and a 440 Hz tone there vanishes.
        string path = _media.Tone("surround-5.1.wav", 48000, 6, 1, "-c:a", "pcm_s16le");

        using FfmpegAudioSource source = new(path, _media.Ffmpeg!);
        float[] samples = MediaFixtures.ReadAll(source);

        AssertTone(source, samples, 48000, "surround-5.1.wav");
    }

    [Fact]
    public void APathWithSpacesAndNonAsciiCharactersWorks()
    {
        _media.SkipWithoutFfmpeg();
        string original = _media.Tone("plain.m4a", 48000, 1, 1, "-c:a", "aac");
        string awkward = Path.Combine(_media.Directory, "réunion d'équipe 会议 -y.m4a");
        File.Copy(original, awkward, overwrite: true);

        using FfmpegAudioSource source = new(awkward, _media.Ffmpeg!);
        float[] samples = MediaFixtures.ReadAll(source);

        AssertTone(source, samples, 48000, awkward);
    }

    [Fact]
    public void RandomBytesNamedM4aAreReportedAsCorrupt()
    {
        _media.SkipWithoutFfmpeg();
        string path = Path.Combine(_media.Directory, "random.m4a");
        byte[] bytes = new byte[4096];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => new FfmpegAudioSource(path, _media.Ffmpeg!));

        Assert.True(e.Failure == AudioDecodeFailure.CorruptOrUnsupportedData, $"got {e.Failure} '{e.Message}', wanted corrupt data");
        Assert.True(e.Message.Contains("Invalid data found", StringComparison.Ordinal) && e.Message.Contains(path, StringComparison.Ordinal), $"got '{e.Message}', wanted ffmpeg's reason and the path");
    }

    [Fact]
    public void AVideoWithoutSoundSaysItHasNoAudioStream()
    {
        _media.SkipWithoutFfmpeg();
        string path = _media.Generate("video-only.mp4", "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=25", "-c:v", "libx264");

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => new FfmpegAudioSource(path, _media.Ffmpeg!));

        Assert.True(e.Failure == AudioDecodeFailure.NoAudioStream, $"got {e.Failure}, wanted no audio stream");
        Assert.True(e.Message.Contains("holds no audio stream", StringComparison.Ordinal), $"got '{e.Message}', wanted the no-audio message");
    }

    [Fact]
    public void AMissingFileIsReportedAsNotFound()
    {
        _media.SkipWithoutFfmpeg();
        string path = Path.Combine(_media.Directory, "missing.m4a");

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => new FfmpegAudioSource(path, _media.Ffmpeg!));

        Assert.True(e.Failure == AudioDecodeFailure.FileNotFound, $"got {e.Failure} '{e.Message}', wanted file not found");
    }

    [Fact]
    public void AnFfmpegThatCannotStartIsReportedAsMissing()
    {
        _media.SkipWithoutFfmpeg();
        string path = _media.Tone("plain.m4a", 48000, 1, 1, "-c:a", "aac");
        FfmpegLocation broken = new(Path.Combine(_media.Directory, "no-such-ffmpeg"), FfmpegOrigin.EnvironmentVariable);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => new FfmpegAudioSource(path, broken));

        Assert.True(e.Failure == AudioDecodeFailure.DecoderMissing && e.Message.Contains("MAT_FFMPEG", StringComparison.Ordinal), $"got {e.Failure} '{e.Message}', wanted decoder missing with the override named");
    }

    [Fact]
    public void ATruncatedM4aYieldsTheAudioBeforeTheCutOrAClearError()
    {
        _media.SkipWithoutFfmpeg();
        string whole = _media.Tone("faststart-3s.m4a", 48000, 1, 3, "-c:a", "aac", "-movflags", "+faststart");
        string cut = Path.Combine(_media.Directory, "truncated.m4a");
        byte[] bytes = File.ReadAllBytes(whole);
        File.WriteAllBytes(cut, bytes[..(bytes.Length * 6 / 10)]);

        try
        {
            using FfmpegAudioSource source = new(cut, _media.Ffmpeg!);
            float[] samples = MediaFixtures.ReadAll(source);
            double seconds = samples.Length / (double)source.SampleRate;
            Assert.True(seconds is > 0.5 and < 3.05, $"got {seconds:0.000} s from a file cut at 60 %, wanted the audio before the cut (warnings: {string.Join(" | ", source.Warnings)})");
        }
        catch (AudioDecodeException e)
        {
            Assert.True(e.Message.Contains(cut, StringComparison.Ordinal) && e.Message.Contains("; ", StringComparison.Ordinal), $"got '{e.Message}', wanted a clear error naming the file");
        }
    }

    [Fact]
    public void DisposingMidStreamStopsTheChildPromptly()
    {
        _media.SkipWithoutFfmpeg();
        string path = _media.Tone("long-120s.wav", 48000, 2, 120, "-c:a", "pcm_s16le");
        FfmpegAudioSource source = new(path, _media.Ffmpeg!);
        float[] buffer = new float[1024];
        Assert.True(source.Read(buffer) > 0, "got no samples, wanted the decode running before it is stopped");
        int pid = source.ProcessId;

        Stopwatch watch = Stopwatch.StartNew();
        source.Dispose();
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"got {watch.Elapsed.TotalMilliseconds:0} ms to stop ffmpeg, wanted under 1 s");
        Assert.True(!IsRunningOrZombie(pid), $"got process {pid} still present after Dispose, wanted it killed and reaped");
        Assert.Throws<ObjectDisposedException>(() => source.Read(buffer));
    }

    [Fact]
    public void DisposingFromAnotherThreadUnblocksAPendingRead()
    {
        _media.SkipWithoutFfmpeg();
        string path = _media.Tone("long-120s.wav", 48000, 2, 120, "-c:a", "pcm_s16le");
        FfmpegAudioSource source = new(path, _media.Ffmpeg!);
        float[] buffer = new float[1 << 20];
        Exception? failure = null;
        Thread reader = new(() =>
        {
            try
            {
                while (source.Read(buffer) > 0)
                {
                }
            }
            catch (ObjectDisposedException)
            {
                // Dispose ran between two reads, which is the other correct outcome.
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        reader.Start();
        Thread.Sleep(50);

        source.Dispose();
        bool stopped = reader.Join(TimeSpan.FromSeconds(2));

        Assert.True(stopped && failure is null, $"got stopped={stopped}, failure={failure}, wanted the reading thread to end quietly");
    }

    [Fact]
    public void TheOpenerUsesFfmpegForAnM4aOnLinux()
    {
        _media.SkipWithoutFfmpeg();
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows opens an .m4a through Media Foundation, which the Windows tests cover");
        string path = _media.Tone("mono-48k.m4a", 48000, 1, 1, "-c:a", "aac");
        AudioFileOpener opener = new(_ => throw new InvalidOperationException("the WAV reader must not be asked for an .m4a"), _ => false);

        using IAudioSource source = opener.Open(path);

        Assert.True(opener.DecoderName.StartsWith("ffmpeg (", StringComparison.Ordinal) && source.SampleRate == 48000, $"got '{opener.DecoderName}' at {source.SampleRate} Hz, wanted ffmpeg at 48000 Hz");
    }

    [Theory]
    [InlineData("en-10min.m4a")]
    [InlineData("en-10min.mp4")]
    public void ALongRecordingDecodesToTheLengthOfItsWavSibling(string name)
    {
        _media.SkipWithoutFfmpeg();
        string? directory = LongDataDirectory();
        Assert.SkipUnless(directory is not null, "needs bench/data/long: run the test set fetch tool to download the long recordings");
        string path = Path.Combine(directory, name);
        string wav = Path.ChangeExtension(path, ".wav");
        Assert.SkipUnless(File.Exists(path) && File.Exists(wav), $"needs {path} and {wav}: run the test set fetch tool to download them");

        using FfmpegAudioSource reference = new(wav, _media.Ffmpeg!);
        using FfmpegAudioSource source = new(path, _media.Ffmpeg!);
        long count = 0;
        float[] buffer = new float[1 << 16];
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            count += read;
        }

        double decoded = count / (double)source.SampleRate;
        double expected = reference.DurationSeconds!.Value;

        // An AAC stream ends on a whole 1024-sample frame, and a file muxed without an end trim keeps
        // that padding (52 ms on these files, which ffmpeg's own command line decodes too), so the
        // decoded length may exceed the WAV by up to one frame plus the 50 ms tolerance.
        double frame = 1024d / source.SampleRate;
        Assert.True(
            decoded > expected - 0.05 && decoded < expected + frame + 0.05,
            $"got {decoded:0.000} s decoded from {name}, wanted {expected:0.000} s within 50 ms plus at most one AAC frame of end padding");
        Assert.True(source.DurationSeconds is double stated && Math.Abs(stated - expected) < 0.05, $"got a stated {source.DurationSeconds} s, wanted {expected:0.000} s within 50 ms");
    }

    private static void AssertTone(FfmpegAudioSource source, float[] samples, int rate, string input)
    {
        double seconds = samples.Length / (double)source.SampleRate;
        double rms = MediaFixtures.Rms(samples);
        double hz = MediaFixtures.ZeroCrossingHz(samples, source.SampleRate);
        double expectedRms = MediaFixtures.ToneAmplitude / Math.Sqrt(2);

        Assert.True(source.SampleRate == rate, $"got {source.SampleRate} Hz, wanted {rate} for {input}");
        Assert.True(source.DurationSeconds is double stated && Math.Abs(stated - 1) < 0.05, $"got a stated {source.DurationSeconds} s, wanted 1 s within 50 ms for {input}");
        Assert.True(Math.Abs(seconds - 1) < 0.05, $"got {seconds:0.000} s of samples, wanted 1 s within 50 ms for {input}");
        Assert.True(Math.Abs(rms - expectedRms) < 0.01, $"got an RMS of {rms:0.0000}, wanted {expectedRms:0.0000} (channels averaged, not summed) for {input}");
        Assert.True(Math.Abs(hz - MediaFixtures.ToneHz) < 5, $"got {hz:0.0} Hz from zero crossings, wanted {MediaFixtures.ToneHz} for {input}");
    }

    /// <summary>The first bench/data/long folder above the test, which reaches the main checkout from a worktree inside it.</summary>
    private static string? LongDataDirectory()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, "bench", "data", "long");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsRunningOrZombie(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            // A reaped child has no /proc entry; a zombie keeps one until its parent waits for it.
            return Directory.Exists($"/proc/{pid}");
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
