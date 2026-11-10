# Final review

Working document. Review of the repository at `claude/admiring-bell-dajief` (48512df, fast-forwarded
into the reviewer's worktree) against the build prompt, on 2026-11-10, on the shared 4-core build
machine (load average about 3.3 from another engineer during the runs, so timings are lower bounds).
Environment: `MAT_MODELS_DIR=/home/user/meeting-audio-transcriptor-models`,
`MAT_FFMPEG=.../ffmpeg/linux-x64/ffmpeg` (BtbN n8.1.3 LGPL), `MAT_BENCH_DATA=.../bench/data`,
`DOTNET_ROOT=/opt/dotnet`.

## 1. Requirements

Verdicts: met, partly, not met, cannot verify here.

### 1.1 Functional (prompt section 3)

| Requirement | Evidence (run by the reviewer) | Verdict |
|---|---|---|
| Input `.wav`, `.m4a`, `.mp4` (audio track) | CLI on `long/en-10min.wav`, `.m4a`, `.mp4`: 1457 words each, same 78 segments, decoder `wav` / `ffmpeg (MAT_FFMPEG)`; window run on a generated `.mp4` with a video track (de) and an `.m4a` (en) | met |
| Language chosen per file, en/fr/de | `--language fr|de|en` runs; `--language it` exit 1 with the allowed list; window radio buttons | met (the recogniser ignores the choice, it selects cleaning only: decision 0002, 0011) |
| Outputs `.vtt`, `.json`, `.txt`, any combination | `--formats txt,json`, `--formats vtt,txt`, default all three; `--formats srt` exit 1 | met |
| VTT: segment cues with voice tags | parsed: starts `WEBVTT`, 78 cues, every cue `<v Speaker N>`, starts monotonic, end > start; no voice tag with `--no-speakers`; `&`, `<`, `>` escaped (code read) | met |
| TXT: turns with start timestamp and label, paragraphs | `[00:00:41] Speaker 2:` headings, paragraphs split at gaps over 2 s or 700 characters; timestamped paragraphs without speakers | met |
| JSON: metadata, speakers, segments with id/start/end/speaker/text/rawText/confidence/words | validated against `docs/transcript.schema.json` with `jsonschema` 4.26: valid; metadata holds source file, duration, speech seconds, language, created time, processing seconds, application version, models (role and name), settings (separation, hint, threads, formats, raw files); 1457 word timings, monotonic | met (models carry no revision: decision 0011) |
| Schema published in `docs/` | `docs/transcript-schema.md`, `docs/transcript.schema.json` | met |
| Speaker labels, off switch, count hint | `--speakers 3` gives 3; `--no-speakers` gives "not separated"; `--speakers 11` and `--speakers` with `--no-speakers` refused (exit 1); en-10min 6 of 6 speakers, fr 4/4, de 5/5 in the benchmark | met (at most 10 speakers: now recorded in 0005, 0011 and README) |
| Cleaned default, raw kept in JSON and on request | `rawText` on every segment; `--raw` writes `.raw.vtt`, `.raw.txt` | met |
| CLI: file, language, formats, output, diarisation, threads, benchmark | every option exercised; `models [--verify]`, `--version`; exit codes 0, 1, 2, 4, 130 observed | met |
| Desktop UI: pick or drop, language, formats, diarisation, start, progress, cancel, open folder | Shell integration tier 4/4 (drop, Start, `run:finished`, refusals, policy, real run on fr wav); reviewer's own automation run on mp4 (de) and m4a (en) completed with three files each | met on Linux; Windows cannot verify here |
| Windows x64 and Linux x64, WebView2 on Windows, Linux webview behind one interface | `IDesktopHost` with WebView2 and WebKitGTK 4.1 hosts on one page (decision 0006); `dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror` and the Cli for win-x64: 0 warnings | met (Windows run: cannot verify here) |

### 1.2 Non-functional (prompt section 3)

| Requirement | Evidence | Verdict |
|---|---|---|
| Offline at run time, models shipped | `strace -f -e trace=network` over a WAV run and an M4A run (ffmpeg child included): only `socket(AF_UNIX)` + `bind`/`listen` on `/tmp/dotnet-diagnostic-<pid>-...-socket` (the .NET diagnostics IPC, local); no AF_INET or AF_INET6. Window under `xvfb-run` with strace: AF_UNIX to X11 and the D-Bus system bus, two AF_NETLINK `RTM_GETROUTE` queries (GLib's network monitor reading the routing table, local); no AF_INET/AF_INET6. Source guard green; CSP `connect-src 'none'`; WebView2 reputation check off | met on Linux; Windows: the WebView2 runtime's own background traffic is not switched off (defect D1) |
| 2 hours in 10 minutes or less at 4 threads, AVX2 | acceptance file `2026-11-10-accept-avx2-120min-m4a.json`: 287.9 s, 25.0x, 2.18 GB. Reviewer's masked `benchmark --set long --threads 4` (Release, `CPUIDMASK=avx2`, exact kernels on): de 26.0x, en 25.1x, fr 25.3x against the acceptance 24.5, 24.9, 24.5; WER identical to the acceptance file (8.76, 1.15, 2.51) | met (laptop itself: cannot verify here) |
| Peak memory 4 GB or less | reviewer's 30-minute masked CLI run: max RSS 1.83 GB, 24.2x including the model load; acceptance 2-hour 2.18 GB | met |
| Hand-written components, dependencies justified | packages: ONNX Runtime, WebView2, xunit.v3 only (`Directory.Packages.props`); WAV, resampler, FFT, features, CTC, VAD wrapper, clustering, cleaning, writers, WER in source; decision 0008 | met |
| Decoding: Media Foundation on Windows, bundled pinned LGPL ffmpeg on Linux, PATH fallback, ffmpeg fallback on Windows | `build/ffmpeg.lock` pins BtbN LGPL builds by size and SHA-256; locator order `MAT_FFMPEG`, beside the app, PATH; Windows MF then ffmpeg (decision 0009) | met (MF: cannot verify here; Wine and CI evidence only) |
| Licences allow commercial use, recorded in `NOTICE` | Granite Apache-2.0, Silero MIT, WeSpeaker Apache-2.0 + CC-BY-4.0 weights (VoxCeleb note), ONNX Runtime MIT, WebView2 BSD-style, .NET MIT, ffmpeg LGPL-3.0+ (unmodified, separate process, replaceable, source pointer); `System.Numerics.Tensors` (MIT, a dependency of ONNX Runtime) was not named: added to the .NET section | met |
| Errors: corrupt, unsupported, cancel, disk full | `garbage.m4a`, a 30 KB truncated `.m4a`: exit 2 with ffmpeg's words; README.md as input: exit 2 "not a supported recording"; missing file: exit 2; SIGINT at 20 %: exit 130, output folder empty, no ffmpeg left; 64 KB tmpfs as output: exit 4 "the disk ... is full", folder empty | met |

### 1.3 Cleaning (prompt section 5)

| Requirement | Evidence | Verdict |
|---|---|---|
| Fillers, stutters, repetitions, false starts, backchannel removed | AMI clips raw against cleaned: `um` removed in five places, `well well`, `than than`, `and and`, `my my`, `you'd you'd`, `i don't i don't`, `please please` collapsed; `Yep.`, `Okay,`, `Yeah,` segments removed as backchannel; `And.` removed as low-confidence | met |
| Never drop content words, numbers, names, decisions | every change on the three AMI clips reviewed by hand: nothing meaningful removed; numbers protected (`twelve fifty`, `twenty five`); left in place: `it should be i it should do`, an inner `yeah` (conservative, acceptable). One meaningful repetition lost in German: `denkt und denkt` became `denkt` (the reference has both): recorded in 0011 | met with one recorded exception |
| Retention at or above 99 % | reviewer's run: en 1.000, fr 1.000, de 0.997, all 0.999; acceptance clips and long files likewise | met |
| Silence and noise give no text | the four noise files through the CLI: 0 segments, 0 speakers, 0 speech seconds, empty VTT, exit 0 | met (the TXT header said "Speakers: not separated" though separation was on: fixed, now "none") |
| No generative rewrite; language-specific rules | rule-based `SegmentCleaner`, lexicons per language | met |

### 1.4 Engine (prompt section 6)

| Requirement | Evidence | Verdict |
|---|---|---|
| Staged streaming pipeline with overlap | producer thread (decode, resample, VAD, chunking), N workers over one model, bounded queue of 2 per worker; code read; 2.2 of BENCHMARKS shows decode hidden behind recognition | met |
| Recogniser compared with current candidates | Parakeet v3 fp32/int8, Parakeet Ultra int8, Granite, Whisper turbo (decision 0002) | met |
| Diarisation decided by benchmark, on by default if it fits | on by default, 46 s of a 2-hour run (BENCHMARKS 2) | met (embedding windows over VAD regions, no segmentation model: 0005) |
| Quality gate within 1.0 point of best candidate per language | French and German best measured; English only when read and AMI are pooled (7.90 against 8.10); on AMI alone 24.0 against 18.2 for Parakeet v3 fp32, recorded under the priority rule in 0002 | partly (recorded deviation; meetings are the product's main input) |
| Levers in order, quality cost recorded | VAD, int8, workers versus intra-op, chunk length, exact kernels (0003, 0010) | met |

### 1.5 Definition of done (prompt section 11)

| Bullet | Verdict |
|---|---|
| CLI and UI transcribe m4a, mp4, wav in en, fr, de to vtt, json, txt on Linux | met: CLI on all three containers and languages; UI on wav (fr, test), mp4 (de) and m4a (en) by the reviewer |
| Windows target compiles; Windows paths thin, CI-covered, listed in the checklist | met for compile and checklist; CI results not readable here (STATE records a green run 42906481860) |
| 2 hours in 10 minutes at the reference thread count, measured | met: 4.8 min, 25.0x under the AVX2 mask; reviewer's 10-minute recheck 25 to 26x |
| Quality gates met or deviations recorded | met with the recorded English-meetings deviation |
| Tests pass; CI and release workflows; README, NOTICE, CLAUDE.md, decisions, benchmarks, RELEASING | met (counts in chapter 4); `pwsh` absent here, so `publish.ps1` and `release.ps1` were read, not run |
| Everything on `main` | not met at review time: `origin/main` (de08870) is 18 commits behind the work branch and the local `main` is the initial commit; the orchestrator's final merge is pending |

## 2. Defects

| Id | Severity | Where | Scenario | Status |
|---|---|---|---|---|
| D1 | medium (Windows) | `Shell/Windows/WindowsDesktopHost.cs`, environment creation | The WebView2 runtime (Edge) is started without `AdditionalBrowserArguments`, so its own background networking (component updater, variations, crash reporting) is not switched off; checklist item 21 is likely to show `msedgewebview2` connections to Microsoft on a corporate laptop behind a proxy. Suggested: `CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-background-networking --disable-component-update --no-pings" }` (plus `--disable-features=` for the Edge services the checklist finds), tested on Windows | fixed: `Shell/BrowserArguments.cs` (documented Chromium switches and WebView2 flag-list features) passed as `AdditionalBrowserArguments`, custom crash reporting on, tracking prevention off; the exact list is asserted by `BrowserArgumentsTests`; the effect is checked on Windows by the checklist |
| D2 | low | `App/TranscriptFiles.cs` `Write`, the move loop | When the k-th `File.Move` fails (a previous `meeting.json` open in another program on Windows), files 1..k-1 are already the new run's and the rest the old run's: each file is whole but the set is mixed, and README chapter 2 says the old transcript "is replaced only when the new one is complete". Suggested: move existing targets aside to `.bak`, move the new ones in, roll back on failure, then delete the backups | fixed: every destination checked for exclusive write first, previous versions renamed to `.mat-bak`, rolled back on a failed rename, `OutputNotWritable` naming the file; `TranscriptFilesTests` fails each step through an injected file system |
| D3 | low | `App/TranscriptFiles.cs` `TemporaryPath` | The temporary is `<name>.<ext>.tmp`; a user file of exactly that name in the output folder is overwritten and then deleted. Suggested: add a per-run random part to the name | fixed: `.<name>.<random>.mat-tmp`, created with `CreateNew` and redrawn on a collision; `TranscriptFilesTests` keeps a user's `meeting.vtt.tmp` |
| D4 | low | `App/TranscriptionService.Run` | Models load (several seconds, 1.5 GB) before the input is opened, so on a first run a damaged or unsupported file is reported only after the load (seen as "Loading models 0%" before the decode error). Suggested: open or at least sniff the input before `Models(...)` | fixed: the recording is opened (sniff, probe, decoder start) before the models are checked and loaded; `ABadRecordingIsReportedBeforeAnyModelLoads` asserts no load |
| D5 | low | `build/release.ps1`, the version comparison | Pre-release labels are stripped before comparing, so `v1.0.0-rc.2` is accepted after `v1.0.0` is released. Suggested: refuse a pre-release whose number is already released | open (not run: no `pwsh` here) |
| D6 | low | runtime configuration | The .NET diagnostics IPC listens on a Unix socket in `/tmp` during every run (seen by strace). Local only, but a hardened build can switch it off (`DOTNET_EnableDiagnostics=0`) | open |
| D7 | low | `App/Bridge/RunCoordinator.Normalise` | The fallback sentence for `noSpeech` says no transcript was written, while the service writes an empty one; reached only when a transcriber returns no message | open |
| F1 | low | `Engine/Writers/PlainTextWriter.cs` | A run with separation on that heard no speech printed "Speakers: not separated" | fixed, with a test |
| F2 | low | `Guards.Tests/PurposeHeaderGuardTests` | The script and workflow header rule passed when it found no file | fixed: asserts it found both groups |
| F3 | low | `Guards.Tests/NoNetworkGuardTests` | The forbidden list missed `UdpClient`, `HttpListener` and `WebSocket` | fixed |
| F4 | doc | `README.md` | TXT example header `Duration: 00:42:10.0` differs from the writer (`0:42:10`); "repeated names stay" is untrue for German; the 10-speaker bound was unstated | fixed |
| F5 | doc | `src/MeetingAudioTranscriptor.Media/CLAUDE.md` | Said no environment variable sets `PreferFfmpeg` (`MAT_PREFER_FFMPEG` does) and that Media is the only project allowing unsafe code (the Shell does too) | fixed |
| F6 | doc | `NOTICE` | `System.Numerics.Tensors` shipped beside ONNX Runtime was not named | fixed |
| F7 | hygiene | `.gitignore` | `out/`, the publish folder `docs/TESTING.md` uses, was not ignored | fixed |

Read and found sound: the pipeline's failure and cancellation path (first failure kept, others cancelled, every thread joined before rethrow, queue completed in `finally`); CTC collapse and word timing (blank and control runs, punctuation attachment, end clamped to the next word); the gain normaliser's interpolation; VTT escaping and monotonic cue starts; `EmbeddedAssets` (manifest lookup only, dot segments, backslashes and control characters refused); the Windows request filter, navigation cancel and the Linux content filter; ffmpeg as an argument list with the `file:` protocol, stderr drained asynchronously, kill of the process tree and reap on dispose (a path with spaces, accents, quotes, `$` and `;` transcribed); Media Foundation pointers released per step and COM uninitialised on its own thread; the fetch tools (lock shape validated, relative paths enforced, download to `.part` renamed only after size and hash match, cache copies verified first); workflow inputs passed through `env:` and `contents: read` except the publish job.

## 3. Deviations from the prompt

| Deviation | Recorded in |
|---|---|
| Recogniser is Granite Speech 5.0 470M TurboCTC, not Parakeet | 0002 |
| English quality gate met only with read and meeting speech pooled; AMI 5.8 points behind Parakeet v3 fp32 | 0002 (priority rule), BENCHMARKS 3 and 6 |
| Engine figures compared with Python spike figures of the other candidates | 0002 |
| Timings on a masked server processor standing in for the laptop; 8 threads not measurable | 0004, BENCHMARKS 9 |
| Speaker separation from embedding windows over VAD regions, no segmentation model; overlap goes to one speaker | 0005, README 3 |
| At most 10 speakers | added to 0005, 0011, README 3 |
| ffmpeg bundled on Windows too | 0007, 0009 |
| Packages neither trimmed nor ReadyToRun (the reference trims) | added in 0011 (0007 named only the native library) |
| Language choice selects cleaning only | 0002, added to 0011 |
| Word times at 80 ms resolution | added in 0011 |
| JSON names models without revision | added in 0011 |
| German repetition rule without name guard | added in 0011, README 4 |
| French and German test material is read speech, no real meetings | BENCHMARKS 9 |
| Python used for spikes only, kept under `bench/spikes`, nothing ships | prompt allows it; `CLAUDE.md` 3 |
| Work not yet on `main` | to be closed by the orchestrator's merge |

## 4. Commands run and key outputs

```
git merge --ff-only claude/admiring-bell-dajief                          -> fast-forward to 48512df
nice -n 10 dotnet build -warnaserror -m:1                                -> 0 warnings, 0 errors
dotnet test (unit tier, before fixes)                                    -> 1328 total, 1323 passed, 5 skipped
dotnet test (unit tier, after fixes)                                     -> 1329 total, 1324 passed, 5 skipped
dotnet test --filter-trait "Tier=integration" --ignore-exit-code 8       -> 26 total, 23 passed, 3 skipped (Media Foundation, Windows only)
dotnet test --filter-trait "Tier=models" --ignore-exit-code 8            -> 80 total, 80 passed (2 min 53 s)
dotnet test --project tests/MeetingAudioTranscriptor.Shell.Tests --filter-trait "Tier=integration"  -> 4 of 4 passed
dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror  -> 0 warnings
node --test "frontend/**/*.test.js"                                      -> 84 passed
node --test build/*.test.mjs                                             -> 37 passed
node build/fetch-models.mjs --into $MAT_MODELS_DIR --offline             -> 4 files ok, already verified
cli transcribe long/en-10min.{wav,m4a,mp4} --language en --raw --threads 4  -> 53.0x / 50.3x / 51.2x unmasked, 1.69 GB, 6 speakers, 1457 words
cli transcribe clips/fr/fr-multi-01.wav --language fr --speakers 3 --threads 2  -> 3 speakers
cli transcribe clips/de/de-multi-01.wav --language de --formats txt,json --threads 1  -> 192 words, 190 after cleaning
cli transcribe ... --no-speakers --formats vtt,txt                       -> no voice tags, "Speakers: not separated"
cli transcribe noise/*.wav                                               -> 0 segments each, exit 0
strace -f -qq -e trace=network cli transcribe (wav and m4a)              -> AF_UNIX diagnostics socket only
xvfb-run strace -f -e trace=connect,socket,sendto shell (demo, 6 s)      -> AF_UNIX (X11, D-Bus), AF_NETLINK route query; no AF_INET/AF_INET6
SIGINT after 8 s on en-10min.m4a                                         -> exit 130, "no transcript file was written", folder empty
tmpfs of 64 KB as --output                                               -> exit 4, disk-full sentence, folder empty
MASK cli benchmark --set long --threads 4 (Release)                      -> 26.0x / 25.1x / 25.3x, WER 8.76 / 1.15 / 2.51, retention 0.999, 1856 MB
MASK /usr/bin/time cli transcribe long/en-30min.wav --threads 4          -> 74.7 s, 24.2x, max RSS 1.83 GB
git ls-files | du                                                       -> largest tracked file 100 KB; no model, binary or audio (one 64 KB screenshot in docs/build)
reference repository comparison (6-line shingles)                        -> P/Invoke declarations and short boilerplate (proxy re-exec, sha256 helper, csproj skeletons) only; no long verbatim copy, no client content
```

BENCHMARKS spot checks against the result files: 2-hour AVX2 287.9 s, 25.01x, 2.18 GB; no speakers 241.9 s, 29.77x, 1.93 GB; AVX2 clips English read WER 2.17; AMI 24.00; fast-kernel German 12.42; 10-minute English peak 1.86 GB (per file). All match.

## 5. Issues in files the reviewer was asked not to edit

- `docs/WINDOWS-VERIFICATION.md` item 21 expects no connection at all from `msedgewebview2`; without D1 fixed the runtime's own update and configuration traffic will appear. Either fix D1 or name the Microsoft endpoints that are acceptable.
- `docs/RELEASING.md` chapter 4 says `--version` prints `X.Y.Z`; it prints `meeting-audio-transcriptor X.Y.Z` (the Windows checklist has it right).
- `docs/RELEASING.md` chapter 2 says the final `0.2.0` may follow `0.2.0-rc.1`, true, but `release.ps1` also accepts `0.2.0-rc.2` after `0.2.0` (D5).
- `docs/TESTING.md` chapter 3: the network guard now also names `UdpClient`, `HttpListener` and `WebSocket`; the guard's description there stays true as written ("an HTTP or socket type").
- `docs/TESTING.md` uses `-o out/linux`; that folder is now ignored by git.
