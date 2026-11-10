// The window services both hosts share: every event and reply, and every dialog, is moved to the
// interface thread, and nothing reaches a page that has gone.
using System;
using System.Threading.Tasks;
using MeetingAudioTranscriptor.App.Bridge;

namespace MeetingAudioTranscriptor.Shell;

/// <summary>
/// The part of <see cref="IHostServices"/> that is the same on every platform. A host supplies how
/// work is posted to its interface thread and how one envelope is handed to the page.
/// </summary>
/// <remarks>
/// The bridge calls <see cref="Emit"/> from any thread. The envelope is posted to the interface
/// thread, because the web view may only be touched there, and is dropped when the page is not
/// attached yet or the window has closed: an event that finishes during start-up or after close has
/// nowhere to go and is not an error. The attached flag is written and read on the interface thread
/// alone, so it needs no lock.
/// </remarks>
internal abstract class HostServicesBase : IHostServices
{
    private bool _pageAttached;

    /// <summary>
    /// Marks the page as able to receive envelopes, or no longer able to; called on the interface
    /// thread when the web view exists and when the window closes.
    /// </summary>
    public void SetPageAttached(bool attached) => _pageAttached = attached;

    /// <inheritdoc />
    public void Emit(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        PostToUiThread(() =>
        {
            if (_pageAttached)
            {
                DeliverToPage(json);
            }
        });
    }

    /// <inheritdoc />
    public abstract Task<string?> PickAudioFileAsync();

    /// <inheritdoc />
    public abstract Task<string?> PickFolderAsync(string? initialFolder);

    /// <inheritdoc />
    public abstract void OpenFolder(string folderPath);

    /// <summary>Queues work for the interface thread and returns at once; callable from any thread.</summary>
    protected abstract void PostToUiThread(Action work);

    /// <summary>Hands one envelope to the page; called on the interface thread only.</summary>
    protected abstract void DeliverToPage(string json);

    /// <summary>
    /// Runs the work on the interface thread and hands its answer, or its exception, back to the
    /// caller's task. A modal dialog runs this way, because it belongs to the thread that owns the
    /// window and keeps that thread's loop turning while it is open.
    /// </summary>
    protected Task<T> OnUiThread<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUiThread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                completion.SetException(failure);
            }
        });
        return completion.Task;
    }
}
