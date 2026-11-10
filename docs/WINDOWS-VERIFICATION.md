# Windows verification

The checklist the owner runs on a Windows 10 or 11 x64 laptop. Windows code could not be executed while the application was built: the Windows window host and the Media Foundation decoder were compiled, the decoder was exercised under Wine, and the CI Windows legs run the unit and integration tiers. Everything below is what remains unproven. Work in order; each item says what to do, what is expected and what to report when it differs. Report the item number, the exact message and the output of `meeting-audio-transcriptor-cli.exe --version`.

Use a real meeting recording of about 30 minutes in each language you speak (`.m4a` and `.mp4` as your phone or meeting tool writes them) and a short `.wav`. In PowerShell, `$d = "C:\path\to\package"` shortens the commands.

## 1. Package and command line

1. **Download and extract.** Take `meeting-audio-transcriptor-<version>-win-x64.zip` and `SHA256SUMS.txt` from the Releases page. `Get-FileHash .\<zip> -Algorithm SHA256` equals the line in the sums file. Extract to a folder with a space in its name (for example `C:\Temp\Meeting Audio`). Expected: `meeting-audio-transcriptor.exe`, `meeting-audio-transcriptor-cli.exe`, `models\`, `ffmpeg\`, `models.lock` and `NOTICE` are there. Report a missing file.
2. **SmartScreen.** Double-click `meeting-audio-transcriptor.exe`. Expected: "Windows protected your PC" (nothing is signed); "More info" then "Run anyway" starts the window. Report any other block, such as an antivirus quarantine of a file in the folder.
3. **Version and models.** `& "$d\meeting-audio-transcriptor-cli.exe" --version` prints `meeting-audio-transcriptor <version>`. `& "$d\meeting-audio-transcriptor-cli.exe" models --verify` lists every file as `found` and ends with "Every file matches its size and SHA-256 in the lock." (it takes a minute). Report any `missing` or failed file and the sentence printed.

## 2. Decoding (Media Foundation)

Media Foundation is the decoder for `.m4a` and `.mp4` on Windows; the engine's own reader takes plain `.wav`; the bundled ffmpeg is the fallback when Media Foundation fails.

4. **One file per kind and language.** For each of a `.wav`, an `.m4a` and an `.mp4`, and each language you can judge:
   `& "$d\meeting-audio-transcriptor-cli.exe" transcribe "C:\rec\meeting.m4a" --language en --raw`
   Expected: exit code 0 (`$LASTEXITCODE`), the files `meeting.vtt`, `.json`, `.txt`, `.raw.vtt`, `.raw.txt` beside the recording, a readable transcript, and a speed line. Report the exit code and the message of any failure; a message starting "Media Foundation could not" names an HRESULT, which is what to report.
5. **Which decoder ran.** The summary of `transcribe` has a `Decoder:` line. Expected: `wav` for the `.wav` file and `media-foundation` for the `.m4a` and `.mp4` files. Report a line starting `ffmpeg`, with the first failing run's message.
6. **Compare with ffmpeg.** `$env:MAT_PREFER_FFMPEG = "1"` makes the next runs try the bundled ffmpeg before Media Foundation. Transcribe the same `.m4a` as in item 4 and read the `Decoder:` line: it starts `ffmpeg (bundled)`. Remove the variable afterwards (`Remove-Item Env:MAT_PREFER_FFMPEG`). Expected: the two `.txt` files differ in a few words at most and the speed is alike. Report a difference larger than that, or a shifted timeline (compare the last cue times in the two `.vtt` files with the recording's length).
7. **Decoder missing.** Rename the `ffmpeg` folder to `ffmpeg-off` and transcribe the `.m4a` again: it still works through Media Foundation (exit code 0, `Decoder: media-foundation`). Rename it back afterwards. On a Windows N edition without the Media Feature Pack the expected message names the pack; report any other wording.
8. **Bad input.** Rename a text file to `fake.m4a` and transcribe it: exit code 2 and a sentence saying the file is not a supported recording or is damaged. A zero-byte `.wav`: exit code 2, "is empty". Report a crash or a different code.

## 3. The desktop window

9. **Start.** The window opens titled "Meeting Audio Transcriptor", about 900 by 720 pixels, the page is styled, the version shows in the footer. Closing it exits with code 0 (`Start-Process -Wait`, then check `$LASTEXITCODE`, or `cmd /c start /wait`).
10. **Choose file.** "Choose file..." opens the system dialog titled "Choose a meeting recording" with one filter, "Meeting recordings (*.m4a, *.mp4, *.wav)". A chosen file appears on the page; Cancel changes nothing. Try a path with accents and spaces, and one over 260 characters.
11. **Drop.** Drag an `.m4a` from Explorer onto the window: the drop styling shows and the file is selected. Drop a `.txt`: one refusal message. Drop two files: the first supported one is taken and a note says so.
12. **Output folder.** "Choose folder..." opens the folder picker at the current output folder; the chosen path shows; Cancel changes nothing.
13. **A run.** Start a transcription of the 30-minute recording: the progress bar moves forward only, stages change, the summary shows the speed and the files. "Open output folder" opens File Explorer on them.
14. **Cancel.** Start again, press Cancel at about half progress. Expected: the run ends within a few seconds with "cancelled", and the output folder holds no new or partial file (also no `*.tmp`). Report leftovers.
15. **Close while running.** Start a run and close the window: the process ends at once, no error dialog, and Task Manager shows no leftover `meeting-audio-transcriptor.exe`, `meeting-audio-transcriptor-cli.exe` or `msedgewebview2.exe` of this window.
16. **DPI and size.** At 100, 150 and 200 percent scaling the text is sharp; dragging the window between two monitors of different scale keeps it sharp; it cannot be made smaller than 640 by 480; maximise and restore work.
17. **Hardening.** Right-click shows no context menu; F12, Ctrl+Shift+I, F5, Ctrl+R and Ctrl+wheel do nothing.
18. **WebView2 missing (optional).** On a machine without the runtime, or with `WebView2Loader.dll` removed from the folder, the window shows a message box naming the runtime and the download address, and exits with code 2.
19. **Trimmed publish (optional).** The release package is self-contained and not trimmed, so this applies only to a trimmed build you make yourself: `dotnet publish src/MeetingAudioTranscriptor.Shell -c Release -r win-x64 --self-contained -p:PublishTrimmed=true` must start, render and pass items 10, 11 and 15.
20. **Automation hook (optional).** `set MAT_UI_AUTOMATION=C:\t\script.js` with a script that posts `{"automation":{"log":"hi"}}` and `{"automation":{"exit":5}}` through `chrome.webview.postMessage(JSON.stringify(...))`; run `meeting-audio-transcriptor.exe > out.txt`: `out.txt` holds `automation: log hi` and the exit code is 5.

## 4. Offline proof

21. Run items 13 and 4 with the network disabled (airplane mode), then again with Resource Monitor (Network tab, "TCP Connections" and "Listening Ports") filtered on `meeting-audio-transcriptor` and `msedgewebview2`. Expected: it works offline, and no connection, listening port or UDP endpoint appears for any of the processes. Report any remote address.

## 5. The speed check

22. On the real 30-minute recording, and a 2-hour one if you have it, with nothing else running and the laptop on mains power:
    `& "$d\meeting-audio-transcriptor-cli.exe" benchmark --long-file "C:\rec\meeting30.m4a" --language en --threads 4 --out C:\rec\bench`
    then again with `--threads 8`, and once with `--no-speakers`. Read the line for the file and the table, and the first line of the output, which names the processor, whether it reports VNNI and whether the exact 8-bit kernels are on: the "x real" figure is the speed as times real time (30 minutes in 2.5 minutes is 12). The target is a 2-hour meeting in 10 minutes or less, that is 12 times real time or more at `--threads 4` (the default on a 4-core machine). Read also "peak working set": it must stay under 4 GB. Report the machine (processor, cores, RAM), the three figures, the peak memory and the result file in `C:\rec\bench`; send the file.
23. **Quality spot check.** Open the `.txt` and `.raw.txt` of a real meeting side by side. Expected: the cleaned file differs by fillers, stutters and bare "yeah" lines only; the speaker labels change where people change; all the quiet speakers appear. Report any quiet speaker whose words are in neither file, any meaningful word cleaning removed, and the speaker count found against the real one (then retry with `--speakers <n>`).

24. **Processor without VNNI.** A processor before the 12th Intel generation or the 4th AMD generation has no VNNI. Expected: the first line of the benchmark output says VNNI is not reported and the exact 8-bit kernels are on, which the engine chooses by itself (decision 0010); the speed is about a fifth lower than with VNNI and the German word error rate stays low. `$env:MAT_ORT_QUANT_PRECISION = "0"` or `"1"` forces the choice for a comparison; remove it afterwards. Report the first line and both speeds.

## 6. Disk footprint and removal

25. Besides the files you saved (and the temporary `.tmp` files that disappear when a run ends), the only folder written is `%LOCALAPPDATA%\meeting-audio-transcriptor\webview`. Check with a before and after listing of `%LOCALAPPDATA%` and `%APPDATA%`, and `reg query HKCU\Software /s /f meeting-audio-transcriptor` finds nothing. Uninstalling is deleting the extracted folder and `%LOCALAPPDATA%\meeting-audio-transcriptor`. Report anything else found.
