// Gathers speaker embeddings while the recording is recognised block by block, and turns them into a
// speaker timeline once the whole recording has been read.
using System;
using System.Collections.Generic;
using System.Threading;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>The outcome of speaker separation: the timeline and what it was built from.</summary>
/// <param name="Timeline">The speaker turns, the speaking times and the word assignment.</param>
/// <param name="WindowCount">How many embedding windows the clustering read.</param>
public sealed record DiarisationResult(SpeakerTimeline Timeline, int WindowCount);

/// <summary>Collects speaker windows from the recognition workers and clusters them at the end.</summary>
/// <remarks>
/// <para>
/// The recognition workers call <see cref="AddBlock"/> from several threads at once, each with the
/// block of audio it is recognising and the speech regions voice activity found in it; the
/// embeddings are computed on the calling thread, and only the finished windows are appended under
/// the lock. Audio is never kept: the state is one embedding per window (2 KB for CAM++, about
/// 20 MB for a two-hour meeting) and the speech regions.
/// </para>
/// <para>
/// A speech region that crosses a block boundary arrives as two regions, one per block; each is cut
/// into windows on its own. The caller creates no collector when separation is switched off.
/// </para>
/// </remarks>
public sealed class DiarisationCollector
{
    private readonly SpeakerEmbedder _embedder;
    private readonly DiarisationOptions _options;
    private readonly int _sampleRate;
    private readonly Lock _lock = new();
    private readonly List<SpeakerWindow> _windows = [];
    private readonly List<(long Start, long End)> _regions = [];

    /// <summary>Creates a collector over a shared embedder.</summary>
    /// <param name="embedder">The embedding model wrapper, shared by every worker.</param>
    /// <param name="options">The window, clustering and timeline settings; <see cref="DiarisationOptions.Enabled"/> must be true.</param>
    /// <param name="sampleRate">The sample rate of the audio blocks; the embedding model needs 16000.</param>
    public DiarisationCollector(SpeakerEmbedder embedder, DiarisationOptions options, int sampleRate = KaldiFbank.SampleRate)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!options.Enabled)
        {
            throw new ArgumentException("speaker separation is switched off in the options; create no collector when it is off", nameof(options));
        }

        if (sampleRate != KaldiFbank.SampleRate)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, $"the speaker embedding model reads {KaldiFbank.SampleRate} Hz audio; resample the blocks before passing them");
        }

        _embedder = embedder;
        _options = options;
        _sampleRate = sampleRate;
    }

    /// <summary>The number of windows collected so far.</summary>
    public int WindowCount
    {
        get
        {
            lock (_lock)
            {
                return _windows.Count;
            }
        }
    }

    /// <summary>Computes the speaker windows of one block of audio and keeps them. Safe to call from several threads.</summary>
    /// <param name="samples">The block's samples in [-1, 1].</param>
    /// <param name="startSample">The position of the block's first sample in the recording.</param>
    /// <param name="speechRegions">The speech regions of the block, as (start, end) samples relative to the block, end exclusive.</param>
    /// <param name="cancellation">Stops between model calls.</param>
    public void AddBlock(ReadOnlySpan<float> samples, long startSample, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(speechRegions);
        int windowSamples = (int)Math.Round(_options.WindowSeconds * _sampleRate);
        int hopSamples = (int)Math.Round(_options.HopSeconds * _sampleRate);
        int minimumSamples = (int)Math.Round(_options.MinimumWindowSeconds * _sampleRate);

        List<SpeakerWindow> found = [];
        List<(long Start, long End)> regions = [];
        foreach ((int start, int end) in speechRegions)
        {
            if (start < 0 || end > samples.Length || end < start)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(speechRegions),
                    $"the speech region {start}..{end} lies outside the block of {samples.Length} samples; pass regions relative to the block's first sample");
            }

            if (end == start)
            {
                continue;
            }

            regions.Add((startSample + start, startSample + end));
            List<(int Start, int Length)> windows = SpeakerWindowCutter.Cut(end - start, windowSamples, hopSamples, minimumSamples);
            if (windows.Count == 0)
            {
                continue;
            }

            float[][] embeddings = new float[windows.Count][];
            _embedder.EmbedRegion(samples[start..end], windows, embeddings, cancellation);
            for (int w = 0; w < windows.Count; w++)
            {
                long windowStart = startSample + start + windows[w].Start;
                found.Add(new SpeakerWindow(windowStart, windowStart + windows[w].Length, embeddings[w]));
            }
        }

        lock (_lock)
        {
            _windows.AddRange(found);
            _regions.AddRange(regions);
        }
    }

    /// <summary>Clusters every window collected and builds the timeline.</summary>
    /// <param name="options">Clustering and timeline settings for this pass; null uses the collector's own. Lets one set of embeddings be clustered several ways.</param>
    /// <param name="cancellation">Stops the clustering.</param>
    public DiarisationResult Finish(DiarisationOptions? options, CancellationToken cancellation)
    {
        DiarisationOptions settings = options ?? _options;
        settings.Validate();
        SpeakerWindow[] windows;
        (long Start, long End)[] regions;
        lock (_lock)
        {
            windows = [.. _windows];
            regions = [.. _regions];
        }

        // Blocks arrive in any order; the clustering's blocks and tie-breaks read windows in time order.
        Array.Sort(windows, static (a, b) => a.StartSample != b.StartSample ? a.StartSample.CompareTo(b.StartSample) : a.EndSample.CompareTo(b.EndSample));
        float[][] embeddings = new float[windows.Length][];
        for (int i = 0; i < windows.Length; i++)
        {
            embeddings[i] = windows[i].Embedding;
        }

        int[] labels = SpeakerClusterer.Cluster(embeddings, settings, cancellation);
        SpeakerTimeline timeline = SpeakerTimeline.Build(windows, labels, regions, _sampleRate, settings.MinimumTurnSeconds, settings.StrongTurnSeconds);
        return new DiarisationResult(timeline, windows.Length);
    }
}
