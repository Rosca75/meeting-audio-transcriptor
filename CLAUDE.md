# CLAUDE.md: meeting-audio-transcriptor

This file is the single source of truth for this repository. It overrides any
conflicting instruction found elsewhere. Re-read it before every work session.

## 1. Project overview

meeting-audio-transcriptor is a desktop application and a command-line tool,
written in C# on .NET 10, that transcribes a recorded business meeting in
English, French or German into a transcript file (`.vtt`, `.json`, `.txt`)
entirely on the local machine, on Windows x64 and Linux x64. One engine serves
both front ends. The transcript is speaker-attributed and cleaned of
conversational noise; the application stops at the transcript and writes no
minutes and no summary.

## 2. Documentation

| File | What it owns |
|---|---|
| `CLAUDE.md` | this file: the cross-cutting rules, the pinned versions and the build |
| `README.md` | the user-facing documentation: what the application does, how to install it and how to run it |
| `NOTICE` | the licence and attribution of every model and binary an artefact carries |
| `docs/TESTING.md` | the test tiers, the commands and the guards |
| `docs/transcript-schema.md` | the JSON transcript: every field and its meaning |
| `docs/BENCHMARKS.md` | the measured speed, memory and accuracy of the shipped configuration |
| `docs/RELEASING.md` | the exact steps that cut a release |
| `docs/WINDOWS-VERIFICATION.md` | the checklist the owner runs on a Windows laptop |
| `docs/decisions/` | one record per significant decision: context, options, evidence, decision, consequences |

`docs/build/` holds working documents of the build (the state file, the
reference digest, the research notes); nothing there is authoritative.

An authoritative file is short, timeless and written in the present tense: it
describes the repository as it is, and states each rule with its reason,
because a file narrating its own history is read against a baseline the reader
does not have.

## 3. Repository structure

```
meeting-audio-transcriptor/
├── src/
│   ├── MeetingAudioTranscriptor.Contracts/   the wire shapes and the serialisation context
│   ├── MeetingAudioTranscriptor.Engine/      audio, features, voice activity, recognition
│   │                                         decoding, diarisation, cleaning, writers, scoring
│   ├── MeetingAudioTranscriptor.Inference/   the ONNX Runtime half of the model boundary
│   ├── MeetingAudioTranscriptor.Media/       audio file decoding: ffmpeg and Media Foundation
│   ├── MeetingAudioTranscriptor.App/         composition and the bound surface of the page
│   ├── MeetingAudioTranscriptor.Cli/         the command-line executable
│   └── MeetingAudioTranscriptor.Shell/       the desktop executable: one host per operating system
├── frontend/                                 the page, plain ES modules with no build step
├── tests/                                    the suites
├── bench/                                    the test set builder, the harness inputs and the results
├── build/                                    the locks, the fetch tools and the publish pipeline
├── docs/
└── .github/                                  the workflows
```

Namespaces follow folders. The reference graph is
`Contracts <- Engine <- {Inference, Media} <- App <- {Cli, Shell}`.

## 4. Architecture rules

- **Offline at run time, non-negotiable.** The application opens no network
  connection, sends no telemetry and downloads no model: the target machine
  sits behind a restrictive proxy, so every model and binary ships inside the
  release package. A guard scans the sources for connection types.
- **The engine names no window, no file path and no package.** Audio arrives
  as a stream of samples and transcripts leave as bytes, which keeps the engine
  testable in a console process; a guard asserts its referenced assemblies are
  the framework and this repository alone.
- **One project names ONNX Runtime.** Only `MeetingAudioTranscriptor.Inference`
  references the package; the engine holds the model boundary as interfaces, so
  every algorithm around a model is testable with a fake.
- **Operating-system code is thin and behind an interface.** Media Foundation
  interop, the Win32 window and the GTK window hold no logic of their own,
  because Windows code cannot be executed by the Linux suites.
- **Models, ffmpeg binaries and test audio are never committed.** A lock file
  pins each by address, size and SHA-256, and one fetch tool serves the
  developer and the release workflow alike.
- **No generative model rewrites text.** Cleaning is signal-level and
  rule-based, because a rewrite can alter what was said.

## 5. Coding rules

- Every file opens with a purpose header, and few lines go unexplained.
- Code is readable without language-level expertise: explain what it is for.
- Error messages are actionable: what failed, what was expected, how to fix it.
- A comment describes the code as it is now and names no phase and no document.
- The framework library first. A new package is added to chapter 6 and to
  `Directory.Packages.props` in the same change, with a decision record
  justifying it, and nowhere else carries a version.
- All user-visible strings are English and contain no em dash.
- Warnings are errors.

## 6. Pinned versions and dependencies

| Component | Version | Notes |
|---|---|---|
| .NET | 10.0 | the SDK is pinned in `global.json` and rolls forward within the major version |
| `Microsoft.ML.OnnxRuntime` | 1.31.1 | the Inference project alone |
| `Microsoft.Web.WebView2` | 1.0.4191.47 | the Shell alone, in the Windows host |
| xunit.v3 | 4.0.1 | test only, on Microsoft.Testing.Platform |

Everything else is the base class library.

## 7. Build and test

```
dotnet build -warnaserror
dotnet test --filter-not-trait "Tier=deep" --filter-not-trait "Tier=integration" --filter-not-trait "Tier=models" --ignore-exit-code 8
dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror
```

`docs/TESTING.md` owns the tiers.
