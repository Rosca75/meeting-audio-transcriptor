// Measures a deep-tier benchmark both by the wall clock and by the processor time the process spent,
// because on a shared machine the wall clock also counts the time other programs held the core.
using System;
using System.Diagnostics;

namespace MeetingAudioTranscriptor.Engine.Tests.Audio;

/// <summary>Times one single-threaded measurement in wall-clock and processor seconds.</summary>
/// <remarks>
/// The throughput tests share one collection so they never run beside each other, which keeps the
/// process's processor time the measured work alone. The processor time is what the assertions read.
/// </remarks>
internal sealed class ThroughputClock
{
    /// <summary>The collection every throughput test joins, so they run one at a time.</summary>
    public const string Collection = "throughput";

    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly TimeSpan _processorAtStart = Process.GetCurrentProcess().TotalProcessorTime;

    public double WallSeconds { get; private set; }

    public double ProcessorSeconds { get; private set; }

    public void Stop()
    {
        _wall.Stop();
        WallSeconds = _wall.Elapsed.TotalSeconds;
        ProcessorSeconds = (Process.GetCurrentProcess().TotalProcessorTime - _processorAtStart).TotalSeconds;
    }

    public override string ToString() => $"{ProcessorSeconds:F2} s of processor time ({WallSeconds:F2} s wall clock)";
}
