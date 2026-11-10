// Turns the recording's timed words into the transcript document: segments, the low-confidence guard, cleaning, speakers and metadata.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Cleaning;
using MeetingAudioTranscriptor.Engine.Recognition;

namespace MeetingAudioTranscriptor.Engine.Pipeline;

/// <summary>The sequential tail of the pipeline, after the last chunk is recognised.</summary>
/// <remarks>
/// Everything here runs on one thread over words already in chunk order, so the document is the
/// same whatever the number of workers that recognised the chunks.
/// </remarks>
internal static class TranscriptAssembler
{
    /// <summary>The shape version written into every transcript.</summary>
    internal const string SchemaVersion = "1";

    /// <summary>The reason a segment the low-confidence guard removed carries.</summary>
    internal const string LowConfidence = "low-confidence";

    /// <summary>The facts the metadata records that the words do not carry.</summary>
    internal sealed record Facts(
        PipelineRequest Request,
        double AudioSeconds,
        double SpeechSeconds,
        double ProcessingSeconds,
        DateTimeOffset CreatedUtc,
        int Threads,
        bool SpeakerSeparation);

    /// <summary>Builds the document.</summary>
    /// <param name="words">Every word in time order, absolute times.</param>
    /// <param name="speakers">One speaker index per word, or null without speaker separation.</param>
    /// <param name="options">The segmentation and the guard.</param>
    /// <param name="cleaner">The cleaner of the run's language.</param>
    /// <param name="facts">The metadata facts.</param>
    /// <param name="onSegment">Called with the fraction of segments cleaned, for progress.</param>
    /// <param name="cancellation">Stops between segments.</param>
    public static TranscriptDocument Build(
        IReadOnlyList<RecognizedWord> words,
        IReadOnlyList<int>? speakers,
        SegmentationOptions options,
        SegmentCleaner cleaner,
        Facts facts,
        Action<double>? onSegment,
        CancellationToken cancellation)
    {
        List<WordRange> ranges = SegmentBuilder.Build(words, speakers, options);

        // Speakers are numbered by first appearance in the transcript, so S1 speaks first.
        Dictionary<int, int> numberOf = [];
        List<double> seconds = [];
        if (speakers is not null)
        {
            foreach (WordRange range in ranges)
            {
                int speaker = speakers[range.First];
                if (!numberOf.ContainsKey(speaker))
                {
                    numberOf[speaker] = numberOf.Count;
                    seconds.Add(0);
                }
            }
        }

        List<TranscriptSegment> segments = new(ranges.Count);
        string? previousKeptText = null;
        int? previousKeptSpeaker = null;
        for (int s = 0; s < ranges.Count; s++)
        {
            cancellation.ThrowIfCancellationRequested();
            WordRange range = ranges[s];
            RecognizedWord[] segmentWords = Slice(words, range);
            int? speaker = speakers is null ? null : numberOf[speakers[range.First]];

            string text;
            string rawText;
            string? removedReason;
            bool[] removed;
            if (IsLowConfidence(segmentWords, options))
            {
                rawText = JoinTexts(segmentWords);
                text = string.Empty;
                removedReason = LowConfidence;
                removed = new bool[segmentWords.Length];
                Array.Fill(removed, true);
            }
            else
            {
                // An answer to a question by someone else may be a decision, so the cleaner keeps it.
                bool afterQuestion = previousKeptText is not null
                    && (speaker is null || previousKeptSpeaker != speaker)
                    && previousKeptText.TrimEnd().EndsWith('?');
                CleanedSegment cleaned = cleaner.Clean(segmentWords, afterQuestion);
                text = cleaned.Text;
                rawText = cleaned.RawText;
                removedReason = cleaned.RemovedReason;
                removed = cleaned.Removed;
            }

            if (removedReason is null)
            {
                previousKeptText = text;
                previousKeptSpeaker = speaker;
            }

            double start = segmentWords[0].Start;
            double end = start;
            double confidenceSum = 0;
            TranscriptWord[] transcriptWords = new TranscriptWord[segmentWords.Length];
            for (int w = 0; w < segmentWords.Length; w++)
            {
                RecognizedWord word = segmentWords[w];
                end = Math.Max(end, word.End);
                confidenceSum += word.Confidence;
                transcriptWords[w] = new TranscriptWord
                {
                    Text = word.Text,
                    Start = word.Start,
                    End = word.End,
                    Confidence = word.Confidence,
                    Removed = removed[w] ? true : null,
                };
            }

            if (speaker is int index)
            {
                seconds[index] += end - start;
            }

            segments.Add(new TranscriptSegment
            {
                Id = s + 1,
                Start = start,
                End = end,
                Speaker = speaker is int number ? SpeakerId(number) : null,
                Text = text,
                RawText = rawText,
                Removed = removedReason,
                Confidence = confidenceSum / segmentWords.Length,
                Words = transcriptWords,
            });
            onSegment?.Invoke((s + 1) / (double)ranges.Count);
        }

        List<TranscriptSpeaker> speakerList = new(seconds.Count);
        for (int i = 0; i < seconds.Count; i++)
        {
            speakerList.Add(new TranscriptSpeaker
            {
                Id = SpeakerId(i),
                Label = "Speaker " + (i + 1).ToString(CultureInfo.InvariantCulture),
                SpeakingSeconds = seconds[i],
            });
        }

        PipelineRequest request = facts.Request;
        return new TranscriptDocument
        {
            SchemaVersion = SchemaVersion,
            Metadata = new TranscriptMetadata
            {
                Application = request.ApplicationName,
                ApplicationVersion = request.ApplicationVersion,
                SourceFile = request.SourceFileName,
                DurationSeconds = facts.AudioSeconds,
                SpeechSeconds = facts.SpeechSeconds,
                Language = CleaningLanguages.Code(request.Language),
                CreatedUtc = facts.CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ProcessingSeconds = facts.ProcessingSeconds,
                Models = request.Models,
                Settings = request.Settings with
                {
                    Threads = facts.Threads,
                    SpeakerSeparation = facts.SpeakerSeparation,
                },
            },
            Speakers = speakerList,
            Segments = segments,
        };
    }

    /// <summary>True when the guard removes the segment: few words, all unsure, none holding a digit.</summary>
    internal static bool IsLowConfidence(IReadOnlyList<RecognizedWord> words, SegmentationOptions options)
    {
        if (options.LowConfidenceThreshold <= 0 || words.Count == 0 || words.Count > options.LowConfidenceMaxWords)
        {
            return false;
        }

        foreach (RecognizedWord word in words)
        {
            if (word.Confidence >= options.LowConfidenceThreshold)
            {
                return false;
            }

            foreach (char c in word.Text)
            {
                if (char.IsDigit(c))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string SpeakerId(int index) => "S" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static RecognizedWord[] Slice(IReadOnlyList<RecognizedWord> words, WordRange range)
    {
        RecognizedWord[] slice = new RecognizedWord[range.Count];
        for (int i = 0; i < range.Count; i++)
        {
            slice[i] = words[range.First + i];
        }

        return slice;
    }

    private static string JoinTexts(RecognizedWord[] words)
    {
        string[] texts = new string[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            texts[i] = words[i].Text;
        }

        return string.Join(' ', texts);
    }
}
