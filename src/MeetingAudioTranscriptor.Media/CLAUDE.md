# CLAUDE.md: media decoding

The charter of `src/MeetingAudioTranscriptor.Media/`. The root `CLAUDE.md` overrides it where they conflict.

## 1. What this project owns

Everything that turns an audio file into an `IAudioSource` for the engine: the container sniff, the opener that chooses a decoder, the ffmpeg child process decoder, the Media Foundation decoder, the shared PCM converter and the wording of every decode failure. It references the Engine alone. It allows unsafe code, as the Shell's native hosts do and no other project, because the Media Foundation calls are made through function pointers.

## 2. The decoder order

**The opener decides on content first and on the extension second.** The first twelve bytes say WAV or ISO media whatever the file is called, so a renamed recording opens, and an unrelated file is refused with the supported types named. An empty file, a missing file and a file that cannot be read are refused before any decoder runs, each with its own sentence.

**A plain WAV goes to the engine's own reader on every system.** The composition root supplies the reader and the predicate that says "an encoding I do not read", and a WAV the reader refuses (ADPCM, MP3 in WAV) falls through to the platform decoder, because one reader means one behaviour for the common case.

**On Windows `.m4a` and `.mp4` go to Media Foundation, then to ffmpeg when one is found; on Linux they go to ffmpeg alone.** Media Foundation is the system's decoder and needs no binary; ffmpeg is the bundled fallback that Linux has no alternative to (`docs/decisions/0009-audio-decoding.md`). `AudioFileOpenerOptions.PreferFfmpeg` reverses the Windows order for a diagnosis; the service sets it when `MAT_PREFER_FFMPEG` is `1`, and no command-line option or window control does. When every decoder fails the message names each one's answer, and the failure kind is the first one's, because that is the one the user acts on.

**ffmpeg is found in the order of intent.** `MAT_FFMPEG` (an administrator or developer naming the binary), then the `ffmpeg` folder beside the application (the build the release was tested with), then the PATH. The locator takes its environment, folder and file system as parameters, so every branch is tested without touching the machine's PATH.

**Every decoder delivers mono float samples at the file's own rate.** Downmix (the mean of the channels) and the PCM conversion live in `PcmConverter` and `PcmSampleReader`, and the engine's resampler converts to 16 kHz, so no path has a second resampler or a second downmix.

**`DecoderName` says which decoder read the file** (`wav`, `media-foundation`, `ffmpeg (bundled) at ...`) and is carried on the service's outcome for logs; no output file records it.

## 3. The ffmpeg process

**ffmpeg runs as a child process with an argument list, never through a shell.** A path with spaces or quotes is one argument, and a recording name cannot inject a command.

**A probe asks for the audio stream's rate and channels first, then the decode runs with raw float output on stdout.** Standard error is drained on its own thread and the last lines are kept, so a chatty ffmpeg never blocks on a full pipe and every failure can quote what ffmpeg said (`FfmpegErrors` reads the kind from phrases stable across releases, since exit codes vary).

**Disposing the source kills the whole process tree and waits briefly.** A cancelled run must leave no ffmpeg behind; a failure caused by that kill is reported by the service as a cancellation.

## 4. Media Foundation

**The Windows source is a sequence of calls and holds no decision.** The format mapping (`ToPcmFormat`), the layout check and every HRESULT's wording and advice are in `MediaFoundationErrors`, which is platform-neutral and tested on Linux; `MediaFoundationAudioSource` opens a source reader, selects the first audio stream alone, asks for float output and falls back to 16-bit PCM, reads the format back and copies each block into the shared converter. Thin Windows code is the rule because the Linux suites cannot run it.

**Interfaces are called through the object's virtual table, never through built-in COM marshalling or reflection.** The shipped build may be trimmed, which removes both. A call reads the function pointer at the interface method's slot and calls it as `delegate* unmanaged[Stdcall]` with the object as first argument.

**Every slot index is written down and cites its header.** `MediaFoundationNative` lists the complete ordered vtable of each interface used (`IUnknown`, `IMFAttributes`, `IMFMediaType`, `IMFSample`, `IMFMediaBuffer`, `IMFSourceReader`) from `Unknwn.h`, `mfobjects.h` and `mfreadwrite.h`, and every constant states the slot it is and the signature it calls. A slot is counted from zero with the base interface first; a wrong number calls another method, so a reviewer must be able to check each one line by line.

**Exported functions use `LibraryImport`.** Its marshalling is generated at compile time, which the trimmer can analyse.

**COM is started on the constructing thread and every pointer is released as soon as its step is done.** The source reader may be read from any thread of the multithreaded apartment; `Dispose` uninitialises COM only on the thread that initialised it, because an uninitialise belongs to its thread. The rate and channel count are never forced, so the engine's resampler stays the only one.

**The Windows path is checked by hand.** It was exercised under Wine and runs in the CI Windows leg; `docs/WINDOWS-VERIFICATION.md` lists what only a real Windows proves.

## 5. Failures are sentences

**Every decode failure is an `AudioDecodeException` with a closed kind and a message saying what failed and what to do.** The kinds (`FileNotFound`, `AccessDenied`, `EmptyFile`, `UnsupportedType`, `NoAudioStream`, `CorruptOrUnsupportedData`, `DecoderMissing`, `DecoderFailed`) are what the App maps to outcome kinds; a Windows N edition without the Media Feature Pack and a missing ffmpeg each name their remedy.

## 6. Testing

`tests/MeetingAudioTranscriptor.Media.Tests`: the PCM converter, the banner parser, the error wording, the sniff and the opener's decision table with fake decoders run in the unit tier; the ffmpeg decode tests generate their files with the ffmpeg they find and skip by name without one; the Media Foundation tests run on Windows alone and skip elsewhere. `docs/TESTING.md` owns the commands.

## 7. Where to look next

The interfaces it implements: `src/MeetingAudioTranscriptor.Engine/CLAUDE.md`. Its caller: `src/MeetingAudioTranscriptor.App/CLAUDE.md`. The suites: `tests/CLAUDE.md`.
