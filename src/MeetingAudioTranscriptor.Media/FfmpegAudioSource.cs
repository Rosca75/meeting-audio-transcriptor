// Decodes an audio file through an ffmpeg child process that writes mono float samples to its stdout.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MeetingAudioTranscriptor.Media;

/// <summary>An audio source fed by an ffmpeg child process.</summary>
/// <remarks>
/// <para>
/// Two runs of ffmpeg are made. The first, a probe (<c>ffmpeg -i file</c> with no output), prints the
/// input section to stderr and exits at once; it gives the first audio stream's rate and the
/// container's duration before any sample is read, which <c>IAudioSource.SampleRate</c> requires, and it
/// tells a file without audio from a damaged one before a decode is started. The bundled build carries
/// no ffprobe, and reading the banner of the decoding process itself would need info-level logging
/// mixed with the error lines that are kept as warnings, so the separate probe is the robust way. The
/// decode then pins <c>-ar</c> to that rate and <c>-ac</c> to the stream's own channel count, so stdout
/// is always at the announced rate and in frames of a known width, which the converter averages to mono.
/// </para>
/// <para>
/// stdout is read in 64 KiB blocks into one reused byte buffer and converted as interleaved little-endian float.
/// stderr is drained on its own thread by the process class, so a chatty ffmpeg never blocks on a full
/// pipe. Dispose kills a running child and waits for it, which reaps it, so no zombie is left; a read
/// blocked on the pipe at that moment returns zero.
/// </para>
/// <para>
/// A file truncated mid-stream decodes as far as it goes: ffmpeg's error lines are kept in
/// <see cref="Warnings"/> and the audio ends there. Only a decode that produced no sample at all and
/// exited with an error is reported as a failure.
/// </para>
/// </remarks>
internal sealed class FfmpegAudioSource : INamedAudioSource
{
    /// <summary>The size of one read from ffmpeg's stdout.</summary>
    private const int ReadBlockBytes = 1 << 16;

    /// <summary>How many of ffmpeg's last stderr lines are kept.</summary>
    private const int KeptStderrLines = 32;

    /// <summary>How long the probe may take; it reads only the file's header.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long Dispose waits for a killed child to be reaped.</summary>
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(2);

    private readonly string _path;
    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly Queue<string> _stderr = new();
    private readonly Lock _stderrLock = new();
    private readonly PcmSampleReader _reader;
    private long _samplesDelivered;
    private int _disposed;

    /// <summary>Probes the file, then starts the decode; throws <see cref="AudioDecodeException"/> when either fails.</summary>
    public FfmpegAudioSource(string path, FfmpegLocation ffmpeg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(ffmpeg);
        _path = path;
        DecoderName = ffmpeg.ToString();

        FfmpegInputInfo info = Probe(path, ffmpeg);
        SampleRate = info.AudioSampleRate!.Value;
        DurationSeconds = info.DurationSeconds;

        // A layout the table does not name is mixed down to stereo by ffmpeg and averaged here: the
        // frames then have a known width whatever the file holds.
        int channels = Math.Clamp(info.AudioChannels ?? 2, 1, PcmConverter.MaxChannels);

        ProcessStartInfo start = NewStartInfo(ffmpeg, FfmpegBanner.DecodeArguments(path, SampleRate, channels));
        _process = new Process { StartInfo = start };
        _process.ErrorDataReceived += OnStderrLine;
        try
        {
            _process.Start();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            _process.Dispose();
            throw FfmpegErrors.NotStartable(ffmpeg, e);
        }

        _process.StandardInput.Close();
        _process.BeginErrorReadLine();
        _stdout = _process.StandardOutput.BaseStream;
        _reader = new PcmSampleReader(new PcmConverter(PcmSampleFormat.Float32, channels), Refill, ReadBlockBytes * 2);
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public double? DurationSeconds { get; }

    /// <inheritdoc />
    public string DecoderName { get; }

    /// <summary>The child's process id, for diagnostics and the tests that check it was stopped.</summary>
    internal int ProcessId => _process.Id;

    /// <summary>The error lines ffmpeg wrote while decoding, such as those of a truncated file; empty for a clean decode.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_stderrLock)
            {
                return [.. _stderr];
            }
        }
    }

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        int written = _reader.Read(buffer);
        _samplesDelivered += written;
        return written;
    }

    /// <summary>Stops the child if it is still running and releases the pipes.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            // Waiting reaps the child; a bounded wait keeps a hung kill from hanging the caller.
            _process.WaitForExit(KillWait);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The child exited between the check and the kill: there is nothing left to stop.
        }

        _process.Dispose();
    }

    /// <summary>Runs the probe and returns what it read, or throws the failure it reported.</summary>
    private static FfmpegInputInfo Probe(string path, FfmpegLocation ffmpeg)
    {
        using Process probe = new() { StartInfo = NewStartInfo(ffmpeg, FfmpegBanner.ProbeArguments(path)) };
        try
        {
            probe.Start();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            throw FfmpegErrors.NotStartable(ffmpeg, e);
        }

        probe.StandardInput.Close();
        System.Threading.Tasks.Task<string> stdout = probe.StandardOutput.ReadToEndAsync();
        System.Threading.Tasks.Task<string> stderr = probe.StandardError.ReadToEndAsync();
        if (!probe.WaitForExit(ProbeTimeout))
        {
            try
            {
                probe.Kill(entireProcessTree: true);
                probe.WaitForExit(KillWait);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // Already gone.
            }

            throw new AudioDecodeException(
                AudioDecodeFailure.DecoderFailed,
                $"{ffmpeg} took more than {ProbeTimeout.TotalSeconds:0} seconds to read the header of '{path}'; copy the file to a local disk and try again");
        }

        stdout.GetAwaiter().GetResult();
        string text = stderr.GetAwaiter().GetResult();
        FfmpegInputInfo info = FfmpegBanner.Parse(text);
        if (!info.HasInput)
        {
            throw FfmpegErrors.Classify(path, text.Split('\n', StringSplitOptions.RemoveEmptyEntries), probe.ExitCode);
        }

        if (info.AudioStreamCount == 0)
        {
            throw new AudioDecodeException(AudioDecodeFailure.NoAudioStream, FfmpegErrors.NoAudioStreamMessage(path));
        }

        if (info.AudioSampleRate is null)
        {
            throw new AudioDecodeException(
                AudioDecodeFailure.CorruptOrUnsupportedData,
                $"ffmpeg found an audio stream in '{path}' but no sample rate for it; the stream header is damaged, so export the recording again as .m4a, .mp4 or .wav");
        }

        return info;
    }

    /// <summary>The start information shared by the probe and the decode: no shell, every stream redirected, no window.</summary>
    private static ProcessStartInfo NewStartInfo(FfmpegLocation ffmpeg, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new(ffmpeg.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    /// <summary>Keeps the last stderr lines; called on the process class's reader thread.</summary>
    private void OnStderrLine(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        lock (_stderrLock)
        {
            _stderr.Enqueue(FfmpegErrors.Clean(e.Data));
            while (_stderr.Count > KeptStderrLines)
            {
                _stderr.Dequeue();
            }
        }
    }

    /// <summary>Reads the next block of stdout; at its end, checks how ffmpeg exited.</summary>
    private bool Refill(PcmPendingBytes pending)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        int read;
        try
        {
            read = _stdout.Read(pending.GetWriteSpan(ReadBlockBytes));
        }
        catch (Exception e) when ((e is IOException or ObjectDisposedException) && Volatile.Read(ref _disposed) != 0)
        {
            // Dispose killed the child mid-read: the audio ends here.
            return false;
        }

        if (read > 0)
        {
            pending.Commit(read);
            return true;
        }

        // stdout is closed: wait for the exit (this also waits for the last stderr line).
        int exitCode;
        try
        {
            _process.WaitForExit();
            exitCode = _process.ExitCode;
        }
        catch (InvalidOperationException) when (Volatile.Read(ref _disposed) != 0)
        {
            // Dispose, on another thread, released the process while this read waited for it.
            return false;
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        if (exitCode != 0 && _samplesDelivered == 0)
        {
            throw FfmpegErrors.Classify(_path, Warnings, exitCode);
        }

        return false;
    }
}
