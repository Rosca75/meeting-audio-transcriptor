// The pipeline's speaker separator over the diarisation stages: embeddings collected while the
// recording is recognised, clustered once at the end, and one speaker per word numbered by first appearance.
using System;
using System.Collections.Generic;
using System.Threading;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>Adapts <see cref="DiarisationCollector"/> and <see cref="SpeakerTimeline"/> to <see cref="ISpeakerSeparator"/>.</summary>
/// <remarks>
/// <para>
/// One instance serves one recording, because the collector holds that recording's windows; the
/// embedder under it (and the loaded model) is shared by every run. The recognition workers hand it
/// their chunks from several threads at once; the collector computes the embeddings on the calling
/// thread and sorts the windows by time before clustering, so the answer does not depend on the
/// thread count.
/// </para>
/// <para>
/// The words are assigned through the timeline, then renumbered in the order their speakers first
/// speak a word: the timeline numbers speakers by their first turn, and a turn may hold no word
/// (a cough, a backchannel the recogniser skipped), so only the words' own order guarantees that the
/// transcript's first speaker is Speaker 1 and that no speaker is listed without a word. When no
/// window could be embedded (every speech region shorter than the shortest window), every word goes
/// to one speaker, since nothing tells two voices apart.
/// </para>
/// </remarks>
public sealed class DiarisationSpeakerSeparator : ISpeakerSeparator
{
    private readonly DiarisationCollector _collector;
    private readonly DiarisationOptions _options;
    private int _finished;

    /// <summary>Creates the separator of one run over a shared embedder.</summary>
    /// <param name="embedder">The speaker embedding model wrapper, shared by every run and worker.</param>
    /// <param name="options">The diarisation settings; null for the defaults. The hint given to <see cref="Finish"/> overrides its own.</param>
    public DiarisationSpeakerSeparator(SpeakerEmbedder embedder, DiarisationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        _options = options ?? new DiarisationOptions();
        _collector = new DiarisationCollector(embedder, _options);
    }

    /// <summary>The windows embedded so far.</summary>
    public int WindowCount => _collector.WindowCount;

    /// <summary>The timeline the last <see cref="Finish"/> built; null before it.</summary>
    public SpeakerTimeline? Timeline { get; private set; }

    /// <inheritdoc />
    public void AddBlock(ReadOnlySpan<float> samples, long startSample, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        _collector.AddBlock(samples, startSample, speechRegions, cancellation);
    }

    /// <inheritdoc />
    public SpeakerAssignment Finish(IReadOnlyList<RecognizedWord> words, int? speakerCountHint, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (Interlocked.Exchange(ref _finished, 1) != 0)
        {
            throw new InvalidOperationException("the speaker separator of a run was finished twice; create one separator per recording");
        }

        DiarisationResult result = _collector.Finish(_options with { SpeakerCountHint = speakerCountHint ?? _options.SpeakerCountHint }, cancellation);
        Timeline = result.Timeline;
        int[] timelineSpeakers = result.Timeline.AssignWords(words);
        double[] timelineSeconds = result.Timeline.SpeakingSeconds();
        return Renumber(timelineSpeakers, timelineSeconds);
    }

    /// <summary>
    /// Numbers the speakers 0, 1, 2 ... in the order they first speak a word, and keeps the speaking
    /// time of each speaker who has a word. The timeline gives -1 to every word only when it holds no
    /// turn at all, and those words then make up one speaker.
    /// </summary>
    internal static SpeakerAssignment Renumber(IReadOnlyList<int> timelineSpeakers, IReadOnlyList<double> timelineSeconds)
    {
        Dictionary<int, int> numberOf = [];
        int[] speakers = new int[timelineSpeakers.Count];
        List<double> seconds = [];
        for (int i = 0; i < speakers.Length; i++)
        {
            int original = Math.Max(timelineSpeakers[i], -1);
            if (!numberOf.TryGetValue(original, out int number))
            {
                number = numberOf.Count;
                numberOf[original] = number;
                seconds.Add(original >= 0 && original < timelineSeconds.Count ? timelineSeconds[original] : 0);
            }

            speakers[i] = number;
        }

        return new SpeakerAssignment(speakers, seconds);
    }
}
