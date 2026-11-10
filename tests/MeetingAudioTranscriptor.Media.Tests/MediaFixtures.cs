// Generates the small media files the integration tests decode (a 440 Hz tone in WAV, M4A and MP4,
// a video without sound, random bytes) with the ffmpeg found on the machine, and measures decoded audio.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace MeetingAudioTranscriptor.Media.Tests;

/// <summary>The generated files, made once per test class in a temporary folder deleted afterwards.</summary>
public sealed class MediaFixtures : IDisposable
{
    /// <summary>The tone's frequency.</summary>
    public const double ToneHz = 440;

    /// <summary>The amplitude of ffmpeg's sine source (1/8 of full scale).</summary>
    public const double ToneAmplitude = 0.125;

    public MediaFixtures()
    {
        Ffmpeg = new FfmpegLocator().Locate();
        Directory = Path.Combine(Path.GetTempPath(), "mat-media-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>The ffmpeg the tests generate and decode with, or null when there is none.</summary>
    internal FfmpegLocation? Ffmpeg { get; }

    /// <summary>The folder holding the generated files.</summary>
    public string Directory { get; }

    /// <summary>Skips the calling test when no ffmpeg is installed.</summary>
    public void SkipWithoutFfmpeg() =>
        Assert.SkipUnless(Ffmpeg is not null, "needs ffmpeg: none was found through MAT_FFMPEG, beside the test or on the PATH; install it");

    /// <summary>Generates a file once with the given ffmpeg output arguments and returns its path.</summary>
    public string Generate(string name, params string[] arguments)
    {
        string path = Path.Combine(Directory, name);
        if (!File.Exists(path))
        {
            RunFfmpeg(["-nostdin", "-hide_banner", "-loglevel", "error", "-y", .. arguments, path]);
        }

        return path;
    }

    /// <summary>A tone of the given length, rate and channel count, encoded by the given codec arguments.</summary>
    /// <remarks>Every channel carries the tone at full level: the pan filter copies it, where ffmpeg's own upmix would scale it by 0.707.</remarks>
    public string Tone(string name, int rate, int channels, double seconds, params string[] codec) =>
        Generate(name, ["-f", "lavfi", "-i", Sine(rate, seconds), "-af", CopyToChannels(channels), .. codec]);

    /// <summary>ffmpeg's sine source at the tone's frequency.</summary>
    public static string Sine(int rate, double seconds) =>
        $"sine=frequency={ToneHz}:sample_rate={rate}:duration={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>A pan filter putting the mono tone at full level on every one of the given channels.</summary>
    public static string CopyToChannels(int channels)
    {
        List<string> outputs = [];
        for (int channel = 0; channel < channels; channel++)
        {
            outputs.Add($"c{channel}=c0");
        }

        string layout = channels switch { 1 => "mono", 2 => "stereo", 6 => "5.1", _ => $"{channels}c" };
        return $"pan={layout}|{string.Join("|", outputs)}";
    }

    /// <summary>Runs ffmpeg and fails with its stderr when it fails.</summary>
    public void RunFfmpeg(IEnumerable<string> arguments)
    {
        ProcessStartInfo start = new(Ffmpeg!.ExecutablePath) { RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"got ffmpeg exit code {process.ExitCode} ({stderr}), wanted 0 while generating a fixture");
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by a child that is being reaped; the temporary folder is cleaned by the system.
        }
    }

    /// <summary>Reads a source to its end.</summary>
    internal static float[] ReadAll(Engine.Audio.IAudioSource source)
    {
        List<float> samples = [];
        float[] buffer = new float[4096];
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return [.. samples];
    }

    /// <summary>The root mean square of the samples.</summary>
    internal static double Rms(float[] samples)
    {
        double sum = 0;
        foreach (float sample in samples)
        {
            sum += sample * (double)sample;
        }

        return Math.Sqrt(sum / Math.Max(1, samples.Length));
    }

    /// <summary>The tone's frequency estimated from the sign changes in the middle of the signal, away from codec edges.</summary>
    internal static double ZeroCrossingHz(float[] samples, int rate)
    {
        int from = samples.Length / 10;
        int to = samples.Length - from;
        int crossings = 0;
        for (int i = from + 1; i < to; i++)
        {
            if ((samples[i - 1] < 0) != (samples[i] < 0))
            {
                crossings++;
            }
        }

        return crossings / 2d / ((to - from) / (double)rate);
    }

    /// <summary>A 16-bit PCM WAV of a tone, written byte by byte, for the decoders that need no generator.</summary>
    internal static byte[] ToneWav(int rate, int channels, double seconds)
    {
        int frames = (int)(rate * seconds);
        int dataBytes = frames * channels * 2;
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataBytes);
            for (int frame = 0; frame < frames; frame++)
            {
                short value = (short)Math.Round(ToneAmplitude * 32767 * Math.Sin(2 * Math.PI * ToneHz * frame / rate));
                for (int channel = 0; channel < channels; channel++)
                {
                    writer.Write(value);
                }
            }
        }

        return stream.ToArray();
    }
}
