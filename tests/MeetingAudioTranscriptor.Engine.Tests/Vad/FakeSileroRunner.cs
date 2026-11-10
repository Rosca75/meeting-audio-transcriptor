// A stand-in for the Silero VAD graph that answers from a rule the test chooses and records every call it receives.
using System;
using System.Collections.Generic;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;

namespace MeetingAudioTranscriptor.Engine.Tests.Vad;

/// <summary>One call the fake received: the batch size and copies of the input rows and the state.</summary>
internal sealed record FakeCall(int Lanes, float[] Input, float[] State)
{
    /// <summary>The 576 samples a lane read in this call.</summary>
    public ReadOnlySpan<float> Row(int lane) => Input.AsSpan(lane * 576, 576);

    /// <summary>The first state value of a lane, which the fake uses as a frame counter.</summary>
    public float Counter(int lane) => State[lane * 128];
}

/// <summary>Implements the graph's contract: input [B, 576], state [2, B, 128], sr; output [B, 1] and stateN.</summary>
/// <remarks>
/// The new state is the old state plus one everywhere, so a lane's first state value counts the
/// frames it has read since it started from an empty state. The probability is whatever the rule
/// computes from the lane's 576 samples and that counter.
/// </remarks>
internal sealed class FakeSileroRunner : ITensorRunner
{
    private readonly Func<float[], int, float, float> _rule;

    /// <summary>Creates the fake.</summary>
    /// <param name="rule">The probability from the input array, the lane's row offset in it, and the lane's counter.</param>
    public FakeSileroRunner(Func<float[], int, float, float> rule)
    {
        _rule = rule;
    }

    /// <summary>The probability is the first new sample of the frame, which identifies the frame.</summary>
    public static FakeSileroRunner FirstNewSample() => new((input, offset, _) => input[offset + 64]);

    /// <summary>The probability is the lane's frame counter, which shows where state came from.</summary>
    public static FakeSileroRunner StateCounter() => new((_, _, counter) => counter);

    /// <summary>The probability is high when the frame's mean magnitude exceeds 0.05, like a crude energy detector.</summary>
    public static FakeSileroRunner Energy() => new((input, offset, _) =>
    {
        double sum = 0;
        for (int i = 64; i < 576; i++)
        {
            sum += Math.Abs(input[offset + i]);
        }

        return sum / 512 > 0.05 ? 0.9f : 0.05f;
    });

    /// <summary>Every call in order.</summary>
    public List<FakeCall> Calls { get; } = [];

    /// <summary>Records the inputs only while true; tests over long audio turn it off.</summary>
    public bool Record { get; set; } = true;

    public TensorValue[] Run(ReadOnlySpan<TensorValue> inputs, ReadOnlySpan<string> outputNames, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        TensorValue input = Find(inputs, "input");
        TensorValue state = Find(inputs, "state");
        TensorValue rate = Find(inputs, "sr");
        if (rate.Int64s is not [16000L] || rate.Shape.Length != 0)
        {
            throw new ArgumentException("sr must be the int64 scalar 16000");
        }

        int lanes = input.Shape[0];
        if (input.Shape is not [_, 576] || state.Shape is not [2, _, 128] || state.Shape[1] != lanes)
        {
            throw new ArgumentException($"got input [{string.Join(", ", input.Shape)}] and state [{string.Join(", ", state.Shape)}], wanted [B, 576] and [2, B, 128]");
        }

        float[] samples = input.Floats!;
        float[] oldState = state.Floats!;
        if (Record)
        {
            // The state is stored lane-major per layer: [layer, lane, 128]; the copy keeps layer 0.
            Calls.Add(new FakeCall(lanes, (float[])samples.Clone(), oldState.AsSpan(0, lanes * 128).ToArray()));
        }

        float[] probabilities = new float[lanes];
        for (int lane = 0; lane < lanes; lane++)
        {
            probabilities[lane] = _rule(samples, lane * 576, oldState[lane * 128]);
        }

        float[] newState = new float[oldState.Length];
        for (int i = 0; i < newState.Length; i++)
        {
            newState[i] = oldState[i] + 1;
        }

        string[] names = outputNames.ToArray();
        TensorValue[] results = new TensorValue[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            results[i] = names[i] switch
            {
                "output" => TensorValue.OfFloats("output", [lanes, 1], probabilities),
                "stateN" => TensorValue.OfFloats("stateN", [2, lanes, 128], newState),
                _ => throw new ArgumentException($"no output {names[i]}"),
            };
        }

        return results;
    }

    public void Dispose()
    {
    }

    private static TensorValue Find(ReadOnlySpan<TensorValue> inputs, string name)
    {
        foreach (TensorValue value in inputs)
        {
            if (value.Name == name)
            {
                return value;
            }
        }

        throw new ArgumentException($"input {name} missing");
    }
}
