# Diarisation measurements

Speaker separation as built in `src/MeetingAudioTranscriptor.Engine/Diarisation/`: Kaldi
filterbank, WeSpeaker CAM++ LM embeddings (`wespeaker-campplus-lm`), agglomerative clustering on
cosine distance, a frame-voting timeline and word assignment. Every number here comes from the
suites in `tests/MeetingAudioTranscriptor.Pipeline.Tests/Diarisation/` (models and deep tiers) and
`tests/MeetingAudioTranscriptor.Engine.Tests/Diarisation/`.

## 1. Conditions

- Machine: the shared development VM (Intel Xeon at 2.1 GHz, AVX-512), every process pinned to one
  core (`nice -n 19 taskset -c 3`), Release build, ONNX Runtime 1.31.1 with one intra-op thread.
  Other sessions ran benchmarks on the same machine, so timings carry about +/-20 % noise.
- Cost is the calling thread's CPU time (`/proc/thread-self/schedstat`), which is the single-core
  cost: with one intra-op thread the model runs on the calling thread.
- Speech regions are the reference's own (the union of the RTTM segments), so the scores measure
  speaker separation alone, not voice activity detection. The audio is fed to the collector in
  60 s blocks, as the recognition workers will feed it.
- Scoring: DER with a 0.25 s collar, with overlapped reference speech excluded and included;
  attribution accuracy = fraction of the non-overlapped reference speech time (no collar) given to
  the correctly mapped speaker. Mapping is the optimal one-to-one assignment (Hungarian).
- Files: 7 synthetic multi-speaker clips (LibriSpeech/MLS read speech, 2 to 4 speakers,
  en/fr/de), 3 AMI excerpts (4 speakers, real meetings with overlaps and backchannels, one distant
  microphone), 3 ten-minute files (4 to 6 speakers, en/fr/de), plus `en-30min` for the two-stage check.

## 2. Chosen defaults

| Setting | Value | Why |
|---|---|---|
| Features | 80 Kaldi fbank bins, 25/10 ms, Hamming window, CMN per window | the WeSpeaker recipe; matches kaldi-native-fbank within 1e-3 and the Python reference embedding within 2e-3 |
| Window / hop | 3.0 s / 2.0 s | best accuracy for its cost (section 4) |
| Shortest region embedded | 0.4 s | shorter regions take the nearest speaker in time |
| Batch | 4 windows per model call | flat between 4 and 8 (section 5) |
| Linkage | average | complete linkage is clearly worse at every threshold |
| Threshold | cosine distance 0.5 | centre of the flat region 0.45 to 0.525 (section 4) |
| Minimum speaker | 4 s of window hops | 0 s over-counts badly, 8 s merges quiet AMI speakers |
| Refinement passes | 0 | two passes change attribution by at most 0.005 |
| Speaker cap | 10 | product requirement |
| Two-stage above | 2,000 windows (one 16 MB matrix) | exact for up to about 65 min of speech at a 2 s hop |
| Minimum turn / strong turn | 0.5 s / 1.0 s | product requirement / word smoothing |

## 3. Results with the defaults

Per file (from `DiarisationAccuracyTests.TheFullTableIsWritten`):

| File | Group | Speech s | Speakers | Est. no hint | DER excl. | DER incl. | Attribution | Est. hint | DER excl. hint | DER incl. hint | Attribution hint |
|---|---|---|---|---|---|---|---|---|---|---|---|
| en-multi-01 | Synthetic | 81.9 | 3 | 3 | 0.000 | 0.000 | 1.000 | 3 | 0.000 | 0.000 | 1.000 |
| fr-multi-01 | Synthetic | 85.5 | 2 | 2 | 0.000 | 0.000 | 1.000 | 2 | 0.000 | 0.000 | 1.000 |
| fr-multi-02 | Synthetic | 111.0 | 3 | 3 | 0.000 | 0.000 | 1.000 | 3 | 0.000 | 0.000 | 1.000 |
| fr-multi-03 | Synthetic | 111.1 | 4 | 4 | 0.000 | 0.000 | 1.000 | 4 | 0.000 | 0.000 | 1.000 |
| de-multi-01 | Synthetic | 85.6 | 2 | 2 | 0.000 | 0.000 | 1.000 | 2 | 0.000 | 0.000 | 1.000 |
| de-multi-02 | Synthetic | 98.9 | 3 | 3 | 0.000 | 0.000 | 1.000 | 3 | 0.000 | 0.000 | 1.000 |
| de-multi-03 | Synthetic | 94.8 | 4 | 4 | 0.000 | 0.000 | 1.000 | 4 | 0.000 | 0.000 | 1.000 |
| en-ami-01 | AMI | 64.0 | 4 | 5 | 0.180 | 0.253 | 0.836 | 4 | 0.038 | 0.144 | 0.952 |
| en-ami-02 | AMI | 64.8 | 4 | 3 | 0.012 | 0.128 | 0.970 | 4 | 0.069 | 0.174 | 0.917 |
| en-ami-sdm-01 | AMI (distant mic) | 93.1 | 4 | 3 | 0.025 | 0.131 | 0.942 | 4 | 0.025 | 0.131 | 0.945 |
| en-10min | Long | 548.8 | 6 | 6 | 0.000 | 0.000 | 1.000 | 6 | 0.000 | 0.000 | 1.000 |
| fr-10min | Long | 583.8 | 4 | 4 | 0.001 | 0.001 | 0.999 | 4 | 0.001 | 0.001 | 0.999 |
| de-10min | Long | 577.7 | 5 | 6 | 0.005 | 0.005 | 0.994 | 5 | 0.005 | 0.005 | 0.994 |

Per group (means):

| Group | Count exact, no hint | DER excl. / incl., no hint | Attribution, no hint | Count exact, hint | DER excl. / incl., hint | Attribution, hint |
|---|---|---|---|---|---|---|
| Synthetic (7) | 7 of 7 | 0.000 / 0.000 | 1.000 | 7 of 7 | 0.000 / 0.000 | 1.000 |
| AMI (3) | 0 of 3 (5, 3, 3 for 4) | 0.072 / 0.171 | 0.916 | 3 of 3 | 0.044 / 0.150 | 0.938 |
| Long 10 min (3) | 2 of 3 (one extra) | 0.002 / 0.002 | 0.998 | 3 of 3 | 0.002 / 0.002 | 0.998 |
| en-30min (1) | 6 of 6 | 0.000 | 1.000 | 6 of 6 | 0.000 | 1.000 |

Reading: on read speech the separation is perfect; on real meetings about 92 % (94 % with the
count) of single-speaker time is attributed to the right person. The AMI DER including overlap
(0.15 to 0.17) is dominated by overlapped speech, which this design gives one speaker only. The
speaker count without a hint is off by one on every AMI clip: one quiet participant merged (en-ami-02,
sdm) or one split (en-ami-01). The hint fixes the count, and its rule (fold the smallest surplus
cluster first, cut the dendrogram deeper only when short) keeps the long files at 0.998.

The tests assert floors with a margin under these numbers: synthetic clips exact count and
attribution >= 0.9 with and without the hint; long files exact count with the hint (within one
without) and attribution >= 0.95; AMI mean attribution >= 0.8 with and without the hint.

## 4. Tuning

### Window and hop (average linkage, minimum speaker 4 s, no refinement; attribution synthetic / AMI / long)

| Window / hop | CPU s per audio hour | Threshold 0.5 | Threshold 0.6 |
|---|---|---|---|
| 1.0 / 0.5 | 92.7 | 0.694 / 0.616 / 0.864 | 0.859 / 0.805 / 0.899 |
| 1.5 / 0.75 | 83.1 | 0.898 / 0.853 / 0.912 | 0.995 / 0.897 / 0.992 |
| 1.5 / 1.0 | 67.7 | 0.860 / 0.758 / 0.919 | 0.996 / 0.856 / 0.987 |
| 1.5 / 1.5 | 46.6 | 0.875 / 0.686 / 0.906 | 0.987 / 0.873 / 0.977 |
| 2.0 / 1.0 | 79.3 | 0.986 / 0.851 / 0.984 | 0.998 / 0.941 / 0.970 |
| 2.0 / 1.5 | 59.2 | 0.977 / 0.855 / 0.993 | 0.997 / 0.943 / 0.970 |
| 2.0 / 2.0 | 40.5 | 0.978 / 0.788 / 0.974 | 0.991 / 0.890 / 0.967 |
| 2.5 / 1.5 | 68.4 | 1.000 / 0.918 / 0.996 | 0.936 / 0.947 / 0.970 |
| 2.5 / 2.0 | 54.1 | 0.989 / 0.822 / 0.991 | 0.936 / 0.919 / 0.970 |
| 3.0 / 1.5 | 79.5 | 1.000 / 0.961 / 1.000 | 0.936 / 0.963 / 0.972 |
| **3.0 / 2.0** | **60.1** | **1.000 / 0.916 / 0.998** | 0.936 / 0.944 / 0.970 |
| 3.0 / 3.0 | 45.8 | 1.000 / 0.930 / 0.998 | 0.936 / 0.920 / 0.970 |
| 4.0 / 2.0 | 74.1 | 1.000 / 0.963 / 0.998 | 0.936 / 0.954 / 0.971 |

Longer windows give steadier voice prints, so they are both more accurate and, at the same hop,
barely dearer (the model's cost grows slower than the window). The best accuracy is 3.0 / 1.5
and 4.0 / 2.0 at about 75 to 80 s per hour; 3.0 / 2.0 keeps nearly all of it for 60 s per hour and
3.0 / 3.0 for 46 s per hour. The 1.5 s / 0.75 s windows of the reference recipe are both dearer and
worse here. The best threshold moves down as the window grows (0.6 for 1.5 s, 0.5 for 3 s),
because longer windows give closer embeddings of one voice.

### Threshold sensitivity at 3.0 / 2.0 (minimum speaker 4 s; count exact and attribution, synthetic / AMI / long)

| Threshold | Count exact (of 7 / 3 / 3) | Attribution |
|---|---|---|
| 0.400 | 5 / 2 / 1 | 0.950 / 0.780 / 0.933 |
| 0.425 | 5 / 0 / 1 | 0.957 / 0.844 / 0.969 |
| 0.450 | 7 / 3 / 2 | 1.000 / 0.912 / 0.998 |
| 0.475 | 7 / 0 / 2 | 1.000 / 0.916 / 0.998 |
| **0.500** | **7 / 0 / 2** | **1.000 / 0.916 / 0.998** |
| 0.525 | 7 / 1 / 1 | 1.000 / 0.905 / 0.996 |
| 0.550 | 7 / 1 / 2 | 1.000 / 0.905 / 0.970 |
| 0.575 | 6 / 1 / 2 | 0.936 / 0.944 / 0.970 |
| 0.600 | 6 / 1 / 2 | 0.936 / 0.944 / 0.970 |
| 0.650 | 6 / 2 / 2 | 0.936 / 0.956 / 0.970 |

The plateau runs from 0.45 to 0.525; above 0.55 two speakers of one synthetic clip
and of one long file merge, below 0.45 the AMI speakers split. With a minimum speaker of 0 s every
threshold up to 0.55 ends at the 10-speaker cap on the AMI and long files (attribution stays high
because the surplus clusters are tiny, but the transcript would name ten people); with 8 s, two AMI
clips drop to 2 speakers. The minimum is applied in seconds of window hops (2 windows at a 2 s hop).

### Linkage and refinement (1.5 / 0.75 windows, the first sweep)

Complete linkage peaks at attribution 0.973 / 0.909 / 0.942 (threshold 0.95) against average
linkage's 0.995 / 0.897 / 0.992 (0.6) and 0.995 / 0.935 / 0.964 (0.65): its maximum distance is
pulled up by one noisy window, so it splits speakers unless the threshold is so high that it then
merges others. Two refinement passes (each window moved to the nearest centroid) change no group
mean by more than 0.005 in either direction and are off by default.

## 5. Cost

| Item | Measured |
|---|---|
| CAM++ per 3 s window, batch 1 / 4 / 8 / 16 (ms, best of 6) | 33.8 / 30.9 / 31.3 / 33.5 |
| CAM++ per 1.5 s window, batch 1 / 4 / 8 / 16 (ms, two runs) | 24.6 / 18.2 / 16.8 / 22.1 and 22.7 / 23.4 / 20.1 / 20.0 |
| Features and embeddings, 13 files, 3.0 / 2.0 | **60.8 s per hour of audio**, 65.5 s per hour of speech (92.8 % speech) |
| Same on en-30min | 30.0 s for 30 min (60 s per hour) |
| Clustering and timeline, a 10-minute file | under 0.01 s |
| Clustering, 10,000 synthetic 512-d windows (two stages) | 1.7 s, 101 MB allocated in total, one 16 MB matrix at a time |
| State kept | 2 KB per window: about 3,600 windows and 7 MB for a 2-hour meeting |

**Two-hour meeting, single core:** 2 x 60.8 = about 122 core-seconds at this speech density
(93 %); a real meeting with 80 % speech is about 105 core-seconds, plus 1 to 2 s of clustering.
That is inside the 150 core-second budget and slightly above the 100 core-second ideal. On 4 cores
it is about 30 s of wall time, shared with recognition. The cheaper 3.0 / 3.0 setting costs about
46 s per hour (92 core-seconds for 2 hours at 93 % speech) with attribution 1.000 / 0.930 / 0.998;
it is the fallback if the target laptop measures slower than this VM.

### Two-stage clustering check

`en-30min` (817 windows) clustered directly and with the two-stage scheme forced by lowering the
direct limit to 1000, 500 and 250 windows: every variant gives 6 of 6 speakers, DER 0.000 and
attribution 1.000, with and without the hint, in under 0.07 s. The 10,000-window synthetic test
recovers its six clusters exactly through the two stages.

## 6. Recommendation

**On by default**, with 3.0 s windows every 2.0 s, average linkage and threshold 0.5. It costs
about 105 to 125 core-seconds for a two-hour meeting (within the 150 budget), attributes read
speech perfectly and about 92 to 94 % of single-speaker meeting time correctly, and the speaker-count
hint is worth offering: it fixed the count on every AMI clip and never made a file worse by more
than 0.06 of attribution (en-ami-02: 0.970 to 0.917). Re-measure the cost on the target laptop;
if it exceeds the budget, move the hop to 3.0 s.

The pyannote segmentation model (optional step) was not evaluated: the AMI attribution (0.916
without the hint, 0.938 with it) is above the 0.8 level that would justify it. Its value would be
in overlapped speech, which is where the remaining AMI DER lies.

## 7. Reproducing

```
MAT_MODELS_DIR=<models> MAT_BENCH_DATA=<bench/data> dotnet test --project tests/MeetingAudioTranscriptor.Pipeline.Tests -c Release --filter-trait "Tier=models"
MAT_MODELS_DIR=<models> MAT_BENCH_DATA=<bench/data> dotnet test --project tests/MeetingAudioTranscriptor.Pipeline.Tests -c Release --filter-trait "Tier=deep"
```

The tables are written beside the test binaries: `diarisation-table.md`,
`diarisation-sweep-clustering.txt`, `diarisation-sweep-window.txt`, `diarisation-two-stage.txt`
and `diarisation-batch.txt`. The window sweep takes about 17 minutes on one core.
