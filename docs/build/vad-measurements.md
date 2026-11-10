# VAD measurements

The cost and accuracy of the Silero VAD v6 driver (`SileroVadModel`, `SileroVadSession`) and the result of the VAD chunking stage on the benchmark audio. A working document: the figures come from one shared machine and are approximate.

## 1. Setup

- Machine: 4 vCPU Intel Xeon at 2.1 GHz (AVX-512), shared with a running benchmark. Every run was pinned to one core (`nice -n 19 taskset -c 3`).
- Runtime: ONNX Runtime 1.31.1 through `OrtTensorRunner`, one intra-op thread, so every call runs on the calling thread.
- Cost is process CPU time, the cheapest of three passes per file, because wall time on the shared machine varied by a factor of two between identical runs. It is the cost on one core.
- Agreement is measured against the one-lane, frame-by-frame answer, which is the reference's own way of running the graph.
- Reproduce: `MAT_MODELS_DIR=<models> MAT_BENCH_DATA=<bench/data> MAT_VAD_REPORT=<file> dotnet exec tests/MeetingAudioTranscriptor.Pipeline.Tests/bin/Debug/net10.0/MeetingAudioTranscriptor.Pipeline.Tests.dll --filter-trait "Tier=deep"` (the `VadMeasurementTests` class).

## 2. Batch layouts

Audio: 785.5 s in 3 files (long/en-10min.wav, clips/en/en-ami-01.wav, clips/fr/fr-multi-01.wav), digital-silence shortcut off.

| Lanes | Block s | Warm-up s | Calls | CPU s | x real time | ms per call | 2 h in CPU s | Max abs diff | Mean abs diff | Decisions differing at 0.5 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 128 | 4 | 24550 | 2.98 | 264 | 0.121 | 27.3 | 0.0000 | 0.00000 | 0.000 % |
| 4 | 32 | 1 | 6739 | 1.75 | 450 | 0.259 | 16.0 | 0.6042 | 0.00783 | 0.635 % |
| 8 | 32 | 1 | 3774 | 2.08 | 378 | 0.550 | 19.0 | 0.8694 | 0.01164 | 0.908 % |
| 16 | 32 | 1 | 2455 | 1.78 | 440 | 0.727 | 16.4 | 0.8576 | 0.01419 | 1.047 % |
| 32 | 32 | 0.5 | 1703 | 2.74 | 287 | 1.607 | 25.1 | 0.9477 | 0.02042 | 1.662 % |
| 4 | 64 | 4 | 7477 | 2.33 | 337 | 0.311 | 21.3 | 0.5520 | 0.00378 | 0.244 % |
| 4 | 96 | 4 | 6997 | 1.67 | 471 | 0.238 | 15.3 | 0.3553 | 0.00307 | 0.216 % |
| 8 | 128 | 2 | 3466 | 1.81 | 433 | 0.523 | 16.6 | 0.4091 | 0.00538 | 0.432 % |
| **8** | **128** | **4** | 3858 | 1.58 | **498** | 0.409 | **14.5** | 0.4818 | 0.00443 | **0.322 %** |
| 8 | 192 | 4 | 3798 | 1.65 | 477 | 0.433 | 15.1 | 0.4160 | 0.00351 | 0.240 % |
| 16 | 256 | 4 | 2140 | 2.09 | 377 | 0.975 | 19.1 | 0.5977 | 0.00527 | 0.375 % |

A warm-up sweep with two lanes in 64 s blocks (one lane boundary per block, wall time, so cost not comparable) decided 0.212 % of frames differently with a 1 s warm-up, 0.126 % with 4 s, 0.110 % with 8 s and 0.061 % with 16 s.

Reading:

- A call costs about 0.06 ms plus about 0.055 ms per lane (a linear fit to the ms-per-call column). The per-call overhead is half the cost of a one-lane call, so batching can at most halve the cost per frame; the graph's own work, about 0.055 ms per frame, puts two hours at about 12 s of one core at best.
- Silero's recurrent state has a long memory: a lane started from an empty state keeps deciding some frames differently for many seconds, so the disagreement falls only slowly with the warm-up and grows with the number of lane boundaries. Short blocks with many lanes are fast but disagree on about 1 % of frames.
- Few boundaries with a long warm-up keep the disagreement under 0.5 % at nearly the best speed. The default is 8 lanes, 128 s blocks, 4 s warm-up: 498 times real time, two hours in about 14.5 s of one core, 0.32 % of frames deciding differently. The cost is a 128 s lookahead (8 MB of samples held by the VAD and up to as much again by the chunker), which the pipeline's memory budget allows. 4 lanes in 96 s blocks (0.22 %, 471 times real time) is the alternative when less lookahead is wanted.
- One lane meets the 30 s target as well (27.3 s), with no approximation at all; it is the fallback if the disagreement ever matters.

## 3. Digital-silence shortcut

A run of frames whose every sample is below 1e-4 (exact zeros, as recorders pad files) is given probability 0 without a call, and the state is reset to empty, the state a stream starts with. The block is split around the run, and each side is laid out in lanes on its own.

Audio: the three files above plus a padded recording (en-ami-01 twice between 40 s, 130 s and 45 s of exact zeros, 398.3 s), 1183.9 s in all. Agreement against the same layout with the shortcut off.

| Lanes | Block s | Warm-up s | Shortest skipped run s | Frames skipped | Calls | CPU s | Decisions differing |
|---|---|---|---|---|---|---|---|
| 1 | 128 | 4 | off | 0 | 36998 | 4.61 | 0 of 36998 |
| 1 | 128 | 4 | 2 | 6718 | 30280 | 3.76 | 18 of 36998 |
| 1 | 128 | 4 | 1 | 7689 | 29309 | 3.79 | 90 of 36998 |
| 1 | 128 | 4 | 0.5 | 8415 | 28583 | 3.53 | 143 of 36998 |
| 8 | 128 | 4 | off | 0 | 5981 | 2.39 | 0 of 36998 |
| **8** | **128** | **4** | **2** | 6718 | 4898 | 2.06 | **40 of 36998** |
| 8 | 128 | 4 | 1 | 7689 | 9342 | 2.68 | 110 of 36998 |
| 8 | 128 | 4 | 0.5 | 8415 | 15303 | 2.97 | 130 of 36998 |

Reading: the shortcut changes a few decisions, because the state after a silence differs from the empty state the shortcut restarts from (0.05 % of frames with one lane at 2 s). With lanes, skipping the 0.3 to 1.5 s gaps of the constructed clips splits each block into utterance-length spans too short for several lanes, which costs more calls than it saves. The default is on with a 2 s minimum: 18 % fewer calls on this mix, 0.11 % of decisions changed, and a file of pure digital silence costs almost nothing (silence-60s.wav: 0.06 s for the whole stage).

## 4. Segmentation defaults and noise

The reference thresholds (0.5 and 0.35, minimum speech 0.25 s) kept 96.5 % of the audible reference speech of en-ami-01: the AMI excerpts hold short, quiet backchannels ("mm-hmm", "yeah", about -28 dBFS) with probabilities of 0.2 to 0.37. Variants measured with the default batch layout (coverage of audible reference speech; chunks from the noise files):

| Setting | en-ami-01 | en-ami-02 | en-ami-sdm-01 | en-multi-01 | fr-multi-03 | en-10min | office, pink, white noise |
|---|---|---|---|---|---|---|---|
| 0.5 / 0.35, 0.25 s | 96.46 % | 99.73 % | 100 % | 100 % | 100 % | 99.13 % | 0, 0, 0 |
| 0.5 / 0.35, 0.1 s | 96.74 % | 99.73 % | 100 % | 100 % | 100 % | 99.13 % | 0, 0, 0 |
| 0.4 / 0.25, 0.25 s | 97.03 % | 99.73 % | 100 % | 100 % | 100 % | 99.16 % | 0, 0, 0 |
| **0.4 / 0.25, 0.1 s** | **99.23 %** | 99.73 % | 100 % | 100 % | 100 % | 99.16 % | 0, 0, 0 |
| 0.3 / 0.15, 0.25 s | 99.43 % | 99.84 % | 100 % | 100 % | 100 % | 99.20 % | 0, 0, 0 |

The defaults are threshold 0.4, negative threshold 0.25, minimum speech 0.1 s, minimum silence 0.3 s, padding 0.12 s: a dropped word is lost while kept noise only costs recognition time, and no noise file yields a chunk. Chunking defaults: target 20 s, split silence 0.6 s, shortest cut silence 0.2 s, maximum from the recogniser (30 s in these tests).

## 5. Stage results with the defaults

Coverage counts the 32 ms frames inside a reference RTTM turn that are louder than -40 dBFS and lie inside a chunk. A turn of the constructed clips is a whole corpus utterance with the recording's own silence before and after the words, and an AMI turn joins words up to 0.5 s apart, so the whole-turn figure counts silence the recogniser does not need; it is reported beside.

| File | Length s | Chunks | Audible reference covered | Whole turns covered | Audio kept | Forced cuts |
|---|---|---|---|---|---|---|
| en-multi-01 | 92.2 | 15 | 100.00 % | 94.30 % | 83.4 % | 0 |
| en-ami-01 | 91.7 | 16 | 99.45 % | 89.70 % | 63.4 % | 0 |
| en-ami-02 | 69.4 | 5 | 99.78 % | 99.12 % | 96.8 % | 1 |
| en-ami-sdm-01 | 100.8 | 12 | 100.00 % | 97.07 % | 92.8 % | 0 |
| fr-multi-01 | 90.2 | 18 | 100.00 % | 85.42 % | 80.5 % | 0 |
| fr-multi-02 | 115.5 | 18 | 100.00 % | 89.55 % | 85.8 % | 0 |
| fr-multi-03 | 115.2 | 17 | 100.00 % | 91.86 % | 88.1 % | 0 |
| de-multi-01 | 90.7 | 9 | 98.55 % | 93.86 % | 88.5 % | 0 |
| de-multi-02 | 103.9 | 12 | 99.95 % | 91.57 % | 86.9 % | 0 |
| de-multi-03 | 100.3 | 12 | 100.00 % | 90.54 % | 85.5 % | 0 |
| silence-60s, noise-white-60s, noise-pink-60s, noise-office-60s | 60 each | 0 | | | | |

en-10min.wav (603.7 s): 72 chunks, mean 7.17 s, longest 20.75 s, shortest 0.91 s; 85.5 % of the audio kept in chunks, 84.7 % inside padded speech regions; 99.17 % of the audible reference covered (94.11 % of whole turns); no forced cut. The whole stage (VAD, segmentation, chunking and the copies into chunks) took 1.37 to 1.58 s of CPU in two runs, 381 to 442 times real time, about 16 to 19 s of one core for two hours. The chunks are short because the clip's 0.3 to 1.5 s gaps between utterances end a chunk whenever they reach the 0.6 s split silence. The forced cut in en-ami-02 is a stretch of AMI speech longer than 30 s without a 0.2 s pause.
