<!-- The changes of every release, newest first, in the Keep a Changelog form. build/release.ps1 refuses a version that has no "## [X.Y.Z]" heading here. -->
# Changelog

All notable changes to meeting-audio-transcriptor are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). A version number comes from its git tag alone.

## [Unreleased]

### Added

- The window names the speakers: with a number of speakers entered, one name per speaker can be
  typed, and the `.vtt`, `.json` and `.txt` transcripts carry the names in place of `Speaker 1`,
  `Speaker 2`. The run request gains the optional `speakerNames` (`frontend/BRIDGE.md`, chapter 4).

### Changed

- The window is compact (560 by 440) and never scrolls: it shows the recording, the output folder
  and Start, and every setting sits behind a gear button. Languages are chosen by flag, the title
  carries a logo and the colours follow the corporate palette in both light and dark schemes.
- The Windows executable has its own icon, the same mark as the window's logo, in Explorer, the
  title bar and the taskbar.

## [0.1.0]

The first release: an offline meeting transcriber for Windows x64 and Linux x64.

### Added

- Transcribes a recorded meeting in English, French or German into `.vtt`, `.json` and `.txt`
  files, entirely on the local machine: no network connection, no telemetry and no download at
  run time.
- One engine behind two front ends: the command-line tool `meeting-audio-transcriptor-cli`
  (`transcribe`, `benchmark` and `models` commands) and the desktop executable
  `meeting-audio-transcriptor`.
- Speech recognition with the IBM Granite Speech 5.0 470M TurboCTC model, run on the CPU with
  ONNX Runtime.
- Voice activity detection with Silero VAD, so silence and noise are never sent to the recogniser.
- Speaker separation from WeSpeaker CAM++ voice embeddings and clustering, with an optional speaker count of 1 to 10 in the command line and the window. A speaker below the larger of 4 seconds and 1.5 % of the speaking time is merged into the nearest voice, and the count is exact on the 10-minute, 30-minute and two-hour test files (`docs/decisions/0005-speaker-separation.md`).
- Conversational noise cleaned by signal-level, rule-based steps; no generative model rewrites
  what was said.
- Audio decoding with Media Foundation on Windows and the bundled LGPL ffmpeg on both systems. CI runs the real application on a hosted Windows runner: a 10-minute `.m4a` is decoded by Media Foundation and by ffmpeg to transcripts that differ by 3 words in 1,457, and the clip word error rates equal Linux's within 0.4 point (`docs/BENCHMARKS.md`, chapter 9).
- A documented JSON transcript (`docs/transcript-schema.md`) and a benchmark command that
  measures speed, memory and accuracy. `docs/BENCHMARKS.md` reports the measured results: a
  two-hour recording in 4.8 minutes (25 times real time, 2.2 GB) on a processor limited to AVX2,
  confirmed on the final build at 4.7 minutes (25.6 times real time, 2.0 GB, 6 of 6 speakers), with the accuracy per language, the cleaning and speaker results and how to reproduce each table.
- Exact 8-bit matrix kernels, chosen automatically on a processor without VNNI, which keep the
  German word error rate at 8.8 % instead of 12.4 % for about a fifth of the speed
  (`docs/decisions/0010-exact-quantised-kernels.md`). The benchmark records whether the processor
  reports VNNI and whether the exact kernels are on.
- `MAT_PREFER_FFMPEG=1` makes a Windows run try the bundled ffmpeg before Media Foundation, and the
  summary of `transcribe` names the decoder that read the recording.
- Two portable packages, `meeting-audio-transcriptor-<version>-win-x64.zip` and
  `meeting-audio-transcriptor-<version>-linux-x64.tar.gz`, each a single self-contained folder
  holding both executables, the models, ffmpeg and the notices; nothing to install.
- A release pipeline: `build/release.ps1` tags a version, and the release workflow builds both
  packages on hosted runners and publishes them with SHA-256 checksums.

### Fixed

- Transcripts are placed as a set: existing files are set aside and put back if any step fails, so
  a folder never holds some new and some old transcripts, and the temporary files are hidden files
  with random names (`.<name>.<random>.mat-tmp`, `.mat-bak`) that never overwrite a user's file.
- A missing, damaged or unsupported recording is reported before the models load.
- The Windows web view starts with its own background networking (component updates, metrics,
  sync, SmartScreen lookups) switched off.
- Media Foundation reads the first decoded block while opening, because its AAC decoder announces
  a file at 24 kHz or below at twice its rate and corrects it with that block.
- The release script orders versions by semantic-version precedence, so a pre-release of an
  already released version is refused.
- A run that finds no speech writes "Speakers: none" in the text transcript and the fallback
  message says the files were written empty.

### Known limits

- The .NET runtime keeps a local diagnostics socket on Linux; it has no supported build-time
  switch, and only the `DOTNET_EnableDiagnostics=0` environment variable removes it.
- Real meetings with overlapping speech score about 24 % word error rate on the AMI excerpts, and
  the speaker count is one short on two of three of them without the count hint.
- The window and the speed on a Windows laptop are checked by hand (`docs/WINDOWS-VERIFICATION.md`).
