// Every setting of speaker separation in one place: whether it runs, how speech is cut into embedding
// windows, how the windows are clustered and how the timeline is smoothed.
using System;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>How clusters are compared while they are merged.</summary>
public enum SpeakerLinkage
{
    /// <summary>The mean cosine distance over every pair of windows of the two clusters.</summary>
    Average,

    /// <summary>The largest cosine distance between a window of one cluster and a window of the other.</summary>
    Complete,
}

/// <summary>The settings of speaker separation.</summary>
/// <remarks>
/// The defaults are the values measured best on the benchmark set, with round values preferred to
/// the exact optimum so a dozen files do not tune the product: 3 s windows every 2 s (longer windows
/// give steadier voice prints, and the hop sets the cost), average linkage, a cosine-distance
/// threshold of 0.5 and no refinement pass. When <see cref="Enabled"/> is false the
/// caller creates no <see cref="DiarisationCollector"/> at all, so no embedding is computed and
/// the transcript carries no speaker.
/// </remarks>
public sealed record DiarisationOptions
{
    /// <summary>Whether speakers are separated. Off means nothing in this namespace runs.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The number of speakers the user says took part, or null to let the threshold decide.</summary>
    public int? SpeakerCountHint { get; init; }

    /// <summary>The most speakers a transcript names; more clusters are merged pairwise, closest first.</summary>
    public int MaxSpeakers { get; init; } = 10;

    /// <summary>The length of one embedding window in seconds.</summary>
    public double WindowSeconds { get; init; } = 3.0;

    /// <summary>The step between consecutive windows of one speech region in seconds.</summary>
    public double HopSeconds { get; init; } = 2.0;

    /// <summary>A speech region shorter than the window but at least this long gets one window of its own length; a shorter one gets none.</summary>
    public double MinimumWindowSeconds { get; init; } = 0.4;

    /// <summary>How many equal-length windows go through the model in one call.</summary>
    public int BatchSize { get; init; } = 4;

    /// <summary>How clusters are compared while merging.</summary>
    public SpeakerLinkage Linkage { get; init; } = SpeakerLinkage.Average;

    /// <summary>The cosine distance above which two clusters are never merged when no speaker count is given.</summary>
    public double Threshold { get; init; } = 0.5;

    /// <summary>A cluster holding less speech than this, in seconds of window hops, is folded into the nearest larger one.</summary>
    public double MinimumSpeakerSeconds { get; init; } = 4.0;

    /// <summary>How many times every window is re-assigned to its nearest cluster centroid after clustering.</summary>
    public int RefinementPasses { get; init; }

    /// <summary>Above this many windows the clustering runs in two stages (see <see cref="SpeakerClusterer"/>).</summary>
    public int MaxDirectWindows { get; init; } = 2000;

    /// <summary>The shortest speaker turn the timeline keeps inside continuous speech, in seconds.</summary>
    public double MinimumTurnSeconds { get; init; } = 0.5;

    /// <summary>A turn at least this long, in seconds, is trusted over the speaker of the words around it.</summary>
    public double StrongTurnSeconds { get; init; } = 1.0;

    /// <summary>Checks every value and names the one that is out of range.</summary>
    internal void Validate()
    {
        if (SpeakerCountHint is int hint && (hint < 1 || hint > 100))
        {
            throw new ArgumentOutOfRangeException(nameof(SpeakerCountHint), hint, "the speaker count hint must be between 1 and 100; leave it empty to let the application decide");
        }

        Require(MaxSpeakers >= 1, nameof(MaxSpeakers), "at least 1");
        Require(WindowSeconds >= 0.5 && WindowSeconds <= 10, nameof(WindowSeconds), "between 0.5 and 10 seconds");
        Require(HopSeconds > 0 && HopSeconds <= WindowSeconds, nameof(HopSeconds), "above 0 and at most the window length");
        Require(MinimumWindowSeconds >= 0.1 && MinimumWindowSeconds <= WindowSeconds, nameof(MinimumWindowSeconds), "between 0.1 seconds and the window length");
        Require(BatchSize >= 1 && BatchSize <= 64, nameof(BatchSize), "between 1 and 64");
        Require(Threshold > 0 && Threshold < 2, nameof(Threshold), "a cosine distance between 0 and 2");
        Require(MinimumSpeakerSeconds >= 0, nameof(MinimumSpeakerSeconds), "zero or more");
        Require(RefinementPasses >= 0 && RefinementPasses <= 20, nameof(RefinementPasses), "between 0 and 20");
        Require(MaxDirectWindows >= 100 && MaxDirectWindows <= 8000, nameof(MaxDirectWindows), "between 100 and 8000");
        Require(MinimumTurnSeconds >= 0, nameof(MinimumTurnSeconds), "zero or more");
        Require(StrongTurnSeconds >= 0, nameof(StrongTurnSeconds), "zero or more");
    }

    private static void Require(bool condition, string name, string wanted)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, $"the diarisation option {name} is out of range; it must be {wanted}");
        }
    }
}
