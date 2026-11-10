// The speaker floor: after the words are assigned, a speaker who barely speaks is folded into the
// acoustically nearest other speaker, so the transcript names no voice from a few stray seconds.
using System;
using System.Collections.Generic;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Diarisation;

/// <summary>Folds the speakers below the speaking-time floor into their nearest neighbour.</summary>
/// <remarks>
/// <para>
/// The clusterer's own minimum size judges clusters by their windows, before anything is known about
/// the words. A cluster that passes it can still end up with a sentence or two once the timeline has
/// voted and the words have been placed, and a speaker named for one sentence is the most visible
/// error of a transcript's labels. So the floor is applied to what the transcript will show: each
/// speaker's speaking time in the timeline, counting only speakers who hold a word (a speaker with
/// no word has none).
/// </para>
/// <para>
/// While some speaker is below the floor, the one speaking least (the lower cluster label on a tie)
/// has every window moved to the cluster whose centroid is most similar to its own (cosine of the
/// mean embeddings, the lower label on a tie), preferring a cluster at or above the floor; then the
/// timeline is built and the words assigned again. A speaker is never dropped: its words go to the
/// voice they sound most like. The floor is the larger of an absolute time and a share of the total,
/// so it grows with a long recording, where stray windows add up to more seconds, and stays small on
/// a short clip, where a real participant may say only a few sentences. It never applies with a
/// speaker-count hint, because the user's count overrides the floor, and it stops at one speaker.
/// </para>
/// </remarks>
internal static class SpeakerFloor
{
    /// <summary>The floor in seconds for a recording whose speakers speak <paramref name="totalSeconds"/> in all.</summary>
    public static double Seconds(DiarisationOptions options, double totalSeconds)
    {
        return Math.Max(options.SpeakerFloorSeconds, options.SpeakerFloorShare * totalSeconds);
    }

    /// <summary>Folds the speakers below the floor and returns the final labels, timeline and word speakers.</summary>
    /// <param name="windows">The windows in time order.</param>
    /// <param name="labels">The cluster of each window.</param>
    /// <param name="regions">The speech regions in samples.</param>
    /// <param name="sampleRate">Samples per second.</param>
    /// <param name="options">The floor and the timeline settings; with a hint no speaker is folded.</param>
    /// <param name="words">The recognised words in time order.</param>
    public static (int[] Labels, SpeakerTimeline Timeline, int[] SpeakerOfWord) Apply(
        IReadOnlyList<SpeakerWindow> windows,
        int[] labels,
        IReadOnlyList<(long Start, long End)> regions,
        int sampleRate,
        DiarisationOptions options,
        IReadOnlyList<RecognizedWord> words)
    {
        int[] current = labels;
        while (true)
        {
            SpeakerTimeline timeline = SpeakerTimeline.Build(windows, current, regions, sampleRate, options.MinimumTurnSeconds, options.StrongTurnSeconds);
            int[] speakers = timeline.AssignWords(words);
            if (options.SpeakerCountHint is not null || (options.SpeakerFloorSeconds <= 0 && options.SpeakerFloorShare <= 0))
            {
                return (current, timeline, speakers);
            }

            // The speaking time of each cluster that holds a word.
            int clusters = 0;
            foreach (int label in current)
            {
                clusters = Math.Max(clusters, label + 1);
            }

            bool[] hasWord = new bool[clusters];
            foreach (int speaker in speakers)
            {
                if (speaker >= 0)
                {
                    hasWord[timeline.ClusterOfSpeaker[speaker]] = true;
                }
            }

            double[] seconds = new double[clusters];
            double[] timelineSeconds = timeline.SpeakingSeconds();
            for (int s = 0; s < timelineSeconds.Length; s++)
            {
                seconds[timeline.ClusterOfSpeaker[s]] = timelineSeconds[s];
            }

            double total = 0;
            int speaking = 0;
            for (int c = 0; c < clusters; c++)
            {
                if (hasWord[c])
                {
                    total += seconds[c];
                    speaking++;
                }
            }

            double floor = Seconds(options, total);
            int smallest = -1;
            for (int c = 0; c < clusters; c++)
            {
                if (hasWord[c] && seconds[c] < floor && (smallest < 0 || seconds[c] < seconds[smallest]))
                {
                    smallest = c;
                }
            }

            if (smallest < 0 || speaking <= 1)
            {
                return (current, timeline, speakers);
            }

            int target = Nearest(windows, current, clusters, smallest, c => hasWord[c] && seconds[c] >= floor);
            if (target < 0)
            {
                target = Nearest(windows, current, clusters, smallest, c => hasWord[c]);
            }

            int[] folded = new int[current.Length];
            for (int i = 0; i < current.Length; i++)
            {
                folded[i] = current[i] == smallest ? target : current[i];
            }

            current = SpeakerClusterer.Compact(folded);
        }
    }

    // The cluster other than the source whose mean embedding has the highest cosine similarity to the
    // source's, among those the filter admits; -1 when none is admitted.
    private static int Nearest(IReadOnlyList<SpeakerWindow> windows, int[] labels, int clusters, int source, Func<int, bool> admitted)
    {
        int dimension = windows[0].Embedding.Length;
        float[][] sums = new float[clusters][];
        for (int c = 0; c < clusters; c++)
        {
            sums[c] = new float[dimension];
        }

        for (int i = 0; i < windows.Count; i++)
        {
            VectorMath.Add(sums[labels[i]], windows[i].Embedding);
        }

        foreach (float[] sum in sums)
        {
            VectorMath.Normalise(sum);
        }

        int best = -1;
        float bestSimilarity = float.NegativeInfinity;
        for (int c = 0; c < clusters; c++)
        {
            if (c == source || !admitted(c))
            {
                continue;
            }

            float similarity = VectorMath.Dot(sums[source], sums[c]);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                best = c;
            }
        }

        return best;
    }
}
