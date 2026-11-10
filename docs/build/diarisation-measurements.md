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
| Minimum speaker | 4 s of speech, a window counting one hop or its own shorter length | 0 s over-counts badly, 8 s merges quiet AMI speakers; short windows counted as hops pass for speakers (section 7) |
| Speaker floor | the larger of 4 s and 1.5 % of the speaking time, on the finished transcript | folds the stray speakers of the full pipeline (section 7) |
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

## 7. Through the full pipeline

The sections above read the reference's own speech regions. A user's run reads the voice-activity
detector's regions and the recogniser's words, and there the threshold cut named extra speakers.
Measured with the benchmark command (`--threads 4`, the speaker metrics of each result file, whose
attribution scores transcript segments against the reference and is therefore lower than the
window-level figures above) and with `PipelineDiarisationTuningTests` (deep tier), which keeps one
run's embeddings and words and re-clusters them under each setting.

### Where the extra speakers came from

Every extra speaker was a handful of short speech regions. Voice activity closes a region at a
pause of 0.3 s, so read speech arrives as many regions of 0.4 to 2 s ("En cet endroit,", "Thus",
"What is slang?"), each embedded as one window of its own length, and a voice print of under a
second is unsteady enough to form its own cluster. The minimum cluster size counted every window
as one 2 s hop, so two such windows (1.5 s of speech) passed the 4 s minimum. The other candidates
were checked and ruled out: no extra speaker sat on a region straddling two reference speakers or
on a chunk boundary of the constructed files; the two-stage clustering gives the same speakers as
direct clustering of all 3,109 windows of the two-hour file; and renumbering after word assignment
only removes speakers. The stray clusters grow with the recording, because the same short
fragments recur: about 3.5 s in `en-10min`, 5.9 s and 4.8 s in `en-30min`, 42 s in `en-120min`
(0.4 to 0.7 % of the speaking time). The two loops repeat the six speakers of `en-10min`, so their
true count is 6.

Speaking time per estimated speaker, threshold cut alone (seconds of timeline turns; the stray
speakers in bold):

| File | True | Estimated | Speaking time of each speaker |
|---|---|---|---|
| de-multi-03 | 4 | 5 | 15.9, 15.8, 23.5, 26.3, **2.9** |
| en-10min | 6 | 7 | 57.0, 165.2, 21.8, **3.5**, 75.0, 160.5, 28.1 |
| fr-10min | 4 | 6 | 173.6, 157.8, 116.8, **2.4**, **2.2**, 40.6 |
| en-30min | 6 | 8 | 171.2, 495.7, 65.7, **5.9**, **4.8**, 225.1, 482.7, 75.3 |
| en-120min | 6 | 7 | 684.8, 1948.9, 262.3, **42.4**, 893.3, 1930.3, 328.9 |

The quietest real speakers: 9.8 s in `fr-multi-02` (one reader of one sentence in a 2-minute clip),
about 4 s in the AMI clips, 25 s (4 to 5 % of the speaking time) in the long files.

### The two rules

1. **A window counts for its own length in the minimum cluster size**, one hop at most. This
   alone makes `de-multi-03`, `en-10min` and `fr-10min` exact. With a speaker-count hint every
   window still counts one hop, because the hint path's folding order attributes the AMI clips
   better that way (0.952 against 0.850 on `en-ami-01` with the reference regions).
2. **A speaker floor on the finished transcript**: after the words are assigned, a speaker whose
   speaking time is below the larger of `SpeakerFloorSeconds` and `SpeakerFloorShare` of the
   total is folded into the speaker with the nearest mean voice print, the timeline is rebuilt and
   the words assigned again, until none is below it. No hint applies it.

Floor sweep after rule 1 (speaker count exact of 15 files; attribution mean of the constructed
clips / AMI / long files, from word runs as turns):

| Floor s | Share | Synthetic exact (7) | AMI estimated (4 each) | Long exact (5) | Attribution S / A / L |
|---|---|---|---|---|---|
| 0 | 0 | 7 | 5, 3, 3 | 3 | 0.909 / 0.845 / 0.900 |
| 0 | 1.5 % | 7 | 5, 3, 3 | 5 | 0.909 / 0.845 / 0.902 |
| 3 | 1.5 % | 7 | 5, 3, 3 | 5 | 0.909 / 0.845 / 0.902 |
| **4** | **1.5 %** | **7** | **4, 3, 3** | **5** | **0.909 / 0.845 / 0.902** |
| 5 | 1.5 % | 7 | 4, 2, 3 | 5 | 0.909 / 0.829 / 0.902 |
| 8 | 1.5 % | 7 | 2, 2, 3 | 5 | 0.909 / 0.809 / 0.902 |
| 10 | 1.5 % | 6 (fr-multi-02 merged) | 2, 2, 3 | 5 | 0.897 / 0.809 / 0.902 |
| 15 | 1.5 % | 6 | 1, 2, 3 | 5 | 0.897 / 0.757 / 0.902 |
| 4 | 0 | 7 | 4, 3, 3 | 3 | 0.909 / 0.845 / 0.900 |
| 4 | 0.5 % | 7 | 4, 3, 3 | 4 (en-120min 7) | 0.909 / 0.845 / 0.901 |
| 4 | 1 % to 3 % | 7 | 4, 3, 3 | 5 | 0.909 / 0.845 / 0.902 |

An absolute floor of 8 s or more merges real speakers on the clips, and no absolute floor alone
catches the 42 s stray of the two-hour file; a share alone does nothing on a clip. Every share from
1 % to 3 % gives the same result; 1.5 % keeps about twice the largest stray share (0.7 %) and under
half the quietest real speaker's (4 %). The cost is stated in decision 0005: a participant who
speaks less than 1.5 % of a meeting is merged into the nearest voice. With the hint every file is
exact under every floor, and the `en-ami-02` AMI speaker kept at 4 s sits exactly on the floor.

### Before and after, through the benchmark command

`bench/results/2026-11-10-speakers-tuned-clips.json` and `2026-11-10-speakers-tuned-long.json`
(both with `--speakers-hint-from-reference`); the threshold-cut figures come from the same command
on the same machine before the two rules. Attribution is the benchmark's (segments as turns).

| File | True | Estimated, threshold cut | Estimated, with the rules | Estimated, hint | Attribution, threshold cut | Attribution, with the rules |
|---|---|---|---|---|---|---|
| en-multi-01 | 3 | 3 | 3 | 3 | 0.889 | 0.889 |
| fr-multi-01 | 2 | 2 | 2 | 2 | 0.799 | 0.799 |
| fr-multi-02 | 3 | 3 | 3 | 3 | 0.859 | 0.859 |
| fr-multi-03 | 4 | 4 | 4 | 4 | 0.876 | 0.876 |
| de-multi-01 | 2 | 2 | 2 | 2 | 0.907 | 0.907 |
| de-multi-02 | 3 | 3 | 3 | 3 | 0.864 | 0.864 |
| de-multi-03 | 4 | 5 | 4 | 4 | 0.834 | 0.857 |
| en-ami-01 | 4 | 6 | 4 | 4 | 0.683 | 0.683 |
| en-ami-02 | 4 | 3 | 3 | 4 | 0.889 | 0.889 |
| en-ami-sdm-01 | 4 | 3 | 3 | 4 | 0.885 | 0.885 |
| en-10min | 6 | 7 | 6 | 6 | 0.883 | 0.889 |
| fr-10min | 4 | 6 | 4 | 4 | 0.817 | 0.824 |
| de-10min | 5 | 5 | 5 | 5 | 0.911 | 0.911 |
| en-30min | 6 | 8 | 6 | 6 | 0.882 | 0.888 |
| en-120min | 6 | 7 | 6 | 6 | 0.882 | 0.888 |

Per group (mean attribution of the files): constructed clips 0.861 to 0.864, AMI 0.819 to 0.819,
ten-minute files 0.870 to 0.875, 30 and 120 minutes 0.882 to 0.888. Speaker count exact without the
hint: 6 of 7 to 7 of 7 clips, 0 of 3 to 1 of 3 AMI clips (all within one), 1 of 5 to 5 of 5 long
files. The speaker stage (clustering, timeline, floor) takes 0.33 s of the two-hour file's 92 s
wall time, against 0.26 s for the threshold cut alone; no model runs again.

The models tier asserts the counts through the real pipeline (`PipelineDiarisationTests`): exact on
the three ten-minute files with and without the hint and on the seven constructed clips, the AMI
clips within one, and the hint overriding a floor.

## 8. Reproducing

```
MAT_MODELS_DIR=<models> MAT_BENCH_DATA=<bench/data> dotnet test --project tests/MeetingAudioTranscriptor.Pipeline.Tests -c Release --filter-trait "Tier=models"
MAT_MODELS_DIR=<models> MAT_BENCH_DATA=<bench/data> dotnet test --project tests/MeetingAudioTranscriptor.Pipeline.Tests -c Release --filter-trait "Tier=deep"
```

The tables are written beside the test binaries: `diarisation-table.md`,
`diarisation-sweep-clustering.txt`, `diarisation-sweep-window.txt`, `diarisation-two-stage.txt`
and `diarisation-batch.txt`; the full-pipeline measurements write `diarisation-pipeline-sizes.txt`
and `diarisation-pipeline-floor.txt` (about 4 minutes on 4 cores, the two-hour file included). The
window sweep takes about 17 minutes on one core.
