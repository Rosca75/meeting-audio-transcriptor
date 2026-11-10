// The Windows interface thread's synchronisation context: the one way work from another thread
// reaches the window and the web view.
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using MeetingAudioTranscriptor.Shell.Windows.Native;

namespace MeetingAudioTranscriptor.Shell.Windows;

/// <summary>
/// Queues work for the thread that owns the window. A posted callback is queued and a private message
/// is posted to a hidden message-only window, whose procedure drains the queue when the pump
/// dispatches it. Every awaited continuation on that thread, every envelope to the page and every
/// dialog takes this path, which makes "the web view is touched only on its own thread" mechanical.
/// </summary>
/// <remarks>Sending is not supported, because a thread waiting on the pump is a window that is not drawing.</remarks>
internal sealed unsafe class UiThread : SynchronizationContext
{
    private const string ClassName = "MeetingAudioTranscriptorUiThread";

    /// <summary>The private message that says the queue has work.</summary>
    private const uint WM_DRAIN = Win32.WM_APP + 1;

    private static UiThread? Installed;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly nint _hwnd;

    private UiThread(nint hwnd)
    {
        _hwnd = hwnd;
    }

    /// <summary>Creates the message-only window on the calling thread and installs the context there; called once.</summary>
    public static UiThread Install()
    {
        if (Installed is not null)
        {
            throw new InvalidOperationException("the interface thread context is installed once, by the host");
        }

        nint instance = Win32.GetModuleHandleW(null);
        fixed (char* className = ClassName)
        {
            Win32.WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                lpszClassName = className,
            };
            if (Win32.RegisterClassExW(&windowClass) == 0)
            {
                throw new InvalidOperationException("the interface thread's message window class could not be registered: " + Marshal.GetLastPInvokeErrorMessage());
            }
        }

        nint hwnd = Win32.CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, instance, 0);
        if (hwnd == 0)
        {
            throw new InvalidOperationException("the interface thread's message window could not be created: " + Marshal.GetLastPInvokeErrorMessage());
        }

        UiThread context = new(hwnd);
        Installed = context;
        SetSynchronizationContext(context);
        return context;
    }

    /// <summary>Queues an action for the interface thread.</summary>
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Post(static state => ((Action)state!)(), work);
    }

    /// <inheritdoc />
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        _queue.Enqueue((d, state));
        Win32.PostMessageW(_hwnd, WM_DRAIN, 0, 0);
    }

    /// <inheritdoc />
    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException(
            "sending to the interface thread is not supported; post the work instead, because a thread waiting on the pump is a window that is not drawing");

    /// <summary>The same context: there is one interface thread, so a copy would be a lie.</summary>
    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs the message loop until a quit message arrives and answers the exit code it carried.</summary>
    public static int RunLoop()
    {
        Win32.MSG message;
        int status;
        while ((status = Win32.GetMessageW(&message, 0, 0, 0)) != 0)
        {
            if (status == -1)
            {
                throw new InvalidOperationException("the message loop failed: " + Marshal.GetLastPInvokeErrorMessage());
            }

            Win32.TranslateMessage(&message);
            Win32.DispatchMessageW(&message);
        }

        return (int)message.wParam;
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out (SendOrPostCallback Callback, object? State) item))
        {
            item.Callback(item.State);
        }
    }

    /// <summary>
    /// The message-only window's procedure. An exception escaping a callback would cross into native
    /// code, where the runtime ends the process without a word, so it fails fast with the cause.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (message == WM_DRAIN && Installed is not null)
        {
            try
            {
                Installed.Drain();
            }
            catch (Exception failure)
            {
                Environment.FailFast("unhandled exception on the interface thread: " + failure.Message, failure);
            }

            return 0;
        }

        return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
