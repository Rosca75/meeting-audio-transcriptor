// Decodes an audio file on Windows through the Media Foundation source reader, handing each decoded
// block to the platform-neutral converter.
using System;
using System.IO;
using System.Runtime.Versioning;
using static MeetingAudioTranscriptor.Media.MediaFoundationNative;

namespace MeetingAudioTranscriptor.Media;

/// <summary>An audio source read through Media Foundation's synchronous source reader.</summary>
/// <remarks>
/// <para>
/// The source holds no logic of its own: it makes the calls in order (start COM and Media Foundation,
/// open a reader, select the first audio stream alone, ask for float output and fall back to 16-bit
/// PCM, read the rate, channels and bits back, read the duration) and copies each decoded block into
/// the shared <see cref="PcmSampleReader"/>. Every decision (the format mapping, the HRESULT wording)
/// is in <see cref="MediaFoundationErrors"/>, which the Linux suites test.
/// </para>
/// <para>
/// The rate and channel count are never forced, because a resampler here would be a second one: the
/// engine resamples to 16 kHz itself. COM is initialised as multithreaded on the constructing thread,
/// so the reader may be read from any thread of the process's multithreaded apartment; Dispose
/// uninitialises COM only when it runs on that same thread, since an uninitialise belongs to the
/// thread that initialised. Every COM pointer is released as soon as its step is done.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class MediaFoundationAudioSource : INamedAudioSource
{
    /// <summary>The decoder name reported for the transcript metadata.</summary>
    public const string Name = "media-foundation";

    private readonly string _path;
    private readonly PcmSampleReader _reader;
    private readonly int _comThreadId;
    private void* _sourceReader;
    private bool _comInitialized;
    private bool _mediaFoundationStarted;
    private bool _endOfStream;
    private bool _disposed;

    /// <summary>Opens the file and settles the output format; throws <see cref="AudioDecodeException"/> on any failure.</summary>
    public MediaFoundationAudioSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _comThreadId = Environment.CurrentManagedThreadId;
        try
        {
            Start();
            fixed (void** reader = &_sourceReader)
            {
                Check(MFCreateSourceReaderFromURL(Path.GetFullPath(path), null, reader), "open");
            }

            // Only the first audio stream is decoded: a video track would otherwise be decoded and dropped.
            Check(SetStreamSelection(_sourceReader, AllStreams, false), "deselect the streams of");
            Check(SetStreamSelection(_sourceReader, FirstAudioStream, true), "select the audio stream of");
            ChooseOutputType();
            (PcmConverter converter, int rate) = ReadCurrentFormat();
            SampleRate = rate;
            _reader = new PcmSampleReader(converter, Refill);
            DurationSeconds = ReadDuration();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Dispose();
            throw MediaFoundationErrors.Unavailable(e);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public double? DurationSeconds { get; }

    /// <inheritdoc />
    public string DecoderName => Name;

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _reader.Read(buffer);
    }

    /// <summary>Releases the reader, then shuts Media Foundation down and uninitialises COM, in reverse order of start.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release(ref _sourceReader);
        if (_mediaFoundationStarted)
        {
            _mediaFoundationStarted = false;
            _ = MFShutdown();
        }

        if (_comInitialized && Environment.CurrentManagedThreadId == _comThreadId)
        {
            _comInitialized = false;
            CoUninitialize();
        }
    }

    /// <summary>Initialises COM (multithreaded) and starts Media Foundation without its network layer.</summary>
    private void Start()
    {
        int hr = CoInitializeEx(null, CoInitMultithreaded);
        if (hr >= 0)
        {
            // S_OK and S_FALSE both take a matching CoUninitialize.
            _comInitialized = true;
        }
        else if (hr != RpcEChangedMode)
        {
            Check(hr, "initialise COM to open");
        }

        Check(MFStartup(MfVersion, MfStartupLite), "start up to open");
        _mediaFoundationStarted = true;
    }

    /// <summary>Asks for uncompressed float output, and for 16-bit PCM when the decoder refuses float.</summary>
    private void ChooseOutputType()
    {
        int hr = TrySetOutputType(AudioFormatFloat, bitsPerSample: 0);
        if (hr < 0)
        {
            hr = TrySetOutputType(AudioFormatPcm, bitsPerSample: 16);
        }

        Check(hr, "set an uncompressed output type for");
    }

    /// <summary>Builds a partial audio type (major type, sub-type and optionally bits) and offers it to the reader.</summary>
    private int TrySetOutputType(Guid subtype, uint bitsPerSample)
    {
        void* mediaType = null;
        try
        {
            Check(MFCreateMediaType(&mediaType), "create a media type for");
            Check(SetGuid(mediaType, MtMajorType, MediaTypeAudio), "describe the output type of");
            Check(SetGuid(mediaType, MtSubtype, subtype), "describe the output type of");
            if (bitsPerSample != 0)
            {
                Check(SetUInt32(mediaType, MtAudioBitsPerSample, bitsPerSample), "describe the output type of");
            }

            return SetCurrentMediaType(_sourceReader, FirstAudioStream, mediaType);
        }
        finally
        {
            Release(ref mediaType);
        }
    }

    /// <summary>Reads back the type the reader settled on and builds the converter for it.</summary>
    private (PcmConverter Converter, int SampleRate) ReadCurrentFormat()
    {
        void* mediaType = null;
        try
        {
            Check(GetCurrentMediaType(_sourceReader, FirstAudioStream, &mediaType), "read the output type of");
            Check(GetGuid(mediaType, MtSubtype, out Guid subtype), "read the output sub-type of");
            Check(GetUInt32(mediaType, MtAudioSamplesPerSecond, out uint rate), "read the sample rate of");
            Check(GetUInt32(mediaType, MtAudioNumChannels, out uint channels), "read the channel count of");
            Check(GetUInt32(mediaType, MtAudioBitsPerSample, out uint bits), "read the sample size of");

            MediaFoundationSubtype kind = subtype == AudioFormatFloat ? MediaFoundationSubtype.Float
                : subtype == AudioFormatPcm ? MediaFoundationSubtype.Pcm
                : MediaFoundationSubtype.Other;
            MediaFoundationErrors.ValidateLayout((int)Math.Min(rate, int.MaxValue), (int)Math.Min(channels, int.MaxValue), _path);
            PcmSampleFormat format = MediaFoundationErrors.ToPcmFormat(kind, (int)bits, _path);
            return (new PcmConverter(format, (int)channels), (int)rate);
        }
        finally
        {
            Release(ref mediaType);
        }
    }

    /// <summary>Reads <c>MF_PD_DURATION</c> from the source; null when the container does not state it.</summary>
    private double? ReadDuration()
    {
        PropVariant value = default;
        int hr = GetPresentationAttribute(_sourceReader, MediaSource, PdDuration, &value);
        try
        {
            return hr >= 0 && value.VariantType == VtUI8 ? MediaFoundationErrors.DurationSeconds(value.UnsignedLong) : null;
        }
        finally
        {
            _ = PropVariantClear(&value);
        }
    }

    /// <summary>Reads samples until one carries data, copies it into the pending bytes, and reports the end.</summary>
    private bool Refill(PcmPendingBytes pending)
    {
        while (!_endOfStream)
        {
            uint actualStream = 0;
            uint flags = 0;
            long timestamp = 0;
            void* sample = null;
            try
            {
                Check(ReadSample(_sourceReader, FirstAudioStream, &actualStream, &flags, &timestamp, &sample), "read the next block of");
                if ((flags & ReaderFlagError) != 0)
                {
                    throw MediaFoundationErrors.ToException(unchecked((int)0x80004005), _path, "keep reading");
                }

                if ((flags & ReaderFlagEndOfStream) != 0)
                {
                    _endOfStream = true;
                }

                if ((flags & ReaderFlagCurrentMediaTypeChanged) != 0)
                {
                    // Only whole frames are pending when a refill runs, so the converter can be replaced.
                    (PcmConverter converter, int rate) = ReadCurrentFormat();
                    if (rate != SampleRate)
                    {
                        throw new AudioDecodeException(
                            AudioDecodeFailure.CorruptOrUnsupportedData,
                            $"the sample rate of '{_path}' changes from {SampleRate} Hz to {rate} Hz part way through, which Media Foundation decoding does not follow; convert the file to .wav with one rate and try again");
                    }

                    _reader.Converter = converter;
                }

                if (sample != null && CopySample(sample, pending))
                {
                    return true;
                }
            }
            finally
            {
                Release(ref sample);
            }
        }

        return false;
    }

    /// <summary>Copies one sample's bytes into the pending buffer; false when the sample held none.</summary>
    private bool CopySample(void* sample, PcmPendingBytes pending)
    {
        void* buffer = null;
        try
        {
            Check(ConvertToContiguousBuffer(sample, &buffer), "gather a decoded block of");
            byte* data = null;
            uint length = 0;
            Check(Lock(buffer, &data, null, &length), "lock a decoded block of");
            try
            {
                pending.Append(new ReadOnlySpan<byte>(data, checked((int)length)));
            }
            finally
            {
                _ = Unlock(buffer);
            }

            return length > 0;
        }
        finally
        {
            Release(ref buffer);
        }
    }

    /// <summary>Throws the worded failure for a failed HRESULT.</summary>
    private void Check(int hresult, string step)
    {
        if (hresult < 0)
        {
            throw MediaFoundationErrors.ToException(hresult, _path, step);
        }
    }
}
