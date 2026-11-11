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

The ffmpeg path is covered by integration tests. The Media Foundation path is
executed on a real Windows by CI (`windows-latest`, run 42926115111): the
integration tier decodes generated files, a 16 kHz mono `.m4a` among them,
and the end-to-end job transcribes a 10-minute `.m4a` whose `Decoder:` line
reads `media-foundation`, then the same file through ffmpeg
(`MAT_PREFER_FFMPEG=1`). The two transcripts differ by 3 word edits out of
1,457 (0.21 %), and the file reads at 13.2 times real time through Media
Foundation and 13.5 through ffmpeg on the hosted runner (`docs/BENCHMARKS.md`,
chapter 9). The vtable slots, GUIDs and structure layouts were also exercised
under Wine 9 before any Windows run, which decoded WAV, AAC in M4A and AAC in
MP4. The reading on a laptop and on recordings from real phones and meeting
tools remains on the owner's checklist (`docs/WINDOWS-VERIFICATION.md`).

## Consequences

A compressed WAV (ADPCM, MP3 in WAV) falls through to the platform decoder.

Media Foundation's AAC decoder announces a file at 24 kHz or below at twice its
rate (it assumes implicit SBR) and corrects the type with the first decoded
block, which only a real Windows shows. The source therefore reads the first
block while opening and takes the rate it carries; a rate change after a sample
has been handed on is refused. It also keeps the encoder's priming frame that
ffmpeg drops, so its decode starts up to 64 ms earlier at 16 kHz.
