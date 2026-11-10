// Tests the run lifecycle: one run at a time, progress that never decreases, cancellation and
// exactly one terminal event however a run ends.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;
using MeetingAudioTranscriptor.Contracts.Bridge;
using Xunit;

namespace MeetingAudioTranscriptor.App.Tests.Bridge;

/// <summary>A clock the test advances by hand.</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void AdvanceMilliseconds(long ms) => Interlocked.Add(ref _ticks, ms);
}

[Trait("Category", "transcript")]
public sealed class RunLifecycleTests
{
    [Fact]
    public async Task StartResolvesNullAndTheRunFinishesWithOneCompletedEvent()
    {
        using BridgeHarness h = new();
        h.Transcriber.Run = (r, _, _) => Task.FromResult(new TranscribeOutcome
        {
            Kind = TranscribeOutcomeKinds.Completed,
            OutputFolder = r.OutputFolder,
            OutputFiles = [System.IO.Path.Combine(r.OutputFolder!, "meeting.vtt")],
            DurationSeconds = 60,
            ProcessingSeconds = 6,
            SpeedFactor = 10,
            Speakers = 2,
            Words = 100,
        });

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request());
        JsonElement finished = await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.Equal(JsonValueKind.Null, reply.GetProperty("result").ValueKind);
        Assert.Equal("completed", finished.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrEmpty(finished.GetProperty("message").GetString()), "got no message, wanted a default sentence");
        Assert.Equal(2, finished.GetProperty("speakers").GetInt32());
        Assert.Equal(1, finished.GetProperty("outputFiles").GetArrayLength());
        Assert.Equal(h.Folder, finished.GetProperty("outputFolder").GetString());
    }

    [Fact]
    public async Task TheTranscriberReceivesTheNormalisedRequestWithTheInputFolderAsDefault()
    {
        using BridgeHarness h = new();
        TranscribeRequest request = h.Request() with { Language = "FR", Formats = ["VTT", "vtt", "txt"], SpeakerSeparation = false, SpeakerCount = 4 };

        await h.CallAsync(BridgeMethods.RunStart, request);
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        TranscribeRequest seen = h.Transcriber.LastRequest!;
        Assert.Equal("fr", seen.Language);
        Assert.Equal(["vtt", "txt"], seen.Formats.ToArray());
        Assert.Null(seen.SpeakerCount);
        Assert.Equal(h.Folder, seen.OutputFolder);
    }

    [Theory]
    [InlineData(TranscribeOutcomeKinds.NoSpeech)]
    [InlineData(TranscribeOutcomeKinds.Failed)]
    [InlineData(TranscribeOutcomeKinds.Cancelled)]
    public async Task EveryKindPassesThroughWithADefaultMessage(string kind)
    {
        using BridgeHarness h = new();
        h.Transcriber.Run = (_, _, _) => Task.FromResult(new TranscribeOutcome { Kind = kind });

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        JsonElement finished = await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.Equal(kind, finished.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(finished.GetProperty("message").GetString()), $"got no message for kind {kind}, wanted a default");
    }

    [Fact]
    public async Task AThrowingTranscriberEndsWithOneFailedEventCarryingItsMessage()
    {
        using BridgeHarness h = new();
        h.Transcriber.Run = (_, _, _) => throw new InvalidOperationException("The audio could not be decoded. Convert it to WAV.");

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        JsonElement finished = await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.Equal("failed", finished.GetProperty("kind").GetString());
        Assert.Equal("The audio could not be decoded. Convert it to WAV.", finished.GetProperty("message").GetString());
        Assert.Single(h.Host.Events(BridgeEvents.RunFinished));
    }

    [Fact]
    public async Task AProgrammingFaultInTheTranscriberIsNotShownRaw()
    {
        using BridgeHarness h = new();
        h.Transcriber.Run = (_, _, _) => throw new KeyNotFoundException("internal detail");

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        JsonElement finished = await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.Equal("failed", finished.GetProperty("kind").GetString());
        Assert.DoesNotContain("internal detail", finished.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AnUnknownOutcomeKindBecomesAFailure()
    {
        using BridgeHarness h = new();
        h.Transcriber.Run = (_, _, _) => Task.FromResult(new TranscribeOutcome { Kind = "weird" });

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        JsonElement finished = await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.Equal("failed", finished.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ASecondStartIsRefusedWhileARunIsActiveAndTheFirstStillFinishesOnce()
    {
        using BridgeHarness h = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Transcriber.Run = async (_, _, _) =>
        {
            await release.Task;
            return new TranscribeOutcome { Kind = TranscribeOutcomeKinds.Completed };
        };

        JsonElement first = await h.CallAsync(BridgeMethods.RunStart, h.Request());
        JsonElement second = await h.CallAsync(BridgeMethods.RunStart, h.Request());
        release.SetResult();
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.False(first.TryGetProperty("error", out _), "got an error on the first start, wanted acceptance");
        Assert.Contains("already running", second.GetProperty("error").GetString());
        Assert.Single(h.Host.Events(BridgeEvents.RunFinished));
    }

    [Fact]
    public async Task AfterTheTerminalEventANewRunCanStart()
    {
        using BridgeHarness h = new();

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);
        JsonElement again = await h.CallAsync(BridgeMethods.RunStart, h.Request());

        Assert.False(again.TryGetProperty("error", out _), "got an error starting after a finished run, wanted acceptance");
    }

    [Fact]
    public async Task CancelStopsTheRunAndEmitsOneCancelledEvent()
    {
        using BridgeHarness h = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Transcriber.Run = async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new TranscribeOutcome { Kind = TranscribeOutcomeKinds.Completed };
        };

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        await started.Task;
        JsonElement cancel = await h.CallAsync(BridgeMethods.RunCancel);
        JsonElement finished = await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.Equal(JsonValueKind.Null, cancel.GetProperty("result").ValueKind);
        Assert.Equal("cancelled", finished.GetProperty("kind").GetString());
        Assert.Single(h.Host.Events(BridgeEvents.RunFinished));
    }

    [Fact]
    public async Task AnInvalidRequestIsRejectedAndEmitsNoTerminalEvent()
    {
        using BridgeHarness h = new();

        JsonElement reply = await h.CallAsync(BridgeMethods.RunStart, h.Request() with { Formats = [] });
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Contains("output format", reply.GetProperty("error").GetString());
        Assert.Empty(h.Host.Events(BridgeEvents.RunFinished));
        Assert.Null(h.Transcriber.LastRequest);
    }

    [Fact]
    public async Task ProgressIsForwardedNeverDecreasesAndStopsBeforeTheTerminalEvent()
    {
        ManualClock clock = new();
        using BridgeHarness h = new(clock);
        h.Transcriber.Run = (_, progress, _) =>
        {
            double[] fractions = [0.1, 0.3, 0.2, 0.5, 0.4, 0.9];
            foreach (double fraction in fractions)
            {
                clock.AdvanceMilliseconds(150);
                progress.Report(new TranscribeProgress { Stage = "Recognising words", Fraction = fraction });
            }

            return Task.FromResult(new TranscribeOutcome { Kind = TranscribeOutcomeKinds.Completed });
        };

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        double[] seen = h.Host.Events(BridgeEvents.RunProgress).Select(p => p.GetProperty("fraction").GetDouble()).ToArray();
        Assert.Equal([0.1, 0.3, 0.5, 0.9], seen);
        Assert.Equal(BridgeEvents.RunFinished, h.Host.Messages().Last().GetProperty("event").GetString());
    }

    [Fact]
    public async Task ProgressIsThrottledToAboutTenPerSecondWithinAStage()
    {
        ManualClock clock = new();
        using BridgeHarness h = new(clock);
        h.Transcriber.Run = (_, progress, _) =>
        {
            for (int i = 1; i <= 20; i++)
            {
                clock.AdvanceMilliseconds(10);
                progress.Report(new TranscribeProgress { Stage = "Finding speech", Fraction = i / 100.0 });
            }

            return Task.FromResult(new TranscribeOutcome { Kind = TranscribeOutcomeKinds.Completed });
        };

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        // 200 ms of reports at 10 ms: the first, then one per 100 ms.
        int count = h.Host.Events(BridgeEvents.RunProgress).Count;
        Assert.InRange(count, 2, 4);
    }

    [Fact]
    public async Task AStageChangeAndCompletionAreNeverThrottledAway()
    {
        ManualClock clock = new();
        using BridgeHarness h = new(clock);
        h.Transcriber.Run = (_, progress, _) =>
        {
            progress.Report(new TranscribeProgress { Stage = "A", Fraction = 0.1 });
            progress.Report(new TranscribeProgress { Stage = "B", Fraction = 0.2 });
            progress.Report(new TranscribeProgress { Stage = "B", Fraction = 1.0 });
            return Task.FromResult(new TranscribeOutcome { Kind = TranscribeOutcomeKinds.Completed });
        };

        await h.CallAsync(BridgeMethods.RunStart, h.Request());
        await h.Host.WaitForEventAsync(BridgeEvents.RunFinished);

        Assert.Equal(3, h.Host.Events(BridgeEvents.RunProgress).Count);
    }
}
