# meeting-audio-transcriptor

Turns a recording of a business meeting into a transcript, on your own computer. You choose the language (English, French or German), the program writes the transcript as subtitles (`.vtt`), as data (`.json`) and as readable text (`.txt`), and tells the speakers apart with anonymous labels such as "Speaker 1".

It is a desktop window and a command-line tool on one engine, for Windows x64 and Linux x64.

## 1. What it does and does not do

- It writes a transcript and stops there. It writes no minutes, no summary and no action list.
- It works entirely offline. It opens no network connection, sends no telemetry and downloads nothing: the speech models and the audio decoder are inside the package.
- It reads `.wav`, `.m4a` and the audio track of an `.mp4`.
- It transcribes English, French or German. You choose the language for each file. It does not detect the language, and one file holds one language.
- It cleans the text by default (section 4). No generative model rewrites what was said; the raw text stays available.
- It does not translate, does not recognise who a speaker is and does not read live audio.

## 2. Outputs

For `meeting.m4a` the program writes next to the recording (or into the folder you choose):

| File | What it holds |
|---|---|
| `meeting.vtt` | subtitles, one cue per segment, the speaker as a voice tag |
| `meeting.json` | everything: metadata, speakers, and for each segment the cleaned text and the raw text, with word times and confidences; the fields are in `docs/transcript-schema.md` |
| `meeting.txt` | readable text grouped into speaker turns |
| `meeting.raw.vtt`, `meeting.raw.txt` | the same without cleaning; written only when you ask (`--raw`, or the checkbox in the window) |

You pick which of the three formats to write. The JSON always carries both texts, so a raw file is only a convenience. The files are placed as a set, whole or not at all: each is first written under a hidden temporary name in the same folder, and an existing transcript of the same name is set aside and deleted only once every new file is in place. A cancelled or failed run leaves no partial file and the previous transcripts as they were, and a transcript that another program holds open stops the run at once, naming the file.

Examples (made up, to show the shapes).

`meeting.vtt`:

```
WEBVTT

1
00:00:00.500 --> 00:00:03.600
<v Speaker 1>Good morning everyone.</v>

2
00:00:04.100 --> 00:00:09.800
<v Speaker 2>Let's start with the budget for the second quarter.</v>
```

`meeting.txt`:

```
Transcript of meeting.m4a
Duration: 0:42:10
Language: en
Date: 2026-11-10
Speakers: 2

[00:00:00] Speaker 1:
Good morning everyone.

[00:00:04] Speaker 2:
Let's start with the budget for the second quarter.
```

`meeting.json` (shortened):

```json
{
  "schemaVersion": "1",
  "metadata": { "sourceFile": "meeting.m4a", "language": "en", "durationSeconds": 2530.0 },
  "speakers": [ { "id": "S1", "label": "Speaker 1", "speakingSeconds": 812.4 } ],
  "segments": [
    {
      "id": 1, "start": 0.5, "end": 3.6, "speaker": "S1",
      "text": "Good morning everyone.",
      "rawText": "um good morning everyone",
      "confidence": 0.93
    }
  ]
}
```

## 3. Speaker separation

The program tells the voices in a recording apart and labels them `Speaker 1`, `Speaker 2` and so on, in order of first appearance. The labels are anonymous: the program never knows who is who.

- It is on by default. `--no-speakers` (or its checkbox) switches it off; the transcript then has no labels.
- If you know how many people speak, give the number (`--speakers 4`, or the number of speakers in the window's settings). The count is a hint that makes the result steadier; without it the program may find one speaker too many or too few in a real meeting.
- Without the number, a participant who speaks only a few seconds (less than the larger of 4 seconds and 1.5 % of all speaking time) may be merged into the closest voice, and their words then appear under another speaker's label. Give the speaker count to keep everyone.
- Overlapping speech goes to one speaker only: the recogniser reads one voice at a time, so when two people talk at once the louder or earlier one is transcribed and the other is mostly lost.
- Two voices that sound alike, or one person speaking from very different microphones, can be confused.
- At most 10 speakers are told apart; in a larger meeting the voices nearest each other share a label.

## 4. Cleaning

Cleaning only removes words; it never rewrites or reorders them. The rules run per language and are listed word by word in `src/MeetingAudioTranscriptor.Engine/Cleaning/Lexicons/`.

Removed:

- hesitation sounds (`um`, `uh`, `euh`, `ähm`, and stretched forms);
- immediate repetitions and stutters (`I I think` becomes `I think`; `th- the plan` becomes `the plan`);
- clear false starts made only of function words (`I was, I think we should` becomes `I think we should`);
- segments that are only an acknowledgement (`yeah`, `mm-hmm`, `oui`, `d'accord`, `ja`, `genau`).

Never removed:

- meaningful words such as `like`, `well`, `so`, `you know`, `en fait`, `du coup`, `also`, `halt`, because each has a real meaning in some sentences;
- negations (`no`, `not`, `pas`, `nicht`, `nein`);
- numbers and repeated numbers (`two two five` stays), and in English and French a repeated capitalised name (`Anna Anna` stays; German capitalises every noun, so there the repetition rule cannot tell a name);
- an answer to a question (`yes` after a question may be a decision, so it stays), and a question like `Okay?`.

The raw text is always in the JSON, and in `.raw.vtt` and `.raw.txt` with `--raw`.

## 5. Install and run

Download the portable package for your system from the Releases page of this repository, check it against `SHA256SUMS.txt` if you wish, and extract it anywhere. Nothing is installed. The package holds both programs, the models, the decoder and the notices.

### Windows

Extract `meeting-audio-transcriptor-<version>-win-x64.zip` and run `meeting-audio-transcriptor.exe`. The window needs the Microsoft Edge WebView2 runtime, which is part of Windows 11; on Windows 10 install it from `https://developer.microsoft.com/microsoft-edge/webview2/`. Nothing in the package is signed, so the first start of a downloaded copy shows the SmartScreen prompt "Windows protected your PC": choose "More info", then "Run anyway". If you would rather not, extract with a tool that does not mark the files as downloaded, or right-click the zip, open Properties and tick "Unblock" before extracting.

### Linux

The window needs GTK 3 and WebKitGTK 4.1 from the distribution:

```
sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0     # Debian, Ubuntu (24.04 names the first libgtk-3-0t64)
sudo dnf install gtk3 webkit2gtk4.1                 # Fedora
```

Then `tar -xzf meeting-audio-transcriptor-<version>-linux-x64.tar.gz` and run `./meeting-audio-transcriptor` from the extracted folder. The command-line tool needs neither library.

## 6. The desktop window

1. Start `meeting-audio-transcriptor`. A small window opens with the recording, the output folder and Start; the version is beside the title, and a message says so if the models are not in place.
2. Choose the recording: press "Choose file...", or drop a `.m4a`, `.mp4` or `.wav` file on the window. Anything else is refused with a message; when several files are dropped the first supported one is used.
3. Optionally change the output folder; by default the transcripts go next to the recording.
4. The gear button opens the settings, and Done returns:
   - the language the meeting is spoken in, chosen by its flag (English, French or German);
   - the formats to write, and the raw files box if you want `.raw.vtt` and `.raw.txt`;
   - speaker separation, on by default, and the number of speakers (1 to 10) if you know it;
   - once the number is given, a name for each speaker. A name replaces the label of the speaker in that position, in order of first appearance; an empty field keeps `Speaker 1`, `Speaker 2` and so on.
5. Press Start. A progress bar shows the stage (reading the audio, finding speech, recognising words, telling speakers apart, writing the transcript). The settings are locked while it runs. Cancel stops it and writes nothing.
6. When it ends, a summary shows what was written, how long the recording was and how fast the run was. "Open output folder" shows the files.

## 7. The command line

The tool is `meeting-audio-transcriptor-cli` (`.exe` on Windows).

```
meeting-audio-transcriptor-cli transcribe <file> --language en|fr|de [options]
meeting-audio-transcriptor-cli benchmark [options]
meeting-audio-transcriptor-cli models [--models <dir>] [--verify]
meeting-audio-transcriptor-cli --version
meeting-audio-transcriptor-cli <command> --help
```

Options are written `--name value` or `--name=value`; switches take no value.

### transcribe

| Option | Meaning |
|---|---|
| `--language en\|fr\|de` | the language spoken; required |
| `--formats <list>` | any of `vtt`, `json`, `txt`, separated by commas; default all three |
| `--output <dir>` | the folder to write to; default the recording's folder |
| `--no-speakers` | do not tell speakers apart |
| `--speakers <n>` | the number of speakers, 1 to 10, when known; not with `--no-speakers` |
| `--threads <n>` | recognition threads, 1 to 256; default the logical cores, at most 4 |
| `--raw` | also write `.raw.vtt` and `.raw.txt` |
| `--models <dir>` | the models folder; default `MAT_MODELS_DIR`, else `models` beside the program |
| `--quiet` | print nothing but errors |

Progress goes to standard error and the summary to standard output; the summary names the files, the speed, the speaker count, the words and the decoder that read the recording (`wav`, `media-foundation` or `ffmpeg (bundled) ...`). Ctrl+C cancels and leaves no partial file; a second Ctrl+C ends the process at once.

```
meeting-audio-transcriptor-cli transcribe standup.m4a --language en
meeting-audio-transcriptor-cli transcribe reunion.mp4 --language fr --speakers 4 --raw
meeting-audio-transcriptor-cli transcribe sitzung.wav --language de --formats txt,json --output transcripts
```

### models

`models` lists the files a transcription needs and whether each is installed; `--verify` also checks every file's size and SHA-256 against `models.lock`, which takes a while for the large model.

### Environment variables

| Variable | Meaning |
|---|---|
| `MAT_MODELS_DIR` | the models folder; default `models` beside the program |
| `MAT_FFMPEG` | the ffmpeg executable to decode with; default the `ffmpeg` folder beside the program, then the PATH |
| `MAT_PREFER_FFMPEG` | `1` makes Windows try the bundled ffmpeg before Media Foundation, to compare the two decoders |
| `MAT_ORT_QUANT_PRECISION` | `0` or `1` forces the fast or the exact 8-bit kernels; by default the program uses the exact ones on a processor without VNNI (section 8) |
| `MAT_BENCH_DATA` | the benchmark's test set folder; default `bench/data` |
| `MAT_WEBKIT_SOFTWARE` | `1` forces software drawing in the Linux window, `0` forbids it; by default it is chosen from the machine |

### Exit codes

| Code | Meaning |
|---|---|
| 0 | done (also when the recording held no speech and an empty transcript was written) |
| 1 | the command line could not be read |
| 2 | the recording is missing, unreadable, unsupported or damaged |
| 3 | a model file is missing or does not load |
| 4 | the transcripts could not be written (folder, permissions, full disk) |
| 5 | unexpected failure |
| 130 | cancelled with Ctrl+C |

### benchmark and your own speed check

```
meeting-audio-transcriptor-cli benchmark --long-file <recording> --language en --threads 4
```

This runs the real service on your recording, transcripts included, and prints the audio length, the wall time and the speed as a multiple of real time ("12.0x" means one hour of audio in five minutes), the peak memory and writes a result file under `bench/results/` (change it with `--out`; `--label` names it). The first line and the result file also record the processor, whether it reports VNNI and whether the exact 8-bit kernels are on. `--repeat <n>` runs each file n times and reports the median; `--long-file ... --language ...` may be repeated; `--set clips|long|containers|noise|all` runs the test set of section 12. Close other programs while measuring.

## 8. Performance

The design target is a two-hour meeting transcribed in ten minutes or less on a 4-core laptop with AVX2 (12 times real time), within 4 GB of memory. Measured on a 4-core machine with its processor features masked to AVX2 only, which stands for such a laptop (`docs/BENCHMARKS.md` gives the method, every table and the limits of the evidence), a two-hour recording takes:

| Configuration | Two-hour recording | Times real time | Peak memory |
|---|---|---|---|
| AVX2 only, with speaker separation (the target machine) | 4.8 minutes | 25.0 | 2.18 GB |
| AVX2 only, without speaker separation | 4.0 minutes | 29.8 | 1.93 GB |
| AVX2 and AVX-VNNI | 2.8 minutes | 43.3 | 1.95 GB |
| the processor as it is (AMX, AVX-512 VNNI) | 1.6 minutes | 75.5 | 1.98 GB |

These rows are from the acceptance build with an `.m4a` input. One run of the final build on a two-hour WAV file, AVX2 only, with speaker separation, takes 4.7 minutes (280.9 seconds, 25.6 times real time, 1.98 GB) and finds all 6 of the recording's speakers, with a word error rate of 1.2 % on that read English recording (`bench/results/2026-11-10-final-avx2-120min.json`).

On a hosted Windows runner (4 virtual processors, AVX2 without VNNI, shared with other work, so a data point and not the laptop's figure) the command line transcribes a 10-minute `.m4a` at 13.2 times real time through Media Foundation and 13.5 through the bundled ffmpeg, end to end, with the two transcripts differing by 3 words in 1,457 (`docs/BENCHMARKS.md`, chapter 9).

Speed is the same at 10, 30 and 120 minutes within one percent. Word error rate is 1.5 % for read English, 2.7 % for French and 8.3 % for German on the test set, and 24 % for real English meetings with overlapping speech, which is the weak case (`docs/BENCHMARKS.md`, chapters 3 and 6). On a processor without VNNI the program switches to exact 8-bit kernels by itself: they cost about a fifth of the speed and keep the German error at 8.8 % instead of 12.4 % (`docs/decisions/0010-exact-quantised-kernels.md`). Speed depends mostly on the core count and on the processor's vector instructions; a machine below the requirements will be slower, and `--threads` and `--no-speakers` are the two levers (section 11). Measure your own machine with the command of section 7.

## 9. System requirements

- Windows 10 or 11 x64, or a 64-bit Linux with glibc (Ubuntu 22.04 or later, Debian 12, Fedora and equivalents).
- An x64 processor with AVX2 (any laptop from about 2015 on), 4 cores or more.
- 8 GB of memory recommended; the measured peak for a two-hour recording is 2.2 GB (`docs/BENCHMARKS.md`).
- About 1 GB of disk for the extracted package.

## 10. Privacy

Nothing leaves your machine. The program has no network code, and the window refuses every address except its own embedded page. On Windows the web view starts with the browser engine's own background traffic (component updates, usage metrics, sync, SmartScreen lookups) switched off. Outside the program's reach are the Microsoft Edge Update service, which Windows runs on its own for the WebView2 runtime, and anything a group policy forces on; on Linux the .NET runtime also keeps a local diagnostics socket in the temporary folder, which no connection to the outside uses. Where it writes:

- the transcripts, only into the folder you chose (or beside the recording);
- a hidden temporary file for each transcript (`.<name>.<random>.mat-tmp`) while it is written, and a hidden `.mat-bak` copy of an existing transcript while the new set is put in place, both in the same folder and both removed when the run ends;
- the window's web view profile, in `%LOCALAPPDATA%\meeting-audio-transcriptor\webview` on Windows and `$XDG_DATA_HOME/meeting-audio-transcriptor/webview` (default `~/.local/share/meeting-audio-transcriptor/webview`) on Linux.

It writes no registry key. To uninstall, delete the extracted folder and that data folder. The recording itself is only ever read.

## 11. Troubleshooting

| Message or symptom | What to do |
|---|---|
| "ffmpeg was not found" on Linux | the package holds an `ffmpeg` folder beside the program; keep it there, or install ffmpeg, or point `MAT_FFMPEG` at an ffmpeg executable |
| The window says the WebView2 runtime is missing (Windows) | install the runtime from the address in section 5; exit code 2 |
| The window says GTK or WebKitGTK could not be loaded (Linux) | install the packages of section 5, or use the command-line tool |
| "no display could be opened" (Linux) | start the window from a desktop session |
| "not a supported recording" | choose a `.wav`, `.m4a` or `.mp4` file, or convert the recording with a media player or ffmpeg |
| "damaged" or "no audio stream" | the file is cut short or has no audio; play it in a media player, and export it again |
| Windows N edition without the Media Feature Pack | the bundled ffmpeg decodes `.m4a` and `.mp4` instead; if the `ffmpeg` folder was removed, restore it, install the Media Feature Pack from Microsoft, or convert the file to `.wav` |
| "models folder lacks ..." | run from the extracted package, or pass `--models <dir>` or set `MAT_MODELS_DIR`; from source see section 12 |
| Slow on an older or small machine | `--threads 2` if the machine is also busy, `--no-speakers` to skip speaker separation, and close other programs |
| Fewer speakers than the meeting had | a participant who spoke only a few seconds is merged into the closest voice: run again with `--speakers <n>` (or the number of speakers in the window's settings) |
| A hidden `.mat-bak` file remains in the output folder after a crash | it is the previous transcript, set aside while the new set was placed: rename it to the transcript's name (drop the leading dot, the random part and the `.mat-bak` ending), or delete it |
| Quiet speakers missing | compare `.txt` with `.raw.txt`; if the words are in the raw file, cleaning removed them, and if not the recogniser did not hear them |

## 12. Building from source

Prerequisites: the .NET 10 SDK (pinned in `global.json`), Node 22 or later, and ffmpeg on the PATH for the integration tests. No npm package is installed.

```
node build/fetch-models.mjs --into models           # the speech, voice-activity and speaker models, verified against build/models.lock
node build/fetch-ffmpeg.mjs --rid linux-x64         # or win-x64; lands in tools/ffmpeg/<rid>/, then set MAT_FFMPEG to it for a source build
dotnet build -warnaserror
dotnet test --filter-not-trait "Tier=deep" --filter-not-trait "Tier=integration" --filter-not-trait "Tier=models" --ignore-exit-code 8
dotnet run --project src/MeetingAudioTranscriptor.Cli -- transcribe meeting.m4a --language en
dotnet run --project src/MeetingAudioTranscriptor.Shell
dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror     # the Windows host, from Linux
```

A source build finds its models in `models/` of the checkout (or `MAT_MODELS_DIR`). The test tiers and their commands are in `docs/TESTING.md`. The speech test set used by the benchmark is built from public sources with `node bench/testset/fetch-testset.mjs` (see `bench/testset/README.md`). Releases are cut only by the tag workflow, described in `docs/RELEASING.md`.

## 13. How it works

A recording goes through these stages, each a folder of `src/MeetingAudioTranscriptor.Engine/`:

1. Decode: the engine's own reader for WAV, Media Foundation on Windows (the bundled LGPL ffmpeg is its fallback) and the bundled ffmpeg elsewhere for `.m4a` and `.mp4`, to mono samples (`docs/decisions/0009-audio-decoding.md`).
2. Resample to 16 kHz and cut the audio at silences with Silero VAD, so silence and noise are never recognised.
3. Recognise each chunk with IBM Granite Speech 5.0 470M TurboCTC (int8, ONNX Runtime, CPU), several chunks at once over one shared model (`docs/decisions/0002-recogniser.md`, `docs/decisions/0003-threading-model.md`).
4. Tell speakers apart with WeSpeaker CAM++ voice embeddings and clustering (`docs/decisions/0005-speaker-separation.md`).
5. Cut the words into segments and clean them with the per-language rules of section 4.
6. Write the files.

Why these choices, and the measurements behind them, are in `docs/decisions/`; the packaging is `docs/decisions/0007-models-and-packaging.md`; the two window hosts are `docs/decisions/0006-desktop-hosts.md`. The charters of each part start at `CLAUDE.md`.

## 14. Licence

MIT, see `LICENSE`. The models, the decoder and the libraries the package carries are under their own licences, credited with their texts in `NOTICE`.
