// Stand-ins for the service's models and files: an energy VAD graph, a scripted recogniser, WAV fixtures and the seams that wire them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Vad;

namespace MeetingAudioTranscriptor.App.Tests;

/// <summary>A Silero-shaped graph whose probability is high when a frame is loud, like a crude energy detector.</summary>
internal sealed class EnergyVadRunner : ITensorRunner
{
    public TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
    {
        TensorValue input = default!;
        TensorValue state = default!;
        foreach (TensorValue value in inputs)
        {
            if (value.Name == "input")
            {
                input = value;
            }
            else if (value.Name == "state")
            {
                state = value;
            }
        }

        int lanes = input.Shape[0];
        float[] probabilities = new float[lanes];
        for (int lane = 0; lane < lanes; lane++)
        {
            double sum = 0;
            for (int i = 64; i < 576; i++)
            {
                sum += Math.Abs(input.Floats![(lane * 576) + i]);
            }

            probabilities[lane] = sum / 512 > 0.05 ? 0.9f : 0.05f;
        }

        TensorValue[] results = new TensorValue[outputNames.Length];
        for (int i = 0; i < outputNames.Length; i++)
        {
            results[i] = outputNames[i] == "output"
                ? TensorValue.OfFloats("output", [lanes, 1], probabilities)
                : TensorValue.OfFloats("stateN", [2, lanes, 128], new float[state.Floats!.Length]);
        }

        return results;
    }

    public void Dispose()
    {
    }
}

/// <summary>Answers every chunk with the same words, and can be gated, failed or counted.</summary>
internal sealed class FakeRecognizer : ISpeechRecognizer
{
    private int _calls;

    public double MaxChunkSeconds => 30;

    /// <summary>When set, a call waits for it or for cancellation.</summary>
    public ManualResetEventSlim? Gate { get; init; }

    /// <summary>When set, every call throws it.</summary>
    public Func<Exception>? Failure { get; init; }

    public int Calls => Volatile.Read(ref _calls);

    public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation)
    {
        Interlocked.Increment(ref _calls);
        Gate?.Wait(cancellation);
        if (Failure is not null)
        {
            throw Failure();
        }

        return [new("Hello", 0.2, 0.5, 0.9f), new("world.", 0.6, 0.9, 0.9f)];
    }
}

/// <summary>A temporary folder deleted at the end of a test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mat-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string[] Names() => Array.ConvertAll(Directory.GetFileSystemEntries(Path), entry => System.IO.Path.GetFileName(entry));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is harmless.
        }
    }
}

/// <summary>Builds services over fakes and writes WAV fixtures.</summary>
internal static class ServiceKit
{
    /// <summary>A 16-bit mono WAV of silence with tone bursts at the given seconds (each burst 2 s).</summary>
    public static string WriteWav(string path, double seconds, params double[] burstStarts)
    {
        int rate = AudioFormat.ModelSampleRate;
        int count = (int)(seconds * rate);
        short[] samples = new short[count];
        foreach (double start in burstStarts)
        {
            int first = (int)(start * rate);
            for (int i = first; i < Math.Min(count, first + (2 * rate)); i++)
            {
                samples[i] = (short)(10000 * Math.Sin(i * 0.2));
            }
        }

        using FileStream stream = new(path, FileMode.Create);
        using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (count * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 2);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }

        return path;
    }

    /// <summary>The models a fake service loads.</summary>
    public static LoadedModels Models(ISpeechRecognizer recognizer, Func<ISpeakerSeparator?>? separators = null) =>
        new(
            new SileroVadModel(new EnergyVadRunner(), new SileroVadOptions { Lanes = 2, BlockSeconds = 8, WarmUpSeconds = 0.5 }),
            recognizer,
            separators,
            [new() { Role = "recognition", Name = "fake" }],
            []);

    /// <summary>A service over the real WAV opener and the given models; counts the loads in <paramref name="loads"/>.</summary>
    public static TranscriptionService Service(
        ISpeechRecognizer recognizer,
        StrongBox<int>? loads = null,
        IReadOnlyList<string>? missing = null,
        Func<string, (IAudioSource Source, string Decoder)>? open = null,
        Func<string, LoadedModels>? load = null)
    {
        ServiceSeams seams = ServiceSeams.Default with
        {
            OpenAudio = open ?? ServiceSeams.Default.OpenAudio,
            LoadModels = load ?? (_ =>
            {
                if (loads is not null)
                {
                    loads.Value++;
                }

                return Models(recognizer);
            }),
            MissingFiles = _ => missing ?? [],
            Options = options => options with { ProgressInterval = TimeSpan.Zero },
        };
        return new TranscriptionService(Path.GetTempPath(), seams);
    }
}

/// <summary>A mutable box, so a lambda can count into a test's variable.</summary>
internal sealed class StrongBox<T>
{
    public T Value { get; set; } = default!;
}
