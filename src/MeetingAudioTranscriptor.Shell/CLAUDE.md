# CLAUDE.md: the shell

The charter of `src/MeetingAudioTranscriptor.Shell/`. The root `CLAUDE.md` overrides it where they conflict.

## 1. What this project owns

The shell is the desktop executable `meeting-audio-transcriptor`: one window holding a web view that shows the embedded page, the native file and folder dialogs, the drop handling and the carrying of the bridge's messages both ways. It is the only project that names a window type. The bridge, the service and every rule about a run live in the App; the page is `frontend/`, embedded and never edited from here.

## 2. Two hosts behind one interface

**`IDesktopHost` is the seam: one implementation per operating system, `Windows/WindowsDesktopHost` over WebView2 and `Linux/LinuxDesktopHost` over GTK 3 and WebKitGTK 4.1.** The decision is in `docs/decisions/0006-desktop-hosts.md`. Both are written against the same `IHostServices` the bridge needs and share `HostServicesBase`, `PageMessageRouter`, `EmbeddedAssets`, `ResponseHeaders`, `DropEnvelope` and `DataFolder`, because whatever is not a platform call is one piece of code whose tests run on both systems.

**The `ShellHost` build property decides which host is compiled.** The runtime identifier sets it (`win-*` gives `windows`, anything else `linux`), and without one the build machine does. The other host's folder is removed from the compile, so a build carries no code and no reference for the other system, and `dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror` builds the Windows host from Linux. `Program` refuses to start when the compiled host does not match the operating system (exit code 4).

**Platform code is thin.** The native declarations (`Windows/Native`, `Linux/Native`) are source-generated imports and function pointers, and callbacks are static functions that reach their instance through a handle, because the Windows code cannot be run by the Linux suites and a logic error there would be found by a user.

**The composition root is one place.** `ShellComposition.CreateBridge` builds the bridge over a transcriber: the demo transcriber when `MAT_UI_DEMO=1`, else the one `CreateTranscriber` returns. A host takes a factory, so the bridge exists only once the window services do.

## 3. Threads

**The thread that calls `Run` owns the window and the web view, and no other thread touches either.** Windows installs `UiThread`, a synchronisation context over a hidden message-only window; Linux installs `GtkMainThread` over `g_idle_add`. Work from any thread, every envelope to the page and every dialog is posted to it. Because one rule is checkable ("this call is on the interface thread"), a web view is not used from the wrong thread.

**Nothing blocks the interface thread.** `Send` throws `NotSupportedException` on both contexts and no code waits on a task from that thread, because a thread waiting on its own loop is a window that stops drawing. A dialog runs on the interface thread through `OnUiThread` and hands its answer back as a task.

**`Emit` is callable from any thread and never throws.** It posts to the interface thread, and an envelope that arrives before the page is attached or after the window closed is dropped, because the page it was for does not exist. The attached flag is written and read on the interface thread alone.

**An exception that would cross into native code fails fast with a record.** The Linux callbacks cannot let it escape, because the runtime would end the process without a word.

## 4. The embedded page and the no-network rule

**The page is served from the assembly, from a private origin, and every other request is answered with a 404.** `EmbeddedAssets` reads resources whose names start `frontend/`, refuses a path with a dot segment, a backslash or a control character before looking it up, and serves nothing else of the assembly. The origin is `https://meeting-audio-transcriptor.app/` (a virtual host, never resolved) on Windows and `app://meeting-audio-transcriptor/` on Linux. Only the page's own files are embedded: its tests, markdown and hash list are left out.

**Nothing leaves the machine.** Every response carries `default-src 'self'` with `connect-src 'none'`, no caching and no content sniffing (`ResponseHeaders`). Windows filters every http and https request, cancels navigation off the origin, refuses new windows, downloads and permission requests and switches the SmartScreen reputation check off; Linux compiles a content filter that blocks everything but the origin, ignores navigation off it and refuses new windows. `NoNetworkGuardTests` scans the sources for connection types, because a request to an outside address is a leak whatever its purpose.

**The WebView2 browser process starts with its own background traffic switched off.** `BrowserArguments` is the one list of switches, each a documented Chromium switch or a feature of Microsoft's WebView2 flag list (background networking, component updates, domain reliability, sync, metrics upload, hyperlink pings, first-run tasks, SmartScreen, optimisation hints and the cast receiver search), and the environment also sets custom crash reporting, so a crash dump stays on the machine, and turns tracking prevention off, since the window shows only its own page. The list sits outside the Windows folder so a Linux test asserts it word for word. Outside the application's reach: the Evergreen runtime's updater, a machine-wide Windows service this process does not start, and any feature a machine policy forces on.

**The web view's profile is the only folder the shell creates.** `DataFolder` resolves `%LOCALAPPDATA%\meeting-audio-transcriptor` on Windows and `$XDG_DATA_HOME/meeting-audio-transcriptor` (else `~/.local/share`) on Linux, with the profile in `webview` under it, and the application writes no registry key.

**Release builds switch the browser's developer features off.** The context menu, the developer tools, reload and zoom are on in a Debug build alone, because a user of the shipped window has no use for them and a reload would drop a run's display.

## 5. Drops and dialogs

**The page cannot read a dropped file's path, so the host resolves it.** On Windows the page posts the file objects with the drop and the host turns them into a `{"drop":{"paths":[...]}}` message; on Linux the host handles the drag itself. `DropEnvelope` decodes `file://` URIs. The rest of the protocol is `frontend/BRIDGE.md`.

**Dialogs are the system's.** Windows calls `IFileOpenDialog` through raw virtual-table calls whose slots are documented against `ShObjIdl_core.h`; opening a folder is `ShellExecuteW`; Linux uses GTK's chooser and a launcher.

## 6. Start-up failures and exit codes

**A missing runtime is a sentence and a code, never a silent exit.** 0 is a normal close, 2 a missing web view runtime, missing GTK or WebKitGTK libraries or no display, 3 a window or web view that could not be set up (and an unreadable automation script), 4 a host that does not match the system. Windows shows a message box naming the runtime and its download page; Linux prints the missing library and the packages to install. WebKit's software drawing is chosen when the machine has no render node (`MAT_WEBKIT_SOFTWARE=1` forces it, `0` forbids it) because accelerated drawing leaves a blank page without a GPU.

## 7. The automation hook

**The hook is off unless `MAT_UI_AUTOMATION` or `MAT_UI_EXIT_AFTER_MS` is set.** `MAT_UI_AUTOMATION` names a script evaluated in the page once it has loaded and `MAT_UI_EXIT_AFTER_MS` closes the window after a delay; while either is set the page may post `log`, `echo`, `dropUris` and `exit` messages, and with neither the router ignores them, because a message the shipped window honours would be a way for a page to drive the host. The script is wrapped to run after the page's own modules. `docs/TESTING.md` owns what the tests do with it.

## 8. What cannot be verified on Linux

**The Windows host is compiled and never run by the Linux suites.** Its shared parts (assets, headers, routing, drop decoding, the data folder, threading with fakes) are unit-tested; the web view, the dialogs, the per-monitor scaling, the close behaviour and the offline proof are checked by hand with `docs/WINDOWS-VERIFICATION.md`. The Linux window is driven for real under `xvfb-run` in the integration tier; a real drag and drop is not simulated, the test feeds URIs to the handler the toolkit's drop reaches.

## 9. Where to look next

The bridge and the service: `src/MeetingAudioTranscriptor.App/CLAUDE.md`. The page and its contract: `frontend/CLAUDE.md`, `frontend/BRIDGE.md`. The checks only a Windows machine can make: `docs/WINDOWS-VERIFICATION.md`.
