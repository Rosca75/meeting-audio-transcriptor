# 0007. Models, ffmpeg and packaging

## Context

The target machine is offline behind a restrictive proxy: no first-run
download. Models and ffmpeg binaries must not be committed.

## Decision

- `build/models.lock` pins every model file by address (commit or release
  tag), size and SHA-256; `build/fetch-models.mjs` (Node, no dependency)
  fetches and verifies them. `build/ffmpeg.lock` and `build/fetch-ffmpeg.mjs`
  do the same for the LGPL ffmpeg builds. The developer, the tests, CI and the
  release workflow use the same tools.
- One self-contained portable package per operating system
  (`win-x64` zip, `linux-x64` tar.gz), holding the desktop executable, the
  command-line executable, the `models/` folder, the `ffmpeg/` folder, `NOTICE`
  and the licence. Nothing is installed and nothing is downloaded at run time.
- ffmpeg is bundled on both operating systems. Linux has no other decoder for
  M4A and MP4. On Windows Media Foundation is the decoder and ffmpeg is the
  fallback, because the Media Foundation path cannot be executed by the Linux
  suites (it was exercised under Wine only).
- The recognition model ships as a graph derived at fetch time: the pinned
  export returns token ids only, and `build/onnx-patch.mjs` appends two nodes
  and one output so the graph also returns the per-frame peak probability. The
  edit is byte-deterministic and the lock pins the derived file's hash too, so
  the fetch tool refuses any other source and any other result.
- No single-file executable and no trimming of the model runtime's native
  library; the package is a folder, as in the reference.

## Consequences

- The package is large (about 0.6 GB of model plus about 0.15 GB of ffmpeg).
- A release is built only by the tag-triggered workflow, which needs network
  access to Hugging Face and GitHub at build time, never at run time.
