# Build state

Working file of the autonomous build. Re-read after any context reset.

## Phase

Phase 0 gate met (digest, state file, charter on `main`).
Phase 1, research and harness: in progress.

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

1. Phase 1 spike running: `docs/build/phase1-candidates.md`,
   `bench/results/2026-11-10-phase1-candidates.json`.
2. Running in worktrees: writers, cleaning and scoring, WAV reader and
   resampler and FFT, ONNX Runtime runner and fetch tools.
3. After the spike: decision records (recogniser, threading, diarisation,
   hosts, packaging), then recogniser decoding, VAD and chunker, pipeline, CLI.
