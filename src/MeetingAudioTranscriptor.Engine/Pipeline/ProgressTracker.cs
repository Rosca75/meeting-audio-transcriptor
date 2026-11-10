// Turns per-stage progress into one overall fraction that never decreases, and throttles how often it is reported.
using System;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>The one place the pipeline's overall fraction is computed.</summary>
/// <remarks>
/// Each stage owns a fixed share of the bar: recognition dominates the run's time, so it holds most
/// of it. A run without speaker separation skips that share in one step. Several threads report at
/// once, so the tracker holds a lock and reports inside it, which keeps the reports in order and the
/// fraction non-decreasing as the receiver sees it.
/// </remarks>
internal sealed class ProgressTracker
{
    /// <summary>Where the transcribing share ends.</summary>
    internal const double TranscribingEnd = 0.92;

    /// <summary>Where the speaker share ends and cleaning starts.</summary>
    internal const double SpeakersEnd = 0.97;

    private readonly Lock _lock = new();
    private readonly IProgress<PipelineProgress>? _sink;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private double _last;
    private TranscriptionStage? _lastStage;
    private long _lastTimestamp;

    public ProgressTracker(IProgress<PipelineProgress>? sink, TimeProvider time, TimeSpan interval)
    {
        _sink = sink;
        _time = time;
        _interval = interval;
    }

    /// <summary>Reports a stage's fraction; within one stage, at most one report per interval unless forced.</summary>
    public void Report(TranscriptionStage stage, double stageFraction, bool force = false)
    {
        if (_sink is null)
        {
            return;
        }

        double clamped = double.IsFinite(stageFraction) ? Math.Clamp(stageFraction, 0, 1) : 0;
        lock (_lock)
        {
            long now = _time.GetTimestamp();
            bool sameStage = _lastStage == stage;
            if (!force && sameStage && _time.GetElapsedTime(_lastTimestamp, now) < _interval)
            {
                return;
            }

            double overall = Math.Max(_last, Overall(stage, clamped));
            _last = overall;
            _lastStage = stage;
            _lastTimestamp = now;
            _sink.Report(new PipelineProgress(stage, clamped, overall));
        }
    }

    /// <summary>The overall fraction of a stage's fraction.</summary>
    internal static double Overall(TranscriptionStage stage, double stageFraction) => stage switch
    {
        TranscriptionStage.Transcribing => stageFraction * TranscribingEnd,
        TranscriptionStage.IdentifyingSpeakers => TranscribingEnd + (stageFraction * (SpeakersEnd - TranscribingEnd)),
        TranscriptionStage.Cleaning => stageFraction >= 1 ? 1.0 : SpeakersEnd + (stageFraction * (1 - SpeakersEnd)),
        _ => 0,
    };
}

/// <summary>An <see cref="IProgress{T}"/> that calls back on the reporting thread.</summary>
/// <remarks>The framework's Progress posts to a synchronisation context or the thread pool, which would reorder reports and outlive the run.</remarks>
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _callback;

    public InlineProgress(Action<T> callback)
    {
        _callback = callback;
    }

    public void Report(T value) => _callback(value);
}
