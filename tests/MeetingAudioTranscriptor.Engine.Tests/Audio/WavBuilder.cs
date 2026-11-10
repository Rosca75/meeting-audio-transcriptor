// Builds WAV files in memory for the reader's tests, in every layout the reader accepts and several
// it must refuse, so no audio file is committed to the repository.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MeetingAudioTranscriptor.Engine.Tests.Audio;

/// <summary>Writes a WAV file from interleaved sample values, with switches for each container variation.</summary>
/// <remarks>
/// Integer samples are quantised by rounding v * 2^(bits-1) and clamping, the inverse of the reader's
/// scaling, so a round trip differs from the input by at most one quantisation step.
/// </remarks>
internal sealed class WavBuilder
{
    public ushort FormatTag { get; set; } = 1;

    public int Channels { get; set; } = 1;

    public int SampleRate { get; set; } = 16000;

    public int BitsPerSample { get; set; } = 16;

    /// <summary>Writes WAVE_FORMAT_EXTENSIBLE with <see cref="FormatTag"/> as the sub-format.</summary>
    public bool Extensible { get; set; }

    /// <summary>The sub-format GUID's tail bytes when a test needs a foreign GUID; null writes the standard one.</summary>
    public byte[]? SubFormatTail { get; set; }

    /// <summary>Writes an RF64 file whose sizes live in a ds64 chunk.</summary>
    public bool Rf64 { get; set; }

    /// <summary>The size written into the data chunk header in place of the real one.</summary>
    public uint? DataSizeOverride { get; set; }

    /// <summary>Bytes cut from the end of the data after the header states its full size.</summary>
    public int TruncateDataBytes { get; set; }

    public int? BlockAlignOverride { get; set; }

    public List<(string Id, byte[] Body)> ChunksBeforeFormat { get; } = [];

    public List<(string Id, byte[] Body)> ChunksBeforeData { get; } = [];

    public List<(string Id, byte[] Body)> ChunksAfterData { get; } = [];

    /// <summary>The value the reader should produce for one sample after quantisation.</summary>
    public double Quantise(double value)
    {
        bool isFloat = FormatTag == 3;
        if (isFloat)
        {
            return BitsPerSample == 32 ? (float)value : value;
        }

        double fullScale = Math.Pow(2, BitsPerSample - 1);
        double q = Math.Clamp(Math.Round(value * fullScale), -fullScale, fullScale - 1);
        return q / fullScale;
    }

    /// <summary>The mono values the reader should produce: each frame's quantised channels averaged.</summary>
    public double[] ExpectedMono(double[] interleaved)
    {
        int frames = interleaved.Length / Channels;
        double[] mono = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < Channels; c++)
            {
                sum += Quantise(interleaved[(f * Channels) + c]);
            }

            mono[f] = sum / Channels;
        }

        return mono;
    }

    public byte[] Build(double[] interleaved)
    {
        byte[] data = EncodeSamples(interleaved);
        int dataLength = data.Length - TruncateDataBytes;

        using MemoryStream output = new();
        using BinaryWriter writer = new(output);

        writer.Write(Encoding.ASCII.GetBytes(Rf64 ? "RF64" : "RIFF"));
        writer.Write(Rf64 ? uint.MaxValue : 0u); // Patched below for RIFF.
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        if (Rf64)
        {
            WriteChunkHeader(writer, "ds64", 28);
            writer.Write(0UL); // RIFF size, not read.
            writer.Write((ulong)data.Length);
            writer.Write((ulong)(data.Length / BlockAlign()));
            writer.Write(0u); // No table entries.
        }

        foreach ((string id, byte[] body) in ChunksBeforeFormat)
        {
            WriteChunk(writer, id, body);
        }

        WriteChunk(writer, "fmt ", FormatBody());

        foreach ((string id, byte[] body) in ChunksBeforeData)
        {
            WriteChunk(writer, id, body);
        }

        uint dataSize = Rf64 ? uint.MaxValue : DataSizeOverride ?? (uint)data.Length;
        WriteChunkHeader(writer, "data", dataSize);
        writer.Write(data, 0, dataLength);
        if (TruncateDataBytes == 0)
        {
            if ((data.Length & 1) == 1)
            {
                writer.Write((byte)0);
            }

            foreach ((string id, byte[] body) in ChunksAfterData)
            {
                WriteChunk(writer, id, body);
            }
        }

        writer.Flush();
        byte[] bytes = output.ToArray();
        if (!Rf64)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        }

        return bytes;
    }

    private int ContainerBytes() => (BitsPerSample + 7) / 8;

    private int BlockAlign() => BlockAlignOverride ?? (Channels * ContainerBytes());

    private byte[] FormatBody()
    {
        using MemoryStream body = new();
        using BinaryWriter writer = new(body);
        writer.Write(Extensible ? (ushort)0xFFFE : FormatTag);
        writer.Write((ushort)Channels);
        writer.Write((uint)SampleRate);
        writer.Write((uint)(SampleRate * BlockAlign()));
        writer.Write((ushort)BlockAlign());
        writer.Write((ushort)BitsPerSample);
        if (Extensible)
        {
            writer.Write((ushort)22);
            writer.Write((ushort)BitsPerSample);
            writer.Write(0u); // Channel mask: not read.
            writer.Write(FormatTag);
            writer.Write(SubFormatTail ?? [0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71]);
        }

        writer.Flush();
        return body.ToArray();
    }

    private byte[] EncodeSamples(double[] interleaved)
    {
        using MemoryStream data = new();
        using BinaryWriter writer = new(data);
        foreach (double value in interleaved)
        {
            if (FormatTag == 3)
            {
                if (BitsPerSample == 32)
                {
                    writer.Write((float)value);
                }
                else
                {
                    writer.Write(value);
                }

                continue;
            }

            long q = (long)Math.Round(Quantise(value) * Math.Pow(2, BitsPerSample - 1));
            switch (BitsPerSample)
            {
                case 8:
                    writer.Write((byte)(q + 128));
                    break;
                case 16:
                    writer.Write((short)q);
                    break;
                case 24:
                    writer.Write((byte)(q & 0xFF));
                    writer.Write((byte)((q >> 8) & 0xFF));
                    writer.Write((byte)((q >> 16) & 0xFF));
                    break;
                case 32:
                    writer.Write((int)q);
                    break;
                default:
                    // An unsupported depth for a refusal test: any bytes of the right count do.
                    writer.Write(new byte[ContainerBytes()]);
                    break;
            }
        }

        writer.Flush();
        return data.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string id, byte[] body)
    {
        WriteChunkHeader(writer, id, (uint)body.Length);
        writer.Write(body);
        if ((body.Length & 1) == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteChunkHeader(BinaryWriter writer, string id, uint size)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(size);
    }
}
