# CLAUDE.md: the engine

The charter of `src/MeetingAudioTranscriptor.Engine/`. The root `CLAUDE.md` overrides it where they conflict.

## 1. What this project owns

The engine turns samples into a transcript document: audio handling, signal processing, voice activity, recognition decoding, speaker separation, the pipeline, cleaning, the three writers and the scoring used by the benchmark. It depends on the framework and on `MeetingAudioTranscriptor.Contracts` alone.

| Folder | Owns |
|---|---|
| `Audio/` | `IAudioSource`, the WAV reader, the resampler to 16 kHz |
| `Dsp/` | the real FFT the feature extractors share |
| `Vad/` | the Silero driver, the speech segmenter and the chunker that cuts at silences |
| `Recognition/` | `ISpeechRecognizer`, `IRegionAwareRecognizer`, the Granite recogniser: gain levelling, CTC decoding, the BPE vocabulary, the low-yield retry |
| `Diarisation/` | Kaldi filterbank features, the embedder, the window cutter, the clusterer, the speaker timeline |
| `Pipeline/` | the streaming pipeline, the segment builder, the transcript assembler, progress, `ISpeakerSeparator` |
| `Cleaning/` | the rule-based cleaner and the per-language lexicons |
| `Writers/` | the VTT, JSON and plain text writers |
| `Scoring/` | word error rate, retention, diarisation error rate, text normalisation |
| `Models/` | `ITensorRunner` and `TensorValue`, the model boundary |

## 2. The boundaries

**Four interfaces are the only way the engine meets the outside.** `IAudioSource` (a forward-only stream of mono float samples), `ITensorRunner` (named tensors in, named tensors out), `ISpeechRecognizer` (a chunk in, timed words out) and `ISpeakerSeparator` (blocks in during the run, one speaker per word out at the end). Each is implemented elsewhere (Media, Inference, the Granite and Diarisation folders over a runner) and faked in the tests, because an algorithm that reaches its model through an interface is tested without the model.

**The engine receives samples and returns bytes, never a path.** A source is handed in and a stream is written to, so the engine runs in a console test with arrays and needs no file system.

**The engine names no window, no package and no operating-system API.** The project file takes no package reference and sets `AllowUnsafeBlocks` to false; `EngineReferencesGuardTests` fails when the assembly references anything but `System.*` and the contracts, and `NoNetworkGuardTests` fails when a source names a network type, because a dependency in the engine would leak a runtime into every test.

**A model's algorithms live here, the runtime lives in Inference.** Features, decoding, clustering and cleaning are engine code over `ITensorRunner`, so a change to the model package never moves an algorithm.

## 3. Threading

**One producer, N recognition workers, one assembler.** The producer reads the source, resamples, runs the VAD and cuts chunks; the chunks wait in a bounded queue of two per worker; the workers share one recogniser and one loaded model; the calling thread assembles the transcript at the end. A bounded queue keeps memory flat for any recording length, and one shared model keeps one copy of the weights (`docs/decisions/0003-threading-model.md`).

**The default worker count is the logical cores, at most 4.** A hyper-thread adds little to a model that keeps a core's vector units busy; `PipelineOptions.Threads` overrides it.

**Every recogniser and separator is safe to call from several threads, and answers the same whatever the thread count.** The separator orders blocks by their start sample itself and the pipeline orders words by chunk index, because a result that depends on thread timing cannot be tested or compared between machines.

**A failure on any thread cancels the others and the original exception is rethrown unwrapped.** A cancelled run throws `OperationCanceledException`; callers map it, the engine does not.

## 4. Determinism and allocation

**The same samples, models, options and language give the same transcript, and the writers' bytes do not depend on the locale.** Formatting is invariant (`InvariantGlobalization` is on) and times are rounded the same way in every writer, because a benchmark and a user compare files.

**No randomness and no wall clock in an algorithm.** Time comes from the `TimeProvider` the pipeline is given, and appears only in the metadata and the timings.

**Audio is streamed and a chunk's samples are dropped when its worker is done.** A two-hour recording is never held whole and only the words are kept, because the pipeline's memory budget is part of the product.

## 5. Cleaning

**Cleaning decides which words to drop and never rewrites one.** The only text changes are the capital of a new first word and the removal of punctuation a removal orphaned. The rules and their order are in `SegmentCleaner` (fillers, fragments and repetitions, false starts, pure backchannel, reassembly) and are idempotent, because cleaning cleaned text must change nothing.

**The lexicons own what counts as a filler, an acknowledgement or a protected word.** `Lexicons/EnglishLexicon.cs`, `FrenchLexicon.cs` and `GermanLexicon.cs` say in their remarks why each ambiguous word (`like`, `ben`, `er`, negations) is kept; a word is a filler only when it is almost never a content word, because a missing word in a meeting can change a decision.

**An answer after a question is content.** The cleaner is told whether the previous segment, by another speaker, ended with a question, and then keeps `yes`, `oui` and `ja`.

**The raw text of every segment is kept beside the cleaned text.** A segment cleaning removed whole carries the reason, so no word is lost without a trace.

## 6. Transcript shape

**A segment is a sentence-like stretch by one speaker.** The segment builder cuts at sentence ends, at pauses, and at the largest gap of an overlong run. A short segment of unsure words is removed with the reason `low-confidence`, except one holding a digit, because a number is content too precious to risk. The JSON shape is `docs/transcript-schema.md`; the contracts project owns its types.

## 7. Where measurements live

**Numbers are measured, recorded in a document and not repeated in code or charters.** Recogniser settings are in `docs/build/granite-measurements.md`, the VAD's in `docs/build/vad-measurements.md`, speaker separation's in `docs/build/diarisation-measurements.md`, the choices in `docs/decisions/` and the shipped build's speed and accuracy in `docs/BENCHMARKS.md`. A default carries a remark saying which measurement chose it.

## 8. Testing

Unit tests drive each folder with scripted chunks, fakes and tiny fixtures; the pipeline tests script chunks, a recogniser and a separator. Real-model tests are in `tests/MeetingAudioTranscriptor.Pipeline.Tests` and run in the models and deep tiers. `tests/CLAUDE.md` and `docs/TESTING.md` own the tiers.

## 9. Where to look next

The service that composes the engine: `src/MeetingAudioTranscriptor.App/CLAUDE.md`. The runtime and decoders that implement its interfaces: `src/MeetingAudioTranscriptor.Media/CLAUDE.md`. The suites: `tests/CLAUDE.md`.
