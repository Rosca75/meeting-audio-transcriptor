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

## 4. Commands

```
dotnet test --filter "Tier!=deep&Tier!=integration&Tier!=models"   # the unit tier
dotnet test --filter "Tier=integration"                            # integration
dotnet test --filter "Tier=models"                                 # real models
dotnet test --filter "Tier=deep"                                   # benchmarks
dotnet test --project tests/MeetingAudioTranscriptor.Guards.Tests  # the guards alone
dotnet test --filter "Category=writers"                            # one category
```

```
node build/fetch-models.mjs --into models                          # fetch the default models (add --set all for the candidates)
MAT_MODELS_DIR=<dir> dotnet test --filter "Tier=models"            # the models tier over a models folder elsewhere
node --test build/*.test.mjs                                       # the fetch tools, against a loopback server
```

The models tier reads its model files from `MAT_MODELS_DIR` when that variable is set, and from the `models` folder of the repository otherwise; the application's catalog resolves the same folder, so a development run and a test see one set of files. A model that is not installed skips its tests by name and prints the fetch command. `build/models.lock` pins every model file by address, size and SHA-256, and `build/ffmpeg.lock` pins the ffmpeg builds; the fetch tools verify each file against them and leave a verified file alone, so a second run downloads nothing.

Read the test count after every run: zero tests proves nothing. A suite with no test in a tier exits with code 8; add `--ignore-exit-code 8` when running a tier some suites do not hold.

The Windows desktop host builds from Linux with `dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror`.
