# Build state

> Working document, not authoritative: where it differs from `CLAUDE.md`, `docs/decisions/` or `docs/BENCHMARKS.md`, those win.

Working file of the autonomous build. Re-read after any context reset.

## Final status

All gates are met and the work is on `main`.

- The product transcribes `.wav`, `.m4a` and `.mp4` in English, French and German to `.vtt`, `.json` and
  `.txt` from the CLI and the desktop window, on Linux (executed here) and on Windows (executed by CI).
- Acceptance: a two-hour recording in 4.8 minutes under the AVX2-only mask (25.0 times real time, 2.18 GB);
  the final build confirms it at 280.9 s (25.6 times real time, 1.98 GB, WER 1.2 %, 6 of 6 speakers) in
  `bench/results/2026-11-10-final-avx2-120min.json`. The quality gate holds in every language except English
  meetings (recorded deviation, decision 0002).
- CI run 42926115111 (`ci`) and the release dry run 42926119455 are green. The dry run built both packages
  with models, smoke-tested a transcription from inside each staged package, and recorded
  `build/size-budgets.json` (linux-x64 tar.gz 586,938,980 bytes, win-x64 zip 579,592,570 bytes).
- On the hosted Windows runner the application reads a 10-minute `.m4a` through Media Foundation at 13.2 times
  real time (13.5 through ffmpeg), with 3 word edits of 1,457 between the two decoders, and the clip word
  error rates equal Linux's within 0.4 point (`docs/BENCHMARKS.md`, chapter 9). The end-to-end job found a
  Media Foundation defect (AAC at 24 kHz or below is announced at twice its rate); the source now reads the
  first block while opening.
- The final review (`docs/build/final-review.md`) found no open defect except the diagnostics socket below.

The leftover remote branch `claude/ci-fix` is fully merged and could not be deleted from the build session
(the git proxy refuses branch deletion); the owner deletes it.

## Open items

1. The owner's Windows checklist (`docs/WINDOWS-VERIFICATION.md`): the window, drag and drop, SmartScreen,
   real recordings and the speed on the target laptop. CI cannot prove these.
2. The .NET runtime's local diagnostics socket on Linux stays: no supported build-time switch exists
   (decision 0011, `docs/build/final-review.md` D6).
3. The AMI gap: English meetings score 24 % word error rate against 18.2 % for the best spike candidate, and
   the speaker count is one short on two of three AMI clips without the count hint (decisions 0002 and 0005).
4. Delete the remote branch `claude/ci-fix`.

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
- Speaker count without the hint (`2026-11-10-speakers-tuned-*.json`): exact on 13 of 15 files, all five long
  files included.

## Key findings

- Recogniser choice: IBM Granite Speech 5.0 470M TurboCTC, int8 ONNX
  (Apache-2.0, community export pinned by commit and SHA-256). Python spike
  WER (%), en-read / en-ami / fr / de: Granite 1.58 / 36.6 / 3.79 / 8.45;
  Parakeet v3 fp32 4.07 / 18.2 / 6.55 / 10.54; Parakeet Ultra int8
  2.54 / 24.0 / 5.67 / 11.59; Whisper turbo 2.35 / 28.3 / 5.85 / 9.24;
  Parakeet v3 int8 12.6 / 39.5 / 13.7 / 13.6 (drops speech).
- Granite drops quiet or overlapped meeting speech unless the input level is
  normalised: with a slow gain control AMI falls to about 26 %. Level
  normalisation and a low-yield retry are in the recogniser.
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
- Media Foundation interop was first executed under Wine and is executed on a real Windows by CI.
- GitHub Actions results can be read from the build session (GitHub MCP `actions_list`, `get_job_logs`), and
  were: the Windows legs of `ci.yml` are the only execution of Windows code available to the build.

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
