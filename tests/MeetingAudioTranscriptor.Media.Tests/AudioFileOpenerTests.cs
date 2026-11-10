// Tests the opener's decision table with fake decoders: content before extension, the WAV reader and its
// fallback, the platform order on Windows and elsewhere, and the wording when everything fails.
using System;
using System.Collections.Generic;
using System.IO;
using MeetingAudioTranscriptor.Engine.Audio;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

[Trait("Category", "audio")]
public sealed class AudioFileOpenerTests : IDisposable
{
    private static readonly byte[] WavHead = "RIFF\x24\0\0\0WAVEfmt "u8.ToArray();
    private static readonly byte[] Mp4Head = "\0\0\0 ftypM4A \0\0\0\0isom"u8.ToArray();
    private static readonly FfmpegLocation FoundFfmpeg = new("/usr/bin/ffmpeg", FfmpegOrigin.SearchPath);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mat-opener-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _calls = [];

    public AudioFileOpenerTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void APlainWavGoesToTheWavReader()
    {
        string path = File("meeting.wav", WavHead);
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "wav" && opener.DecoderName == "wav", $"got calls '{Calls()}' and decoder '{opener.DecoderName}', wanted the WAV reader alone");
    }

    [Fact]
    public void AnMp4NamedWavGoesByItsContentToThePlatformDecoder()
    {
        string path = File("meeting.wav", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "ffmpeg", $"got calls '{Calls()}', wanted ffmpeg for MP4 content named .wav");
    }

    [Fact]
    public void AWavNamedM4aGoesByItsContentToTheWavReader()
    {
        string path = File("meeting.m4a", WavHead);
        AudioFileOpener opener = Opener(isWindows: true, ffmpeg: FoundFfmpeg);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "wav", $"got calls '{Calls()}', wanted the WAV reader for WAV content named .m4a");
    }

    [Fact]
    public void ACompressedWavFallsThroughToThePlatformDecoder()
    {
        string path = File("adpcm.wav", WavHead);
        AudioFileOpener opener = Opener(isWindows: true, ffmpeg: FoundFfmpeg, wavRefuses: true);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "wav,mf" && opener.DecoderName == "mf", $"got calls '{Calls()}', wanted the WAV reader then Media Foundation");
    }

    [Fact]
    public void AnotherWavReaderFailureIsNotHidden()
    {
        string path = File("broken.wav", WavHead);
        AudioFileOpener opener = new(
            _ => throw new InvalidDataException("the RIFF size is wrong"),
            e => e is NotSupportedException,
            new AudioFileOpenerOptions { Locator = Locator(FoundFfmpeg) },
            Decoders(isWindows: false));

        InvalidDataException e = Assert.Throws<InvalidDataException>(() => opener.Open(path));

        Assert.True(e.Message == "the RIFF size is wrong" && Calls() == "", $"got '{e.Message}' and calls '{Calls()}', wanted the reader's own failure and no fallback");
    }

    [Fact]
    public void UnknownContentWithASupportedExtensionStillReachesTheDecoder()
    {
        string path = File("old-quicktime.mp4", "\0\0\0\x08wide\0\0\0\0"u8.ToArray());
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "ffmpeg", $"got calls '{Calls()}', wanted ffmpeg to judge an .mp4 the sniff did not recognise");
    }

    [Fact]
    public void UnknownContentWithAnotherExtensionIsRefusedNamingTheSupportedTypes()
    {
        string path = File("notes.mp3", "ID3\x04\0\0\0\0\0\0\0\0"u8.ToArray());
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(path));

        Assert.True(e.Failure == AudioDecodeFailure.UnsupportedType, $"got {e.Failure}, wanted unsupported type");
        Assert.True(e.Message.Contains(".wav, .m4a, .mp4", StringComparison.Ordinal) && Calls() == "", $"got '{e.Message}' and calls '{Calls()}', wanted the three types named and no decoder tried");
    }

    [Fact]
    public void AnMp4NamedTxtIsAcceptedByItsContent()
    {
        string path = File("export.txt", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "ffmpeg", $"got calls '{Calls()}', wanted MP4 content accepted whatever its name");
    }

    [Fact]
    public void AMissingFileIsNamed()
    {
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);
        string path = Path.Combine(_directory, "gone.m4a");

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(path));

        Assert.True(e.Failure == AudioDecodeFailure.FileNotFound && e.Message.Contains(path, StringComparison.Ordinal), $"got {e.Failure} '{e.Message}', wanted file not found naming the path");
    }

    [Fact]
    public void AnEmptyFileIsRefusedBeforeAnyDecoder()
    {
        string path = File("empty.m4a", []);
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(path));

        Assert.True(e.Failure == AudioDecodeFailure.EmptyFile && Calls() == "", $"got {e.Failure} and calls '{Calls()}', wanted empty file and no decoder");
    }

    [Fact]
    public void AFolderIsRefusedAsUnreadable()
    {
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: FoundFfmpeg);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(_directory));

        Assert.True(e.Failure is AudioDecodeFailure.AccessDenied or AudioDecodeFailure.FileNotFound, $"got {e.Failure} '{e.Message}', wanted a folder refused");
    }

    [Fact]
    public void OnWindowsMediaFoundationIsTriedFirst()
    {
        string path = File("m.m4a", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: true, ffmpeg: FoundFfmpeg);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "mf" && opener.DecoderName == "mf", $"got calls '{Calls()}', wanted Media Foundation alone");
    }

    [Fact]
    public void OnWindowsFfmpegTakesOverWhenMediaFoundationFails()
    {
        string path = File("m.m4a", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: true, ffmpeg: FoundFfmpeg, failing: ["mf"]);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "mf,ffmpeg" && opener.DecoderName == "ffmpeg", $"got calls '{Calls()}' and '{opener.DecoderName}', wanted ffmpeg after Media Foundation");
    }

    [Fact]
    public void PreferFfmpegReversesTheWindowsOrder()
    {
        string path = File("m.m4a", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: true, ffmpeg: FoundFfmpeg, preferFfmpeg: true, failing: ["ffmpeg"]);

        using IAudioSource source = opener.Open(path);

        Assert.True(Calls() == "ffmpeg,mf", $"got calls '{Calls()}', wanted ffmpeg first then Media Foundation");
    }

    [Fact]
    public void WhenBothFailTheMessageSaysWhatEachSaid()
    {
        string path = File("m.m4a", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: true, ffmpeg: FoundFfmpeg, failing: ["mf", "ffmpeg"]);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(path));

        Assert.True(
            e.Message.Contains("every decoder failed: mf refused it; then ffmpeg refused it", StringComparison.Ordinal),
            $"got '{e.Message}', wanted both attempts named in order");
        Assert.True(e.Failure == AudioDecodeFailure.CorruptOrUnsupportedData, $"got {e.Failure}, wanted the first failure's kind");
    }

    [Fact]
    public void OnWindowsWithoutFfmpegAMediaFoundationFailureSaysNoFallbackWasFound()
    {
        string path = File("m.m4a", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: true, ffmpeg: null, failing: ["mf"]);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(path));

        Assert.True(e.Message.Contains("mf refused it; no ffmpeg was found to try as well", StringComparison.Ordinal), $"got '{e.Message}', wanted the missing fallback named");
    }

    [Fact]
    public void ElsewhereWithoutFfmpegTheMessageSaysHowToGetIt()
    {
        string path = File("m.m4a", Mp4Head);
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: null);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(path));

        Assert.True(e.Failure == AudioDecodeFailure.DecoderMissing, $"got {e.Failure}, wanted decoder missing");
        Assert.True(e.Message.Contains("reinstall the package or install ffmpeg", StringComparison.Ordinal) && Calls() == "", $"got '{e.Message}', wanted the remedy and no decoder tried");
    }

    [Fact]
    public void ACompressedWavWithNoDecoderSaysBothWhy()
    {
        string path = File("adpcm.wav", WavHead);
        AudioFileOpener opener = Opener(isWindows: false, ffmpeg: null, wavRefuses: true);

        AudioDecodeException e = Assert.Throws<AudioDecodeException>(() => opener.Open(path));

        Assert.True(
            e.Message.StartsWith("the WAV reader does not read this encoding (ADPCM is compressed); ffmpeg was not found", StringComparison.Ordinal),
            $"got '{e.Message}', wanted the WAV refusal then the missing decoder");
    }

    private string File(string name, byte[] content)
    {
        string path = Path.Combine(_directory, name);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    private string Calls() => string.Join(",", _calls);

    private AudioFileOpener Opener(bool isWindows, FfmpegLocation? ffmpeg, bool wavRefuses = false, bool preferFfmpeg = false, string[]? failing = null)
    {
        HashSet<string> failingSet = [.. failing ?? []];
        return new AudioFileOpener(
            stream =>
            {
                _calls.Add("wav");
                if (wavRefuses)
                {
                    throw new NotSupportedException("ADPCM is compressed");
                }

                return new FakeSource("wav", stream);
            },
            e => e is NotSupportedException,
            new AudioFileOpenerOptions { Locator = Locator(ffmpeg), PreferFfmpeg = preferFfmpeg },
            Decoders(isWindows, failingSet));
    }

    private AudioDecoders Decoders(bool isWindows, HashSet<string>? failing = null) =>
        new(
            isWindows,
            isWindows ? path => Fake("mf", failing) : null,
            (path, ffmpeg) => Fake("ffmpeg", failing));

    private FakeSource Fake(string name, HashSet<string>? failing)
    {
        _calls.Add(name);
        if (failing?.Contains(name) == true)
        {
            throw new AudioDecodeException(AudioDecodeFailure.CorruptOrUnsupportedData, $"{name} refused it");
        }

        return new FakeSource(name, null);
    }

    private static FfmpegLocator Locator(FfmpegLocation? ffmpeg) =>
        new(_ => null, "/opt/mat", isWindows: false, path => ffmpeg is not null && path == Path.Combine("/opt/mat", "ffmpeg", "ffmpeg"));

    /// <summary>A decoder stand-in that owns the stream it was given, as a real source does.</summary>
    private sealed class FakeSource(string name, Stream? stream) : INamedAudioSource
    {
        public string DecoderName { get; } = name;

        public int SampleRate => 16000;

        public double? DurationSeconds => null;

        public int Read(Span<float> buffer) => 0;

        public void Dispose() => stream?.Dispose();
    }
}
