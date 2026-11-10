// Tests that envelopes reach the page only on the interface thread and only while it is attached,
// and that dialog work runs there and hands its answer or its failure back.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MeetingAudioTranscriptor.Shell.Tests;

[Trait("Category", "desktop")]
public sealed class HostServicesBaseTests
{
    [Fact]
    public void AnEnvelopeWaitsForTheInterfaceThread()
    {
        FakeHost host = new();
        host.SetPageAttached(true);

        host.Emit("{\"event\":\"a\"}");

        Assert.True(host.Delivered.Count == 0, $"got {host.Delivered.Count} delivered before the queue ran, wanted 0");
        host.RunQueue();
        Assert.True(host.Delivered.Count == 1 && host.Delivered[0] == "{\"event\":\"a\"}", $"got ({string.Join(" | ", host.Delivered)}), wanted the one envelope");
    }

    [Fact]
    public void AnEnvelopeForADetachedPageIsDropped()
    {
        FakeHost host = new();

        host.Emit("{\"event\":\"before\"}");
        host.RunQueue();
        host.SetPageAttached(true);
        host.Emit("{\"event\":\"during\"}");
        host.SetPageAttached(false);
        host.RunQueue();

        Assert.True(host.Delivered.Count == 0, $"got ({string.Join(" | ", host.Delivered)}), wanted nothing delivered to a detached page");
    }

    [Fact]
    public async Task EmitIsSafeFromManyThreads()
    {
        FakeHost host = new();
        host.SetPageAttached(true);

        CancellationToken token = TestContext.Current.CancellationToken;
        await Task.WhenAll(Task.Run(() => Emit(host, 0), token), Task.Run(() => Emit(host, 1), token), Task.Run(() => Emit(host, 2), token));
        host.RunQueue();

        Assert.True(host.Delivered.Count == 300, $"got {host.Delivered.Count} delivered, wanted 300");
    }

    [Fact]
    public async Task DialogWorkRunsOnTheInterfaceThreadAndAnswers()
    {
        FakeHost host = new() { Answer = "/tmp/a.wav" };

        Task<string?> picked = host.PickAudioFileAsync();
        Assert.True(!picked.IsCompleted, "got a completed pick before the queue ran, wanted it to wait for the interface thread");
        host.RunQueue();

        string? got = await picked;
        Assert.True(got == "/tmp/a.wav", $"got '{got}', wanted /tmp/a.wav");
    }

    [Fact]
    public async Task ADialogFailureFaultsTheTask()
    {
        FakeHost host = new() { Failure = new InvalidOperationException("the dialog failed") };

        Task<string?> picked = host.PickAudioFileAsync();
        host.RunQueue();

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => picked);
        Assert.True(failure.Message == "the dialog failed", $"got '{failure.Message}', wanted the dialog's own message");
    }

    private static void Emit(FakeHost host, int thread)
    {
        for (int i = 0; i < 100; i++)
        {
            host.Emit($"{{\"t\":{thread},\"i\":{i}}}");
        }
    }

    private sealed class FakeHost : HostServicesBase
    {
        private readonly ConcurrentQueue<Action> _queue = new();

        public List<string> Delivered { get; } = [];

        public string? Answer { get; init; }

        public Exception? Failure { get; init; }

        public void RunQueue()
        {
            while (_queue.TryDequeue(out Action? work))
            {
                work();
            }
        }

        public override Task<string?> PickAudioFileAsync() => OnUiThread(() => Failure is null ? Answer : throw Failure);

        public override Task<string?> PickFolderAsync(string? initialFolder) => OnUiThread(() => initialFolder);

        public override void OpenFolder(string folderPath)
        {
        }

        protected override void PostToUiThread(Action work) => _queue.Enqueue(work);

        protected override void DeliverToPage(string json) => Delivered.Add(json);
    }
}
