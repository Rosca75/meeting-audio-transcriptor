# Testing

The test tiers, the categories, the guards and the commands that run them.

## 1. Tiers

A test's tier is its `Tier` trait, and the trait is the only tier mechanism: no environment variable gates a test. A test with no `Tier` trait is a unit test.

| Tier | Trait | Requires | Runs |
|---|---|---|---|
| unit | none | pure logic and tiny committed fixtures; the whole tier stays under 10 seconds | every push |
| integration | `Tier=integration` | real file I/O, ffmpeg or other tools on disk | pull requests |
| models | `Tier=models` | the real ONNX models installed on the machine | on demand |
| deep | `Tier=deep` | long benchmarks and wall-clock measurements | on demand |

A test whose dependency is missing skips by name (`Assert.SkipUnless(condition, "needs ffmpeg: <what>; install it")`) and never fails, because a failure must mean the code is wrong. A higher tier tests only what a lower one cannot.

## 2. Categories

Every test class carries a `Category` trait, so a run can be scoped to one concern. The vocabulary:

| Category | What it holds |
|---|---|
| `audio` | decoding, resampling, the WAV reader, the voice activity detector and chunking |
| `cleaning` | the per-language rules that remove fillers, stutters, false starts and acknowledgements |
| `config` | locks, versions, options, wiring and the guards |
| `desktop` | the window hosts, the page transport and the bridge |
| `diarisation` | speaker embeddings, clustering and the speaker separator |
| `errors` | failure mapping, exit codes and the wording of messages |
| `scoring` | word error rate, content retention, the diarisation error rate and the text normaliser |
| `transcript` | the pipeline, segments, the service and the transcript documents |
| `writers` | the `.vtt`, `.json` and `.txt` writers |

A new category is added to this table and to `tests/CLAUDE.md` in the same change.

## 3. Guards

`MeetingAudioTranscriptor.Guards.Tests` holds checks over source text, project files and assemblies, and runs in the unit tier. A guard that reads nothing fails rather than passing.

- Purpose headers: every `.cs`, `.csproj` and script file opens with a comment saying what it is for.
- Engine references: the engine assembly references only the framework and the contracts project.
- Inference isolation: only the inference project references the ONNX Runtime package.
- No network: no file under `src/` names an HTTP or socket type; the exemption list is empty.
- Package versions: a version appears in `Directory.Packages.props` alone.
- Page hashes: every file under `frontend/` except `frontend/frontend-hashes.txt` is listed there as `<sha256> *<path>` and matches. A page edit moves its line in the same commit: `node build/update-frontend-hashes.mjs` rewrites the file.
- Page tests present: the page suite has a test file per logic module, none silenced, and the charters name its command.
- Application icon: the Shell project names `Windows/app.ico` as its `ApplicationIcon`, and the file is an icon holding the 16, 20, 24, 32, 40, 48, 64 and 256 pixel sizes. `build/make-app-icon.test.mjs` holds its pictures to the mark's geometry and that geometry to the page's logo; `node build/make-app-icon.mjs` redraws it.
- Workflow pins: every `uses:` under `.github` is a full 40-character commit SHA, and `NOTICE` names every default model and every ffmpeg build the locks ship.
- Copy: no em dash in `frontend/js/copy.js`, in any `.cs` file under `src/`, or in a document (`docs/**/*.md`, `docs/**/*.json`, `README.md`, `CHANGELOG.md`, `NOTICE` and every charter); `docs/build/reference-digest.md` and `docs/build/research-models.md` are exempt by name in the guard, because they quote titles that carry one. Frontend scripts and stylesheets open with a purpose header like every other source file.

A failing guard names a real inconsistency: fix the inconsistency and leave the guard as it is. An exemption names its file and its reason.

## 4. Media decoding

`MeetingAudioTranscriptor.Media.Tests` holds the media suite. The unit tier covers the PCM converter (every format, channel count and block split), the ffmpeg banner parser over recorded and hand-written banners, the error wording, the content sniff and the opener's decision table with fake decoders. The integration tier generates its files with the ffmpeg it finds (`MAT_FFMPEG`, beside the test, then the PATH) and skips without one; it also decodes `bench/data/long/en-10min.m4a` and `.mp4` when the test set is present. The Media Foundation tests run on Windows alone and skip elsewhere, because the interop cannot run on Linux.

## 5. Pipeline, service and command line

The Engine suite's `Pipeline/` tests drive the streaming pipeline with scripted chunks, a scripted recogniser and a scripted speaker separator: word order when chunks finish out of order, the speech regions handed to a region-aware recogniser, the cancellation handed to the separator, the 25 s chunk target, the bounded queue holding the producer back, cancellation within a second, a worker's or the source's own exception surfacing unwrapped, monotonic progress ending at 1, the segment builder's cuts, the low-confidence guard, the transcript assembly, empty and silent audio, and the same document whatever the thread count.

The Engine suite's `Diarisation/DiarisationSpeakerSeparatorTests` cover the pipeline's speaker separator over a constant embedding graph: speakers numbered by their first word, one speaker when no window could be embedded, one finish per run.

`MeetingAudioTranscriptor.App.Tests` drives the transcription service through its seams (the opener, the model loader) with an energy-based VAD graph and a fake recogniser over WAV files it writes: the files and their names, the formats and raw files, overwriting, every failure kind, the cleanup of temporary files on failure and cancellation, and one model load for several runs. `ServiceWiringTests` holds the factories' file names to `build/models.lock`, the notice of a run whose speaker model is missing, the speaker-embedding model named in the metadata of a run that separates speakers, and the bridge adapter (`ServiceTranscriber`): the request mapping, the page stage words and every outcome kind mapped to `completed`, `noSpeech`, `cancelled` or `failed`. `MeetingAudioTranscriptor.Cli.Tests` holds the argument parser's table tests, the exit code of every outcome and the console wording.

The Pipeline suite's `Pipeline/ServicePipelineTests` runs the real Silero VAD and the pipeline through the service with a fake recogniser over `long/en-10min.wav`, `.m4a` and `.mp4` (chunk statistics, progress, every output format) and over the four noise files (no speech, an empty transcript).

The Pipeline suite's `Pipeline/RealServiceTests` runs the shipped service over the real models (the recogniser, the VAD and the speaker model): one clip per language under a word error rate floor of 15 % with its speakers, every segment's speaker listed, speakers numbered by first appearance, speaking times summed from the segments and the three files valid; the four noise files give no word and no speech; `en-10min.m4a` and `.mp4` read within 2 % of the words of the WAV; a run cancelled at 20 % leaves the output folder empty; a second run on the same service loads nothing, is faster and gives the same transcript.

The benchmark runs the real service over the test set and writes `bench/results/<yyyy-MM-dd>-<label>.json`: per file and per group the word error rates, the retention, the words removed, the noise words, the throughput and the stage timings, and for a file with reference turns the speaker figures (the diarisation error rate with a 0.25 s collar, overlap excluded and included, the confusion share, the attribution accuracy and the estimated against the reference speaker count, the hypothesis turns being the transcript's segments); the machine facts, the thread count, `CPUIDMASK`, whether the processor reports VNNI, whether the exact 8-bit kernels are on (`QuantizedKernelPrecision.Enabled`) and the peak working set are recorded with them, and the first printed line names the same facts.

```
meeting-audio-transcriptor-cli benchmark --set clips --threads 1          # also long, containers, noise, all or a file id
meeting-audio-transcriptor-cli benchmark --set clips --speakers-hint-from-reference   # also a run told the reference's speaker count
meeting-audio-transcriptor-cli benchmark --long-file <path> --language en  # time a recording outside the test set
```

## 6. Desktop window

`MeetingAudioTranscriptor.Shell.Tests` holds the shell suite; it references the shell as built for the machine it runs on. The unit tier covers what both hosts share: the embedded page (lookup, media types, the 404 for everything else, path traversal, which files are embedded), the response headers, the JavaScript literal escaping, the data folder on each platform, the drop message and the `file://` decoding, the page message router and the threading of the window services, with fakes.

The integration tier drives the real Linux window under `xvfb-run`, with `MAT_UI_DEMO=1` and an automation script, and skips by name on another system, without `xvfb-run`, without WebKitGTK 4.1 or without a built shell. The hook is the host's own and is off unless its variables are set: `MAT_UI_AUTOMATION` names a script the host evaluates in the page once it has loaded, `MAT_UI_EXIT_AFTER_MS` closes the window after that delay, and while either is set the page may post `{"automation":{"log":"..."}}` (printed to standard output), `{"automation":{"echo":"..."}}` (posted back through the host's event path), `{"automation":{"dropUris":["file:///..."]}}` (fed to the native drop handler) and `{"automation":{"exit":<code>}}`. The tests prove that the page loads from the `app` scheme, that `app.info` answers through the page's own transport, that a string of quotes, backslashes, line separators and non-ASCII letters survives the trip to the page, that a dropped `file://` URI reaches the bridge as a path and the page shows the file, that a demo run started from the page's Start button ends with `run:finished`, that a missing file, a markdown file and every address off the origin are refused, that inline script and connections are refused by the policy, that a navigation to another site is cancelled by the host, and that without a display the window exits with code 2. A real drag and drop is not simulated: the test feeds URIs to the same handler the toolkit's drop reaches. `LinuxWindowRealRunTests` runs the window with `MAT_UI_DEMO=0` and the real service: a copy of `clips/fr/fr-multi-01.wav` is dropped, Start is pressed, the run must end with `run:finished` of kind `completed`, and the three transcript files must exist beside the recording with a JSON holding a segment and a speaker; it also skips by name without the models (`MAT_MODELS_DIR` or the repository's `models`) or the test set audio (`MAT_BENCH_DATA` or `bench/data`).

`MAT_SHELL_PATH` points the integration tier at another build of the shell, such as a trimmed self-contained publish:

```
dotnet publish src/MeetingAudioTranscriptor.Shell -c Release -r linux-x64 --self-contained -p:PublishTrimmed=true -o out/linux
MAT_SHELL_PATH=$PWD/out/linux/meeting-audio-transcriptor dotnet test --project tests/MeetingAudioTranscriptor.Shell.Tests --filter-trait "Tier=integration"
```

Without a GPU render node the Linux host draws in software (it sets `WEBKIT_DISABLE_COMPOSITING_MODE=1` and `WEBKIT_DISABLE_DMABUF_RENDERER=1`), which is what makes the window draw under xvfb; `MAT_WEBKIT_SOFTWARE=1` forces that path and `MAT_WEBKIT_SOFTWARE=0` forbids it. The Windows host cannot run here: it is compiled with `dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror` and checked by hand.

## 7. Commands

```
dotnet test --filter-not-trait "Tier=deep" --filter-not-trait "Tier=integration" --filter-not-trait "Tier=models" --ignore-exit-code 8   # the unit tier
dotnet test --filter-trait "Tier=integration" --ignore-exit-code 8   # integration
dotnet test --filter-trait "Tier=models" --ignore-exit-code 8        # real models
dotnet test -c Release --filter-trait "Tier=deep" --ignore-exit-code 8   # benchmarks, on a quiet machine
dotnet test --project tests/MeetingAudioTranscriptor.Guards.Tests    # the guards alone
dotnet test --filter-trait "Category=writers" --ignore-exit-code 8   # one category
```

```
node --test "frontend/**/*.test.js"                                # the page suite (the quoted recursive glob is required)
node build/update-frontend-hashes.mjs                              # after a page edit, rewrites frontend/frontend-hashes.txt
```

The page suite runs on Node with no DOM and no bridge: state transitions, the api over a fake transport, the controller flows and the copy rules. The bridge's own tests are in `tests/MeetingAudioTranscriptor.App.Tests/Bridge/` over a fake window host and a fake transcriber.

```
node build/fetch-models.mjs --into models                          # fetch the default models (add --set all for the candidates)
MAT_MODELS_DIR=<dir> dotnet test --filter-trait "Tier=models" --ignore-exit-code 8   # the models tier over a models folder elsewhere
node --test build/*.test.mjs                                       # the fetch tools and the graph patch against a loopback server, and the icon
node build/fetch-models.mjs --into models --keep-sources           # also keep the pinned sources a shipped graph is derived from
```

`MeetingAudioTranscriptor.Pipeline.Tests` runs the engine's stages over the real models and the benchmark audio, in the models and deep tiers only. It reads the audio from `MAT_BENCH_DATA` when that variable is set, and from `bench/data` of the repository otherwise (`node bench/testset/fetch-testset.mjs` builds it); a missing file skips its test by name. Its deep-tier measurements print markdown tables, and also write them to the file `MAT_VAD_REPORT` (the VAD) or `MAT_GRANITE_REPORT` (the recogniser) names when that variable is set; run them pinned to one core, because they report single-core cost. The models-tier test comparing the derived Granite graph with its pinned source skips unless the source was kept with `--keep-sources`.

```
MAT_BENCH_DATA=<dir> dotnet test --project tests/MeetingAudioTranscriptor.Pipeline.Tests --filter-trait "Tier=models"
dotnet test --project tests/MeetingAudioTranscriptor.Cli.Tests       # the command line alone
```

The models tier reads its model files from `MAT_MODELS_DIR` when that variable is set, and from the `models` folder of the repository otherwise; the application's catalog resolves the same folder, so a development run and a test see one set of files. A model that is not installed skips its tests by name and prints the fetch command. `build/models.lock` pins every model file by address, size and SHA-256, and `build/ffmpeg.lock` pins the ffmpeg builds; the fetch tools verify each file against them and leave a verified file alone, so a second run downloads nothing.

Read the test count after every run: zero tests proves nothing. A suite with no test in a tier exits with code 8; add `--ignore-exit-code 8` when running a tier some suites do not hold.

The Windows desktop host builds from Linux with `dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror`.

## 8. Continuous integration

`.github/workflows/ci.yml` runs on every pull request to `main`, every push to `main` and a manual start (workflow dispatch), on `ubuntu-latest` and `windows-latest`, because the application ships for both and a break on one system is invisible on the other.

| Job | Runs |
|---|---|
| `build-and-test` | restore, `dotnet build --no-restore -warnaserror`, the unit tier (guards included), the page suite and the build tool suites |
| `integration` | the integration tier with the pinned ffmpeg (`MAT_FFMPEG`); on Windows the Media Foundation tests run against a real Windows, on Linux the window tests run under `xvfb-run` |
| `end-to-end` | builds the reduced test set on the runner (`fetch-testset.mjs --subset ci`: every clip, the noise files and the three ten-minute files with `en-10min.m4a` and `.mp4`, with the pinned ffmpeg), runs the models tier over it in Release (`MAT_MODELS_DIR`, `MAT_BENCH_DATA`, `MAT_FFMPEG`), checks that the Pipeline suite ran at least 70 tests and skipped none for absent audio, transcribes `long/en-10min.m4a` with the command line and prints the summary and the first 15 lines of the text, and runs `benchmark --set clips --threads 4`, whose result file it keeps as the artefact `benchmark-<os>`. On Windows the transcription runs twice: by default, where the `Decoder:` line must read `media-foundation`, and with `MAT_PREFER_FFMPEG=1`; the two transcripts must differ by at most 2 % of their words |
| `testset-parity` | compares the hash of every test set file the two `end-to-end` legs built: the WAV files, references and RTTM must be byte-identical on both systems, and only the `.m4a` and `.mp4` copies may differ |
| `package` | `build/publish.ps1 -SkipModels`: publish, shared-runtime check, removal, unexpected-file refusal and smoke test, without the models |

The models and ffmpeg are fetched by the Node tools through the composite actions under `.github/actions/` and cached keyed on the hash of their lock files; the test set sources are cached keyed on `bench/testset/testset.lock`, and the builder verifies each against the lock whether it was restored or downloaded. On the hosted Windows runner the command-line run reads the 10-minute `.m4a` at 13.2 times real time through Media Foundation and 13.5 through ffmpeg, and the two transcripts differ by 3 word edits out of 1,457 (CI run 42926115111; `docs/BENCHMARKS.md`, chapter 9). `bench/tools/ci-checks.mjs` holds the job's checks (the test count from the TRX reports, the transcript comparison and the parity of the test set files), so each can be run by hand on a local result. Two guards cover the CI files: every `uses:` in `.github` is a full 40-character commit SHA (local `./.github/actions/` paths excepted), and every model of the default set and every ffmpeg build is credited in `NOTICE`. `docs/RELEASING.md` owns the release workflow.
