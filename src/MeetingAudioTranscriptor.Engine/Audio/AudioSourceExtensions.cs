// Helpers that read an audio source whole or a buffer at a time, for short clips and for tests.
using System;
using System.Collections.Generic;

namespace MeetingAudioTranscriptor.Engine.Audio;

/// <summary>Reading helpers over any <see cref="IAudioSource"/>.</summary>
/// <remarks>
/// A source may return fewer samples than asked for, so every consumer that needs a full frame would
/// otherwise repeat the same loop. <see cref="ReadAll"/> holds the whole audio in memory and is meant
/// for clips and tests only: a meeting recording is consumed block by block.
/// </remarks>
public static class AudioSourceExtensions
{
    /// <summary>Reads every remaining sample of the source into one array.</summary>
    public static float[] ReadAll(this IAudioSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // The stated duration only sizes the first block; the source's actual end decides the length.
        int initial = 65536;
        if (source.DurationSeconds is double seconds && seconds > 0 && seconds * source.SampleRate < int.MaxValue / 2)
        {
            initial = Math.Max(16, (int)Math.Ceiling(seconds * source.SampleRate) + 1);
        }

        List<float> all = new(initial);
        float[] block = new float[Math.Min(initial, 65536)];
        int read;
        while ((read = source.Read(block)) > 0)
        {
            all.AddRange(block.AsSpan(0, read));
        }

        return all.ToArray();
    }

    /// <summary>Reads until the buffer is full or the source ends, and returns how many samples were written.</summary>
    /// <returns>The buffer's length, or less only when the source reached its end.</returns>
    public static int ReadBlock(this IAudioSource source, Span<float> buffer)
    {
        ArgumentNullException.ThrowIfNull(source);

        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = source.Read(buffer[filled..]);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }
}
