// Tests of the streaming pipeline's threading: order, back-pressure, cancellation, failures, progress, empty input and determinism.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.Contracts.Transcripts;
using MeetingAudioTranscriptor.Engine.Audio;
using MeetingAudioTranscriptor.Engine.Pipeline;
using MeetingAudioTranscriptor.Engine.Recognition;
using MeetingAudioTranscriptor.Engine.Tests.Vad;
using MeetingAudioTranscriptor.Engine.Vad;
using MeetingAudioTranscriptor.Engine.Writers;
using Xunit;

namespace MeetingAudioTranscriptor.Engine.Tests.Pipeline;

[Trait("Category", "transcript")]
public sealed class TranscriptionPipelineTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void WordsComeOutInChunkOrderWhenChunksFinishOutOfOrder()
    {
        ScriptedRecognizer recognizer = new() { DelayMilliseconds = index => index % 4 == 0 ? 40 : 0 };
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(new ScriptedChunkSource(24), recognizer, threads: 4);

        PipelineResult result = pipeline.Run(new SilentSource(48), PipelineTestKit.Request(), null, Token);

        int[] finished = recognizer.Finished.ToArray();
        Assert.True(!finished.SequenceEqual(finished.Order()), $"got chunks finishing in order {string.Join(",", finished)}, wanted some out of order for the test to mean anything");
        TranscriptWord[] words = result.Document.Segments.SelectMany(segment => segment.Words!).ToArray();
        string[] expected = Enumerable.Range(0, 24).SelectMany(k => new[] { $"w{k}a", $"w{k}b." }).ToArray();
        Assert.True(words.Select(word => word.Text).SequenceEqual(expected), $"got words {string.Join(" ", words.Select(word => word.Text))}, wanted chunk order");
        Assert.True(words.Zip(words.Skip(1)).All(pair => pair.First.Start <= pair.Second.Start), "word times should never go backwards");
        Assert.True(Math.Abs(words[2].Start - 2.1) < 1e-9 && Math.Abs(words[3].End - 2.7) < 1e-9, $"got chunk 1's words at {words[2].Start} and {words[3].End}, wanted shifted by its 2 s start to 2.1 and 2.7");
        Assert.True(result.ChunkCount == 24 && result.RecognizedWords == 48 && result.Threads == 4, $"got {result.ChunkCount} chunks, {result.RecognizedWords} words, {result.Threads} threads");
    }

    [Fact]
    public async Task TheQueueHoldsTheProducerBackWhileTheWorkersAreBusy()
    {
        using ManualResetEventSlim gate = new(false);
        ScriptedChunkSource chunks = new(40);
        ScriptedRecognizer recognizer = new() { Gate = gate };
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(chunks, recognizer, threads: 2);

        Task<PipelineResult> run = Task.Run(() => pipeline.Run(new SilentSource(80), PipelineTestKit.Request(), null, Token), Token);
        SpinWait.SpinUntil(() => recognizer.Started == 2, 5000);
        await Task.Delay(300, Token);
        int held = chunks.Yielded;
        gate.Set();
        PipelineResult result = await run;

        // Two in the workers, four in the queue (twice the workers), and one the producer holds while it waits for a slot.
        Assert.True(held <= 2 + 4 + 1, $"got {held} chunks pulled while both workers were blocked, wanted at most 7");
        Assert.True(result.ChunkCount == 40, $"got {result.ChunkCount} chunks after the gate opened, wanted all 40");
    }

    [Fact]
    public async Task CancellingMidRunThrowsPromptlyAndStopsTheProducer()
    {
        ScriptedChunkSource chunks = new(100_000);
        ScriptedRecognizer recognizer = new() { DelayMilliseconds = _ => 5 };
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(chunks, recognizer, threads: 3);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        Task<PipelineResult> run = Task.Run(() => pipeline.Run(new SilentSource(200_000), PipelineTestKit.Request(), null, cancel.Token), Token);
        SpinWait.SpinUntil(() => recognizer.Started >= 10, 5000);
        Stopwatch watch = Stopwatch.StartNew();
        cancel.Cancel();
        Exception? thrown = await Record.ExceptionAsync(() => run);
        watch.Stop();

        Assert.True(thrown is OperationCanceledException, $"got {thrown?.GetType().Name ?? "no exception"}, wanted OperationCanceledException");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"got the run stopping {watch.Elapsed.TotalMilliseconds:F0} ms after the cancel, wanted under 1 s");
        int pulled = chunks.Yielded;
        await Task.Delay(100, Token);
        Assert.True(chunks.Yielded == pulled && pulled < 100_000, $"got the producer at {pulled} then {chunks.Yielded} chunks, wanted it stopped");
    }

    [Fact]
    public void AWorkerFailureStopsEverythingAndSurfacesAsItself()
    {
        ScriptedChunkSource chunks = new(10_000);
        ScriptedRecognizer recognizer = new() { ThrowAt = 3 };
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(chunks, recognizer, threads: 2);

        Exception? thrown = Record.Exception(() => pipeline.Run(new SilentSource(20_000), PipelineTestKit.Request(), null, Token));

        Assert.True(thrown is InvalidOperationException && thrown.Message.Contains("chunk 3", StringComparison.Ordinal), $"got {thrown?.GetType().Name}: {thrown?.Message}, wanted the recogniser's own InvalidOperationException");
        Assert.True(chunks.Yielded < 10_000, $"got {chunks.Yielded} chunks pulled, wanted the producer stopped well before the end");
    }

    [Fact]
    public void ASourceFailureStopsEverythingAndSurfacesAsItself()
    {
        ScriptedChunkSource chunks = new(100) { ThrowAt = 5 };
        ScriptedRecognizer recognizer = new();
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(chunks, recognizer, threads: 2);

        Exception? thrown = Record.Exception(() => pipeline.Run(new SilentSource(200), PipelineTestKit.Request(), null, Token));

        Assert.True(thrown is AudioFormatException, $"got {thrown?.GetType().Name ?? "no exception"}, wanted the source's AudioFormatException");
        Assert.True(recognizer.Started <= 5, $"got {recognizer.Started} chunks recognised, wanted at most the 5 before the failure");
    }

    [Fact]
    public void ProgressNeverDecreasesAndEndsAtOne()
    {
        ProgressLog log = new();
        ScriptedSeparator separator = new((_, _) => 0);
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(new ScriptedChunkSource(30), new ScriptedRecognizer { DelayMilliseconds = i => i % 3 }, threads: 3, () => separator);

        pipeline.Run(new SilentSource(60), PipelineTestKit.Request(), log, Token);

        List<PipelineProgress> reports = log.Reports;
        Assert.True(reports.Count > 3, $"got {reports.Count} reports, wanted several");
        Assert.True(reports.Zip(reports.Skip(1)).All(pair => pair.First.Fraction <= pair.Second.Fraction), $"got fractions {string.Join(" ", reports.Select(r => r.Fraction.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}, wanted non-decreasing");
        Assert.True(reports[0].Fraction == 0 && reports[^1].Fraction == 1 && reports[^1].Stage == TranscriptionStage.Cleaning, $"got first {reports[0]} and last {reports[^1]}, wanted 0 and Cleaning at 1");
        TranscriptionStage[] stages = reports.Select(r => r.Stage).Distinct().ToArray();
        Assert.True(stages.SequenceEqual([TranscriptionStage.Transcribing, TranscriptionStage.IdentifyingSpeakers, TranscriptionStage.Cleaning]), $"got stages {string.Join(",", stages)}");
    }

    [Fact]
    public void ProgressIsThrottledWithinAStage()
    {
        ProgressLog log = new();
        TranscriptionPipeline pipeline = new(
            _ => new ScriptedChunkSource(2000),
            new ScriptedRecognizer(),
            null,
            new PipelineOptions { Threads = 2, ProgressInterval = TimeSpan.FromSeconds(10) },
            null);

        pipeline.Run(new SilentSource(4000), PipelineTestKit.Request(), log, Token);

        int transcribing = log.Reports.Count(r => r.Stage == TranscriptionStage.Transcribing);
        Assert.True(transcribing <= 3, $"got {transcribing} transcribing reports for 2000 chunks inside one interval, wanted the start, perhaps one more, and the end");
    }

    [Fact]
    public void NoChunksGiveAValidEmptyDocument()
    {
        ProgressLog log = new();
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(new ScriptedChunkSource(0), new ScriptedRecognizer(), threads: 2, () => new ScriptedSeparator((_, _) => 0));

        PipelineResult result = pipeline.Run(new SilentSource(0), PipelineTestKit.Request(), log, Token);

        Assert.True(result.Document.Segments.Count == 0 && result.Document.Speakers.Count == 0, $"got {result.Document.Segments.Count} segments and {result.Document.Speakers.Count} speakers, wanted none");
        Assert.True(log.Reports[^1].Fraction == 1, $"got the last progress {log.Reports[^1]}, wanted 1");
        using MemoryStream json = new();
        TranscriptJson.Serialize(result.Document, json);
        json.Position = 0;
        TranscriptDocument read = TranscriptJson.Deserialize(json);
        Assert.True(read.Segments.Count == 0 && read.Metadata.SourceFile == "meeting.wav", "the empty document should round-trip through the JSON contract");
    }

    [Fact]
    public void SilenceThroughTheRealVadStageGivesAnEmptyDocument()
    {
        FakeSileroRunner runner = FakeSileroRunner.Energy();
        runner.Record = false;
        SileroVadModel vad = new(runner, new SileroVadOptions { Lanes = 2, BlockSeconds = 8, WarmUpSeconds = 0.5 });
        ScriptedRecognizer recognizer = new();
        TranscriptionPipeline pipeline = new(vad, recognizer, null, new PipelineOptions { Threads = 2 });

        // Ten seconds of silence at 48 kHz, so the resampler is on the path as well.
        using ArrayAudioSource source = new(new float[480_000], 48000);
        PipelineResult result = pipeline.Run(source, PipelineTestKit.Request(), null, Token);

        Assert.True(result.Document.Segments.Count == 0 && recognizer.Started == 0, $"got {result.Document.Segments.Count} segments and {recognizer.Started} recognitions, wanted none");
        Assert.True(Math.Abs(result.AudioSeconds - 10) < 0.01 && result.SpeechSeconds == 0, $"got {result.AudioSeconds} s of audio and {result.SpeechSeconds} s of speech, wanted 10 and 0");
        Assert.True(Math.Abs(result.Document.Metadata.DurationSeconds - 10) < 0.01, $"got the metadata duration {result.Document.Metadata.DurationSeconds}, wanted 10");
    }

    [Fact]
    public void BurstsThroughTheRealVadStageReachTheRecogniserAtTheirTimes()
    {
        FakeSileroRunner runner = FakeSileroRunner.Energy();
        runner.Record = false;
        SileroVadModel vad = new(runner, new SileroVadOptions { Lanes = 2, BlockSeconds = 8, WarmUpSeconds = 0.5 });
        List<float> audio = [];
        audio.AddRange(new float[3 * 16000]);
        audio.AddRange(Enumerable.Range(0, 4 * 16000).Select(i => 0.3f * MathF.Sin(i * 0.2f)));
        audio.AddRange(new float[3 * 16000]);
        ScriptedRecognizer recognizer = new(_ => [new RecognizedWord("Hello.", 0.5, 0.9, 0.9f)]);

        // The recogniser here reads samples, not an index: every chunk gets the same word.
        TranscriptionPipeline pipeline = new(vad, new SamplesIgnoringRecognizer(recognizer), null, new PipelineOptions { Threads = 1 });
        using ArrayAudioSource source = new(audio.ToArray(), 16000);
        PipelineResult result = pipeline.Run(source, PipelineTestKit.Request(), null, Token);

        Assert.True(result.ChunkCount == 1 && result.Document.Segments.Count == 1, $"got {result.ChunkCount} chunks and {result.Document.Segments.Count} segments, wanted one of each");
        double start = result.Document.Segments[0].Start;
        Assert.True(start > 3.0 && start < 3.6, $"got the word at {start:F2} s, wanted about 3 s plus 0.5 s minus the padding");
        Assert.True(result.SpeechSeconds > 3.9 && result.SpeechSeconds < 4.5, $"got {result.SpeechSeconds:F2} s of speech, wanted about 4");
    }

    [Fact]
    public void TheDocumentIsTheSameWhateverTheThreadCount()
    {
        ScriptedSeparator Separator() => new((i, _) => i / 6 % 3);
        string one = Canonical(PipelineTestKit.Pipeline(new ScriptedChunkSource(50), new ScriptedRecognizer { DelayMilliseconds = i => i % 5 }, 1, Separator)
            .Run(new SilentSource(100), PipelineTestKit.Request(), null, Token).Document);
        string four = Canonical(PipelineTestKit.Pipeline(new ScriptedChunkSource(50), new ScriptedRecognizer { DelayMilliseconds = i => (7 - (i % 7)) * 2 }, 4, Separator)
            .Run(new SilentSource(100), PipelineTestKit.Request(), null, Token).Document);

        Assert.True(one == four, "got different documents for 1 and 4 threads, wanted the same bytes apart from the timings and the thread count");
    }

    [Fact]
    public void TheSeparatorHearsEveryChunkAndItsSpeakersReachTheSegments()
    {
        ScriptedSeparator separator = new((i, _) => i / 4 % 2 == 0 ? 7 : 3);
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(new ScriptedChunkSource(8), new ScriptedRecognizer(), threads: 3, () => separator);

        PipelineResult result = pipeline.Run(new SilentSource(16), PipelineTestKit.Request(hint: 2), null, Token);

        Assert.True(separator.Blocks.Order().SequenceEqual(Enumerable.Range(0, 8).Select(k => k * 32000L)), $"got blocks at {string.Join(",", separator.Blocks.Order())}, wanted every chunk once");
        Assert.True(separator.FinishCalls == 1 && separator.HintSeen == 2, $"got {separator.FinishCalls} finish calls with hint {separator.HintSeen}, wanted one with 2");
        TranscriptDocument document = result.Document;
        Assert.True(document.Speakers.Select(s => s.Id + "=" + s.Label).SequenceEqual(["S1=Speaker 1", "S2=Speaker 2"]), $"got speakers {string.Join(", ", document.Speakers.Select(s => s.Id))}");
        string[] bySegment = document.Segments.Select(s => s.Speaker!).ToArray();
        Assert.True(bySegment.SequenceEqual(["S1", "S1", "S2", "S2", "S1", "S1", "S2", "S2"]), $"got segment speakers {string.Join(",", bySegment)}");
        Assert.True(document.Metadata.Settings.SpeakerSeparation && document.Metadata.Settings.Threads == 3, "the settings should record the separation and the thread count used");
        Assert.True(Math.Abs(document.Speakers[0].SpeakingSeconds - 2.4) < 1e-9, $"got S1 speaking {document.Speakers[0].SpeakingSeconds} s, wanted 4 segments of 0.6 s");
    }

    [Fact]
    public void TheNamesOfTheOptionsReachTheSpeakersAndEveryWriter()
    {
        // Each chunk holds two words: cluster 7 speaks chunks 0 and 2, cluster 3 chunk 1, and nobody is third.
        ScriptedSeparator separator = new((i, _) => i / 2 % 2 == 0 ? 7 : 3);
        TranscriptionPipeline pipeline = PipelineTestKit.Pipeline(new ScriptedChunkSource(3), new ScriptedRecognizer(), threads: 2, () => separator, speakerNames: ["Anna", "Björn", "Nobody"]);

        TranscriptDocument document = pipeline.Run(new SilentSource(6), PipelineTestKit.Request(hint: 3), null, Token).Document;

        string[] speakers = document.Speakers.Select(s => s.Id + "=" + s.Label).ToArray();
        Assert.True(speakers.SequenceEqual(["S1=Anna", "S2=Björn"]), $"got speakers {string.Join(", ", speakers)}");
        string vtt = Written(TranscriptFormat.Vtt, document);
        Assert.Equal(
            "WEBVTT\n\n1\n00:00:00.100 --> 00:00:00.700\n<v Anna>w0a w0b.</v>\n\n2\n00:00:02.100 --> 00:00:02.700\n<v Björn>w1a w1b.</v>\n\n3\n00:00:04.100 --> 00:00:04.700\n<v Anna>w2a w2b.</v>\n",
            vtt);
        string txt = Written(TranscriptFormat.Txt, document);
        Assert.EndsWith("Speakers: 2\n\n[00:00:00] Anna:\nw0a w0b.\n\n[00:00:02] Björn:\nw1a w1b.\n\n[00:00:04] Anna:\nw2a w2b.\n", txt);
        string json = Written(TranscriptFormat.Json, document);
        Assert.Contains("\"id\": \"S1\",\n      \"label\": \"Anna\",", json);
        Assert.Contains("\"id\": \"S2\",\n      \"label\": \"Björn\",", json);
        Assert.DoesNotContain("Speaker 1", vtt + txt + json);
    }

    private static string Written(TranscriptFormat format, TranscriptDocument document)
    {
        using MemoryStream stream = new();
        TranscriptWriters.Write(format, TranscriptVariant.Cleaned, document, stream);
        return new System.Text.UTF8Encoding(false).GetString(stream.ToArray());
    }

    [Fact]
    public void WithoutASeparatorNoSegmentCarriesASpeaker()
    {
        PipelineResult result = PipelineTestKit.Pipeline(new ScriptedChunkSource(3), new ScriptedRecognizer(), threads: 2)
            .Run(new SilentSource(6), PipelineTestKit.Request(), null, Token);

        Assert.True(result.Document.Speakers.Count == 0 && result.Document.Segments.All(s => s.Speaker is null), "got speakers without a separator");
        Assert.True(!result.Document.Metadata.Settings.SpeakerSeparation, "the settings should record that separation was off");
    }

    /// <summary>The document as JSON with the run-dependent metadata blanked.</summary>
    private static string Canonical(TranscriptDocument document)
    {
        TranscriptDocument blanked = document with
        {
            Metadata = document.Metadata with
            {
                ProcessingSeconds = 0,
                CreatedUtc = "",
                Settings = document.Metadata.Settings with { Threads = 0 },
            },
        };
        using MemoryStream stream = new();
        TranscriptJson.Serialize(blanked, stream);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Hands every chunk to the scripted recogniser as chunk 0, for chunks the VAD cut from real samples.</summary>
    private sealed class SamplesIgnoringRecognizer : ISpeechRecognizer
    {
        private readonly ScriptedRecognizer _inner;

        public SamplesIgnoringRecognizer(ScriptedRecognizer inner)
        {
            _inner = inner;
        }

        public double MaxChunkSeconds => 30;

        public IReadOnlyList<RecognizedWord> Recognize(ReadOnlyMemory<float> samples, CancellationToken cancellation) =>
            _inner.Recognize(new float[] { 1 }, cancellation);
    }
}
