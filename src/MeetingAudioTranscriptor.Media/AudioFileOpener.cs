// Opens an audio file with the right decoder: the engine's WAV reader for plain WAV, and otherwise Media
// Foundation on Windows or ffmpeg, falling back from one to the other where both exist.
using System;
using System.Collections.Generic;
using System.IO;
using MeetingAudioTranscriptor.Engine.Audio;

namespace MeetingAudioTranscriptor.Media;

/// <summary>Settings of the opener; the defaults are those of a shipped application.</summary>
public sealed class AudioFileOpenerOptions
{
    /// <summary>Finds the ffmpeg executable; the default looks at <c>MAT_FFMPEG</c>, beside the application, then the PATH.</summary>
    public FfmpegLocator Locator { get; init; } = new();

    /// <summary>On Windows, tries ffmpeg before Media Foundation; for diagnosing a difference between the two decoders.</summary>
    public bool PreferFfmpeg { get; init; }
}

/// <summary>The platform decoders the opener chooses between, injected so its decisions are tested with fakes.</summary>
/// <param name="IsWindows">Whether Media Foundation is the first platform decoder.</param>
/// <param name="MediaFoundation">Opens a file through Media Foundation; null where there is none.</param>
/// <param name="Ffmpeg">Opens a file through a located ffmpeg.</param>
internal sealed record AudioDecoders(
    bool IsWindows,
    Func<string, INamedAudioSource>? MediaFoundation,
    Func<string, FfmpegLocation, INamedAudioSource> Ffmpeg);

/// <summary>Opens a .wav, .m4a or .mp4 file as a mono <see cref="IAudioSource"/> at the file's own rate.</summary>
/// <remarks>
/// <para>
/// The decision runs on content first: the first twelve bytes say WAV or ISO media whatever the file
/// is called, and the extension decides only when they say neither, so a renamed recording still
/// opens and an unrelated file is refused with the supported types named.
/// </para>
/// <para>
/// A WAV file goes to the engine's WAV reader, supplied by the composition root; when that reader
/// refuses the encoding (a compressed WAV), the platform decoder takes it. The platform decoder is
/// Media Foundation then ffmpeg on Windows, and ffmpeg alone elsewhere; when every decoder fails, the
/// message says what was tried and what each one said.
/// </para>
/// <para>
/// A returned source owns what it reads: the WAV reader's source is handed the file stream and must
/// dispose it. <see cref="DecoderName"/> names the decoder of the most recent successful open, for the
/// transcript metadata and the logs; an opener is used by one thread at a time.
/// </para>
/// </remarks>
public sealed class AudioFileOpener
{
    /// <summary>The decoder name reported when the engine's WAV reader opened the file.</summary>
    public const string WavDecoderName = "wav";

    /// <summary>The extensions the application accepts, in the order a message names them.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = [".wav", ".m4a", ".mp4"];

    private readonly Func<Stream, IAudioSource> _wavReader;
    private readonly Func<Exception, bool> _isUnsupportedWavFormat;
    private readonly AudioFileOpenerOptions _options;
    private readonly AudioDecoders _decoders;

    /// <summary>Creates an opener over the engine's WAV reader and the platform decoders of this machine.</summary>
    /// <param name="wavReader">Reads a plain WAV stream; the source it returns owns the stream.</param>
    /// <param name="isUnsupportedWavFormat">Says whether an exception of the WAV reader means "an encoding I do not read", which sends the file to the platform decoder.</param>
    /// <param name="options">The ffmpeg locator and the decoder preference; null for the defaults.</param>
    public AudioFileOpener(Func<Stream, IAudioSource> wavReader, Func<Exception, bool> isUnsupportedWavFormat, AudioFileOpenerOptions? options = null)
        : this(wavReader, isUnsupportedWavFormat, options ?? new AudioFileOpenerOptions(), PlatformDecoders())
    {
    }

    /// <summary>Creates an opener over injected decoders.</summary>
    internal AudioFileOpener(Func<Stream, IAudioSource> wavReader, Func<Exception, bool> isUnsupportedWavFormat, AudioFileOpenerOptions options, AudioDecoders decoders)
    {
        ArgumentNullException.ThrowIfNull(wavReader);
        ArgumentNullException.ThrowIfNull(isUnsupportedWavFormat);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(decoders);
        _wavReader = wavReader;
        _isUnsupportedWavFormat = isUnsupportedWavFormat;
        _options = options;
        _decoders = decoders;
    }

    /// <summary>The decoder that opened the most recent file, such as <c>wav</c>, <c>media-foundation</c> or <c>ffmpeg (bundled) at ...</c>; empty before the first open.</summary>
    public string DecoderName { get; private set; } = "";

    /// <summary>Opens the file; throws <see cref="AudioDecodeException"/> with an actionable message when it cannot.</summary>
    public IAudioSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ContainerKind kind = SniffFile(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        bool supportedExtension = ((IList<string>)SupportedExtensions).Contains(extension);

        if (kind == ContainerKind.Unknown && !supportedExtension)
        {
            throw new AudioDecodeException(
                AudioDecodeFailure.UnsupportedType,
                $"'{path}' is not a supported recording: its content is neither WAV nor MP4 and its extension is '{extension}'; choose a {string.Join(", ", SupportedExtensions)} file, or convert the recording to one of them");
        }

        string? wavRefusal = null;
        if (kind == ContainerKind.Wav)
        {
            IAudioSource? wav = TryWavReader(path, out wavRefusal);
            if (wav is not null)
            {
                DecoderName = WavDecoderName;
                return wav;
            }
        }

        INamedAudioSource source = OpenWithPlatformDecoder(path, wavRefusal);
        DecoderName = source.DecoderName;
        return source;
    }

    /// <summary>The real decoders of this machine: Media Foundation on Windows, ffmpeg everywhere.</summary>
    private static AudioDecoders PlatformDecoders() =>
        new(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() ? OpenWithMediaFoundation : null,
            (path, ffmpeg) => new FfmpegAudioSource(path, ffmpeg));

    /// <summary>Opens through Media Foundation; the check repeats the platform guard where the analyser can see it.</summary>
    private static INamedAudioSource OpenWithMediaFoundation(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Media Foundation exists on Windows alone; the opener offers it only there");
        }

        INamedAudioSource source = new MediaFoundationAudioSource(path);
        return source;
    }

    /// <summary>Checks the file exists and is not empty, and reads its container family.</summary>
    private static ContainerKind SniffFile(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0)
            {
                throw new AudioDecodeException(
                    AudioDecodeFailure.EmptyFile,
                    $"the audio file '{path}' is empty (0 bytes); the recording was not saved completely, so copy it again from where it was recorded");
            }

            Span<byte> head = stackalloc byte[ContainerSniffer.HeaderLength];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return ContainerSniffer.Sniff(head[..read]);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new AudioDecodeException(
                AudioDecodeFailure.FileNotFound,
                $"the audio file '{path}' does not exist; check the path is right and the file was not moved or renamed",
                e);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new AudioDecodeException(
                AudioDecodeFailure.AccessDenied,
                $"the audio file '{path}' could not be read ({e.Message}); if it is a folder, choose the file inside it, otherwise copy it to a folder you own or ask its owner for read access",
                e);
        }
        catch (IOException e)
        {
            throw new AudioDecodeException(
                AudioDecodeFailure.AccessDenied,
                $"the audio file '{path}' could not be opened ({e.Message}); close any program holding it open and try again",
                e);
        }
    }

    /// <summary>Opens a WAV file with the engine's reader; null with the reason when it refuses the encoding.</summary>
    private IAudioSource? TryWavReader(string path, out string? refusal)
    {
        refusal = null;
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return _wavReader(stream);
        }
        catch (Exception e) when (_isUnsupportedWavFormat(e))
        {
            stream.Dispose();
            refusal = e.Message;
            return null;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Tries the platform decoders in order and words a failure that names every attempt.</summary>
    private INamedAudioSource OpenWithPlatformDecoder(string path, string? wavRefusal)
    {
        FfmpegLocation? ffmpeg = _options.Locator.Locate();
        List<Func<INamedAudioSource>> attempts = [];
        Func<INamedAudioSource>? mediaFoundation = _decoders.IsWindows && _decoders.MediaFoundation is { } openMf ? () => openMf(path) : null;
        Func<INamedAudioSource>? viaFfmpeg = ffmpeg is not null ? () => _decoders.Ffmpeg(path, ffmpeg) : null;

        if (_options.PreferFfmpeg)
        {
            AddIfPresent(attempts, viaFfmpeg);
            AddIfPresent(attempts, mediaFoundation);
        }
        else
        {
            AddIfPresent(attempts, mediaFoundation);
            AddIfPresent(attempts, viaFfmpeg);
        }

        string wavNote = wavRefusal is null ? "" : $"the WAV reader does not read this encoding ({wavRefusal}); ";
        if (attempts.Count == 0)
        {
            AudioDecodeException missing = FfmpegErrors.NotFound(_options.Locator);
            throw new AudioDecodeException(missing.Failure, wavNote + missing.Message);
        }

        List<AudioDecodeException> failures = [];
        foreach (Func<INamedAudioSource> attempt in attempts)
        {
            try
            {
                return attempt();
            }
            catch (AudioDecodeException e)
            {
                failures.Add(e);
            }
        }

        // Every decoder failed: the first failure's kind is the one a user acts on, and every message is kept.
        AudioDecodeFailure kind = failures[0].Failure;
        string tried = string.Join("; then ", failures.ConvertAll(failure => failure.Message));
        string noFfmpeg = _decoders.IsWindows && ffmpeg is null
            ? $"; no ffmpeg was found to try as well (looked in: {_options.Locator.DescribeSearch()})"
            : "";
        string message = failures.Count == 1 ? wavNote + tried + noFfmpeg : $"{wavNote}every decoder failed: {tried}";
        throw new AudioDecodeException(kind, message, failures[^1]);
    }

    private static void AddIfPresent(List<Func<INamedAudioSource>> attempts, Func<INamedAudioSource>? attempt)
    {
        if (attempt is not null)
        {
            attempts.Add(attempt);
        }
    }
}
