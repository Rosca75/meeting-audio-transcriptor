// The platform-neutral half of the Media Foundation decoder: HRESULT names and advice, and the mapping
// from the output type Media Foundation chose to a PcmConverter format, both tested on Linux.
using System;
using System.Globalization;

namespace MeetingAudioTranscriptor.Media;

/// <summary>The audio sub-types the Media Foundation decoder can be asked for.</summary>
internal enum MediaFoundationSubtype
{
    /// <summary><c>MFAudioFormat_Float</c>: IEEE float samples.</summary>
    Float,

    /// <summary><c>MFAudioFormat_PCM</c>: signed integer samples (unsigned at 8 bits).</summary>
    Pcm,

    /// <summary>Any other sub-type, which the converter cannot read.</summary>
    Other,
}

/// <summary>Maps Media Foundation results to actionable failures and output types to converter formats.</summary>
/// <remarks>
/// Everything that is a decision rather than a call lives here, so the Windows-only source is a
/// sequence of calls whose failures are all worded by tested code. The HRESULT values are those of
/// winerror.h and mferror.h.
/// </remarks>
internal static class MediaFoundationErrors
{
    /// <summary><c>HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)</c>.</summary>
    public const int FileNotFound = unchecked((int)0x80070002);

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND)</c>.</summary>
    public const int PathNotFound = unchecked((int)0x80070003);

    /// <summary><c>E_ACCESSDENIED</c>, also <c>HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED)</c>.</summary>
    public const int AccessDenied = unchecked((int)0x80070005);

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_SHARING_VIOLATION)</c>: another program holds the file open exclusively.</summary>
    public const int SharingViolation = unchecked((int)0x80070020);

    /// <summary><c>MF_E_INVALIDSTREAMNUMBER</c>: asking for the first audio stream of a file without one.</summary>
    public const int InvalidStreamNumber = unchecked((int)0xC00D36B3);

    /// <summary><c>MF_E_INVALIDMEDIATYPE</c>: no decoder produces the requested output type.</summary>
    public const int InvalidMediaType = unchecked((int)0xC00D36B4);

    /// <summary><c>MF_E_NO_MORE_TYPES</c>.</summary>
    public const int NoMoreTypes = unchecked((int)0xC00D36B9);

    /// <summary><c>MF_E_INVALID_FILE_FORMAT</c>.</summary>
    public const int InvalidFileFormat = unchecked((int)0xC00D36BE);

    /// <summary><c>MF_E_UNSUPPORTED_SCHEME</c>.</summary>
    public const int UnsupportedScheme = unchecked((int)0xC00D36C3);

    /// <summary><c>MF_E_UNSUPPORTED_BYTESTREAM_TYPE</c>: no media source reads this container.</summary>
    public const int UnsupportedByteStreamType = unchecked((int)0xC00D36C4);

    /// <summary><c>MF_E_ATTRIBUTENOTFOUND</c>.</summary>
    public const int AttributeNotFound = unchecked((int)0xC00D36E6);

    /// <summary><c>MF_E_TOPO_CODEC_NOT_FOUND</c>: the stream's codec has no decoder installed.</summary>
    public const int TopoCodecNotFound = unchecked((int)0xC00D5212);

    /// <summary><c>MF_E_PLATFORM_NOT_INITIALIZED</c>.</summary>
    public const int PlatformNotInitialized = unchecked((int)0xC00D36B0);

    /// <summary>Builds the exception for a failed Media Foundation call.</summary>
    /// <param name="hresult">The failing HRESULT.</param>
    /// <param name="path">The file being decoded.</param>
    /// <param name="step">What was being done, as a verb phrase ("open", "read the next block of").</param>
    public static AudioDecodeException ToException(int hresult, string path, string step)
    {
        string code = "0x" + hresult.ToString("X8", CultureInfo.InvariantCulture);
        string name = Name(hresult);
        string failed = $"Media Foundation could not {step} '{path}' ({name}, HRESULT {code})";
        return hresult switch
        {
            FileNotFound or PathNotFound => new AudioDecodeException(
                AudioDecodeFailure.FileNotFound, $"{failed}; the file was not found, so check the path is right and the file still exists"),
            AccessDenied or SharingViolation => new AudioDecodeException(
                AudioDecodeFailure.AccessDenied, $"{failed}; the file could not be read, so close any program holding it open, or copy it to a folder you own"),
            InvalidStreamNumber => new AudioDecodeException(
                AudioDecodeFailure.NoAudioStream, FfmpegErrors.NoAudioStreamMessage(path) + $" ({name}, HRESULT {code})"),
            UnsupportedByteStreamType or InvalidFileFormat or UnsupportedScheme => new AudioDecodeException(
                AudioDecodeFailure.CorruptOrUnsupportedData, $"{failed}; the file is damaged or is not an .m4a, .mp4 or .wav container, so check that it plays in a media player or export it again"),
            InvalidMediaType or TopoCodecNotFound or NoMoreTypes => new AudioDecodeException(
                AudioDecodeFailure.CorruptOrUnsupportedData, $"{failed}; no installed decoder reads this audio codec, so export the recording again as AAC (.m4a) or PCM (.wav), or install the Media Feature Pack on a Windows N edition"),
            _ => new AudioDecodeException(
                AudioDecodeFailure.DecoderFailed, $"{failed}; check that the file plays in a media player, and if it does, convert it to .wav and try again"),
        };
    }

    /// <summary>The header name of a common HRESULT, or "an unexpected error".</summary>
    public static string Name(int hresult) => hresult switch
    {
        FileNotFound => "ERROR_FILE_NOT_FOUND",
        PathNotFound => "ERROR_PATH_NOT_FOUND",
        AccessDenied => "E_ACCESSDENIED",
        SharingViolation => "ERROR_SHARING_VIOLATION",
        InvalidStreamNumber => "MF_E_INVALIDSTREAMNUMBER",
        InvalidMediaType => "MF_E_INVALIDMEDIATYPE",
        NoMoreTypes => "MF_E_NO_MORE_TYPES",
        InvalidFileFormat => "MF_E_INVALID_FILE_FORMAT",
        UnsupportedScheme => "MF_E_UNSUPPORTED_SCHEME",
        UnsupportedByteStreamType => "MF_E_UNSUPPORTED_BYTESTREAM_TYPE",
        AttributeNotFound => "MF_E_ATTRIBUTENOTFOUND",
        TopoCodecNotFound => "MF_E_TOPO_CODEC_NOT_FOUND",
        PlatformNotInitialized => "MF_E_PLATFORM_NOT_INITIALIZED",
        _ => "an unexpected error",
    };

    /// <summary>The exception for a machine where Media Foundation itself cannot be loaded.</summary>
    public static AudioDecodeException Unavailable(Exception cause) =>
        new(
            AudioDecodeFailure.DecoderMissing,
            $"Media Foundation is not available on this machine ({cause.Message}); on a Windows N edition install the Media Feature Pack, or place ffmpeg in the application's ffmpeg folder",
            cause);

    /// <summary>Maps the output type Media Foundation settled on to the converter's format.</summary>
    /// <exception cref="AudioDecodeException">The type is not one the converter reads.</exception>
    public static PcmSampleFormat ToPcmFormat(MediaFoundationSubtype subtype, int bitsPerSample, string path) => (subtype, bitsPerSample) switch
    {
        (MediaFoundationSubtype.Float, 32) => PcmSampleFormat.Float32,
        (MediaFoundationSubtype.Pcm, 16) => PcmSampleFormat.Int16,
        (MediaFoundationSubtype.Pcm, 24) => PcmSampleFormat.Int24,
        (MediaFoundationSubtype.Pcm, 32) => PcmSampleFormat.Int32,
        _ => throw new AudioDecodeException(
            AudioDecodeFailure.CorruptOrUnsupportedData,
            $"Media Foundation decoded '{path}' to {subtype} samples of {bitsPerSample} bits, wanted 32-bit float or 16, 24 or 32-bit PCM; convert the file to .wav or .m4a and try again"),
    };

    /// <summary>Checks the rate and channel count Media Foundation reported before a converter is built from them.</summary>
    public static void ValidateLayout(int sampleRate, int channels, string path)
    {
        if (sampleRate <= 0 || channels < 1 || channels > PcmConverter.MaxChannels)
        {
            throw new AudioDecodeException(
                AudioDecodeFailure.CorruptOrUnsupportedData,
                $"Media Foundation reported {sampleRate} Hz and {channels} channels for '{path}', wanted a positive rate and 1 to {PcmConverter.MaxChannels} channels; the stream header is damaged, so export the recording again");
        }
    }

    /// <summary>Converts a duration in 100-nanosecond units (<c>MF_PD_DURATION</c>) to seconds; null when it is zero.</summary>
    public static double? DurationSeconds(ulong hundredNanoseconds) =>
        hundredNanoseconds == 0 ? null : hundredNanoseconds / 10_000_000d;
}
