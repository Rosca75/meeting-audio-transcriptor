// Speaker separation through the full pipeline: each benchmark file is transcribed once by the real
// service (real VAD, real recogniser), the separator's embeddings and the recognised words are kept,
// and any clustering setting can then be scored on them without running a model again.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MeetingAudioTranscriptor.App;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Diarisation;
using MeetingAudioTranscriptor.Engine.Models;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Scoring;
using MeetingAudioTranscriptor.Engine.Writers;
using MeetingAudioTranscriptor.Inference;

namespace MeetingAudioTranscriptor.Pipeline.Tests.Diarisation;

/// <summary>One benchmark file transcribed through the service, with what its speaker separator received.</summary>
/// <param name="Id">The file's id, such as <c>fr-10min</c>.</param>
/// <param name="Group">The scoring group: clips, AMI or long.</param>
/// <param name="Reference">The reference speaker turns.</param>
/// <param name="Collector">The windows the separator embedded during the run, ready to be clustered again.</param>
/// <param name="Words">The recognised words in time order.</param>
/// <param name="SpeechSeconds">The seconds of speech voice activity found.</param>
/// <param name="Regions">The speech regions the separator received, in seconds from the start, with the chunk each came in.</param>
internal sealed record PipelineRun(string Id, BenchGroup Group, List<SpeakerSegment> Reference, DiarisationCollector Collector, IReadOnlyList<RecognizedWord> Words, double SpeechSeconds, IReadOnlyList<(double Start, double End, double ChunkStart, double ChunkEnd)> Regions)
{
    /// <summary>The number of distinct speakers in the reference.</summary>
    public int TrueSpeakers => Reference.Select(s => s.Speaker).Distinct(StringComparer.Ordinal).Count();
}

/// <summary>The score of one clustering setting on one pipeline run.</summary>
/// <param name="Run">The run scored.</param>
/// <param name="Assignment">The separator's answer under the setting.</param>
/// <param name="Attribution">The share of single-speaker reference time given to the right speaker, with segment-like word runs as the hypothesis.</param>
/// <param name="SpeakerSeconds">Per speaker: the seconds of its words and the number of its word runs.</param>
internal sealed record PipelineScore(PipelineRun Run, DiarisationAssignment Assignment, double Attribution, IReadOnlyList<(double Seconds, int Runs)> SpeakerSeconds)
{
    /// <summary>The number of speakers the transcript names.</summary>
    public int EstimatedSpeakers => SpeakerSeconds.Count;
}

/// <summary>Runs the benchmark files through the real service once per test run and scores settings on the result.</summary>
/// <remarks>
/// The service is the shipped one with one seam changed: its separator factory hands out a
/// separator that keeps its collector and words, so the same embeddings can be clustered under many
/// settings. That separator answers with the shipped settings, so the transcript the run writes is
/// the one a user gets.
/// </remarks>
internal static class PipelineDiarisation
{
    /// <summary>The recognition threads of every run, as the benchmark uses.</summary>
    public const int Threads = 4;

    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<string, PipelineRun> Cache = new(StringComparer.Ordinal);

    /// <summary>Why the runs cannot happen here, or null when they can.</summary>
    public static string? SkipReason()
    {
        string root = ModelCatalog.ResolveRoot();
        if (DiarisationBench.EmbeddingModel() is null || !LoadedModels.RequiredFiles().All(file => File.Exists(Path.Combine(root, file))))
        {
            return $"needs the recognition, voice-activity and wespeaker-campplus-lm models in {root}; run: node build/fetch-models.mjs --into <models folder> and set MAT_MODELS_DIR";
        }

        return null;
    }

    /// <summary>The benchmark file of an id under <c>bench/data</c>: a clip or a long file with its RTTM; null when it is not present.</summary>
    public static (string Wav, string Rttm, BenchGroup Group, string Language)? Locate(string id)
    {
        string language = id[..2];
        string clip = Path.Combine(DiarisationBench.DataDirectory, "clips", language, id + ".wav");
        string longFile = Path.Combine(DiarisationBench.DataDirectory, "long", id + ".wav");
        string wav = File.Exists(clip) ? clip : longFile;
        string rttm = Path.ChangeExtension(wav, ".rttm");
        if (!File.Exists(wav) || !File.Exists(rttm))
        {
            return null;
        }

        BenchGroup group = id.Contains("ami", StringComparison.Ordinal) ? BenchGroup.Ami
            : id.Contains("multi", StringComparison.Ordinal) ? BenchGroup.Synthetic
            : BenchGroup.Long;
        return (wav, rttm, group, language);
    }

    /// <summary>The run of one file, transcribed on first use and kept for the test run; null when the file is absent.</summary>
    public static PipelineRun? Run(string id)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(id, out PipelineRun? cached))
            {
                return cached;
            }

            if (Locate(id) is not { } file)
            {
                return null;
            }

            PipelineRun run = Transcribe(id, file.Wav, file.Rttm, file.Group, file.Language);
            Cache[id] = run;
            return run;
        }
    }

    /// <summary>Scores one setting on a run: the separator's answer, the attribution and the size of every speaker.</summary>
    public static PipelineScore Score(PipelineRun run, DiarisationOptions options)
    {
        DiarisationAssignment assignment = run.Collector.Assign(run.Words, options, CancellationToken.None);
        List<SpeakerSegment> hypothesis = WordRuns(run.Words, assignment.SpeakerOfWord);
        DiarisationScore score = DiarisationErrorRate.Compute(run.Reference, hypothesis, new DiarisationScoringOptions(0.25, ExcludeOverlap: true));
        int speakers = assignment.SpeakerOfWord.Count == 0 ? 0 : assignment.SpeakerOfWord.Max() + 1;
        (double Seconds, int Runs)[] sizes = new (double, int)[speakers];
        for (int i = 0; i < run.Words.Count; i++)
        {
            int s = assignment.SpeakerOfWord[i];
            sizes[s].Seconds += run.Words[i].End - run.Words[i].Start;
        }

        foreach (SpeakerSegment segment in hypothesis)
        {
            sizes[int.Parse(segment.Speaker, System.Globalization.CultureInfo.InvariantCulture)].Runs++;
        }

        return new PipelineScore(run, assignment, score.AttributionAccuracy, sizes);
    }

    /// <summary>
    /// The transcript's speaker turns as the benchmark scores them, approximated from the words: a
    /// run of one speaker's words ends at a change of speaker or a pause of a second, as a segment does.
    /// </summary>
    public static List<SpeakerSegment> WordRuns(IReadOnlyList<RecognizedWord> words, IReadOnlyList<int> speakers)
    {
        List<SpeakerSegment> runs = [];
        int start = 0;
        for (int i = 1; i <= words.Count; i++)
        {
            if (i < words.Count && speakers[i] == speakers[start] && words[i].Start - words[i - 1].End < 1.0)
            {
                continue;
            }

            if (words[i - 1].End > words[start].Start)
            {
                runs.Add(new SpeakerSegment(speakers[start].ToString(System.Globalization.CultureInfo.InvariantCulture), words[start].Start, words[i - 1].End));
            }

            start = i;
        }

        return runs;
    }

    private static PipelineRun Transcribe(string id, string wav, string rttm, BenchGroup group, string language)
    {
        using ITensorRunner embeddingRunner = OrtRunnerFactory.Open(DiarisationBench.EmbeddingModel()!, new RunnerOptions(IntraOpThreads: 1, AllowSpinning: false));
        SpeakerEmbedder embedder = new(embeddingRunner, new DiarisationOptions().BatchSize);
        KeepingSeparator? separator = null;
        ServiceSeams seams = ServiceSeams.Default with
        {
            LoadModels = root =>
            {
                LoadedModels real = LoadedModels.Load(root);
                return new LoadedModels(real.Vad, real.Recognizer, () => separator = new KeepingSeparator(embedder), real.Models, [real]);
            },
        };

        string output = Path.Combine(Path.GetTempPath(), "mat-diarisation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using TranscriptionService service = new(ModelCatalog.ResolveRoot(), seams);
            TranscriptionRequest request = new()
            {
                InputPath = wav,
                Language = language,
                Formats = [TranscriptFormat.Json],
                OutputFolder = output,
                SpeakerSeparation = true,
                Threads = Threads,
            };
            TranscriptionOutcome outcome = service.RunAsync(request, null, CancellationToken.None).GetAwaiter().GetResult();
            if (!outcome.Succeeded || separator?.Words is null)
            {
                throw new InvalidOperationException($"the run of {id} did not finish with speakers: {outcome.Kind}, {outcome.Message}");
            }

            List<SpeakerSegment> reference = DiarisationRttm.Parse(File.ReadAllText(rttm));
            return new PipelineRun(id, group, reference, separator.Collector, separator.Words, outcome.Pipeline!.SpeechSeconds, [.. separator.Regions.OrderBy(r => r.Start)]);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    /// <summary>The shipped separator's work, keeping the collector and the words for later scoring.</summary>
    private sealed class KeepingSeparator(SpeakerEmbedder embedder) : ISpeakerSeparator
    {
        public DiarisationCollector Collector { get; } = new(embedder, new DiarisationOptions());

        public IReadOnlyList<RecognizedWord>? Words { get; private set; }

        public List<(double Start, double End, double ChunkStart, double ChunkEnd)> Regions { get; } = [];

        public void AddBlock(ReadOnlySpan<float> samples, long startSample, IReadOnlyList<(int Start, int End)> speechRegions, CancellationToken cancellation)
        {
            Collector.AddBlock(samples, startSample, speechRegions, cancellation);
            lock (Regions)
            {
                foreach ((int start, int end) in speechRegions)
                {
                    Regions.Add(((startSample + start) / 16000.0, (startSample + end) / 16000.0, startSample / 16000.0, (startSample + samples.Length) / 16000.0));
                }
            }
        }

        public SpeakerAssignment Finish(IReadOnlyList<RecognizedWord> words, int? speakerCountHint, CancellationToken cancellation)
        {
            Words = [.. words];
            DiarisationAssignment assignment = Collector.Assign(words, new DiarisationOptions { SpeakerCountHint = speakerCountHint }, cancellation);
            return new SpeakerAssignment(assignment.SpeakerOfWord, assignment.SpeakingSeconds);
        }
    }
}
