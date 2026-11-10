<!-- The changes of every release, newest first, in the Keep a Changelog form. build/release.ps1 refuses a version that has no "## [X.Y.Z]" heading here. -->
# Changelog

All notable changes to meeting-audio-transcriptor are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). A version number comes from its git tag alone.

## [Unreleased]

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
- Speaker separation from WeSpeaker CAM++ voice embeddings and clustering, with an optional speaker count of 1 to 10 in the command line and the window.
- Conversational noise cleaned by signal-level, rule-based steps; no generative model rewrites
  what was said.
- Audio decoding with the bundled LGPL ffmpeg on both systems, and Media Foundation on Windows.
- A documented JSON transcript (`docs/transcript-schema.md`) and a benchmark command that
  measures speed, memory and accuracy. `docs/BENCHMARKS.md` reports the measured results: a
  two-hour recording in 4.8 minutes (25 times real time, 2.2 GB) on a processor limited to AVX2,
  with the accuracy per language, the cleaning and speaker results and how to reproduce each table.
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
