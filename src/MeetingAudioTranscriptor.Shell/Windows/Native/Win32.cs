// The Win32 calls the Windows host makes into the window manager, the module loader, COM and the
// shell, all source generated so a trimmed build needs no run-time marshalling stub.
using System;
using System.Runtime.InteropServices;

namespace MeetingAudioTranscriptor.Shell.Windows.Native;

/// <summary>
/// The window, message pump, message box, COM creation and shell calls of the Windows host. Only what
/// the host uses is declared, each with the constants it needs, so the whole native surface reads in
/// one file. Values and layouts are those of the Windows SDK headers named beside each group.
/// </summary>
internal static unsafe partial class Win32
{
    // WinUser.h: a standard resizable top-level window at the default position.
    internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    internal const int CW_USEDEFAULT = unchecked((int)0x80000000);
    internal const uint CS_HREDRAW = 0x0002;
    internal const uint CS_VREDRAW = 0x0001;

    // WinUser.h: the parent that makes a window message-only, never shown, receiving posted messages.
    internal const nint HWND_MESSAGE = -3;

    // WinUser.h: the messages the host handles.
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_SIZE = 0x0005;
    internal const uint WM_SETFOCUS = 0x0007;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_GETMINMAXINFO = 0x0024;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WM_APP = 0x8000;

    // WinUser.h: ShowWindow commands and SetWindowPos flags.
    internal const int SW_SHOWNORMAL = 1;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;

    // WinDef.h (DPI_AWARENESS_CONTEXT): per-monitor awareness, version 2.
    internal const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // WinUser.h: the stock arrow cursor and application icon (MAKEINTRESOURCE ids).
    internal const nint IDC_ARROW = 32512;
    internal const nint IDI_APPLICATION = 32512;

    // WinUser.h: MessageBox styles.
    internal const uint MB_OK = 0x0000;
    internal const uint MB_ICONERROR = 0x0010;

    // combaseapi.h (CLSCTX): the object runs in this process.
    internal const uint CLSCTX_INPROC_SERVER = 0x1;

    /// <summary>WNDCLASSEXW (WinUser.h), field for field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    /// <summary>POINT (windef.h).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    /// <summary>RECT (windef.h).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    /// <summary>MSG (WinUser.h).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    /// <summary>MINMAXINFO (WinUser.h): five points, the fourth being the minimum tracking size.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    internal static partial nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetMessageW(MSG* message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(MSG* message);

    [LibraryImport("user32.dll")]
    internal static partial nint DispatchMessageW(MSG* message);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hwnd, RECT* rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll")]
    internal static partial nint LoadIconW(nint instance, nint iconName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);

    /// <summary>shellapi.h: opens a file or folder with its default verb; a result above 32 is success.</summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint ShellExecuteW(nint hwnd, string? operation, string file, string? parameters, string? directory, int showCommand);

    /// <summary>ShObjIdl_core.h: the shell item for a file system path.</summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid interfaceId, out nint item);
}
