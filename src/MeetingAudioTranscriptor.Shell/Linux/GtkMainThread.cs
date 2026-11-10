// The GTK main thread's synchronisation context: the one way work from another thread reaches the
// window and the web view on Linux.
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using MeetingAudioTranscriptor.Shell.Linux.Native;

namespace MeetingAudioTranscriptor.Shell.Linux;

/// <summary>
/// Queues work for the thread that runs the GTK main loop. A posted callback is queued and an idle
/// source is added to the default main context (g_idle_add is safe from any thread); the source drains
/// the queue on the main thread and removes itself. Every awaited continuation on the main thread,
/// every envelope to the page and every dialog takes this path.
/// </summary>
/// <remarks>
/// Sending is not supported, because a pool thread blocked on the main loop, or the loop blocked on
/// itself, is a window that stops drawing.
/// </remarks>
internal sealed unsafe class GtkMainThread : SynchronizationContext
{
    private static readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Queue = new();

    /// <summary>Installs the context on the calling thread, which must be the one that runs the main loop.</summary>
    public static GtkMainThread Install()
    {
        GtkMainThread context = new();
        SetSynchronizationContext(context);
        return context;
    }

    /// <summary>Queues an action for the main thread.</summary>
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Post(static state => ((Action)state!)(), work);
    }

    /// <inheritdoc />
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        Queue.Enqueue((d, state));
        _ = GLib.g_idle_add(&Drain, 0);
    }

    /// <inheritdoc />
    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException(
            "sending to the GTK main thread is not supported; post the work instead, because a thread waiting on the main loop is a window that is not drawing");

    /// <summary>The same context: there is one main thread, so a copy would be a lie.</summary>
    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Runs every queued callback on the main thread. An exception escaping it would cross into
    /// native code, where the runtime ends the process without a word, so it is turned into a
    /// fail-fast that records it.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int Drain(nint data)
    {
        try
        {
            while (Queue.TryDequeue(out (SendOrPostCallback Callback, object? State) item))
            {
                item.Callback(item.State);
            }
        }
        catch (Exception failure)
        {
            Environment.FailFast("unhandled exception on the GTK main thread: " + failure.Message, failure);
        }

        return GLib.G_SOURCE_REMOVE;
    }
}
