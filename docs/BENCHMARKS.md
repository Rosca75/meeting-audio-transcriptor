# Benchmarks

The measured speed, memory and accuracy of the shipped configuration, and how to reproduce each table. Every number comes from a result file under `bench/results/`, named beside it, and `node bench/tools/summarise-results.mjs` prints the tables below from those files.

## 1. What is measured and how

**End to end.** A run is the real service, the one a user's run goes through: decode, voice activity, recognition, speaker separation, cleaning and writing the three transcript files. The models load once before the first file; that time is recorded as `stageSeconds.modelLoad` and left out of a file's wall time, because it is a start-up cost.

**The machine.** A 4-core Intel Xeon at 2.1 GHz with 4 hardware threads (no SMT), 15 GB of memory, no GPU, Ubuntu 24.04, .NET 10.0.15 and ONNX Runtime 1.31.1. The machine does nothing else while a benchmark runs, and one benchmark runs at a time. Every run uses 4 threads, except the one named as oversubscribed.

**The mask.** The target is a laptop with 4 cores, 8 threads and AVX2. The build machine also has AVX-512, AVX-512 VNNI and AMX, which make 8-bit kernels several times faster than the laptop's. A timing without a mask would overstate the laptop, so the acceptance runs hide processor features from the process (`bench/tools/cpuidmask`, decision 0004). `machine.cpuIdMask` in a result file says which mask applied:

| Mask | What the process sees | Stands for |
|---|---|---|
| `avx2` | AVX2 and FMA; no AVX-512, no AMX, no VNNI | the reference laptop class (before 2021); the acceptance configuration |
| `avx2-vnni` | the above plus AVX-VNNI | a 12th generation Intel processor or later |
| none (`null`) | the processor as it is | the build machine |

Under `avx2` the engine uses the exact 8-bit kernels (chapter 7); under the other two it uses the runtime's fast kernels, which are exact on a VNNI processor.

**The test set.** Built by `node bench/testset/fetch-testset.mjs` from pinned public sources (`bench/testset/README.md`): 16 clips of 1 to 2 minutes (6 English, 5 French, 5 German; 26 minutes in all), three meeting-style files of about 10 minutes (English 603.7 s, French 615.9 s, German 614.0 s), the English file looped to 30 and to 120 minutes, AAC (`.m4a`) and `.mp4` copies of the English 10-minute file, and four noise inputs of 60 seconds (digital silence, white noise, pink noise, recorded office room tone). The English read clips are LibriSpeech, the French and German clips and files are Multilingual LibriSpeech (read audiobooks; their multi-speaker material is constructed by concatenating different speakers' utterances), and the English meetings are three excerpts of the AMI corpus (two headset mixes and one distant microphone, four speakers each).

**Scoring.** Word error rate is the word-level edit distance (substitutions, deletions and insertions over the reference word count), micro-averaged: the error counts of every file of a group are summed before dividing. Both texts are normalised the same way: Unicode NFKC, lower case, every character that is not a letter or digit becomes a space, so `don't` is two words and accents are kept. No number or spelling normalisation is applied. "Raw" scores the text before cleaning and "cleaned" scores the text a user reads. English combined is the micro-average over English read and English meetings, computed from the per-file counts. Content-word retention is the share of the raw transcript's content words (function words, hesitations and backchannels excluded, immediate repeats collapsed) still present after cleaning. Speaker error rates use frames of 10 ms and a collar of 0.25 s, with the best one-to-one mapping of speakers.

## 2. Speed and memory

The target is a two-hour recording in 10 minutes or less (12 times real time) within 4 GB of memory.

| Configuration | Mask | Threads | Speakers | Audio s | Wall s | Times real time | Wall (min) | Peak GB |
|---|---|---|---|---|---|---|---|---|
| AVX2 only, with speakers | avx2 | 4 | yes | 7201.9 | 287.9 | 25.0 | 4.8 | 2.18 |
| AVX2 only, without speakers (WAV) | avx2 | 4 | no | 7201.9 | 241.9 | 29.8 | 4.0 | 1.93 |
| AVX2 + AVX-VNNI | avx2-vnni | 4 | yes | 7201.9 | 166.5 | 43.3 | 2.8 | 1.95 |
| unmasked | none | 4 | yes | 7201.9 | 95.3 | 75.5 | 1.6 | 1.98 |
| AVX2 only, 8 threads | avx2 | 8 | yes | 7201.9 | 283.6 | 25.4 | 4.7 | 2.26 |

Target: 10 minutes or less (12 times real time), 4 GB. Every row meets it, with at least twice the speed required and about half the memory. The acceptance configuration, the first row (`2026-11-10-accept-avx2-120min-m4a.json`), takes 4.8 minutes (25.0 times real time) and peaks at 2.18 GB of working set. The other rows are `2026-11-10-accept-avx2-120min-wav-nospk.json`, `2026-11-10-accept-avx2vnni-120min-m4a.json`, `2026-11-10-accept-unmasked-120min-m4a.json` and `2026-11-10-accept-avx2-120min-m4a-8threads.json`.

- The rows other than the second use the `.m4a` copy (AAC at 64 kbit/s), so they include the decoder. The second reads the WAV file and has speaker separation off; the two differ in both respects, and decoding runs on its own thread beside recognition, so the 46 seconds between them are speaker separation (its embeddings are computed inside the recognition workers, see section 2.2).
- AVX-VNNI makes the run 1.7 times as fast as AVX2 alone, and the processor as it is 3.0 times as fast.
- The 8-thread run is on a machine with 4 hardware threads. It is oversubscribed, so it is reported for information: it is 1.5 percent faster than the 4-thread run and uses 80 MB more memory. What 8 threads do on a laptop with 8 hardware threads is not measured here.
- Memory is the peak working set of the process, which holds the models, the audio read ahead and the transcript.

### 2.1 Scaling with length

| Length | File | Audio s | Wall s | Times real time | Peak GB | Result file |
|---|---|---|---|---|---|---|
| 10 minutes | en-10min | 603.7 | 24.3 | 24.9 | 1.86 | 2026-11-10-accept-avx2-long.json |
| 30 minutes | en-30min | 1800.1 | 71.4 | 25.2 | 1.89 | 2026-11-10-accept-avx2-30min.json |
| 120 minutes (m4a) | en-120min | 7201.9 | 287.9 | 25.0 | 2.18 | 2026-11-10-accept-avx2-120min-m4a.json |

Speed is the same at 10, 30 and 120 minutes within 1 percent. Peak memory rises by 0.3 GB between 10 minutes and 2 hours.

### 2.2 Where the time goes

| Label | Pipeline s | Streaming s | Decode and voice activity s (producer) | Recognition s (summed over workers) | Recognition per worker s | Workers busy | Speakers s | Cleaning s | Writing s |
|---|---|---|---|---|---|---|---|---|---|
| avx2-120min-m4a | 287.8 | 287.5 | 39.5 | 1141.5 | 285.4 | 99 % | 0.35 | 0.04 | 0.06 |
| avx2-120min-wav-nospk | 241.9 | 241.8 | 25.4 | 960.6 | 240.1 | 99 % | 0.00 | 0.04 | 0.06 |
| avx2vnni-120min-m4a | 166.4 | 166.0 | 39.1 | 658.6 | 164.7 | 99 % | 0.38 | 0.04 | 0.06 |
| unmasked-120min-m4a | 95.2 | 94.9 | 37.6 | 376.3 | 94.1 | 99 % | 0.35 | 0.04 | 0.06 |
| avx2-120min-m4a-8threads | 283.4 | 283.1 | 67.3 | 2243.2 | 280.4 | 99 % | 0.33 | 0.03 | 0.06 |

Decoding and voice activity run on one producer thread that feeds the recognition workers; they overlap, so the columns do not add up. Recognition time is summed over the workers, so dividing it by the thread count gives the time each worker spent recognising, and that is 99 percent of the streaming time in every row: recognition is the bottleneck, and decoding and voice activity (a seventh of the wall time under AVX2) are hidden behind it. Speaker separation costs almost nothing after the stream ends (0.35 seconds for two hours); its work is inside the recognition time, which is 181 worker-seconds longer with speakers on, 45 seconds per worker. Cleaning and writing take under 0.1 seconds.

## 3. Accuracy by language

Word error rate in percent under the AVX2-only mask with the exact kernels, the acceptance configuration. The clips are in `2026-11-10-accept-avx2-clips.json` and the 10-minute files in `2026-11-10-accept-avx2-long.json`; the 10-minute set has no English meeting recording, so its English meetings row is empty.

Clips:

| Language | Reference words | S / D / I (raw) | WER raw % | WER cleaned % |
|---|---|---|---|---|
| English read | 736 | 12 / 3 / 1 | 2.17 | 2.45 |
| English meetings (AMI) | 879 | 27 / 181 / 3 | 24.00 | 25.94 |
| English combined | 1615 | 39 / 184 / 4 | 14.06 | 15.23 |
| French | 1285 | 32 / 1 / 4 | 2.88 | 2.96 |
| German | 1039 | 60 / 7 / 13 | 7.70 | 8.08 |

10-minute files:

| Language | Reference words | S / D / I (raw) | WER raw % | WER cleaned % |
|---|---|---|---|---|
| English read | 1473 | 12 / 3 / 2 | 1.15 | 1.15 |
| English meetings (AMI) | 0 | 0 / 0 / 0 | 0.00 | 0.00 |
| English combined | 1473 | 12 / 3 / 2 | 1.15 | 1.15 |
| French | 1432 | 22 / 10 / 4 | 2.51 | 2.58 |
| German | 1256 | 90 / 8 / 12 | 8.76 | 8.84 |

Clips and 10-minute files together:

| Language | Reference words | S / D / I (raw) | WER raw % | WER cleaned % |
|---|---|---|---|---|
| English read | 2209 | 24 / 6 / 3 | 1.49 | 1.58 |
| English meetings (AMI) | 879 | 27 / 181 / 3 | 24.00 | 25.94 |
| English combined | 3088 | 51 / 187 / 6 | 7.90 | 8.52 |
| French | 2717 | 54 / 11 / 8 | 2.69 | 2.76 |
| German | 2295 | 150 / 15 / 25 | 8.28 | 8.50 |

Read English and French are in the low single digits. German is higher; in the spike (chapter 6) every candidate read `de-single-02` and `de-multi-01` worst, and the reference spells numbers and compounds in ways a transcript need not. English meetings are far behind read speech, for the reason chapter 6 gives: the AMI excerpts hold overlapped speech, back-channels and quiet speakers, and almost all their error is deleted words (181 of 211).

### 3.1 Across masks

The same files under the three masks, from `2026-11-10-accept-avx2-clips.json` with `2026-11-10-accept-avx2-long.json`, `2026-11-10-accept-avx2vnni-long.json` and `2026-11-10-accept-unmasked-long.json` (the last two hold the clips and the 10-minute files together). Raw and cleaned, in percent:

| Language | AVX2 only | AVX2 + AVX-VNNI | Unmasked |
|---|---|---|---|
| English read | 1.49 / 1.58 | 1.49 / 1.58 | 1.49 / 1.58 |
| English meetings (AMI) | 24.00 / 25.94 | 24.00 / 25.94 | 24.12 / 26.05 |
| English combined | 7.90 / 8.52 | 7.90 / 8.52 | 7.93 / 8.55 |
| French | 2.69 / 2.76 | 2.69 / 2.76 | 2.65 / 2.72 |
| German | 8.28 / 8.50 | 8.28 / 8.50 | 7.80 / 8.02 |

The exact kernels bring the AVX2-only result to the VNNI result: the two are identical to the second decimal. The unmasked run differs from the VNNI one by under 0.5 point in each language, German the most (8.28 against 7.80). A quantised model is sensitive to the order in which another kernel accumulates, and AMX and AVX-512 VNNI are such kernels.

## 4. Cleaning

Cleaning removes fillers, stutters, false starts and bare acknowledgements, and keeps the rest. The gate is a content-word retention of at least 0.99 per language, and no word at all from noise. Under the AVX2-only mask (`2026-11-10-accept-avx2-clips.json` and `2026-11-10-accept-avx2-long.json`; noise from `2026-11-10-accept-avx2-noise.json`):

| Language | Content words kept | Of | Retention | Words read | Words removed | Share removed % |
|---|---|---|---|---|---|---|
| English | 1319 | 1319 | 1.0000 | 2842 | 18 | 0.63 |
| French | 1272 | 1272 | 1.0000 | 2519 | 2 | 0.08 |
| German | 1188 | 1190 | 0.9983 | 2295 | 9 | 0.39 |
| All | 3779 | 3781 | 0.9995 | 7656 | 29 | 0.38 |

Retention meets the gate in every language and overall. Of 3,781 content words 2 are lost, both German; English and French lose none. The share removed is the share of recognised words cleaning took out; it is highest in English, whose meeting excerpts hold the most fillers (2.4 percent of the AMI words). Cleaning raises the word error rate slightly (section 3, raw against cleaned), because the reference keeps the fillers and back-channels that cleaning removes.

Noise-only inputs, 60 seconds each, English cleaner; the gate is 0 words:

| File | Seconds | Words produced | Outcome |
|---|---|---|---|
| noise-office-60s | 60 | 0 | NoSpeech |
| noise-pink-60s | 60 | 0 | NoSpeech |
| noise-white-60s | 60 | 0 | NoSpeech |
| silence-60s | 60 | 0 | NoSpeech |

Total 0 words over 4 files, gate 0.

The English 10-minute file as AAC and as a video container gives the same 1,457 words as the WAV (`2026-11-10-accept-avx2-noise.json`, `2026-11-10-accept-avx2-long.json`):

| File | Wall s | Times real time | Words |
|---|---|---|---|
| en-10min-m4a | 24.4 | 24.8 | 1457 |
| en-10min-mp4 | 24.9 | 24.2 | 1457 |

## 5. Speakers

Scores are those of `2026-11-10-accept-avx2-clips.json` and `2026-11-10-accept-avx2-long.json`, without a speaker-count hint. The constructed files (read speech, speakers taking turns) are the easy case; the AMI clips are real meetings with overlap. The error rate excludes or includes overlapped reference speech, the confusion share is the part of the error that is speech given to the wrong speaker, and attribution accuracy is the share of single-speaker reference time given to the right speaker. The last column gives the estimated against the reference speaker count of each file.

| Group | Files | Error rate, overlap excluded | Error rate, overlap included | Confusion share | Attribution accuracy | Count exact | Count error (mean absolute) | Estimated / reference per file |
|---|---|---|---|---|---|---|---|---|
| en-read | 1 | 5.2 % | 5.2 % | 0.0 % | 88.1 % | 1 of 1 | 0.00 | 3/3 |
| en-ami | 3 | 12.4 % | 24.8 % | 2.1 % | 83.4 % | 0 of 3 | 1.33 | 6/4, 3/4, 3/4 |
| fr | 3 | 12.7 % | 12.7 % | 0.0 % | 84.8 % | 3 of 3 | 0.00 | 2/2, 3/3, 4/4 |
| de | 3 | 11.1 % | 11.1 % | 0.8 % | 86.7 % | 2 of 3 | 0.33 | 2/2, 3/3, 5/4 |

| Group | Files | Error rate, overlap excluded | Error rate, overlap included | Confusion share | Attribution accuracy | Count exact | Count error (mean absolute) | Estimated / reference per file |
|---|---|---|---|---|---|---|---|---|
| en-read | 1 | 7.6 % | 7.6 % | 0.5 % | 88.4 % | 0 of 1 | 1.00 | 7/6 |
| fr | 1 | 15.6 % | 15.6 % | 0.7 % | 81.7 % | 0 of 1 | 2.00 | 6/4 |
| de | 1 | 6.4 % | 6.4 % | 0.0 % | 91.3 % | 1 of 1 | 0.00 | 5/5 |

The speaker count is estimated, and it is the weak point: it is exact on 7 of 13 files and off by one or two on the others (the AMI clips, one German clip, the English and the French 10-minute files). The error rates are lower than the count errors suggest, because a spurious speaker takes a few seconds. The two-hour files have no reference speakers, so no accuracy is scored for them; the looped English recording was assigned 7 speakers where its recipe has 6 (`2026-11-10-accept-avx2-120min-m4a.json`).

Result files named `bench/results/*speakers-tuned*.json` supersede this chapter when present.

## 6. The recogniser choice

Decision 0002 chose Granite Speech 5.0 470M TurboCTC from five candidates measured in a Python spike with identical chunking (chunks of at most 28 seconds cut at the lowest-energy frame) and scoring, on 16 clips and three 10-minute files (3,383 seconds of audio). The spike is not the engine: its chunking, level handling and decoding are its own, and its numbers are not comparable digit for digit with chapter 3. They are the evidence for the choice and the reference of the quality gate. Source: `2026-11-10-phase1-candidates.json`, `wer_per_group`, which pools clips and 10-minute files as the engine rows above do.

| Candidate | English read | English meetings | English combined | French | German |
|---|---|---|---|---|---|
| A Parakeet TDT 0.6B v3 int8 | 12.63 | 39.48 | 20.27 | 13.65 | 13.64 |
| B Parakeet TDT 0.6B v3 fp32 | 4.07 | 18.20 | 8.10 | 6.55 | 10.54 |
| C Granite Speech 5.0 470M TurboCTC int8 | 1.58 | 36.63 | 11.56 | 3.79 | 8.45 |
| D Parakeet Ultra int8 (Masterx re-export) | 2.54 | 24.00 | 8.65 | 5.67 | 11.59 |
| E Whisper large-v3-turbo int8 (sherpa-onnx, language forced) | 2.35 | 28.33 | 9.75 | 5.85 | 9.24 |
| best of the five | 1.58 | 18.20 | 8.10 | 3.79 | 8.45 |

Candidate A drops whole spans in its int8 encoder, so it is not a fair picture of Parakeet v3, and Whisper (E) runs at about 2 to 5 times real time, so it is a reference for accuracy only. Recognition speed in the spike, 4 workers sharing one model, 10-minute English file, in times real time: Granite 107 to 114 on the build machine as it is, Parakeet Ultra 24, Parakeet v3 fp32 about 20, Parakeet v3 int8 about 26 (`speed_runs` in the same file). Under the AVX2 mask recognition alone was 34 times real time for Granite against 17.6 for Parakeet Ultra and 11.6 for Parakeet v3 fp32 (decision 0002).

### 6.1 The quality gate

Each language of the shipped engine must be within one point of the best candidate measured. The engine is read under the AVX2-only mask, raw, over the same material as the spike (clips and 10-minute files); the difference is the engine's rate minus the best candidate's, in percentage points, and a negative number means the engine is better:

| Language | Engine errors / words | Engine % | Best candidate | Its errors / words | Its % | Engine minus best (points) | Within one point |
|---|---|---|---|---|---|---|---|
| English read | 33 / 2209 | 1.49 | C | 35 / 2209 | 1.58 | -0.09 | yes |
| English meetings (AMI) | 211 / 879 | 24.00 | B | 160 / 879 | 18.20 | 5.80 | no |
| English combined | 244 / 3088 | 7.90 | B | 250 / 3088 | 8.10 | -0.19 | yes |
| French | 73 / 2717 | 2.69 | C | 103 / 2717 | 3.79 | -1.10 | yes |
| German | 190 / 2295 | 8.28 | C | 194 / 2295 | 8.45 | -0.17 | yes |

Every language meets the gate except English meetings. English counted as one language is 0.19 point better than the best candidate (7.90 against 8.10, candidate B, arithmetic above). The engine's input level normalisation brings Granite's meeting error from the spike's 36.63 percent to 24.00, and its read English is better than any candidate's. French is 1.10 points better than the best candidate and German 0.17.

### 6.2 The recorded deviation on meeting audio

On the three AMI excerpts the engine misses the gate by 5.80 points: 24.00 percent against 18.20 for Parakeet v3 fp32, and it equals Parakeet Ultra int8 (24.00). The deviation is accepted and recorded in decision 0002: under the AVX2 mask Granite recognises 1.9 to 2.9 times as fast as the next candidates, and that margin pays for voice activity detection, speaker separation and a laptop slower than the build machine. The evidence is three excerpts and 879 reference words, so the figure is noisy.

## 7. Exact 8-bit kernels

The recogniser is dynamically quantised to 8 bits. ONNX Runtime's fast 8-bit kernels on x64 without VNNI accumulate in 16 bits, which can saturate, and the German error shows it. The engine opens its sessions with the exact kernels exactly when the processor has no usable VNNI (decision 0010; `MAT_ORT_QUANT_PRECISION` overrides). The 10-minute files under the AVX2-only mask, from `2026-11-10-accept-avx2-fastkernels-long.json` (fast) and `2026-11-10-accept-avx2-long.json` (exact), raw word error rate in percent and speed in times real time over the three files:

| Kernels | English | French | German | Times real time (group) |
|---|---|---|---|---|
| fast | 1.36 | 2.79 | 12.42 | 30.4 |
| exact (shipped) | 1.15 | 2.51 | 8.76 | 24.6 |

On a VNNI processor the fast kernels are exact, and the 10-minute files score as the exact kernels do on AVX2 at 62 to 67 times real time per group (`2026-11-10-accept-avx2vnni-long.json`, `2026-11-10-accept-unmasked-long.json`, group rows; decision 0010 gives the figures). The exact kernels cost about a fifth of the speed on AVX2 and recover the German error from 12.42 to 8.76. The two-hour acceptance run uses them and stays at 25 times real time.

## 8. Reproducing the tables

Prerequisites: the .NET 10 SDK, Node 22 or later, `gcc`, and ffmpeg 6 or later on the PATH (for the test set).

```
node build/fetch-models.mjs --into models                       # the shipped models, verified against build/models.lock
node bench/testset/fetch-testset.mjs                            # the clips, the 10-minute files, the 30-minute file, the containers, the noise
node bench/testset/fetch-testset.mjs --long 120 --long-lang en  # en-120min.wav, the two-hour file
ffmpeg -i bench/data/long/en-120min.wav -map_metadata -1 -c:a aac -b:a 64k bench/data/long/en-120min.m4a
(cd bench/tools/cpuidmask && gcc -O2 -shared -fPIC -o libcpuidmask.so cpuidmask.c -ldl)
dotnet build src/MeetingAudioTranscriptor.Cli -c Release -warnaserror
```

The mask is applied by putting `CPUIDMASK=avx2 LD_PRELOAD=$PWD/bench/tools/cpuidmask/libcpuidmask.so` in front of the command; `MASK` below stands for it. `cli` is `src/MeetingAudioTranscriptor.Cli/bin/Release/net10.0/meeting-audio-transcriptor-cli`. Each invocation writes `bench/results/<date>-<label>.json`, so the label names the file. The first file of each two-hour run is the clip `en-single-01`, which warms the process and is reported in its own group.

| Result file label | Command |
|---|---|
| `accept-avx2-clips` | `MASK cli benchmark --set clips --threads 4 --label accept-avx2-clips` |
| `accept-avx2-long` | `MASK cli benchmark --set long --threads 4 --label accept-avx2-long` |
| `accept-avx2-noise` | `MASK cli benchmark --set containers,noise --threads 4 --label accept-avx2-noise` |
| `accept-avx2-30min` | `MASK cli benchmark --set en-single-01 --long-file bench/data/long/en-30min.wav --language en --threads 4 --label accept-avx2-30min` |
| `accept-avx2-120min-m4a` | `MASK cli benchmark --set en-single-01 --long-file bench/data/long/en-120min.m4a --language en --threads 4 --label accept-avx2-120min-m4a` |
| `accept-avx2-120min-wav-nospk` | the same with `en-120min.wav` and `--no-speakers` |
| `accept-avx2-120min-m4a-8threads` | the `accept-avx2-120min-m4a` command with `--threads 8` |
| `accept-avx2-fastkernels-long` | the `accept-avx2-long` command with `MAT_ORT_QUANT_PRECISION=0` set |
| `accept-avx2vnni-long`, `accept-avx2vnni-120min-m4a` | `CPUIDMASK=avx2-vnni`: `--set clips,long`, and the `accept-avx2-120min-m4a` command |
| `accept-unmasked-long`, `accept-unmasked-120min-m4a` | the same two commands without `MASK` |

Then `node bench/tools/summarise-results.mjs` prints every table of this file from the result files, and `node bench/tools/summarise-results.mjs <folder> <date>` reads another folder or date.

**On your own laptop.** Close other programs and run, on any recording:

```
meeting-audio-transcriptor-cli benchmark --long-file <recording> --language en --threads 4 --label mine
```

No mask is needed: the laptop is the machine. Compare the printed speed with the first table. The result file records the processor, whether it reports VNNI, whether the exact kernels are on, the thread count and the peak memory. The Windows checklist (`docs/WINDOWS-VERIFICATION.md`) asks for this run.

## 9. Limits of the evidence

- The test set is small: 16 clips and three long files, about 8,100 reference words in all, so a word error rate has a margin of a few tenths of a point for read speech and several points for meetings.
- Real meeting speech is three AMI excerpts of 69 to about 100 seconds, 879 reference words. French and German have no real conversational material: they are read audiobooks, with multi-speaker files built by concatenating speakers' utterances, without fillers, overlap or room noise between turns. The cleaning and speaker figures for those languages say little about meetings in them.
- The 30-minute and two-hour files repeat the ten-minute recipe, so they measure speed and memory and say nothing about accuracy.
- The masked server processor is a stand-in for a laptop: it runs the processor's AVX2 code at 2.1 GHz with a server's memory system, and a laptop's thermal limits, background load and power settings are absent. The speed margin of more than two leaves room for that, but the laptop's own figure is the one to read, which is why chapter 8 gives the one-command check.
- 8 threads are not measurable on this machine: it has 4 hardware threads, so the 8-thread row is oversubscribed.
- Each configuration was run once (`settings.repeat` is 1), so the spread between runs of one configuration is not measured here; the spike saw 25 percent spread on a shared machine.
- The acceptance files record `applicationVersion` 1.0.0, which names a development build and not a release, and do not record the kernel precision; the first follows from the build, the second from the mask (chapter 1).
- The Windows build, Media Foundation decoding and the window are not exercised by any benchmark here.
