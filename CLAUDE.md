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
| `CLAUDE.md` | this file: the cross-cutting rules, the vocabulary, the pinned versions and the build |
| `src/MeetingAudioTranscriptor.Engine/CLAUDE.md` | the engine charter: the stages, the model boundary, threading, determinism and cleaning |
| `src/MeetingAudioTranscriptor.App/CLAUDE.md` | the App charter: the service, the outcome kinds, atomic writing and the bridge |
| `src/MeetingAudioTranscriptor.Media/CLAUDE.md` | the media charter: the decoder order, ffmpeg and Media Foundation |
| `src/MeetingAudioTranscriptor.Shell/CLAUDE.md` | the shell charter: the two window hosts, the threads, the embedded page and the automation hook |
| `frontend/CLAUDE.md` | the page charter: the module map, the discipline rules, the layout contract |
| `frontend/BRIDGE.md` | the host to page contract: every method, argument, result and event |
| `tests/CLAUDE.md` | which suite holds what, the traits, the environment variables and the guard rules |
| `README.md` | the user-facing documentation: what the application does, how to install and run it |
| `NOTICE` | the licence and attribution of every model and binary an artefact carries |
| `CHANGELOG.md` | the changes of every release; the release script refuses a version without a heading |
| `docs/TESTING.md` | the test tiers, the commands, the guards and the continuous integration |
| `docs/transcript-schema.md` | the JSON transcript: every field and its meaning |
| `docs/BENCHMARKS.md` | the measured speed, memory and accuracy of the shipped configuration |
| `docs/RELEASING.md` | the exact steps that cut a release |
| `docs/WINDOWS-VERIFICATION.md` | the checklist the owner runs on a Windows laptop |
| `docs/decisions/` | one record per significant decision: context, options, evidence, decision, consequences |

A file added to or removed from this set moves this table in the same change,
because a charter that does not say which documents are authoritative leaves
each session to guess.

`docs/build/` holds working documents of the build (the state file, the
reference digest, the research notes); nothing there is authoritative.

Three rules govern how an authoritative file is written.

- **Text is short, and exclusivity is stated as exclusivity.** Write "only A" in
  place of "A, and not B", because the paired negative doubles the sentence and
  leaves every unnamed alternative open.
- **A file is timeless.** It describes the repository as it is, in the present
  tense, citing no build phase, no change order and no past version of itself,
  because a file narrating its own history is read against a baseline the reader
  does not have.
- **Chapter titles are numbered and generic.** A numbered chapter can be cited,
  and a title that names a count is stale on the first addition.

Every rule is stated with its reason, so a reader can tell when it stops
applying.

## 3. Repository structure

```
meeting-audio-transcriptor/
├── CLAUDE.md                                 this charter
├── README.md                                 user documentation
├── CHANGELOG.md, NOTICE, LICENSE
├── Directory.Build.props, Directory.Build.targets, Directory.Packages.props, global.json
├── MeetingAudioTranscriptor.slnx
├── src/
│   ├── MeetingAudioTranscriptor.Contracts/   the wire shapes and the serialisation context
│   ├── MeetingAudioTranscriptor.Engine/      audio, DSP, voice activity, recognition decoding,
│   │                                         diarisation, pipeline, cleaning, writers, scoring
│   ├── MeetingAudioTranscriptor.Inference/   the ONNX Runtime half of the model boundary
│   ├── MeetingAudioTranscriptor.Media/       audio file decoding: ffmpeg and Media Foundation
│   ├── MeetingAudioTranscriptor.App/         the service, the bound surface of the page
│   ├── MeetingAudioTranscriptor.Cli/         the command-line executable
│   └── MeetingAudioTranscriptor.Shell/       the desktop executable: one host per operating system
├── frontend/                                 the page, plain ES modules with no build step
├── tests/
│   ├── MeetingAudioTranscriptor.Engine.Tests/
│   ├── MeetingAudioTranscriptor.Media.Tests/
│   ├── MeetingAudioTranscriptor.Inference.Tests/
│   ├── MeetingAudioTranscriptor.Pipeline.Tests/   the stages over the real models and audio
│   ├── MeetingAudioTranscriptor.App.Tests/
│   ├── MeetingAudioTranscriptor.Cli.Tests/
│   ├── MeetingAudioTranscriptor.Shell.Tests/
│   └── MeetingAudioTranscriptor.Guards.Tests/     checks over source text, projects and locks
├── bench/
│   ├── testset/                              the builder of the speech test set
│   ├── tools/cpuidmask/                      hides CPU features from a benchmark process
│   ├── results/                              the committed result files
│   └── spikes/                               throwaway research scripts, not needed to build or ship
├── build/                                    the locks, the fetch tools, the publish and release scripts
├── docs/                                     the documentation; docs/build holds working documents
└── .github/
    ├── workflows/                            ci and release
    └── actions/                              the composite actions the workflows share
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
| .NET | 10.0 | the SDK band is pinned in `global.json` (`rollForward: latestFeature`), which also names Microsoft.Testing.Platform as the test runner |
| `Microsoft.ML.OnnxRuntime` | 1.31.1 | the Inference project alone; its native library ships beside the executables |
| `Microsoft.Web.WebView2` | 1.0.4191.47 | the Shell alone, in the Windows host |
| xunit.v3 | 4.0.1 | test only, on Microsoft.Testing.Platform |
| Node | 22 or later | the build tools and the page suite; no npm package |
| Granite Speech 5.0 470M TurboCTC | Apache-2.0 | the recogniser, int8, with an appended score output |
| Silero VAD | MIT | voice activity |
| WeSpeaker CAM++ LM | Apache-2.0 code, CC-BY-4.0 weights | speaker embeddings |
| pyannote segmentation 3.0 | MIT | listed in the lock as an optional default; no code reads it |
| ffmpeg | LGPL-3.0-or-later | the bundled decoder, built without GPL and non-free options |

`build/models.lock` and `build/ffmpeg.lock` are the source of truth for every
model's and ffmpeg build's revision, address, size and hash, and `Directory.Packages.props`
for every package version; this table names what is shipped and under which licence,
and a revision written here would rot. The Parakeet model in the lock is a
candidate that only benchmarks read and no package carries. `NOTICE` credits every
shipped component, and a guard holds it to the locks.

Everything else is the base class library.

## 7. Build, test, fetch and release

```
node build/fetch-models.mjs --into models      # the models, verified against build/models.lock
node build/fetch-ffmpeg.mjs --rid linux-x64    # or win-x64; verified against build/ffmpeg.lock
dotnet build -warnaserror
dotnet test --filter-not-trait "Tier=deep" --filter-not-trait "Tier=integration" --filter-not-trait "Tier=models" --ignore-exit-code 8
dotnet test --project tests/MeetingAudioTranscriptor.Guards.Tests
node --test "frontend/**/*.test.js"
node build/update-frontend-hashes.mjs          # after a page edit
dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror
pwsh build/publish.ps1 -Rid linux-x64 -Version 0.1.0 -SkipModels    # a package without models, for a structural check
pwsh build/release.ps1 -Version X.Y.Z -DryRun                       # then without -DryRun to tag
```

A release is built only by the tag workflow on the hosted runners, because the
runner builds every package the same way; `docs/RELEASING.md` owns the steps.
`docs/TESTING.md` owns the tiers.

## 8. Domain vocabulary

Each word means one thing in code, JSON, copy and documentation.

| Term | Meaning |
|---|---|
| Chunk | a stretch of 16 kHz speech audio, at most about 30 seconds, cut at a silence, handed to the recogniser |
| Speech region | an interval the voice-activity detector marks as speech, inside a chunk or a recording |
| Segment | a sentence-like stretch of words by one speaker: a subtitle cue, a line of the text, the unit the cleaner reads |
| Turn | consecutive segments of one speaker, printed under one speaker heading in the text transcript |
| Cleaned and raw | the cleaned text has fillers, stutters, false starts and bare acknowledgements removed; the raw text is what the recogniser wrote; the JSON holds both for every segment |
| Speaker label | the anonymous name of a voice, `Speaker 1`, `Speaker 2`, in order of first appearance; its id is `S1`, `S2` |
| Confidence | a probability from 0 to 1: per word the recogniser's own, per segment the mean of its words |
| Removed | a segment or word that cleaning took out of the cleaned text, with the reason on a segment |

## 9. Where to look next

- The stages, the model boundary and cleaning: `src/MeetingAudioTranscriptor.Engine/CLAUDE.md`.
- The service, the outcome kinds and the bridge: `src/MeetingAudioTranscriptor.App/CLAUDE.md`.
- Audio decoding: `src/MeetingAudioTranscriptor.Media/CLAUDE.md`.
- The windows and the threads: `src/MeetingAudioTranscriptor.Shell/CLAUDE.md`.
- The page: `frontend/CLAUDE.md`, and the contract it calls: `frontend/BRIDGE.md`.
- The suites: `tests/CLAUDE.md` and `docs/TESTING.md`.
- The decisions and their evidence: `docs/decisions/`.
