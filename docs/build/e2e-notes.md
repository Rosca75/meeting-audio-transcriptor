# End-to-end notes

Working notes of the end-to-end proof of the wired product: the command line over the test set, the
benchmark, the error handling and the Linux window. The machine was shared with other work and every
run was niced (`nice -n 10`, 4 recognition threads, Intel Xeon at 2.1 GHz with 4 logical cores, no
`CPUIDMASK`), so every timing here is indicative; a quiet machine reruns them.

## 1. What is wired

| Piece | Where |
|---|---|
| Granite recogniser, one shared session (1 intra-op thread, no spinning), vocabulary from `tokenizer.json`, low-yield retry off | `src/MeetingAudioTranscriptor.App/RecognizerFactory.cs` |
| Region-aware call: a recogniser implementing `IRegionAwareRecognizer` gets each chunk's speech regions | `src/MeetingAudioTranscriptor.Engine/Pipeline/TranscriptionPipeline.cs` |
| Chunk target 25 s by default (`PipelineOptions.DefaultTargetChunkSeconds`, still an option) | `src/MeetingAudioTranscriptor.Engine/Pipeline/PipelineOptions.cs` |
| Speaker separator over `DiarisationCollector` and `SpeakerTimeline`, one per run, words renumbered by first appearance | `src/MeetingAudioTranscriptor.Engine/Diarisation/DiarisationSpeakerSeparator.cs` |
| Embedding session opened once per service with the other models; separation on when the model is installed; a notice otherwise | `RecognizerFactory.cs` (`SpeakerSeparatorFactory`), `LoadedModels.cs`, `TranscriptionService.cs` |
| `models` metadata lists the models a run used (`speaker-embedding` only when it separated speakers) | `TranscriptionService.cs` |
| Bridge adapter, stage words, outcome kinds | `src/MeetingAudioTranscriptor.App/Bridge/ServiceTranscriber.cs`, `BridgeStages.cs` |
| Desktop window drives the real service (demo stays for `MAT_UI_DEMO=1`) | `src/MeetingAudioTranscriptor.Shell/ShellComposition.cs` |
| Benchmark speaker figures and `--speakers-hint-from-reference` | `src/MeetingAudioTranscriptor.Cli/Benchmark/` |

The page holds no stage names of its own (`frontend/js/copy.js` has none and the page shows the
`stage` string as it arrives), so the hosts use the five phrases the demo already showed: loading
the models maps to `Reading the audio`, recognition to `Recognising words`, speakers to `Telling
speakers apart`, cleaning and writing to `Writing the transcript`. `Finding speech` stays unused by
the real pipeline, whose voice activity runs inside recognition.

## 2. Command line runs

`transcribe` with all three formats and `--raw`, 4 threads:

| File | Audio | Wall | Speed | Speakers (reference) | Words recognised / after cleaning |
|---|---|---|---|---|---|
| `clips/en/en-ami-01.wav` | 1 min 32 s | 4.2 s | 21.7x | 6 (4) | 162 / 157 |
| `clips/fr/fr-multi-02.wav` | 1 min 56 s | 5.2 s | 22.1x | 3 (3) | 270 / 270 |
| `clips/de/de-multi-03.wav` | 1 min 40 s | 4.6 s | 21.8x | 5 (4) | 204 / 204 |
| `long/en-10min.m4a` | 10 min 04 s | 11.5 s | 52.4x | 7 (6) | 1457 / 1457 |
| `long/en-10min.mp4` | 10 min 04 s | 11.5 s | 52.4x | 7 (6) | 1457 / 1457 |
| `long/fr-10min.wav` | 10 min 16 s | 11.2 s | 55.2x | 6 (4) | 1329 / 1328 |

The wall time includes loading the models (about 2.5 s), which a one-file command pays every time.
The m4a and mp4 transcripts are identical to each other and score the same as the WAV (1.2 %).

By eye: speaker labels change only at segment boundaries, timestamps run forward and cues never
overlap, the raw files keep the backchannels the cleaned ones drop ("Yep.", "Okay,"), and the JSON's
`models` names `recognition`, `voice-activity` and `speaker-embedding`. Two things a reader notices:
a speaker whose only segment was removed by cleaning is counted in the TXT header ("Speakers: 6")
but appears only in the raw files; and the extra speakers the clustering finds without a hint hold
very little speech (in `fr-10min`: 2.3 s and 1.8 s over two segments each, against 40 to 170 s for
the real ones; in `en-10min`: 2.4 s), section 4.

Ten lines of `en-ami-01.txt` (cleaned):

```
Transcript of en-ami-01.wav
Duration: 0:01:31
Language: en
Date: 2026-11-10
Speakers: 6

[00:00:05] Speaker 1:
Kind of dog.

[00:00:12] Speaker 2:
```

and the same stretch of `en-ami-01.raw.txt` keeps the backchannel the cleaned text removed:

```
[00:00:08] Speaker 2:
Yep.

It's actually sitting, so sorry it's sitting, it's not standing. okay, I see it as one thing it's very supportive.
```

Six cues of `de-multi-03.vtt`:

```
1
00:00:00.584 --> 00:00:08.984
<v Speaker 1>Mit seinen eigentlichen Wählern ist der Kandidat seit gestern fertig, Nur noch mit dem Volke hat er sich auseinanderzusetzen.</v>

2
00:00:09.064 --> 00:00:15.864
<v Speaker 1>Drum kehrt er auch vorschriftsmäßig jenen den Rücken zu und wendet sich mit dem üblichen Gentleman</v>

3
00:00:18.280 --> 00:00:21.960
<v Speaker 2>Send mich in ab.</v>

4
00:00:23.640 --> 00:00:29.000
<v Speaker 2>Vier Leute gehen an ihm vorüber, die meisten sehen gar nicht auf.</v>

5
00:00:29.624 --> 00:00:35.624
<v Speaker 2>Sein sanfter Blick wird trüb und trüber, doch spielt er immer wacker auf.</v>

6
00:00:37.832 --> 00:00:48.792
<v Speaker 3>Während er auf dem Stuhl neben meinem Kopfkissen saß, fühlte ich mich so beschützt, so sicher, und als die Tür sich hinter ihm schloß, wurde das ganze Zimmer dunkel und mein Herz verzagte von neuem.</v>
```

## 3. Benchmark

Result files: `bench/results/2026-11-10-e2e-clips.json`, `-e2e-long.json`, `-e2e-noise.json`,
`-e2e-containers.json` (`--threads 4`; clips and long with `--speakers-hint-from-reference`).
Throughput is end to end per file (decoding, every stage and writing), model loading excluded.

| Group | Files | WER raw | WER cleaned | Retention | Removed | Throughput |
|---|---|---|---|---|---|---|
| en-read (clips) | 3 | 2.2 % | 2.4 % | 1.0000 | 0.3 % | 52.7x |
| en-ami (clips) | 3 | 24.1 % | 26.1 % | 1.0000 | 2.4 % | 53.9x |
| fr (clips) | 5 | 2.9 % | 3.0 % | 1.0000 | 0.1 % | 51.9x |
| de (clips) | 5 | 7.3 % | 7.7 % | 1.0000 | 0.4 % | 59.0x |
| en-10min | 1 | 1.2 % | 1.2 % | 1.0000 | 0.0 % | 69.4x |
| fr-10min | 1 | 2.4 % | 2.5 % | 1.0000 | 0.1 % | 73.8x |
| de-10min | 1 | 8.2 % | 8.3 % | 0.9970 | 0.4 % | 75.2x |
| containers (m4a, mp4) | 2 | 1.2 % | 1.2 % | 1.0000 | 0.0 % | 66.4x |
| noise | 4 | | | | | 279x, 0 words |

Retention per language over clips and long files: en 1.0000, fr 1.0000, de 0.9983 (clips 530/530,
de-10min 658/660), all 0.9995; every one meets the 0.99 gate. Noise: 0 words over the four files,
each `NoSpeech`. Cleaning raises the word error rate a little because the references transcribe
the backchannels and fillers it removes. Peak working set 1.8 to 1.9 GB.

Speakers (collar 0.25 s; the hypothesis turns are the transcript's segments):

| Group | Files | DER excl. | DER incl. | Confusion | Attribution | Count exact | With the hint: DER excl. / attribution / count exact |
|---|---|---|---|---|---|---|---|
| en-read clip | 1 | 0.043 | 0.043 | 0.000 | 0.889 | 1/1 | 0.043 / 0.889 / 1/1 |
| en-ami | 3 | 0.130 | 0.253 | 0.027 | 0.829 | 0/3 (6, 3, 3 for 4) | 0.167 / 0.795 / 3/3 |
| fr clips | 3 | 0.127 | 0.127 | 0.000 | 0.848 | 3/3 | 0.127 / 0.848 / 3/3 |
| de clips | 3 | 0.111 | 0.111 | 0.008 | 0.867 | 2/3 | 0.103 / 0.875 / 3/3 |
| en-10min | 1 | 0.077 | 0.077 | 0.005 | 0.883 | 7 for 6 | 0.071 / 0.889 / 1/1 |
| fr-10min | 1 | 0.156 | 0.156 | 0.007 | 0.817 | 6 for 4 | 0.148 / 0.824 / 1/1 |
| de-10min | 1 | 0.065 | 0.065 | 0.000 | 0.911 | 5 for 5 | 0.065 / 0.911 / 1/1 |
| containers | 2 | 0.075 | 0.075 | 0.005 | 0.885 | 0/2 (7 for 6) | not run |

The error rates are almost all missed speech: a segment spans its first word's start to its last
word's end, so pauses inside a reference turn and the edges of every utterance count as missed
(on the synthetic clips 6 to 15 s per file, with no confusion at all). Confusion, the time given
to the wrong speaker, is under 1 % everywhere except the AMI meetings (2.7 %). The attribution
accuracy also counts missed speech against itself, which is why it sits near 0.85 to 0.91 where the
diarisation measurements on reference regions read 1.000; it is a lower bound of what a reader sees.

## 4. Findings

- Without a hint the speaker count is over by one or two on four of the thirteen scored files
  (en-ami-01, de-multi-03, en-10min, fr-10min). The extra speakers are tiny clusters of two or three
  windows (2 to 3 s of segments) that survive the `MinimumSpeakerSeconds` fold of 4 s (two windows
  at a 2 s hop): the VAD's padded regions and the 25 s chunk boundaries cut speech into more short
  regions than the reference regions the diarisation measurements used. The hint fixes every
  count. Raising `MinimumSpeakerSeconds` or folding speakers with only a few seconds of words is a
  tuning decision for the diarisation owner; nothing was changed here.
- `TranscriptionOutcome.ModelLoadSeconds` reported a few milliseconds on runs that reused the
  loaded models, against its contract of 0; fixed in `TranscriptionService` (only the loading run
  reports a load time).
- `frontend/BRIDGE.md` said the hosts inject the transport globals; they inject no script, and
  the page's `bridge.js` defines `window.__hostReceive` itself. Corrected, with the stage phrases
  added to the event table, and `frontend/frontend-hashes.txt` refreshed.

## 5. Error handling

Each case through `meeting-audio-transcriptor-cli transcribe`; paths shortened to `<dir>`.

| Case | Exit | Message |
|---|---|---|
| Corrupt file (random bytes named `.m4a`) | 2 | `the audio file '<dir>/corrupt.m4a' could not be decoded: its data is damaged or in a format ffmpeg does not read (ffmpeg said: [in#0] moov atom not found \| ...); check that it plays in a media player, or export the recording again as .m4a, .mp4 or .wav` |
| Unsupported, `.txt` | 2 | `'<dir>/notes.txt' is not a supported recording: its content is neither WAV nor MP4 and its extension is '.txt'; choose a .wav, .m4a, .mp4 file, or convert the recording to one of them` |
| Unsupported, `.mp3` | 2 | the same sentence naming `'.mp3'` |
| Missing file | 2 | `'<dir>/nothere.wav' does not exist; check the path and the file name` |
| Empty file (`.m4a` or `.wav`, 0 bytes) | 2 | `the audio file '<dir>/empty.m4a' is empty (0 bytes); the recording was not saved completely, so copy it again from where it was recorded` |
| Video-only `.mp4` | 2 | `the file '<dir>/video.mp4' holds no audio stream (only video or data); export the meeting recording with its sound track, or choose the audio file itself` |
| Output folder not writable (read-only tmpfs) | 4 | `the transcripts could not be written to <dir>/ro (Read-only file system : '<dir>/ro/.<guid>.probe.tmp'); choose a folder you can write to with the output option, or fix its permissions` |
| Disk full (64 KB tmpfs, `en-10min.wav` with `--raw`) | 4 | `the disk holding <dir>/full is full; free some space or choose another output folder, then run the transcription again`; the folder is left empty |
| Ctrl+C (SIGINT at about 19 %, `en-10min.m4a`) | 130 | `the transcription was cancelled; no transcript file was written`; the folder is left empty, the ffmpeg child stopped |
| Models folder missing | 3 | `the models folder /nonexistent/models lacks silero-vad/silero_vad.onnx, granite-speech-5.0-470m-turboctc/model.int8.scores.onnx, granite-speech-5.0-470m-turboctc/tokenizer.json; fetch them with: node build/fetch-models.mjs --set all --into /nonexistent/models (or point MAT_MODELS_DIR at a folder that holds them)` |
| Speaker model missing (not an error) | 0 | `note: speakers were not told apart: the speaker-embedding model wespeaker-campplus-lm/wespeaker_en_voxceleb_campplus_lm.onnx is not in <dir>; fetch it with node build/fetch-models.mjs --model wespeaker-campplus-lm --into <dir> to label speakers` |

The process-level cases (read-only mount, a full tmpfs, a signal) were run by hand as root. The
tests covering them: the outcome kinds and their exit codes in `Cli.Tests`, every failure kind, the
disk-full recognition and the cleanup after a failure or a cancellation in `App.Tests`, the decoder
messages in `Media.Tests`, and a cancellation at 20 % of a real run leaving the folder empty in
`Pipeline.Tests` (`RealServiceTests`).

## 6. Desktop window

`LinuxWindowRealRunTests` (UI tier) launches the real shell under `xvfb-run -a` with
`MAT_UI_DEMO=0` and `MAT_MODELS_DIR`, drops a copy of `fr-multi-01.wav` through the drop hook,
presses Start and waits for `run:finished`: it passed, with kind `completed`, the summary shown and
`meeting.vtt`, `meeting.json` and `meeting.txt` written beside the recording (the JSON holds
segments and two speakers). The four UI-tier tests took 10 s together. The finished page is
`docs/build/e2e-window-finished.png` (64 KB): 1 min 30 s of audio in 5 s, two speakers, 198 words.
