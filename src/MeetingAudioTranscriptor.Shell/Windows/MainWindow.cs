// The raw Win32 top-level window the web view fills: its class, its size limits, its DPI handling
// and the events the web view host answers.
using System;
using System.Runtime.InteropServices;
using MeetingAudioTranscriptor.Shell.Windows.Native;

namespace MeetingAudioTranscriptor.Shell.Windows;

/// <summary>
/// The application window. It owns nothing but its handle: a resize, a focus and the close are
/// raised as events, and its destruction ends the message loop with <see cref="ExitCode"/>.
/// </summary>
internal sealed unsafe class MainWindow
{
    private const string ClassName = "MeetingAudioTranscriptorWindow";

    private static MainWindow? Current;

    private MainWindow(nint handle)
    {
        Handle = handle;
    }

    /// <summary>The native handle.</summary>
    public nint Handle { get; }

    /// <summary>The code the message loop ends with when the window is destroyed.</summary>
    public int ExitCode { get; set; } = ShellExitCodes.Ok;

    /// <summary>The smallest client size the window may be dragged to, in device-independent pixels.</summary>
    public (int Width, int Height) MinimumSize { get; init; } = (640, 480);

    /// <summary>Raised with the new client width and height, in pixels.</summary>
    public event Action<int, int>? Resized;

    /// <summary>Raised when the window gains keyboard focus, so it can be handed to the web view.</summary>
    public event Action? Focused;

    /// <summary>Raised once, when the window has been destroyed.</summary>
    public event Action? Closed;

    /// <summary>
    /// Creates the window, hidden, at a size in device-independent pixels scaled to the system's DPI,
    /// because the page is laid out for a window of that logical size.
    /// </summary>
    public static MainWindow Create(string title, int logicalWidth, int logicalHeight)
    {
        nint instance = Win32.GetModuleHandleW(null);

        // The executable's own icon when the build embedded one, the stock icon otherwise.
        nint icon = Win32.LoadIconW(instance, Win32.IDI_APPLICATION);
        if (icon == 0)
        {
            icon = Win32.LoadIconW(0, Win32.IDI_APPLICATION);
        }

        fixed (char* className = ClassName)
        {
            Win32.WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hIcon = icon,
                hIconSm = icon,
                hCursor = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                lpszClassName = className,
            };
            if (Win32.RegisterClassExW(&windowClass) == 0)
            {
                throw new InvalidOperationException("the window class could not be registered: " + Marshal.GetLastPInvokeErrorMessage());
            }
        }

        double scale = Win32.GetDpiForSystem() / 96.0;
        nint handle = Win32.CreateWindowExW(
            0, ClassName, title, Win32.WS_OVERLAPPEDWINDOW,
            Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, (int)(logicalWidth * scale), (int)(logicalHeight * scale),
            0, 0, instance, 0);
        if (handle == 0)
        {
            throw new InvalidOperationException("the window could not be created: " + Marshal.GetLastPInvokeErrorMessage());
        }

        Current = new MainWindow(handle);
        return Current;
    }

    /// <summary>Shows the window and paints it.</summary>
    public void Show()
    {
        Win32.ShowWindow(Handle, Win32.SW_SHOWNORMAL);
        Win32.UpdateWindow(Handle);
    }

    /// <summary>Destroys the window, which raises <see cref="Closed"/> and ends the loop.</summary>
    public void Close() => Win32.DestroyWindow(Handle);

    /// <summary>The client area's current size, in pixels.</summary>
    public (int Width, int Height) ClientSize
    {
        get
        {
            Win32.RECT rect;
            Win32.GetClientRect(Handle, &rect);
            return (rect.right - rect.left, rect.bottom - rect.top);
        }
    }

    /// <summary>
    /// The window procedure. Messages sent during creation, before the instance exists, take the
    /// default path; an exception escaping would cross into native code, so it fails fast.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        MainWindow? window = Current;
        if (window is null || window.Handle != hwnd)
        {
            return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
        }

        try
        {
            return window.HandleMessage(message, wParam, lParam);
        }
        catch (Exception failure)
        {
            Environment.FailFast("unhandled exception in the window procedure: " + failure.Message, failure);
            return 0;
        }
    }

    private nint HandleMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32.WM_SIZE:
                // The client width and height ride in the low and high words.
                Resized?.Invoke((int)(lParam & 0xFFFF), (int)((lParam >> 16) & 0xFFFF));
                return 0;

            case Win32.WM_SETFOCUS:
                Focused?.Invoke();
                return 0;

            case Win32.WM_GETMINMAXINFO:
            {
                // The minimum is logical, so it is scaled to the monitor the window is on.
                double scale = Win32.GetDpiForWindow(Handle) / 96.0;
                Win32.MINMAXINFO* limits = (Win32.MINMAXINFO*)lParam;
                limits->ptMinTrackSize.x = (int)(MinimumSize.Width * scale);
                limits->ptMinTrackSize.y = (int)(MinimumSize.Height * scale);
                return 0;
            }

            case Win32.WM_DPICHANGED:
            {
                // The system proposes the rectangle that keeps the window's physical size on the new
                // monitor; taking it stops the window growing or shrinking as it crosses screens.
                Win32.RECT* suggested = (Win32.RECT*)lParam;
                Win32.SetWindowPos(
                    Handle, 0, suggested->left, suggested->top,
                    suggested->right - suggested->left, suggested->bottom - suggested->top,
                    Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                return 0;
            }

            case Win32.WM_CLOSE:
                Win32.DestroyWindow(Handle);
                return 0;

            case Win32.WM_DESTROY:
                Closed?.Invoke();
                Current = null;
                Win32.PostQuitMessage(ExitCode);
                return 0;

            default:
                return Win32.DefWindowProcW(Handle, message, wParam, lParam);
        }
    }
}
