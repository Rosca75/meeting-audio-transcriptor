# 0010. Exact 8-bit kernels on processors without VNNI

## Context

The recogniser is an 8-bit (dynamically quantised) model. ONNX Runtime's fast
8-bit matrix kernels on x64 without VNNI multiply and accumulate in 16 bits,
which can saturate. The build machine has VNNI and AMX, so the effect is
invisible there; the reference laptop class (AVX2 only) is where it shows.

## Evidence

10-minute files, 4 threads, quiet machine, processor masked to AVX2 only
(`bench/results/2026-11-10-accept-avx2-fastkernels-long.json` against
`2026-11-10-accept-avx2-long.json`), word error rate in percent and speed in
times real time:

| Kernels | English | French | German | Speed |
|---|---|---|---|---|
| fast (the runtime's default) | 1.36 | 2.79 | 12.42 | 30 |
| exact (`session.x64quantprecision=1`) | 1.15 | 2.51 | 8.76 | 24.5 |
| for reference, a VNNI processor, fast kernels | 1.15 to 1.2 | 2.4 to 2.5 | 8.2 | 62 to 67 |

On a processor with VNNI the exact mode changes no result and halves the
speed (34 instead of 67 times real time unmasked).

## Decision

Sessions are opened with the exact kernels exactly when the processor has no
usable VNNI (neither AVX-VNNI nor AVX-512 VNNI with AVX-512 enabled). The
environment variable `MAT_ORT_QUANT_PRECISION` (`0` or `1`) overrides the
choice for benchmarks.

## Consequences

- Accuracy on an AVX2-only laptop matches a VNNI machine within noise, for
  about a fifth of the speed there; the 2-hour acceptance run stays at 25
  times real time.
- This is a quality lever chosen over speed where the speed margin allows it,
  in line with the priority rule: the target is met with a factor of two to
  spare.
