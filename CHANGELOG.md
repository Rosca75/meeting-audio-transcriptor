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
- Speaker attribution from a speaker segmentation model and a speaker embedding model.
- Conversational noise cleaned by signal-level, rule-based steps; no generative model rewrites
  what was said.
- Audio decoding with the bundled LGPL ffmpeg on both systems, and Media Foundation on Windows.
- A documented JSON transcript (`docs/transcript-schema.md`) and a benchmark command that
  measures speed, memory and accuracy.
- Two portable packages, `meeting-audio-transcriptor-<version>-win-x64.zip` and
  `meeting-audio-transcriptor-<version>-linux-x64.tar.gz`, each a single self-contained folder
  holding both executables, the models, ffmpeg and the notices; nothing to install.
- A release pipeline: `build/release.ps1` tags a version, and the release workflow builds both
  packages on hosted runners and publishes them with SHA-256 checksums.
