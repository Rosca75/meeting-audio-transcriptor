// The settings of one loaded model session, so every caller states its threading choice in one place.
using Microsoft.ML.OnnxRuntime;

namespace MeetingAudioTranscriptor.Inference;

/// <summary>How hard one session works: its thread count, spinning and graph optimisation.</summary>
/// <remarks>
/// Recognition shares one loaded session between several worker threads, and each worker calls
/// <c>Run</c> on it. The defaults are chosen for that shape: one intra-op thread per call, so the
/// workers (not the session) decide how many cores are busy, and no spinning, so an idle session
/// does not hold a core.
/// </remarks>
/// <param name="IntraOpThreads">
/// Threads one <c>Run</c> may use inside an operator. One by default, because the caller runs
/// several workers and the sum, not each call, should match the core count; the ASR graphs also
/// scale poorly past two or three threads.
/// </param>
/// <param name="AllowSpinning">
/// Whether the session's threads busy-wait for work between operators. Off by default: with several
/// workers each calling a one-thread session, spinning wastes cores that another worker could use.
/// Turn it on for a single latency-bound caller.
/// </param>
/// <param name="GraphOptimization">
/// How much the runtime rewrites the graph at load. All by default, because the speed gain is free
/// at run time and costs only load time.
/// </param>
/// <param name="OptimizedModelCachePath">
/// Where the runtime writes the optimised graph so a later load skips the optimisation; null (the
/// default) writes nothing, because the file is specific to the machine's CPU and a shipped
/// application must not leave a stale one behind.
/// </param>
public sealed record RunnerOptions(
    int IntraOpThreads = 1,
    bool AllowSpinning = false,
    GraphOptimizationLevel GraphOptimization = GraphOptimizationLevel.ORT_ENABLE_ALL,
    string? OptimizedModelCachePath = null);
