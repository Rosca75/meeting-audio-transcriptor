# Build state

Working file of the autonomous build. Re-read after any context reset.

## Phase

Phases 0 done. Phase 1 (research, harness, spikes) nearly done: the spike agent
is finishing its report. Phases 2 to 5 are being worked in parallel by agents;
the gates are checked in order before each merge to `main`.

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

- Phase 1 spike report (sonnet): `docs/build/phase1-candidates.md`.
- Diarisation (opus): `Engine/Diarisation`, `docs/build/diarisation-measurements.md`.
- Granite recogniser and graph patch (opus): `Engine/Recognition/Granite`,
  `build/onnx-patch.mjs`, `docs/build/granite-measurements.md`.
- Pipeline, App service, CLI with benchmark command (opus); part B wires the
  recogniser after it lands (the agent is resumed by message).
- Frontend page and App bridge (sonnet): `frontend/`, `App/Bridge`.
- Desktop hosts (opus): `Shell/`; its placeholder commit under `App/Bridge`
  and `frontend/` is dropped at merge in favour of the real ones.

## Merged so far

Scaffold, writers and schema, cleaning and scoring, WAV reader and resampler
and FFT, ONNX Runtime runner and fetch tools and locks, media decoders, VAD and
chunker. Unit tier: 718 tests passing before the agents above.

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

1. Merge agents as they report; resolve `.slnx` conflicts by keeping all lines.
2. Write decision records 0002 to 0009 (recogniser, threading, benchmark
   calibration, diarisation, hosts, packaging, dependencies, audio decoding).
3. Wire: recogniser into `RecognizerFactory`, diarisation into the separator
   seam, `TranscriptionService` into the bridge's `ITranscriber`, hosts to the
   real bridge and page.
4. Quiet-machine benchmarks under the AVX2 mask: clips, 10 min, 30 min, 2 h.
5. Phase 6: CI and release workflows, publish script, NOTICE, RELEASING.md.
6. Phase 7: acceptance, hardening, README, WINDOWS-VERIFICATION.md, cleanup.
