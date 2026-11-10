// Reads the benchmark's 16 kHz mono PCM16 WAV files and RTTM speaker turns, and serves samples as an audio source.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MeetingAudioTranscriptor.Engine.Audio;

namespace MeetingAudioTranscriptor.Pipeline.Tests;

/// <summary>The minimal readers the suite needs for its fixtures.</summary>
internal static class TestAudio
{
    /// <summary>Reads a 16 kHz mono 16-bit PCM WAV file as samples in [-1, 1), walking the RIFF chunks rather than assuming a 44-byte header.</summary>
    public static float[] ReadWav(string path)
    {
        using BinaryReader reader = new(File.OpenRead(path));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
        {
            throw new InvalidDataException($"{path} is not a RIFF file");
        }

        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
        {
            throw new InvalidDataException($"{path} is not a WAVE file");
        }

        bool formatChecked = false;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint size = reader.ReadUInt32();
            long next = reader.BaseStream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                ushort format = reader.ReadUInt16();
                ushort channels = reader.ReadUInt16();
                uint rate = reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadUInt16();
                ushort bits = reader.ReadUInt16();
                if (format != 1 || channels != 1 || rate != AudioFormat.ModelSampleRate || bits != 16)
                {
                    throw new InvalidDataException($"{path} is format {format}, {channels} channel(s), {rate} Hz, {bits} bits; the tests read 16 kHz mono 16-bit PCM only");
                }

                formatChecked = true;
            }
            else if (id == "data" && formatChecked)
            {
                byte[] bytes = reader.ReadBytes((int)size);
                float[] samples = new float[bytes.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                }

                return samples;
            }

            reader.BaseStream.Position = next;
        }

        throw new InvalidDataException($"{path} has no fmt chunk followed by a data chunk");
    }

    /// <summary>Reads the speaker turns of an RTTM file as [start, end) seconds, unmerged.</summary>
    public static List<(double Start, double End)> ReadRttm(string path)
    {
        List<(double, double)> turns = [];
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 5 && fields[0] == "SPEAKER")
            {
                double start = double.Parse(fields[3], CultureInfo.InvariantCulture);
                double duration = double.Parse(fields[4], CultureInfo.InvariantCulture);
                turns.Add((start, start + duration));
            }
        }

        return turns;
    }
}

/// <summary>An in-memory 16 kHz source that hands out samples in reads of at most a fixed size.</summary>
internal sealed class ArrayAudioSource : IAudioSource
{
    private readonly float[] _samples;
    private readonly int _readSize;
    private int _position;

    public ArrayAudioSource(float[] samples, int readSize = 4000)
    {
        _samples = samples;
        _readSize = readSize;
    }

    public int SampleRate => AudioFormat.ModelSampleRate;

    public double? DurationSeconds => _samples.Length / (double)AudioFormat.ModelSampleRate;

    public int Read(Span<float> buffer)
    {
        int count = Math.Min(Math.Min(buffer.Length, _readSize), _samples.Length - _position);
        _samples.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public void Dispose()
    {
    }
}
