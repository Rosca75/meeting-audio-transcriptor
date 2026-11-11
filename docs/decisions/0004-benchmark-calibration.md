# 0004. Benchmarks run under a CPU-feature mask

## Context

The reference machine is a 4-core, 8-thread laptop with AVX2 and no usable
GPU. The build machine is a 4-core Xeon without SMT that has AVX-512, AVX-512
VNNI and AMX. ONNX Runtime selects its kernels from CPUID, and its 8-bit
kernels are several times faster with those extensions, so an unmasked timing
overstates what the laptop will do.

## Options

1. Report unmasked timings with a warning.
2. Rebuild ONNX Runtime without the wide kernels.
3. Hide the features from the process with CPUID faulting.

## Evidence

`bench/tools/cpuidmask` (option 3) is 80 lines of C loaded with `LD_PRELOAD`.
Under the `avx2` mask Granite int8 recognition drops from about 59 to 34 times
real time on the shared machine, while word error rate is unchanged (1.36 %
unmasked, 1.09 % masked on the 10-minute English file), which also shows the
8-bit kernels do not saturate on the AVX2 path for this model.

## Decision

Option 3. Every acceptance timing is taken with `CPUIDMASK=avx2`, with 4
threads, on a machine doing nothing else. Unmasked and `avx2-vnni` figures are
reported beside it for information. The benchmark result file records the mask.

## Consequences

- The masked figure is this processor's AVX2 code at 2.1 GHz, a fair stand-in
  for a 4-core laptop, not a measurement of it. `docs/WINDOWS-VERIFICATION.md`
  asks the owner to run the benchmark command on the laptop.
- 8 threads cannot be measured here: the machine has 4 hardware threads.
- A hosted Windows runner is an independent data point for the same
  configuration: 4 virtual processors, AVX2 and no VNNI, so the exact 8-bit
  kernels are on. It transcribes the 10-minute English `.m4a` at 13.2 times real
  time end to end (13.5 through ffmpeg), against 24.8 under the mask here
  (CI run 42926115111; `docs/BENCHMARKS.md`, chapters 4 and 9). The runner is
  shared and virtualised, so the figure corroborates that a processor without
  VNNI runs the shipped configuration above the 12 times real time target and
  does not stand for the laptop.
