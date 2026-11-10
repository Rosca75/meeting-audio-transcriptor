# 0005. Speaker separation: windowed embeddings and clustering, on by default

## Context

Segments carry anonymous speaker labels. The whole run must stay inside the
10-minute budget for a 2-hour meeting, so speaker separation may cost about
150 core-seconds at most for it to be on by default.

## Options

1. Speech regions cut into sliding windows, one speaker embedding per window
   (WeSpeaker CAM++ on Kaldi filterbank features), agglomerative clustering in
   C#, a per-frame vote into a speaker timeline, words assigned by time.
2. The pyannote pipeline: a segmentation model over 10-second windows for
   speaker changes and overlap, then embeddings and clustering.
3. Off by default.

## Evidence

`docs/build/diarisation-measurements.md`, on the test set's reference speech
regions, collar 0.25 s, option 1 with 3.0 s windows, 2.0 s hop, average
linkage, cosine-distance threshold 0.5:

| Group | Speaker count exact | Error rate, overlap excluded | Attribution accuracy |
|---|---|---|---|
| Constructed multi-speaker clips (7) | 7 of 7 | 0.000 | 1.000 |
| AMI meeting clips (3) | 0 of 3 (5, 3, 3 for 4); 3 of 3 with the hint | 0.072; 0.044 with the hint | 0.916; 0.938 with the hint |
| 10-minute files (3) | 2 of 3 (one extra); 3 of 3 with the hint | 0.002 | 0.998 |

Cost: about 61 core-seconds per hour of audio (31 ms per window on one core),
so about 105 to 122 core-seconds for a 2-hour meeting; clustering 10,000
windows takes 1.7 s. Results are stable for thresholds from 0.45 to 0.525.
Longer windows were both cheaper and better than 1.5 s windows; complete
linkage was clearly worse.

Option 2 was not built: attribution on the meeting clips is above the 0.8 bar
set for it, and the segmentation model would add its own cost.

## Decision

Option 1, on by default, with a switch to turn it off and an optional
speaker-count hint. With the hint the clusterer starts from the threshold cut
and folds the smallest surplus cluster until the count matches.

## Consequences

- Overlapped speech is attributed to one speaker; the recogniser transcribes
  one voice at a time in any case.
- Without the hint the speaker count can be off by one on real meetings; the
  hint fixes it, and the interface offers it.
- On a slower laptop a 3.0 s hop cuts the cost by a quarter at similar
  accuracy; the option exists in `DiarisationOptions`.
- The embedding model was trained on VoxCeleb; its licence note is in `NOTICE`.
