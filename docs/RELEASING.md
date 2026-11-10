# Releasing

The exact steps that cut a release. A release is a tag `vX.Y.Z` on `main`; the tag starts the
release workflow, which builds both packages on hosted runners and publishes them. The version
number exists nowhere else: no project file holds it, because a second copy is a second place to
forget.

## 1. Prerequisites

- PowerShell 7 (`pwsh`), git, and push rights to the repository.
- Nothing else: the packages are never built on the owner's machine, because the runner builds
  every release the same way.

## 2. Cut a release

1. Move the entries under `## [Unreleased]` in `CHANGELOG.md` into a new `## [X.Y.Z]` section,
   commit and push to `main`. The release script refuses a version with no such heading.
2. Switch to `main` and make it equal to the remote: `git switch main && git pull`.
3. Check everything without tagging: `pwsh build/release.ps1 -Version X.Y.Z -DryRun`. It runs every
   check and prints the two git commands it would run.
4. Tag and push: `pwsh build/release.ps1 -Version X.Y.Z`.

The script refuses, and says what to do, unless the checkout is `main`, is clean and equals
`origin/main`, the changelog has the version, the tag exists neither here nor on the remote, and the
version is not below the highest released one. A pre-release label (`0.2.0-rc.1`) is allowed; the
final `0.2.0` may follow it.

## 3. What the workflow does

`.github/workflows/release.yml` runs on the tag. For each of `win-x64` (Windows runner) and
`linux-x64` (Ubuntu runner) it reads the version from the tag and validates it, restores the cached
models and ffmpeg (or fetches them, verified against `build/models.lock` and `build/ffmpeg.lock`),
builds the reduced speech test set (`node bench/testset/fetch-testset.mjs --subset ci`, its sources
cached), runs the unit tier and runs `build/publish.ps1 -SmokeClip bench/data/clips/en/en-single-01.wav`.
That script publishes both executables into one self-contained folder, verifies the models and
ffmpeg a second time, runs a smoke test of the staged folder (`--version`, `models`, the bundled
ffmpeg, and the clip encoded to `.m4a` and transcribed by the staged command line with the
decoder variables cleared: through the bundled ffmpeg, and on Windows through Media Foundation as
well, each run naming its decoder and recognising words), refuses an archive over 2,000,000,000 bytes (the limit of a release asset), checks the size
against `build/size-budgets.json` and writes the archive. The `publish` job then gathers both
archives, writes `SHA256SUMS.txt` and creates the GitHub release with generated notes.

The release appears under the repository's Releases page, named after the tag, with:

- `meeting-audio-transcriptor-X.Y.Z-win-x64.zip`
- `meeting-audio-transcriptor-X.Y.Z-linux-x64.tar.gz`
- `SHA256SUMS.txt`

## 4. Verify a download

On Linux: `sha256sum -c --ignore-missing SHA256SUMS.txt` in the folder holding the file. On
Windows: `Get-FileHash .\meeting-audio-transcriptor-X.Y.Z-win-x64.zip -Algorithm SHA256` and compare
the hash with the line in `SHA256SUMS.txt`. Then unpack, and run `meeting-audio-transcriptor-cli
--version` (it prints `X.Y.Z`) and `meeting-audio-transcriptor-cli models --verify`.

## 5. Dry run from the workflow

Open Actions, choose `release`, press Run workflow, pick the branch and type a version such as
`0.1.0-rc.1`. The workflow builds both packages exactly as for a tag, smoke test included, and
keeps them as the artefacts `release-win-x64` and `release-linux-x64` for 7 days, with each leg's
`sizes.json` as `sizes-<rid>`; the `publish` job writes the checksums and stops, so a dry run
creates no release and no tag. A dry run on a branch other than `main` is how a change to the
workflow, the publish script or the packaging is proven before it is merged.

Tick `record_budgets` to measure instead of checking: each leg writes its own platform's numbers,
and the `budgets` job merges them into the artefact `size-budgets`, one `build/size-budgets.json`
of the form

```
{ "comment": "...", "budgets": { "linux-x64": { "archiveBytes": 0, "folderBytes": 0 }, "win-x64": { "archiveBytes": 0, "folderBytes": 0 } } }
```

Commit that file in a pull request that shows the numbers moving; from then on every package
must stay within ten percent of its platform's two numbers. A platform without a budget only
prints a notice.

## 6. When a model or ffmpeg address has gone away

The fetch step fails naming the file. The locks pin files by address, size and SHA-256, so a
replacement must be the same bytes:

1. Download the file from another source you trust, or from your own copy, and check that its
   SHA-256 equals the lock's.
2. Mirror it to a permanent address (for example an asset of a release of this repository).
3. Change only the `url` of that entry in `build/models.lock` or `build/ffmpeg.lock`; keep size and
   hash. If the bytes themselves must change, that is a different model: update the hash and size,
   record the decision in `docs/decisions/`, and update `NOTICE` for the new component.
4. Open a pull request, let `ci` pass, then release.

## 7. Rules and hot fixes

- A tag is never moved, deleted and recreated, or reused: a version number names one set of bytes,
  and people may already hold them. A mistake in a release is fixed by the next version.
- Nothing is built for a release on a laptop.
- A hot fix is a normal release with the next patch number: commit the fix to `main` (through a
  pull request), add `## [X.Y.Z]` to the changelog and run the steps above. If `main` holds work
  that must not ship yet, branch from the released tag, fix there, merge the fix into `main` through
  a pull request, and release from `main` once it is ready; the script tags only `main`.
- If the workflow fails before the release is created, fix the cause and run it again from the
  Actions page ("Re-run all jobs" reuses the same tag). If it failed after the release exists,
  delete the release in the web interface (leave the tag) and re-run the `publish` job.
