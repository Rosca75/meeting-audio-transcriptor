// Turns a transcriber's progress reports into run:progress events that never decrease and arrive at
// most about ten times a second.
using System;
using MeetingAudioTranscriptor.Contracts.Bridge;

namespace MeetingAudioTranscriptor.App.Bridge;

/// <summary>Filters progress reports on their way to the page.</summary>
/// <remarks>
/// A progress bar that jumps back looks broken, and a flood of events starves the page, so the
/// fraction is clamped to the highest value seen and reports closer than 100 ms are dropped unless
/// the stage changed or the work is complete. Emission happens under the forwarder's own lock so two
/// reporting threads cannot swap the order of two events on the wire.
/// </remarks>
internal sealed class ProgressForwarder : IProgress<TranscribeProgress>
{
    private static readonly TimeSpan MinimumGap = TimeSpan.FromMilliseconds(100);

    private readonly BridgeEmitter _emitter;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private double _highest;
    private string? _lastStage;
    private long _lastEmitTimestamp;
    private bool _closed;

    public ProgressForwarder(BridgeEmitter emitter, TimeProvider clock)
    {
        _emitter = emitter;
        _clock = clock;
    }

    /// <summary>Stops all further events; called before the terminal event is posted.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
        }
    }

    public void Report(TranscribeProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (double.IsNaN(value.Fraction))
        {
            return;
        }

        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            double fraction = Math.Clamp(Math.Max(value.Fraction, _highest), 0.0, 1.0);
            bool first = _lastStage is null;
            bool stageChanged = !first && !string.Equals(_lastStage, value.Stage, StringComparison.Ordinal);
            bool moved = fraction > _highest;
            _highest = fraction;

            long now = _clock.GetTimestamp();
            bool gapElapsed = _clock.GetElapsedTime(_lastEmitTimestamp, now) >= MinimumGap;
            bool complete = fraction >= 1.0 && moved;
            if (!first && !stageChanged && !complete && !(moved && gapElapsed))
            {
                return;
            }

            _lastStage = value.Stage;
            _lastEmitTimestamp = now;
            _emitter.Event(
                BridgeEvents.RunProgress,
                new TranscribeProgress { Stage = value.Stage, Fraction = fraction },
                BridgeJson.Context.TranscribeProgress);
        }
    }
}
