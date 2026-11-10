# Build state

Working file of the autonomous build. Re-read after any context reset.

## Phase

Phase 0, reconnaissance (in progress).

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

- Work branch: `claude/admiring-bell-dajief`. `main` holds the initial commit.
- A direct push to `main` is attempted at the first gate; the fallback is one
  pull request from the work branch.

## Decisions

See `docs/decisions/`. None yet.

## Open risks

- Windows code cannot be executed here.
- 8-thread behaviour of the reference laptop cannot be measured, only 4 threads.

## Next steps

1. Reference digest, model research, test set (subagents running).
2. Phase 1 spikes: candidate comparison table.
