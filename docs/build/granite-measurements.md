# Granite Speech 5.0 470M TurboCTC: measurements of the recogniser

Working notes behind the defaults of `GraniteCtcRecognizer`, `GainOptions` and `LowYieldRetry`.
All numbers come from the deep-tier tests in
`tests/MeetingAudioTranscriptor.Pipeline.Tests/Recognition/GraniteMeasurementTests.cs`, run on the
development container pinned to one core (`nice -n 19 taskset -c 3`, Debug build, ONNX Runtime
1.31.1, one intra-op thread), with the core shared with another process. CPU times are the test
process's own. Word error rates are micro-averaged per group after `TextNormalizer` (lower case, no
punctuation). Chunks are cut by the real `VadChunkingStage` (Silero VAD), target 20 s and maximum
30 s unless stated.

Groups: en-read = en-multi-01, en-single-01, en-single-02 (282 s); en-ami = en-ami-01, en-ami-02,
en-ami-sdm-01 (260 s, 828 reference words); fr = 5 clips (513 s); de = 5 clips (487 s).

## 1. The graph patch

The export's tail is `/model/ctc_head/MatMul -> /model/ctc_head/Add -> /ArgMax(axis=-1,
keepdims=0) -> token_ids`: the ArgMax reads raw logits (no LogSoftmax), opset 17, IR 8, 3850 nodes.
`build/onnx-patch.mjs` (patch `ctc-token-scores`) appends

- `token_scores/Softmax` (Softmax, axis -1) over `/model/ctc_head/Add_output_0`,
- `token_scores/ReduceMax` (axes [-1], keepdims 0) producing `token_scores`,
- a graph output `token_scores`, float32, with the shape of `token_ids` (`[ArgMaxtoken_ids_dim_0, frames]`).

`model.int8.scores.onnx`: 552,434,586 bytes (290 more than the source), sha256
`6cf799e8dfea6deb341dc1dcf1dcb0fcd9b88a70c7b90edcd552d363e8505986`, derived in 2.6 s wall (0.6 s
CPU), byte-identical on every run. `onnx.checker` accepts it. On the first 20 s of en-single-01
the derived graph's `token_ids` equal the source's on all 250 frames, and `token_scores` lie in
[0.576, 1.000]. Extra cost per 20 s chunk: below the run-to-run noise (best of five, one thread:
0.652 s source, 0.584 s derived asked for both outputs); the softmax over 16,384 classes is about
6 M exponentials per 20 s.

The lock lists the source under the model's `sources` (role `source`) and the derived file under
`files` with `derive: {from, patch}`; the application and the catalog see only `files`. A default
fetch downloads the source only to derive (into `--cache` when given, else temporarily) and leaves
the models folder holding the derived graph and the tokenizer; `--keep-sources` installs the source
too.

## 2. Input-level normalisation

| scheme | en-read | en-ami | fr | de | en-ami-01 | en-ami-02 | en-ami-sdm-01 |
|---|---|---|---|---|---|---|---|
| none | 2.2 | 30.5 | 3.0 | 7.7 | 46.3 | 21.0 | 27.6 |
| chunk rms 0.05 | 2.2 | 30.7 | 3.0 | 7.6 | 45.9 | 20.6 | 28.6 |
| chunk rms 0.1 | 2.2 | 31.2 | 3.1 | 7.7 | 46.3 | 22.1 | 28.4 |
| chunk rms 0.2 | 2.2 | 26.4 | 3.0 | 7.4 | 28.8 | 21.0 | 28.6 |
| chunk rms 0.3 | 2.2 | 26.2 | 3.2 | 7.4 | 26.6 | 22.1 | 28.6 |
| speech rms 0.05 | 2.2 | 30.4 | 3.3 | 7.8 | 45.9 | 20.6 | 27.8 |
| speech rms 0.1 | 2.2 | 30.5 | 3.0 | 7.7 | 45.9 | 21.4 | 27.6 |
| **speech rms 0.2 (default)** | **2.2** | **26.4** | **2.9** | **7.3** | 28.8 | 21.0 | 28.6 |
| speech rms 0.3 | 2.2 | 26.2 | 3.2 | 7.7 | 27.1 | 21.8 | 28.6 |
| region rms 0.05 | 2.2 | 31.2 | 3.3 | 8.1 | 45.9 | 20.6 | 29.6 |
| region rms 0.1 | 2.2 | 30.1 | 3.1 | 7.9 | 45.9 | 20.6 | 27.3 |
| region rms 0.2 | 2.2 | 26.4 | 2.8 | 7.5 | 28.8 | 21.0 | 28.6 |
| region rms 0.4 | 2.0 | 25.8 | 3.5 | 8.1 | 26.6 | 21.0 | 28.6 |
| agc 0.05, 0.5 s, 30x | 2.3 | 28.7 | 3.3 | 8.5 | 40.2 | 20.2 | 27.6 |
| agc 0.1, 0.5 s, 30x | 2.2 | 27.1 | 3.1 | 8.0 | 33.6 | 20.2 | 27.8 |
| agc 0.2, 0.5 s, 30x | 2.0 | 25.7 | 3.1 | 8.2 | 27.5 | 20.2 | 28.4 |
| agc 0.3, 0.5 s, 30x | 2.2 | 25.7 | 3.3 | 7.8 | 26.2 | 21.0 | 28.6 |
| agc 0.4, 0.5 s, 30x | 2.0 | 25.4 | 3.6 | 7.8 | 24.9 | 21.0 | 28.6 |

Slow AGC sweep on the meetings alone (en-ami WER; window 0.25 / 0.5 / 1.0 s, each with caps 10x / 30x / 100x):

| target | 0.25 s | 0.5 s | 1.0 s |
|---|---|---|---|
| 0.05 | 30.0 / 30.0 / 30.1 | 30.1 / 28.7 / 28.8 | 30.4 / 30.3 / 30.0 |
| 0.1 | 28.2 / 26.4 / 27.3 | 28.4 / 27.1 / 27.3 | 28.0 / 27.4 / 27.4 |
| 0.2 | 25.6 / 25.7 / 26.7 | 25.8 / 25.7 / 26.2 | 25.6 / 25.7 / 26.2 |
| 0.3 | 25.8 / 25.8 / 26.4 | 25.7 / 25.7 / 26.1 | 25.8 / 25.6 / 26.1 |
| 0.4 | 25.4 / 25.4 / 26.4 | 25.7 / 25.4 / 25.9 | 25.9 / 25.7 / 26.2 |

Findings:

- The level, not its evenness, is what matters with VAD chunks: every scheme at a target of 0.2 or
  more brings the meetings from 30.5 % to 25.4 to 26.4 %, almost all of it on en-ami-01 (46 to
  25-29 %). The spread inside that band is about 8 words of 828, within noise. Targets of 0.05 and
  0.1 do nothing: the model wants loud input (the spike's gain from RMS 0.1 came from its fixed
  28 s cuts, which carry long silences; VAD chunks already avoid most of that).
- Cap 100x is consistently worst on en-ami-sdm-01 (peak 0.041): it raises the distant
  microphone's background.
- The selection rule (lowest meeting WER with no read-speech group more than 0.2 points worse
  than no gain) admits chunk rms 0.2 and 0.3, speech rms 0.2 and 0.3, region rms 0.2; it excludes
  every AGC (each leaves French or German 0.3 to 0.8 points worse) and region rms 0.4. The
  admitted schemes tie on the meetings (26.2 to 26.4), so the tie is broken on read speech and on
  clipping: **speech rms 0.2, cap 30x** (2.2 / 26.4 / 2.9 / 7.3) improves French and German, clips
  less than 0.3, and without regions falls back to the chunk RMS, which measures the same.

## 3. Noise

The four 60 s noise files fed directly in 30 s chunks: **0 words** under every scheme (none,
chunk / speech / region rms 0.1, AGC 0.1 at 30x and at 100x) and under the default (models-tier
test). The model emits only blanks and control tokens such as `<|nospeech|>` there.

## 4. Low-yield retry

| setting | en-read | en-ami | fr | de | extra runs on 205 chunks | replaced |
|---|---|---|---|---|---|---|
| none (default recogniser) | 2.2 | 26.4 | 2.9 | 7.3 | 0 | 0 |
| region >= 1.5 s, < 0.5 words/s | 2.2 | 26.4 | 2.9 | 7.3 | 0 | 0 |
| region >= 1.5 s, < 1 words/s | 2.2 | 26.4 | 2.9 | 7.3 | 1 (3 per hour) | 0 |
| region >= 1 s, < 1 words/s | 2.2 | 26.4 | 2.9 | 7.3 | 2 (5 per hour) | 0 |
| region >= 2 s, < 1.5 words/s | 2.2 | 26.4 | 2.9 | 7.8 | 1 (3 per hour) | 1 |

With the input gain in place no speech region is left nearly empty, so the retry recovers nothing
and its one replacement made German worse. It is **off by default**; the decorator stays for a
recogniser that drops quiet speakers inside a chunk. The remaining losses on en-ami-01 are
substitutions and short deletions in overlapping speech, not empty regions.

## 5. Chunk length

Default gain; WER per file group; the 10-minute files are meeting-style concatenations of read speech.

| chunker target / max | en-10min | fr-10min | de-10min | en-ami | chunks | CPU s |
|---|---|---|---|---|---|---|
| 10 / 20 s | 1.2 | 2.6 | 9.0 | 26.2 | 289 | 78 |
| 20 / 30 s | 1.2 | 2.4 | 8.2 | 26.4 | 244 | 73 |
| 25 / 30 s | 1.2 | 2.4 | 8.2 | 24.1 | 241 | 71 |
| 30 / 30 s | 1.2 | 2.4 | 8.1 | 24.1 | 240 | 70 |
| 30 / 35 s | 1.2 | 2.4 | 8.2 | 23.9 | 240 | 70 |
| 30 / 40 s | 1.2 | 2.4 | 8.2 | 23.9 | 240 | 73 |
| 20 / 40 s | 1.2 | 2.4 | 8.2 | 26.2 | 244 | 72 |

The target decides, not the maximum. **`MaxChunkSeconds` stays 30** (inside the trained
positions, about 41 s) and the **recommended chunker target is 25 s**: it reads the meetings as
well as 30 s (24.1 %) and leaves the chunker 5 s to find a silence before a forced cut.

## 6. Speed

`en-10min.wav` (604 s, 72 chunks, default settings, after one warm-up chunk): 19.9 s CPU, **30.3x
real time on one core**; 99.3 % of the wall time is inside the graph, so the gain and the
decoding are under 1 %. WER 1.2 %.

## 7. Models-tier floors

Default settings, chunker target 25 s, maximum 30 s:

| clip | measured | floor |
|---|---|---|
| en-single-01 | 0.9 | 4 |
| fr-single-01 | 1.6 | 5 |
| de-single-01 | 3.7 | 8 |
| en-ami-01 | 28.8 | 35 |
| en-ami-02 | 20.6 | 25 |
| en-ami-sdm-01 | 23.7 | 30 |

## 8. Decoding checks

`BpeVocabulary.Decode` matched the reference `tokenizers` 0.23.3 decoding (`skip_special_tokens`)
on 3,100 sequences: 3,000 random ones mixing word pieces, special tokens and byte pieces (valid and
truncated UTF-8) and 97 encoded sentences of the reference transcripts, plus three hand-written French, German and quoted sentences. Word confidence is the
minimum of the tokens' peak probabilities, each token's peak being the highest `token_scores`
value over its frame run.
