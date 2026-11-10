# 0002. Recogniser: Granite Speech 5.0 470M TurboCTC, int8

## Context

The engine must transcribe English, French and German on a CPU-only laptop
(4 cores, AVX2) at 12 times real time or better end to end, with the best
accuracy that speed allows. The starting hypothesis was NVIDIA Parakeet TDT
0.6B v3 with int8 weights.

## Options

| | Candidate | Licence | ONNX source |
|---|---|---|---|
| A | Parakeet TDT 0.6B v3, int8 | CC-BY-4.0 | `istupakov/parakeet-tdt-0.6b-v3-onnx` |
| B | Parakeet TDT 0.6B v3, fp32 | CC-BY-4.0 | same |
| C | IBM Granite Speech 5.0 470M TurboCTC, int8 | Apache-2.0 | `gagni/granite-speech-5.0-470m-turboctc-onnx` (community export of `ibm-granite/granite-speech-5.0-470m-turboctc`) |
| D | Parakeet Ultra (post-trained v3), int8 | CC-BY-4.0 | `Masterx/parakeet-tdt-0.6b-ultra-onnx` (community export) |
| E | Whisper large-v3-turbo, int8 | MIT | sherpa-onnx export |

Canary, Moonshine, Kyutai and the LLM-based models were ruled out on speed,
language coverage, size or licence before measuring
(`docs/build/research-models.md`, chapter A).

## Evidence

Python spike over the fixed test set, identical chunking and scoring for every
candidate (`docs/build/phase1-candidates.md`,
`bench/results/2026-11-10-phase1-candidates.json`). Word error rate in percent:

| Candidate | English read | English meetings (AMI) | French | German |
|---|---|---|---|---|
| A Parakeet v3 int8 | 12.63 | 39.48 | 13.65 | 13.64 |
| B Parakeet v3 fp32 | 4.07 | 18.20 | 6.55 | 10.54 |
| C Granite int8 | 1.58 | 36.63 | 3.79 | 8.45 |
| D Parakeet Ultra int8 | 2.54 | 24.00 | 5.67 | 11.59 |
| E Whisper turbo | 2.35 | 28.33 | 5.85 | 9.24 |

Two candidates lose whole stretches of speech (deletions, not substitutions):
A everywhere, and C on the meeting recordings whose level is low or uneven.
For C the cause is the input level: its in-graph features are not
mean-normalised. With a slow gain control in front of the model its AMI error
falls to about 26 % (per clip 50.7 -> 29.7, 24.4 -> 22.9, 36.6 -> 26.0) and
read speech is unchanged.

Recognition throughput, 4 workers sharing one model with one intra-op thread
each, 10-minute English file, in times real time. The machine was shared with
other work during these runs, so the figures are lower bounds; the ratios are
what matters:

| Candidate | Processor as it is (AMX, AVX-512 VNNI) | Masked to AVX2 + AVX-VNNI | Masked to AVX2 only |
|---|---|---|---|
| C Granite int8 | 59 (100 to 127 when quiet) | 59 | 34 |
| D Parakeet Ultra int8 | 17 to 26 | 21 | 17.6 |
| B Parakeet v3 fp32 | 21 | 16 | 11.6 |

Peak memory: C 1.5 GB, D 2.2 GB, B 3.3 GB. E runs at about 3 to 5 times real
time and is a reference for accuracy only.

## Decision

C, Granite Speech 5.0 470M TurboCTC int8, with input-level normalisation in
front of the model and a second look at speech regions that yield almost no
words.

- It is the most accurate candidate on read speech in all three languages.
- It is two to three times faster than the next candidate under the AVX2 mask,
  which is the margin that pays for voice activity detection, speaker
  separation and a laptop slower than this machine. Parakeet at 17x for
  recognition alone leaves no such margin.
- English counted as one language (read and meetings together, micro-averaged)
  comes to about 8.3 % with level normalisation against 8.0 % for the best
  candidate (B), inside the one-point quality gate; French and German are the
  best measured.

## Consequences

- On meetings with quiet or overlapping speakers Granite remains about 2 points
  behind Parakeet Ultra and 8 behind Parakeet v3 fp32 on the AMI clips. This is
  the price of the speed margin, recorded under the priority rule; the level
  normalisation and the low-yield retry are the mitigations, and their measured
  effect is in `docs/BENCHMARKS.md`.
- The model returns token ids only. The fetch tool derives a second graph that
  also returns the per-frame peak probability, which is where word confidence
  comes from (decision 0007).
- The recogniser cannot be told the language; the user's language choice
  selects the cleaning rules.
- The ONNX file is a community export published days before this decision.
  It is pinned by commit and SHA-256, its export scripts are in the same
  repository, and the upstream weights are IBM's under Apache-2.0. If the
  export repository disappears the owner re-exports with those scripts or
  mirrors the file and updates `build/models.lock`.
- `ISpeechRecognizer` keeps the engine open to a second recogniser; Parakeet
  Ultra int8 is the documented fallback.
