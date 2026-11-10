# CLAUDE.md: the App

The charter of `src/MeetingAudioTranscriptor.App/`. The root `CLAUDE.md` overrides it where they conflict.

## 1. What this project owns

The App composes the engine for both front ends: the transcription service (`TranscriptionService`, `TranscriptionRequest`, `TranscriptionOutcome`), the model loading and verification, the failure mapping and the atomic file writing; and the bound surface of the page (`Bridge/`). It references Contracts, Engine, Inference and Media, and names no window and no console: the Cli and the Shell sit on top of it.

## 2. The service

**The service is the only place that opens the recording, the models and the output folder.** The engine sees samples and bytes, so every file and model decision is made, and tested with fakes, in one class (`ServiceSeams` holds the fakeable collaborators).

**One run at a time, and a second request ends at once as `AlreadyRunning`.** Two runs would share the same cores and slow both, so the service answers instead of queueing.

**The models load on the first run and stay loaded.** Loading takes seconds and the weights are shared by every worker; a failed load is not remembered, so a run after the models were fetched succeeds.

**`RunAsync` never throws for a run that started.** Every ending is one `TranscriptionOutcome` with a kind and one actionable sentence, so a front end maps kinds and does not catch exceptions. An invalid request (no input, no format, an unknown language, a count below 1) is the only throw, because it is a programming fault of the caller.

**`FailureMapping` is the one place an exception becomes an outcome kind.** The kinds are `Succeeded`, `NoSpeech`, `InputNotFound`, `InputUnreadable`, `InputUnsupported`, `InputCorrupt`, `ModelsMissing`, `OutputNotWritable`, `DiskFull`, `Cancelled`, `AlreadyRunning` and `Unexpected`; the Cli maps them to exit codes in `ExitCodes.Of`. A failure caused by a cancellation (a decoder killed mid-read) is a cancellation.

**The recording is opened before the models load.** The opener sniffs the container and starts the decoder first, so a missing, damaged or unsupported file is reported at once rather than after seconds of loading; missing models are reported only for a recording that opened.

**The output is checked before the long run.** The folder is created and probed with a throwaway file, an existing folder in a transcript's place is refused and, under `OverwritePolicy.Refuse`, so is an existing file, because a two-hour run must not fail at its last step.

**Progress is one overall fraction that never decreases.** The pipeline takes 98 percent of the bar and writing the rest; one lock holds the highest value reported, because two threads report and a bar that moves back looks broken.

## 3. Atomic output

**Every file is written to a temporary in the destination folder and moved into place only when all are complete.** A failure or a cancellation deletes every temporary and leaves the old files as they were, so there is never a partial transcript and a running run never destroys the previous good one. The temporary is in the same folder so the move is a rename.

**The set is replaced as one or not at all.** Before anything moves, every existing transcript is opened for exclusive writing and closed again; the previous versions are then renamed aside to `.meeting.vtt.<random>.mat-bak`, the new files renamed in, and the backups deleted only once the whole set is in place. A failure while replacing removes the new files already placed, renames the backups back and ends as `OutputNotWritable` naming the file (`TranscriptFileBusyException`), because a folder holding some new and some old files is a transcript that contradicts itself. A backup that cannot be renamed back is kept, because it is the user's previous transcript. `IOutputFileSystem` is the seam a test fails on cue.

**A temporary's name is new and random, `.meeting.vtt.<random>.mat-tmp`, and it is created only where no file exists.** A fixed name such as `meeting.vtt.tmp` could be a user's own file, which the run would overwrite and then delete.

**Names follow the recording.** `meeting.m4a` gives `meeting.vtt`, `.json`, `.txt` and, for the text formats chosen with raw files on, `meeting.raw.vtt` and `meeting.raw.txt`; the JSON holds both texts and has no raw variant.

## 4. The models

**The models folder is `MAT_MODELS_DIR`, else `models` beside the executable, else the checkout's `models`.** One resolution (`ModelCatalog.ResolveRoot`) serves a package, a development run and the tests. `Check` lists missing files with the fetch command; `Verify` compares size and SHA-256 with the lock that sits beside the models, because a truncated download must not read as installed.

**One factory builds each model** (`RecognizerFactory`, `SpeakerSeparatorFactory`, the VAD in `LoadedModels`) with its session options, so the one-thread, no-spinning setting of the shared session is stated once.

## 5. The bridge

**The bridge names no window.** It reaches the window through `IHostServices` (emit, two dialogs, open a folder) and the transcriber through `ITranscriber`, so a test drives the whole surface with fakes. The contract with the page is `frontend/BRIDGE.md`, and the wire shapes are in the contracts project, serialised only through the generated `BridgeJsonContext`.

**`Receive` returns at once and never throws.** Work moves to the thread pool so the interface thread never waits on a dialog or a run; a message that cannot be read becomes the `host:error` event, and a call whose id can be read is always answered, so no promise on the page waits forever.

**One lock guards the cancellation slot in `RunCoordinator`.** Nothing is awaited or called out while it is held, and cancelling happens outside it, because a token's callbacks run synchronously on the cancelling thread and one that called back into the lock would deadlock.

**Exactly one terminal event per accepted run.** `run:finished` is posted from a `finally`, after the slot is freed and after the last progress event, whatever happened: success, no speech, cancellation, a failure or a transcriber that threw or returned nonsense (it then carries a generic sentence). A rejected start emits no event, because a run that never started has nothing to end.

**Progress is non-decreasing and rate limited.** `ProgressForwarder` clamps to the highest fraction seen and drops reports closer than 100 ms unless the stage changed or the work is complete, and it closes before the terminal event so none follows it.

**A request is validated and normalised before it runs.** `RequestValidator` lower-cases and de-duplicates the formats, drops the speaker count when separation is off and always sets the output folder; validation creates nothing, the service creates the folder.

**The demo transcriber simulates a run.** `DemoTranscriber` (selected by `MAT_UI_DEMO=1` in the Shell) writes nothing and needs no model, so the page can be shown and tested without them.

## 6. Testing

`tests/MeetingAudioTranscriptor.App.Tests` drives the service through its seams with an energy-based VAD graph and a fake recogniser over generated WAV files, and the bridge through a fake host and a fake transcriber. Wire shapes are compared against literals, property order included.

## 7. Where to look next

The engine: `src/MeetingAudioTranscriptor.Engine/CLAUDE.md`. The windows that host the bridge: `src/MeetingAudioTranscriptor.Shell/CLAUDE.md`. The page contract: `frontend/BRIDGE.md`.
