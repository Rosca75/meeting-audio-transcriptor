// One file's pass through the Silero VAD graph: buffers samples into blocks and runs each block as a batch of lanes.
using System;
using System.Collections.Generic;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Models;

namespace MeetingAudioTranscriptor.Engine.Vad;

/// <summary>Turns a stream of 16 kHz samples into one speech probability per 512-sample frame.</summary>
/// <remarks>
/// <para>
/// Samples are appended in blocks of any size, as a decoder delivers them, and probabilities come
/// out a block at a time (<see cref="SileroVadOptions.BlockSeconds"/>), always in frame order.
/// <see cref="Flush"/> processes what is left, padding the last partial frame with zeros, so a stream
/// of n samples yields exactly ceil(n / 512) probabilities.
/// </para>
/// <para>
/// A block of N frames is split into L lanes run together as one batch. Lane 0 continues the
/// recurrent state the previous block ended with and reads the first F + W + r frames; each other
/// lane reads W warm-up frames (whose answers are discarded) followed by its own F frames, starting
/// from an empty state, where N = L F + W + r and r &lt; L. Every lane reads the true 64 samples
/// preceding each frame, so only the state differs from a frame-by-frame run. The state carried to
/// the next block is the last lane's state after its last frame.
/// </para>
/// <para>
/// With <see cref="SileroVadOptions.SkipDigitalSilence"/>, a run of digital silence (exact zeros, as a
/// recorder pads a file with) at least <see cref="SileroVadOptions.DigitalSilenceSeconds"/> long is
/// given probability 0 without a call, and the audio on each side of it is laid out in lanes as a
/// block of its own; the audio after the run starts from an empty state, as a stream does.
/// </para>
/// <para>
/// A stream is used by one thread at a time; the arrays handed to the runner are reused from call
/// to call, so the only allocations per call are the ones the runner makes for its outputs.
/// </para>
/// </remarks>
public sealed class SileroVadSession
{
    private const int Window = SileroVadModel.ContextSamples + SileroVadModel.FrameSamples;
    private const int LaneState = SileroVadModel.StateWidth;

    private readonly ITensorRunner _runner;
    private readonly SileroVadOptions _options;
    private readonly int _blockFrames;
    private readonly int _warmUpFrames;

    /// <summary>The 64 context samples followed by up to one block of samples.</summary>
    private readonly float[] _block;

    /// <summary>The probabilities of the block being processed.</summary>
    private readonly float[] _blockProbabilities;

    /// <summary>The state lane 0 starts the next block with: 2 layers of 128 values.</summary>
    private readonly float[] _carriedState = new float[2 * LaneState];

    /// <summary>The batch buffers per lane count, created on first use (full blocks use one, a short last block another).</summary>
    private readonly BatchBuffers?[] _batches;

    private readonly TensorValue _sampleRate = TensorValue.OfInt64s("sr", [], [16000L]);
    private readonly TensorValue[] _inputs = new TensorValue[3];

    /// <summary>The samples of the current block held after the context.</summary>
    private int _filled;
    private bool _flushed;

    internal SileroVadSession(SileroVadModel model)
    {
        _runner = model.Runner;
        _options = model.Options;
        _blockFrames = _options.BlockFrames;
        _warmUpFrames = _options.WarmUpFrames;
        _block = new float[SileroVadModel.ContextSamples + (_blockFrames * SileroVadModel.FrameSamples)];
        _blockProbabilities = new float[_blockFrames];
        _batches = new BatchBuffers?[_options.Lanes + 1];
    }

    /// <summary>The samples appended so far.</summary>
    public long SamplesAppended { get; private set; }

    /// <summary>The probabilities written so far; frame k's probability is the k-th.</summary>
    public long FramesEmitted { get; private set; }

    /// <summary>The graph calls made so far, for measurements.</summary>
    public long ModelCalls { get; private set; }

    /// <summary>The frames given probability 0 without a call because they were digital silence.</summary>
    public long SilentFramesSkipped { get; private set; }

    /// <summary>Appends samples and writes the probabilities of every block they complete.</summary>
    /// <param name="samples">The next samples of the stream, 16 kHz mono in the range -1 to 1.</param>
    /// <param name="probabilities">Receives the new probabilities, appended in frame order.</param>
    /// <param name="cancellation">Stops the graph between calls and inside a call.</param>
    public void Append(ReadOnlySpan<float> samples, List<float> probabilities, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        ThrowIfFlushed();
        int blockSamples = _blockFrames * SileroVadModel.FrameSamples;
        while (!samples.IsEmpty)
        {
            int take = Math.Min(samples.Length, blockSamples - _filled);
            samples[..take].CopyTo(_block.AsSpan(SileroVadModel.ContextSamples + _filled));
            _filled += take;
            SamplesAppended += take;
            samples = samples[take..];
            if (_filled == blockSamples)
            {
                ProcessBlock(_blockFrames, probabilities, cancellation);
            }
        }
    }

    /// <summary>Processes the samples still held, padding the last partial frame with zeros; the stream then ends.</summary>
    /// <param name="probabilities">Receives the remaining probabilities.</param>
    /// <param name="cancellation">Stops the graph between calls and inside a call.</param>
    public void Flush(List<float> probabilities, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        ThrowIfFlushed();
        _flushed = true;
        if (_filled == 0)
        {
            return;
        }

        int frames = (_filled + SileroVadModel.FrameSamples - 1) / SileroVadModel.FrameSamples;
        int end = SileroVadModel.ContextSamples + (frames * SileroVadModel.FrameSamples);
        Array.Clear(_block, SileroVadModel.ContextSamples + _filled, end - SileroVadModel.ContextSamples - _filled);
        ProcessBlock(frames, probabilities, cancellation);
    }

    private void ThrowIfFlushed()
    {
        if (_flushed)
        {
            throw new InvalidOperationException("the VAD stream was already flushed; create a new stream with SileroVadModel.Create for the next file.");
        }
    }

    /// <summary>Computes the probabilities of the first <paramref name="frames"/> frames of the block and starts the next block.</summary>
    private void ProcessBlock(int frames, List<float> probabilities, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Span<float> output = _blockProbabilities.AsSpan(0, frames);

        // The block is processed as spans: runs of digital silence at least the minimum long are
        // given probability 0 without a call, and the audio between them is run lane by lane.
        int spanStart = 0;
        int frame = 0;
        int minimumRun = _options.SkipDigitalSilence ? Math.Max(1, _options.DigitalSilenceFrames) : int.MaxValue;
        while (frame < frames)
        {
            int run = _options.SkipDigitalSilence ? SilentRunLength(frame, frames) : 0;
            if (run < minimumRun)
            {
                frame += Math.Max(1, run);
                continue;
            }

            if (frame > spanStart)
            {
                RunLanes(spanStart, frame - spanStart, output, cancellation);
            }

            // Nothing to hear: probability 0, and the state a stream starts with, so the audio
            // after the silence begins exactly as a lane does.
            output.Slice(frame, run).Clear();
            Array.Clear(_carriedState);
            SilentFramesSkipped += run;
            frame += run;
            spanStart = frame;
        }

        if (frames > spanStart)
        {
            RunLanes(spanStart, frames - spanStart, output, cancellation);
        }

        probabilities.AddRange(output);
        FramesEmitted += frames;

        // The last 64 samples of this block are the context of the next block's first frame.
        int blockEnd = SileroVadModel.ContextSamples + (frames * SileroVadModel.FrameSamples);
        Array.Copy(_block, blockEnd - SileroVadModel.ContextSamples, _block, 0, SileroVadModel.ContextSamples);
        _filled = 0;
    }

    /// <summary>The number of consecutive frames from <paramref name="first"/> whose every held sample is below the digital-silence level.</summary>
    private int SilentRunLength(int first, int frames)
    {
        float level = _options.DigitalSilenceLevel;
        int heldEnd = SileroVadModel.ContextSamples + _filled;
        int run = 0;
        for (int frame = first; frame < frames; frame++)
        {
            int start = SileroVadModel.ContextSamples + (frame * SileroVadModel.FrameSamples);
            int end = Math.Min(heldEnd, start + SileroVadModel.FrameSamples);
            foreach (float sample in _block.AsSpan(start, end - start))
            {
                if (sample >= level || sample <= -level)
                {
                    return run;
                }
            }

            run++;
        }

        return run;
    }

    /// <summary>Runs the lanes over the frames [<paramref name="first"/>, <paramref name="first"/> + <paramref name="frames"/>) of the block, continuing the carried state.</summary>
    private void RunLanes(int first, int frames, Span<float> blockOutput, CancellationToken cancellation)
    {
        Span<float> output = blockOutput.Slice(first, frames);
        LanePlan plan = LanePlan.For(frames, _options.Lanes, _warmUpFrames);
        BatchBuffers batch = _batches[plan.Lanes] ??= new BatchBuffers(plan.Lanes);
        int lanes = plan.Lanes;

        // Lane 0 continues the stream; every other lane starts empty.
        Array.Clear(batch.State);
        for (int layer = 0; layer < 2; layer++)
        {
            Array.Copy(_carriedState, layer * LaneState, batch.State, layer * lanes * LaneState, LaneState);
        }

        _inputs[0] = batch.Input;
        _inputs[1] = batch.StateTensor;
        _inputs[2] = _sampleRate;
        int lastLane = lanes - 1;
        int lastLaneSteps = plan.LaneSteps(lastLane);
        for (int step = 0; step < plan.Steps; step++)
        {
            for (int lane = 0; lane < lanes; lane++)
            {
                // A lane that has read all its frames idles until lane 0 finishes; its row keeps the
                // previous samples and its answers are ignored.
                if (step < plan.LaneSteps(lane))
                {
                    int frame = first + plan.FirstFrame(lane) + step;
                    Array.Copy(_block, frame * SileroVadModel.FrameSamples, batch.Samples, lane * Window, Window);
                }
            }

            TensorValue[] results = _runner.Run(_inputs, SileroVadModel.OutputNames, cancellation);
            ModelCalls++;
            float[] probability = results[0].Floats!;
            float[] stateN = results[1].Floats!;
            Array.Copy(stateN, batch.State, batch.State.Length);

            for (int lane = 0; lane < lanes; lane++)
            {
                int ownStep = step - plan.WarmUpSteps(lane);
                if (ownStep >= 0 && step < plan.LaneSteps(lane))
                {
                    output[plan.FirstOwnFrame(lane) + ownStep] = probability[lane];
                }
            }

            if (step == lastLaneSteps - 1)
            {
                // The last lane has just read the block's last frame: its state starts the next block.
                for (int layer = 0; layer < 2; layer++)
                {
                    Array.Copy(batch.State, (layer * lanes * LaneState) + (lastLane * LaneState), _carriedState, layer * LaneState, LaneState);
                }
            }
        }
    }

    /// <summary>The arrays and tensors of one batch size, reused for every call of that size.</summary>
    private sealed class BatchBuffers
    {
        public BatchBuffers(int lanes)
        {
            Samples = new float[lanes * Window];
            State = new float[2 * lanes * LaneState];
            Input = TensorValue.OfFloats("input", [lanes, Window], Samples);
            StateTensor = TensorValue.OfFloats("state", [2, lanes, LaneState], State);
        }

        public float[] Samples { get; }

        public float[] State { get; }

        public TensorValue Input { get; }

        public TensorValue StateTensor { get; }
    }
}

/// <summary>Where each lane of one block starts and how long it runs.</summary>
/// <param name="Frames">The frames of the block.</param>
/// <param name="Lanes">The lanes actually used, at most the configured count.</param>
/// <param name="OwnFrames">F: the frames each lane but the first answers for.</param>
/// <param name="WarmUp">W: the warm-up frames of each lane but the first.</param>
internal readonly record struct LanePlan(int Frames, int Lanes, int OwnFrames, int WarmUp)
{
    /// <summary>Lays out a block: as many lanes as the configuration allows while each lane's own part is at least as long as its warm-up.</summary>
    public static LanePlan For(int frames, int maxLanes, int warmUp)
    {
        int lanes = 1;
        if (maxLanes > 1)
        {
            // Each extra lane costs W warm-up frames, so a lane shorter than its warm-up would spend
            // more on settling than on answering; a short last block uses fewer lanes.
            int byLength = warmUp == 0 ? frames : (frames - warmUp) / warmUp;
            lanes = Math.Clamp(byLength, 1, maxLanes);
            lanes = Math.Min(lanes, Math.Max(1, frames - warmUp));
        }

        int own = lanes == 1 ? frames : (frames - warmUp) / lanes;
        return new LanePlan(frames, lanes, own, lanes == 1 ? 0 : warmUp);
    }

    /// <summary>The calls the block needs: lane 0's length.</summary>
    public int Steps => Frames - ((Lanes - 1) * OwnFrames);

    /// <summary>The frames a lane reads, warm-up included.</summary>
    public int LaneSteps(int lane) => lane == 0 ? Steps : WarmUp + OwnFrames;

    /// <summary>The warm-up frames of a lane.</summary>
    public int WarmUpSteps(int lane) => lane == 0 ? 0 : WarmUp;

    /// <summary>The first frame a lane answers for.</summary>
    public int FirstOwnFrame(int lane) => lane == 0 ? 0 : Steps + ((lane - 1) * OwnFrames);

    /// <summary>The first frame a lane reads, its warm-up included.</summary>
    public int FirstFrame(int lane) => FirstOwnFrame(lane) - WarmUpSteps(lane);
}
