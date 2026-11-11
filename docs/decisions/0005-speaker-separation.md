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

Through the full pipeline (the voice-activity detector's regions, the
recogniser's words), the threshold cut alone names extra speakers of 2 to 6 s on
the ten-minute and 30-minute files and of 42 s on the two-hour file. Voice
activity cuts read speech into many regions under 2 s, each embedded as one short
window with an unsteady voice print, and these windows form clusters of their
own. Counting a window as its own length rather than a full hop in the minimum
cluster size, and a floor on each final speaker's speaking time, make the count
exact on the constructed clips and on the 10, 30 and 120-minute files, with no
group's attribution lower (`docs/build/diarisation-measurements.md`, chapter
"Through the full pipeline"):

| Group | Count exact, threshold cut alone | Count exact, with both rules |
|---|---|---|
| Constructed multi-speaker clips (7) | 6 of 7 | 7 of 7 |
| AMI meeting clips (3) | 0 of 3 (6, 3, 3 for 4) | 1 of 3 (4, 3, 3 for 4) |
| 10, 30 and 120-minute files (5) | 1 of 5 | 5 of 5 |

The benchmark command over the shipped service reads the same: without the
hint the count is exact on 13 of the 15 files with reference speakers (all
seven constructed clips and all five long files; two AMI clips are one short),
and with the hint on all 15 (`bench/results/2026-11-10-speakers-tuned-clips.json`
and `-long.json`). The final build under the AVX2 mask finds 6 of 6 speakers in
the two-hour file (`bench/results/2026-11-10-final-avx2-120min.json`). The test
set is the one the rules were tuned on (`docs/BENCHMARKS.md`, chapter 5).

An absolute floor of 8 to 15 s merges a real speaker of a constructed clip who
reads one sentence (10 s) and quiet AMI participants; a share alone does nothing
on a short clip, and an absolute floor alone misses the two-hour file's 42 s.

## Decision

Option 1, on by default, with a switch to turn it off and an optional
speaker-count hint. With the hint the clusterer starts from the threshold cut
and folds the smallest surplus cluster until the count matches.

The clusterer folds a cluster holding less than 4 s of speech into its nearest
large one; without the hint a window counts for one hop or for its own length
when shorter, with the hint for one hop. Without the hint, after the words are assigned, a speaker whose speaking time is
below the speaker floor, the larger of 4 s and 1.5 % of the recording's total
speaking time, is folded into the speaker whose mean voice print is nearest,
preferring one above the floor, and the timeline and the words are rebuilt; this
repeats until every speaker is above the floor or one remains. A speaker is
never dropped. Both values are options (`SpeakerFloorSeconds`,
`SpeakerFloorShare` in `DiarisationOptions`), and the hint overrides the floor:
with a hint of K the transcript names K speakers whenever the clusterer finds K.

## Consequences

- Overlapped speech is attributed to one speaker; the recogniser transcribes
  one voice at a time in any case.
- Without the hint the speaker count can be off by one on real meetings; the
  hint fixes it, and the interface offers it.
- A real participant who speaks less than the floor is merged into the voice
  nearest theirs: about 90 s of a two-hour meeting, 7 s of a ten-minute one,
  4 s of a short clip. Their words stay in the transcript under another
  speaker's label. A user who knows the number of participants gives the hint,
  which keeps every one of them.
- The floor costs no model run: it rebuilds the timeline from the windows
  already embedded, under 0.1 s on a two-hour meeting.
- On a slower laptop a 3.0 s hop cuts the cost by a quarter at similar
  accuracy; the option exists in `DiarisationOptions`.
- At most 10 speakers are told apart (`MaxSpeakers` in `DiarisationOptions`, the
  same bound as the hint): a meeting with more voices has the nearest clusters
  merged until 10 remain.
- The embedding model was trained on VoxCeleb; its licence note is in `NOTICE`.
