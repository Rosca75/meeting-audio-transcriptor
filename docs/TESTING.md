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

Every test class carries a `Category` trait, so a run can be scoped to one concern. The vocabulary: `config`, `audio`, `transcript`, `writers`, `errors`.

## 3. Guards

`MeetingAudioTranscriptor.Guards.Tests` holds checks over source text, project files and assemblies, and runs in the unit tier. A guard that reads nothing fails rather than passing.

- Purpose headers: every `.cs`, `.csproj` and script file opens with a comment saying what it is for.
- Engine references: the engine assembly references only the framework and the contracts project.
- Inference isolation: only the inference project references the ONNX Runtime package.
- No network: no file under `src/` names an HTTP or socket type; the exemption list is empty.
- Package versions: a version appears in `Directory.Packages.props` alone.

A failing guard names a real inconsistency: fix the inconsistency and leave the guard as it is. An exemption names its file and its reason.

## 4. Media decoding

`MeetingAudioTranscriptor.Media.Tests` holds the media suite. The unit tier covers the PCM converter (every format, channel count and block split), the ffmpeg banner parser over recorded and hand-written banners, the error wording, the content sniff and the opener's decision table with fake decoders. The integration tier generates its files with the ffmpeg it finds (`MAT_FFMPEG`, beside the test, then the PATH) and skips without one; it also decodes `bench/data/long/en-10min.m4a` and `.mp4` when the test set is present. The Media Foundation tests run on Windows alone and skip elsewhere, because the interop cannot run on Linux.

## 5. Commands

```
dotnet test --filter-not-trait "Tier=deep" --filter-not-trait "Tier=integration" --filter-not-trait "Tier=models"   # the unit tier
dotnet test --filter-trait "Tier=integration" --ignore-exit-code 8   # integration
dotnet test --filter-trait "Tier=models" --ignore-exit-code 8        # real models
dotnet test -c Release --filter-trait "Tier=deep" --ignore-exit-code 8   # benchmarks, on a quiet machine
dotnet test --project tests/MeetingAudioTranscriptor.Guards.Tests    # the guards alone
dotnet test --filter-trait "Category=writers" --ignore-exit-code 8   # one category
```

```
node build/fetch-models.mjs --into models                          # fetch the default models (add --set all for the candidates)
MAT_MODELS_DIR=<dir> dotnet test --filter-trait "Tier=models" --ignore-exit-code 8   # the models tier over a models folder elsewhere
node --test build/*.test.mjs                                       # the fetch tools and the graph patch, against a loopback server
node build/fetch-models.mjs --into models --keep-sources           # also keep the pinned sources a shipped graph is derived from
```

`MeetingAudioTranscriptor.Pipeline.Tests` runs the engine's stages over the real models and the benchmark audio, in the models and deep tiers only. It reads the audio from `MAT_BENCH_DATA` when that variable is set, and from `bench/data` of the repository otherwise (`node bench/testset/fetch-testset.mjs` builds it); a missing file skips its test by name. Its deep-tier measurements print markdown tables, and also write them to the file `MAT_VAD_REPORT` (the VAD) or `MAT_GRANITE_REPORT` (the recogniser) names when that variable is set; run them pinned to one core, because they report single-core cost. The models-tier test comparing the derived Granite graph with its pinned source skips unless the source was kept with `--keep-sources`.

```
MAT_BENCH_DATA=<dir> dotnet test --project tests/MeetingAudioTranscriptor.Pipeline.Tests --filter "Tier=models"
```

The models tier reads its model files from `MAT_MODELS_DIR` when that variable is set, and from the `models` folder of the repository otherwise; the application's catalog resolves the same folder, so a development run and a test see one set of files. A model that is not installed skips its tests by name and prints the fetch command. `build/models.lock` pins every model file by address, size and SHA-256, and `build/ffmpeg.lock` pins the ffmpeg builds; the fetch tools verify each file against them and leave a verified file alone, so a second run downloads nothing.

Read the test count after every run: zero tests proves nothing. A suite with no test in a tier exits with code 8; add `--ignore-exit-code 8` when running a tier some suites do not hold.

The Windows desktop host builds from Linux with `dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror`.
