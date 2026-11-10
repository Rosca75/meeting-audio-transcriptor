# Phase 1 candidate benchmark (2026-11-10)

Throwaway Python spike, onnxruntime 1.31.1 CPU, 4 vCPU Intel Xeon 2.1 GHz VM (AVX2, AVX-512 F, AVX-512 VNNI, AVX-VNNI, AMX-int8), 16 GB RAM.
Scripts and raw runs: `bench/spikes/`. Machine-readable data: `bench/results/2026-11-10-phase1-candidates.json`. Hypotheses:
`bench/results/2026-11-10-phase1-hypotheses/`.

Candidates: **A** Parakeet TDT 0.6B v3 int8 (istupakov layout, encoder + decoder_joint int8), **B** same in fp32, **C** Granite Speech 5.0 470M TurboCTC int8,
**D** Parakeet Ultra int8 (`Masterx/parakeet-tdt-0.6b-ultra-onnx` @ `99b09f03`), **E** Whisper large-v3-turbo int8 (`csukuangfj/sherpa-onnx-whisper-turbo` @ `2ca6ff69`,
sherpa-onnx 2.0.1, language forced per file, English for noise). File hashes are in the JSON. A, B, D use my own greedy TDT loop (research A.1.4) with
`nemo128.onnx` as front-end; C is the single-graph model with the README's CTC collapse; the same chunker for all (<= 28 s, cut at the lowest-energy 20 ms frame
18-28 s after the previous cut), texts joined with a space. WER is `bench/spikes/wer.py` (NFKC, lower-case, non-alphanumerics to spaces, micro-average).
No `<unk>` was emitted by A, B or D in any accuracy run (the loop does not mask logit 0).

## 1. Comparison (28 s chunks, 16 clips + three 10-min files, 3,383 s of audio)

WER in percent. RTFx is audio seconds over wall seconds, end to end (read chunks, features, encoder, decoding, join; no model load, no VAD/diarisation),
on `en-10min.wav` (604 s), 4 threads, mean of the repetitions (all values in section 2).

| Cand | en-read | en-ami | fr | de | Best RTFx at 4 threads | Config | Load s | Peak RSS MB |
|---|---|---|---|---|---|---|---|---|
| A Parakeet v3 int8 | 12.63 | 39.48 | 13.65 | 13.64 | 25.8 (24.7 without spinning) | 4 threads x intra 1, 28 s | 2.7 | 1,918 |
| B Parakeet v3 fp32 | 4.07 | 18.20 | 6.55 | 10.54 | about 19-21 (4x1 16.4 spin / 19.2 no spin; 8x1 20.7) | 4x1, no spinning, or 8x1 | 3.7 | 2,993 (3,165 max) |
| C Granite TurboCTC int8 | **1.58** | 36.63 | **3.79** | **8.45** | **107-114** (4x1 107.4 spin, 114.1 no spin; per run 94-127) | 4x1, 28 s | 2.1 | 1,471 |
| D Parakeet Ultra int8 | 2.54 | 24.00 | 5.67 | 11.59 | 24.0 (one run) | 4x1, 28 s | 2.6 | 1,941 |
| E Whisper turbo int8 | 2.35 | 28.33 | 5.85 | 9.24 | 2.4 (one run, 4 threads) | 1 stream, 4 threads | about 10.6 (3 recognisers + first decode) | 4,964 (6,944 in the 56 min run) |

Word counts behind the groups are in the JSON (S, D, I, N per file and group). Read this table with three cautions.

- **A is not a fair picture of Parakeet v3.** Its int8 encoder deterministically drops spans: on `fr-single-01` chunk 3 it returns 15 words where the fp32 model returns 62
  (and the deletions are almost all of its error: 62 D of 251 words). Swapping halves shows the **int8 encoder** is the culprit (int8 encoder + fp32 decoder: 17 words; fp32 encoder + int8 decoder: 62).
  The result does not change with threads (intra 1 and 4 agree). D, whose encoder is also int8 and the same size, does not do it. Use B or D, not A, as the Parakeet reference.
- **AMI is dominated by deletions** for every model (overlapped speech, back-channels, fillers): C `en-ami-01` D=110 of 229 words, A 125, D(ultra) 48, B 35. On AMI the ranking flips:
  B 18.2 < D 24.0 < E 28.3 < C 36.6 < A 39.5. C is far ahead on read speech and clearly behind on the meeting audio (3 clips only, so noisy).
- References are normalised read-speech and AMI transcriptions; fr/de WER also contains reference punctuation-free spelling variants (numbers, compounds), no number normalisation.

Per-group detail for German: every model is at 8-14 %, mostly driven by `de-single-02` (16-26 %, all models) and `de-multi-01`; not model-specific (cause not investigated; possibly spelling variants in the reference).

## 2. Thread configurations (en-10min, 604 s, RTFx per repetition)

`WxI` = W Python worker threads sharing ONE session set, intra_op_num_threads = I (inter_op 1). The mel front-end and decoder_joint always run at 1 intra thread.
`wait for an idle CPU` was enforced before each run (3 s of /proc/stat below 5 percent busy); the 1-minute load average printed by `uptime` was 1-5 at the start because
it still carried the previous benchmark, so the "below 0.5" criterion was not met literally (waiting for it would have added about 2 minutes per run). All runs were strictly sequential.
The VM is noisy: C 1x4 went 37.7 then 83.7, A and B 4x1 spread by 25 %.

| Cand | 1x4 | 4x1 | 2x2 | 4x1 no spinning | 8x1 (oversubscribed) |
|---|---|---|---|---|---|
| A | 14.6, 14.4 | 21.9, 26.9, 26.6, 27.9 | 18.4, 18.1 | 20.8, 26.0, 26.5, 25.3 | 25.9, 24.4 |
| B | 12.9, 13.6 | 20.0, 13.7, 14.8, 16.9 | 11.2, 14.7 | 22.4, 21.1, 17.2, 16.3 | 20.6, 20.8 |
| C | 37.7, 83.7 | 126.3, 94.5, 111.9, 97.1 | 82.6, 87.8 | 116.7, 116.3, 96.7, 126.6 | 96.8, 124.9 |
| D | (4x1) 24.0 | | | | |
| E | 1 run, 4 threads: 2.4 | | | | |

Best configuration for every model is **4 workers x 1 intra-op thread on one shared session**: 1.8x over 1x4 for A (14.5 to 25.8), 1.3 to 1.5x for B, 1.2 to 1.8x for C. The shared session
(weights not duplicated) costs about +450 MB RSS over 1x4 for A. Spinning off makes no consistent difference (C and B slightly better, A slightly worse; within the noise). 8 threads on
4 cores is no better and costs memory. Parakeet time split (1x4, A): encoder 37.4 s, decoder loop 2.9 s, mel 0.8 s of a 41.2 s wall, so the encoder is 91 percent, the
combined decoder_joint loop 7 percent. For B the decoder loop is 7.7-7.9 s of 44-47 s (17 percent): the fp32 decoder_joint is slower, and the combined graph re-runs the LSTM on every step.
In 4x1 runs the time spent inside `session.run` is 3.7-3.9 times the wall time (4 threads fully busy), and the loop around the run calls (decoder step glue in Python) is inside "dec_loop_total",
about 1-2 percent more than the `run` time, so Python overhead is not the bottleneck.

## 3. Chunk length

Speed (en-10min, 4x1, mean of two runs, RTFx): C 10 s 100.9, 20 s 106.9, 28 s 107.4, 40 s 100.5 (peak RSS 1,396 / 1,403 / 1,471 / 1,557 MB). A 10 s 26.4, 20 s 25.3, 28 s 25.8 (RSS 1,458 / 1,759 / 1,918). Within the noise: chunk length barely matters
for speed at 4 workers, and shorter chunks mostly save memory.

WER on the three 10-min files (micro-average of en, fr, de, percent; per file in brackets: de / en / fr):

| Max chunk | C Granite | A Parakeet int8 | B fp32 | D Ultra | E |
|---|---|---|---|---|---|
| 10 s | 5.17 (10.35 / 1.97 / 3.91) | 17.06 (20.46 / 14.53 / 16.69) | not run | not run | not run |
| 20 s | 4.18 (8.84 / 1.36 / 3.00) | 13.67 (16.16 / 15.41 / 9.71) | not run | not run | not run |
| 28 s | 4.33 (9.32 / 1.43 / 2.93) | 13.39 (14.65 / 13.78 / 11.87) | 6.75 (10.43 / 4.62 / 5.73) | 6.58 | 5.79 |
| 40 s | 4.37 (9.39 / 1.43 / 3.00) | not run | | | |

Granite: 20 to 40 s are equivalent (4.2-4.4); 10 s costs about 0.8 point absolute (cuts inside speech, shorter context). Parakeet A's numbers are dominated by its random-looking span drops (section 1) and cannot
be used to judge chunk length; no chunk-length effect can be separated from it.  The seams were not inspected for duplicated or cut words.
Design implication: 20-28 s chunks for either model (the 28 s ceiling keeps Parakeet within its comfortable attention memory).

## 4. Noise-only inputs (60 s each, verbatim output, empty expected)

| File | A | B | C | D | E (English forced) |
|---|---|---|---|---|---|
| silence | empty | empty | `Et. Et. .` | empty | `you you you` |
| white noise | empty | empty | empty | empty | `so so so` |
| pink noise | empty | empty | empty | empty | `Thank you. Thank you. so` |
| office (DEMAND) | empty | empty | empty | empty | `so so so` |

Parakeet (all three variants) is clean on all four. Granite emits a blank-collapsed `Et.` twice on digital silence (a CTC artefact on an exactly constant input; max-softmax of that frame 0.84), nothing on noise.
Whisper hallucinates on all four, as expected; a VAD gate is mandatory for it, and it is only the accuracy reference.

## 5. Granite graph tail

`model.int8.onnx` (opset 17 only, `ai.onnx` domain, IR 8, 3,850 nodes). The last six nodes, in order:

1. `Mul /model/encoder/layers.15/Mul_1`
2. `Add /model/encoder/layers.15/Add_3`
3. `LayerNormalization .../layers.15/norm_out` (outputs `.../norm_out/LayerNormalization_output_0`)
4. `MatMul /model/ctc_head/MatMul` (in: that LayerNorm output and `onnx::MatMul_5305`; an fp32 MatMul, the head is not quantised)
5. `Add /model/ctc_head/Add` (in: `model.encoder.out.bias`, `/model/ctc_head/MatMul_output_0`; out: `/model/ctc_head/Add_output_0`)
6. `ArgMax /ArgMax` (in: `/model/ctc_head/Add_output_0`; axis -1, keepdims 0; out: `token_ids`)

The tensor feeding the ArgMax is `/model/ctc_head/Add_output_0`, **raw logits** `[1, T, 16384]` float32 (log-sum-exp over the vocabulary is 6.7 to 20, not 0, and the graph contains no Softmax or LogSoftmax), where T = 12.5 frames per second (350 for 28 s).
Quantisation: **dynamic**, 163 `MatMulInteger` plus 131 `DynamicQuantizeLinear` nodes (50 fp32 `MatMul` remain), no `ConvInteger`, no `QLinearMatMul`/`QuantizeLinear`/`DequantizeLinear`.

Adding `/model/ctc_head/Add_output_0` as a second graph output (copy saved to `/tmp/granite-with-scores.onnx`, not kept) works in onnxruntime without other changes; argmax of the returned scores equals `token_ids` on every frame.
Max-softmax over the 16,384 logits, on frames where a non-blank token is emitted: read English 28 s mean 0.939 (10th percentile 0.70, min 0.47); AMI headset 0.950 (p10 0.80); AMI distant 0.959 (p10 0.93); noise and silence emit one
token with 0.99 and 0.84. Blank frames sit at 0.97 to 0.999. So the softmax peak is high nearly everywhere and **only separates the clearly uncertain tokens (a long tail below about 0.7)**; on AMI it is not lower than on read speech even though the
WER is far worse (confident deletions, not low-confidence substitutions). Usable as a per-word "uncertain" flag (minimum or mean over a word's frames) but not as a calibrated confidence of correctness, and it does not detect dropped speech.
Exposing it costs one extra `[T,16384]` float output (about 23 MB per 28 s chunk); cheaper to add a ReduceMax/ArgMax-gap or a Softmax+ReduceMax inside the graph and expose a `[T]` vector.

## 6. Observations per model

- **Parakeet v3 (B)**: capitalisation and punctuation present (about 1,190 punctuation characters over the whole set), digits written as digits rarely (1 digit word in all hypotheses), numbers mostly as words; no `<unk>`; no language drift into English in fr (0 English stopwords), 10 in de;
  AMI: 26 of the 59 reference fillers (um, uh, mm, yeah, okay...) kept, 35 of 229 words deleted in `en-ami-01`.
- **Parakeet v3 int8 (A)**: as B but with span drops (section 1); that, not rounding noise, is why its WER is 2-3 times B's. Not shippable as is. The int8 decoder_joint is harmless and 2.5x faster than fp32 (2.8 vs 7.9 s per 600 s).
- **Parakeet Ultra int8 (D)**: same speed as A, no span drops observed, better than B on read English (2.54 vs 4.07) and worse on de (11.6 vs 10.5) and en-ami (24.0 vs 18.2). 4 digit words in the hypotheses. Provenance is a community re-export; the 16-file result is a single sample per file.
- **Granite (C)**: capitalisation and punctuation present (976 characters), numbers as words, very good on read English, French and German (1.6 / 3.8 / 8.5), the best of the five on those three groups, but loses 37 percent on AMI because it drops whole overlapped or fast passages (110 deletions in `en-ami-01`), fillers: 12 of 59 kept. No language drift (0 English stopwords in fr, 8 in de, similar to the others).
  Single `Et.` hallucination on digital silence.
- **Whisper (E)**: a lot of digits (8 digit words in the hypotheses), less punctuation (375 characters; sherpa's output), repetition and hallucination on non-speech; competitive accuracy (5.8 percent on the 10-min files) but 2.4 to 3.1 times real time and 5 to 7 GB RSS; unusable for the 10 minute budget.
- Casing and punctuation are scored away by the normalisation; a model-comparison by punctuation quality was not done.

## 7. Caveats

- One 4-vCPU VM, high run-to-run noise (up to 2x for short Granite runs: 6 s wall). The comparison between C and the Parakeet variants (4-5x) is robust, the comparison between spinning on and off, or 4x1 and 8x1, is not.
- The 12x target is met by C (about 100x) and by A, B, D (16 to 26x) before VAD and diarisation; B at 16-21x leaves little room (the real engine needs speech-only audio and diarisation).
- The test set is small (16 clips); the AMI group is 3 clips, so en-ami differences below a few points are not significant.
- A uses istupakov's int8 encoder; the span-dropping may depend on the ORT version (1.31.1 here) or the quantisation; it was not investigated beyond the half-swap. The Parakeet loop is greedy TDT per onnx-asr; `<unk>` was not masked.
- Whisper: sherpa-onnx 2.0.1 greedy decoding on 28 s chunks (not 30 s native windows with timestamps), language forced per file.
- Python timings include a Python greedy loop, which maps to C# only approximately (the C# loop will be at most as slow). Peak RSS is whole process VmHWM including both mel and decoder sessions; Whisper's number includes three recognisers (one per language).
- "Load s" is the construction of the sessions in the process; the first inference was excluded by an untimed warm-up (A: about 1 s extra).
