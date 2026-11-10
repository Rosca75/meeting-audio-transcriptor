# 0009. Audio decoding

## Decision

- `.wav` is read by the engine's own reader (PCM 8 to 32 bit, float, extensible,
  RF64, any channel count), from a stream.
- `.m4a` and `.mp4` are decoded by Media Foundation on Windows (source reader
  through raw COM vtable calls, safe under trimming) and by ffmpeg on Linux
  (bundled binary, then one on the PATH). Windows falls back to ffmpeg when
  Media Foundation fails and an ffmpeg is present.
- The container is recognised by content first and by extension second.
- Every decoder delivers mono float samples at the file's own rate; the
  engine's polyphase resampler converts to 16 kHz, so all paths share one
  resampler and one downmix (the mean of the channels).

## Evidence

The ffmpeg path is covered by integration tests here. The Media Foundation
path was executed under Wine 9 and decoded WAV, AAC in M4A and AAC in MP4
correctly, which confirms the vtable slots, GUIDs and structure layouts; real
Windows remains on the owner's checklist.

## Consequences

A compressed WAV (ADPCM, MP3 in WAV) falls through to the platform decoder.
