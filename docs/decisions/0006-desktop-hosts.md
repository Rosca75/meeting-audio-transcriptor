# 0006. Desktop hosts: WebView2 on Windows, WebKitGTK on Linux

## Context

The reference repository hosts its page in a raw Win32 window with WebView2
and is Windows-only. This application also ships on Linux, where WebView2 does
not exist.

## Options for Linux

1. GTK 3 and WebKitGTK 4.1 through hand-written P/Invoke.
2. Photino.NET (a package wrapping WebKitGTK with its own native library).
3. A local HTTP server and the system browser.
4. A second user interface toolkit (Avalonia, GTK widgets).

## Evidence

- Option 3 opens a listening socket, which the offline rule forbids, and gives
  no window of its own.
- Option 4 duplicates the page.
- Option 2 adds a package and a native binary for what option 1 does in a few
  hundred lines; the 5-hour dependency rule does not justify it.
- WebKitGTK 4.1 is in the default repositories of Ubuntu 22.04 and later,
  Debian 12, Fedora and openSUSE; the window was run here under xvfb.

## Decision

Option 1. One `Shell` project holds both hosts behind `IDesktopHost`; the
`ShellHost` build property, derived from the runtime identifier, compiles one
of them. Both serve the same embedded `frontend/` assets from a private origin
(a virtual HTTPS host on WebView2, a custom URI scheme on WebKitGTK), block
every other request, and speak one bridge envelope. The bridge and all logic
live in the window-free `App` project and are tested with a fake host.

## Consequences

- The Linux package needs `libgtk-3` and `libwebkit2gtk-4.1` from the
  distribution; the host names them when they are missing. They are system
  components, like the WebView2 runtime on Windows.
- The Windows host is compiled from Linux and cannot be executed here; it
  mirrors the reference's proven sequence and is on the owner's checklist.
