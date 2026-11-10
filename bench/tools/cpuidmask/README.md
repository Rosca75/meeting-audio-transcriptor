# cpuidmask

A benchmark-only tool for Linux x64. It hides CPU features from one process so
that a server processor selects the same code paths as an AVX2 laptop. The
benchmark machine of this repository has AVX-512, AVX-512 VNNI and AMX, which
make 8-bit model kernels several times faster than on the reference laptop
(4 cores, AVX2); timings taken without the mask do not transfer.

It works by turning on Linux CPUID faulting for the process and answering each
trapped `CPUID` instruction with the real answer minus the masked bits. Nothing
is emulated: the code that runs is the processor's own AVX2 code.

```
gcc -O2 -shared -fPIC -o libcpuidmask.so cpuidmask.c -ldl
CPUIDMASK=avx2 LD_PRELOAD=$PWD/libcpuidmask.so <command>
```

| `CPUIDMASK` | What the process sees |
|---|---|
| `avx2` (default) | AVX2 and FMA; no AVX-512, no AMX, no VNNI of any kind (a laptop before 2021) |
| `avx2-vnni` | as above plus AVX-VNNI (Intel 12th generation and later) |
| `none` | the processor as it is |

The tool is not part of the application and is never shipped.
