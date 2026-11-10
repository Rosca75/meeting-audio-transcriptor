# Build state

> Working document, not authoritative: where it differs from `CLAUDE.md`, `docs/decisions/` or `docs/BENCHMARKS.md`, those win.

Working file of the autonomous build. Re-read after any context reset.

## Phase

Gates 0 to 6 met and on `main` (de08870): the product works end to end from
the CLI and the Linux window, CI was green on both systems, the acceptance
benchmark passes. Phase 7 (hardening, polish, final report) in progress.

## Acceptance numbers (quiet machine, 4 threads, `bench/results/2026-11-10-accept-*.json`)

- 2-hour M4A end to end, speakers on: AVX2-only mask 288 s (25.0x, 2.18 GB
  peak); AVX2 + AVX-VNNI 166 s (43x); unmasked 95 s (75x); AVX2 without
  speakers 242 s (29.8x); 8 threads (oversubscribed) 284 s.
- WER raw under the AVX2 mask: clips en-read 2.17, en-ami 24.0, fr 2.88,
  de 7.70; 10-minute files en 1.15, fr 2.51, de 8.76. Cleaned is 0.1 to 0.4
  points higher on read speech, 1.9 on AMI.
- Retention at least 0.997 per group, 0.999 overall; noise files 0 words.
- Exact 8-bit kernels are switched on automatically without VNNI (decision
  0010): German 12.4 -> 8.8 for 20 percent less speed.

## Key findings so far

- Recogniser choice: IBM Granite Speech 5.0 470M TurboCTC, int8 ONNX
  (Apache-2.0, community export pinned by commit and SHA-256). Python spike
  WER (%), en-read / en-ami / fr / de: Granite 1.58 / 36.6 / 3.79 / 8.45;
  Parakeet v3 fp32 4.07 / 18.2 / 6.55 / 10.54; Parakeet Ultra int8
  2.54 / 24.0 / 5.67 / 11.59; Whisper turbo 2.35 / 28.3 / 5.85 / 9.24;
  Parakeet v3 int8 12.6 / 39.5 / 13.7 / 13.6 (drops speech).
- Granite drops quiet or overlapped meeting speech unless the input level is
  normalised: with a slow gain control AMI falls to about 26 %. Level
  normalisation and a low-yield retry are being built into the recogniser.
- Speed (recognition only, 4 workers x 1 intra-op thread, machine shared, so
  lower bounds): no mask Granite about 60 to 120x, Parakeet int8 about 17 to
  26x; with the AVX2 mask Granite 34x, Parakeet Ultra int8 17.6x, Parakeet fp32
  11.6x.
- This machine has AMX and AVX-512 VNNI. `bench/tools/cpuidmask` hides them
  (`CPUIDMASK=avx2 LD_PRELOAD=/home/user/cpuidmask/libcpuidmask.so`); every
  acceptance timing is taken under the `avx2` mask, on a quiet machine.
- N workers sharing one model with one intra-op thread each beats one session
  with N intra-op threads by about 2x.
- VAD: Silero v6, 8 lanes per batch, about 16 to 19 core-seconds per 2 hours.
- Media Foundation interop was executed under Wine and decoded WAV, M4A, MP4.

## Agents in flight (worktrees under `.claude/worktrees/`)

- End-to-end CI and release dry run (opus): works on remote branch
  `claude/ci-fix`; adds an `end-to-end` job that transcribes real clips on the
  Windows and Linux runners (Media Foundation decode asserted on Windows),
  triggers `release.yml` by dispatch as a dry run, records size budgets.
- Final review (opus): `docs/build/final-review.md`; fixes small defects;
  must not touch `.github/**`, `build/publish.ps1`, `bench/testset/**`,
  `docs/TESTING.md`, `docs/RELEASING.md`, `docs/WINDOWS-VERIFICATION.md`.

Merged since the last note: polish list and `docs/BENCHMARKS.md`, speaker-count
tuning (floor rule: 4 s or 1.5 percent of speech; counts exact on the 10, 30
and 120 minute files). Unit tier 1323 passing. CI run 42917288395 on `main`
(de08870) was green.

## CI status

Run 42906481860 on `claude/ci-fix` was green on Windows and Linux for every
job; the fixes are merged into the work branch. Real Media Foundation on the
Windows runner decoded M4A and MP4 identically to ffmpeg. The remote branch
`claude/ci-fix` could not be deleted from this session (the git proxy refuses
branch deletion); it is fully merged and is named in the final report for the
owner to delete.

## Merged so far

Scaffold, writers and schema, cleaning and scoring, WAV reader and resampler
and FFT, ONNX Runtime runner and fetch tools and locks, media decoders, VAD and
chunker, pipeline and App service and CLI (part A, fake recogniser), page and
bridge, both desktop hosts (Linux window runs the real page under xvfb), CI and
release workflows, publish and release scripts, NOTICE, CHANGELOG, RELEASING,
speaker separation (decision 0005), the Granite recogniser with the derived
scores graph (WER on VAD chunks: en-read 2.2, en-ami 24 to 26, fr 2.9, de 7.3;
single core 30x real time unmasked).
Unit tier 1138 passing; page tests 84; tool tests 22. `main` equals the work
branch at 3e7e426.

## CI

GitHub Actions results CAN be read from this session (GitHub MCP
`actions_list`, `get_job_logs`). Pushing to `main` triggers `ci.yml`, whose
Windows legs are the only execution of Windows code available: read them and
fix what fails.

## Environment (probed 2026-11-10)

- Linux container, Ubuntu 24.04, Intel Xeon 2.10 GHz, 4 cores, 1 thread per core
  (no SMT), AVX2, AVX-512, AVX-VNNI, 15 GB RAM, no GPU, about 30 GB free disk.
- The reference laptop is 4 cores / 8 threads. This container has 4 hardware
  threads only, so benchmarks are pinned to 4 threads; an 8-thread run here
  oversubscribes and is reported for information only.
- .NET SDK 10.0.404 installed under /opt/dotnet. ffmpeg 6.1.1 (distribution
  build) on PATH. Node 22, Python 3.13 (spikes only).
- GTK 3 and WebKitGTK 4.1 (2.52) runtime installed from apt; xvfb present.
- NuGet and Hugging Face reachable.
- Reference repo present at /home/user/doc-anonymiser-for-.NET (read-only).

## Branch status

- Work branch: `claude/admiring-bell-dajief`, pushed after every commit.
- `main` accepts a direct fast-forward push; it is updated at every gate.
- Implementation agents work in git worktrees and commit on their own
  branches; the orchestrator merges them into the work branch.

## Working rules learned

- The machine has 4 cores shared by every agent. A timing run is valid only on
  a quiet machine: benchmarks run one at a time, and builds that overlap a
  benchmark run as `nice -n 19 taskset -c 3 dotnet ... -m:1`.
- Models sit outside the repo: `/home/user/model-cache/` (spikes) and
  `/home/user/meeting-audio-transcriptor-models/` (`MAT_MODELS_DIR`, fetched by
  `build/fetch-models.mjs`). Test audio: `bench/data/` (git-ignored, rebuilt by
  `node bench/testset/fetch-testset.mjs`).

## Decisions

See `docs/decisions/`.

- 0001 repository layout and conventions.
- Pending (phase 2): recogniser choice. Early probe: the Parakeet TDT 0.6B v3
  int8 encoder alone runs at about 10 to 12 times real time on this machine, so
  the 12x end-to-end target is at risk with it; IBM Granite Speech 5.0 470M
  TurboCTC (Apache-2.0, CTC, EN/FR/DE) measured about 3 times faster in the
  research agent's rough test. The phase 1 spike decides.

## Open risks

- Windows code cannot be executed here.
- 8-thread behaviour of the reference laptop cannot be measured, only 4 threads.

## Next steps

1. Merge the two agents; run unit tier, page and tool tests; quick 2-hour
   masked run to confirm speed after the speaker tuning.
2. Push to `main`, read the CI run (GitHub MCP), fix anything red.
3. Full `build/publish.ps1 -Rid linux-x64` once more with the final lock;
   smoke-test the extracted package (CLI transcribe on a clip, window under
   xvfb).
4. Final review (opus) of the whole tree against the build prompt's definition
   of done; fix findings.
5. Final report: built, benchmark table, decisions, deviations, gaps, Windows
   checks, release commands. Mention the leftover remote branch `claude/ci-fix`.

## Polish list (after the integration agent reports)

1. Wire `PreferFfmpeg` to an environment variable (`MAT_PREFER_FFMPEG=1`) and
   print the decoder name in the CLI summary, so the Windows checklist can
   compare Media Foundation with ffmpeg.
2. `--speakers` range: CLI accepts 1 to 32, the window 1 to 10; make both 1 to 10.
3. `pyannote-segmentation-3.0` is in the default set but unused: make it a
   candidate so it does not ship; keep `NOTICE` consistent.
4. `docs/TESTING.md`: add the `cleaning`, `diarisation`, `scoring` categories;
   two chapters are numbered 7.
5. `docs/transcript.schema.json` holds an em dash; the schema example names a
   model that does not ship.
6. Re-read `README.md` and `docs/WINDOWS-VERIFICATION.md` against the wired CLI.
7. `docs/BENCHMARKS.md` from the quiet-machine runs.

## Pending document edits (apply after the end-to-end CI agent's branch merges)

- `docs/WINDOWS-VERIFICATION.md` offline item, replace with: "**Offline.** With
  Resource Monitor (Network tab) or `Get-NetTCPConnection -OwningProcess
  (Get-Process msedgewebview2,meeting-audio-transcriptor*).Id`, run a
  transcription and leave the window open for five minutes. Expected: no TCP or
  UDP connection from `meeting-audio-transcriptor*.exe`, `ffmpeg.exe` or the
  `msedgewebview2.exe` processes started by the application (no component
  update, variations, SmartScreen, sync, metrics or crash upload). Not the
  application's: `MicrosoftEdgeUpdate.exe` / the Edge Update service, a
  separate machine-wide service, and anything a group policy forces on. Report
  any connection from msedgewebview2 with its remote address."
- Same file: temporary files are now `.<name>.<random>.mat-tmp` and
  `.mat-bak`, not `*.tmp`.
- `docs/RELEASING.md`: "release.ps1 orders versions by semantic-version
  precedence: a pre-release sorts below its final version, so `0.2.0` may
  follow `0.2.0-rc.1`, but `0.2.0-rc.2` is refused once `0.2.0` is released.
  Check the ordering with `pwsh build/release-version.test.ps1`." Also:
  `--version` prints `meeting-audio-transcriptor X.Y.Z`.
