# CLAUDE.md: the suites

The charter of `tests/`. The root `CLAUDE.md` overrides it where they conflict. The tiers, the commands and the guard list are owned by `docs/TESTING.md`; this file owns what belongs where and the rules a test follows.

## 1. Which suite holds what

| Suite | What belongs in it |
|---|---|
| `MeetingAudioTranscriptor.Engine.Tests` | every engine folder against fakes and tiny fixtures: audio, DSP, VAD, recognition decoding, diarisation, pipeline with scripted chunks, cleaning, writers, scoring |
| `MeetingAudioTranscriptor.Media.Tests` | the PCM converter, the ffmpeg banner and error wording, the sniff, the opener's decision table with fake decoders; ffmpeg decoding of generated files; Media Foundation on Windows alone |
| `MeetingAudioTranscriptor.Inference.Tests` | the model catalog, the runner and the VAD model over tiny generated ONNX graphs, and over the real model when installed |
| `MeetingAudioTranscriptor.Pipeline.Tests` | the engine's stages over the real models and the benchmark audio: VAD chunking, the Granite recogniser, diarisation, the service end to end; models and deep tiers only |
| `MeetingAudioTranscriptor.App.Tests` | the transcription service through its seams, and the bridge through a fake host and a fake transcriber |
| `MeetingAudioTranscriptor.Cli.Tests` | the argument parser's table, the exit code of each outcome, the console wording |
| `MeetingAudioTranscriptor.Shell.Tests` | what both hosts share (assets, headers, routing, drops, data folder, threading with fakes) and the real Linux window under `xvfb-run` |
| `MeetingAudioTranscriptor.Guards.Tests` | checks over source text, project files, assemblies and locks; never behaviour |

**A test lives in the suite of the project it exercises.** A test of an interface lives with its implementation's behaviour and uses fakes for the rest, so a failure names the project to open. The page's own tests are `frontend/**/*.test.js`, run by Node.

## 2. Tiers, traits and categories

**The `Tier` trait is the only tier mechanism.** No trait means unit; `integration`, `models` and `deep` are the others, and no environment variable gates a test, because a test that runs only under a variable is a test nobody runs. The unit tier stays under ten seconds.

**Every test class carries a `Category` trait.** The vocabulary in use: `audio`, `cleaning`, `config`, `desktop`, `diarisation`, `errors`, `scoring`, `transcript`, `writers`; a run is scoped with `--filter-trait "Category=..."`.

**A higher tier tests only what a lower one cannot.** A deep test is a measurement and an integration test needs a real tool or file; everything decidable with a fake belongs in the unit tier.

## 3. Environment variables the suites read

| Variable | Read by | Meaning |
|---|---|---|
| `MAT_MODELS_DIR` | Inference, Pipeline | the models folder; else `models` of the checkout |
| `MAT_BENCH_DATA` | Pipeline | the benchmark audio; else `bench/data` of the checkout |
| `MAT_FFMPEG` | Media | the ffmpeg executable to decode with |
| `MAT_SHELL_PATH` | Shell integration | another build of the desktop executable, such as a trimmed publish |
| `MAT_UI_DEMO`, `MAT_UI_AUTOMATION`, `MAT_UI_EXIT_AFTER_MS` | the window under test | demo transcriber, the page script, the self-close delay |
| `MAT_VAD_REPORT`, `MAT_GRANITE_REPORT` | deep measurements | a file the measurement tables are also written to |
| `MAT_GRANITE_CANDIDATES`, `_CUTS`, `_TARGETS`, `_GROUPS` | the Granite measurements | which variants and groups to measure |

## 4. Skipping and failing

**A test whose dependency is missing skips by name and never fails.** It uses `Assert.SkipUnless(condition, "needs <what>; <how to get it>")`, because a failure must mean the code is wrong and a skip must say what to install. A skipped test that prints nothing is a hidden hole.

**A tier a suite does not hold exits with code 8,** so commands pass `--ignore-exit-code 8`. A run that executed zero tests proves nothing: read the count.

**A deep-tier measurement prints a table and runs on a quiet machine.** Its timings depend on the machine, so they are read by a person and recorded in a document, because a wall-clock assertion that fails on a busy runner protects nothing.

## 5. A test states its expectation

**An expectation is written out and not derived from the code under test.** A test that computes its expected value the way the code does asserts that the code agrees with itself. A wire shape is compared with a literal, property order included; a transcript with a literal file; a cleaning rule with sentences written by hand in each language.

**Fakes replace the boundaries and nothing else.** The window is a fake `IHostServices`, the clock a `TimeProvider`, the model a fake `ITensorRunner` or a tiny generated graph, the recogniser a scripted one. The real model runs in the models tier only.

**Fixtures are small and committed beside the test, and everything large is fetched.** A recorded `.wav`, `.m4a` or `.mp4` is committed only under a `Fixtures` folder; models and the benchmark audio are never committed (`node bench/testset/fetch-testset.mjs` builds the audio).

## 6. Guards

**A guard fails when it reads nothing.** Each asserts it found files before it judges them, because a guard whose pattern matched nothing passes forever.

**An exemption names its file and its reason in the guard's own table.** The exemption list of the network guard is empty. A failing guard names a real inconsistency: fix the inconsistency and leave the guard as it is.

The guards hold: purpose headers on source, project, script and page files (markdown is exempt); the engine's references; the ONNX Runtime package in the Inference project alone; no network type under `src`; package versions in `Directory.Packages.props` alone; the page's file hashes; the page's test files; full-commit workflow pins; the notice crediting every shipped model and ffmpeg build; no em dash in the page's copy or in `src`.

## 7. Where to look next

`docs/TESTING.md` for the tiers and the commands, `docs/WINDOWS-VERIFICATION.md` for what the suites cannot run, and the charter of each project for what it expects of its tests.
