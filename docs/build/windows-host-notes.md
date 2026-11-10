# Windows host: what to check by hand

The Windows desktop host (`src/MeetingAudioTranscriptor.Shell/Windows/`) is compiled from Linux with
`dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror` and has never been run
there. This is the checklist for a Windows 10 or 11 x64 machine. Build it with
`dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64` (or publish it) and start
`meeting-audio-transcriptor.exe` with `MAT_UI_DEMO=1` unless a step says otherwise.

## 1. What the host does, in brief

- A raw Win32 window (`MainWindow`), a hidden message-only window behind the `UiThread`
  synchronisation context, and the WebView2 control filling the client area.
- The page is served from `https://meeting-audio-transcriptor.app/` through `WebResourceRequested`
  filters on `http://*` and `https://*`: the embedded page for that origin, a 404 for every other
  address. Navigations off the origin, new windows, downloads and permission requests are refused;
  SmartScreen reputation checks, autofill and password saving are off; dev tools, the context menu and
  the browser accelerator keys are on in a Debug build only.
- Messages: `WebMessageReceived` passes strings to the bridge; a message carrying file objects
  (`postMessageWithAdditionalObjects`) becomes `{"drop":{"paths":[...]}}`. `Emit` posts with
  `PostWebMessageAsString` on the interface thread.
- Dialogs: `IFileOpenDialog` through raw virtual-table calls (slots documented in
  `Windows/Native/FileDialog.cs` against `ShObjIdl_core.h`). Folder: `ShellExecuteW` "open".
- Profile: `%LOCALAPPDATA%\meeting-audio-transcriptor\webview`.

## 2. Checklist

1. **Start-up.** The window opens titled "Meeting Audio Transcriptor", about 900 x 720 logical
   pixels, the page renders with its styles, the version shows in the footer. Exit code 0 on close
   (`echo %ERRORLEVEL%` after `start /wait meeting-audio-transcriptor.exe`).
2. **Missing runtime.** On a machine (or VM) without the Evergreen runtime, or with
   `WebView2Loader.dll` removed from the folder: a message box names the runtime and
   `https://developer.microsoft.com/microsoft-edge/webview2/`, exit code 2.
3. **Open dialog.** "Choose file" opens the system dialog titled "Choose a meeting recording" with
   one filter, "Meeting recordings (*.m4a, *.mp4, *.wav)"; a chosen file appears on the page;
   Cancel leaves the page unchanged. Try a path with non-ASCII letters and spaces, and one over 260
   characters.
4. **Folder dialog.** "Choose folder" opens the folder picker, starting at the current output folder
   when one is set; the chosen path appears; Cancel changes nothing.
5. **Open folder.** After a demo run, "Open folder" opens File Explorer on the output folder.
6. **Drop.** Drag an `.m4a` from Explorer onto the window: the drop styling shows while dragging,
   the file is selected. Drop a `.txt`: the refusal message appears once. Drop several files: the
   first supported one is taken and the note says so.
7. **DPI.** At 100, 150 and 200 percent scaling the window is crisp (no bitmap stretching). Drag it
   between two monitors of different scale: it keeps its physical size and redraws sharp.
8. **Resize.** The page follows every resize without gaps; the window cannot be made smaller than
   640 x 480 logical pixels; maximise and restore work.
9. **Close while running.** Start a demo run and close the window mid-run: the process ends at once
   with exit code 0, no error dialog, no process left in Task Manager (`msedgewebview2.exe`
   children included).
10. **Offline.** With Resource Monitor or `netstat -b` (or Wireshark) watching, run the whole flow:
    no connection leaves the machine from `meeting-audio-transcriptor.exe` or its
    `msedgewebview2.exe` children. In a Debug build, open dev tools (F12) and check the Network tab:
    every request is to `https://meeting-audio-transcriptor.app/`, and typing
    `location.href = "https://example.com"` in the console leaves the page where it is.
11. **Automation hook.** `set MAT_UI_AUTOMATION=<script.js>` with a script that posts
    `{"automation":{"log":"hi"}}` and `{"automation":{"exit":5}}` through
    `chrome.webview.postMessage(JSON.stringify(...))`; run with output redirected
    (`meeting-audio-transcriptor.exe > out.txt`): `out.txt` holds `automation: log hi`, exit code 5.
12. **Trimmed publish.** `dotnet publish src/MeetingAudioTranscriptor.Shell -c Release -r win-x64
    --self-contained -p:PublishTrimmed=true`: the published executable starts, renders and runs steps
    3, 6 and 9 the same way (it depends on the rooted WebView2 core assembly and the built-in COM
    interop the project switches on for a trimmed publish).
13. **Release build hardening.** In a Release build: no context menu on right-click, F12 and
    Ctrl+Shift+I do nothing, Ctrl+R and F5 do not reload, Ctrl+wheel does not zoom.
