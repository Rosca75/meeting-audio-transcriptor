# Research: models and runtimes for the offline meeting transcriber

Date of research: 2026-11-10. Everything below was checked against live sources on that date
(Hugging Face API and files, GitHub raw files and tags, NuGet API, vendor docs). Where a statement is
from memory or a single secondary source, it is marked **(unverified)**. Scratch downloads were kept in
`/tmp/research-models/`. "HF" = Hugging Face. "sha" = commit sha of `main` on 2026-11-10.

Target: C#/.NET 10, ONNX Runtime CPU, 4 cores / 8 threads laptop, 16 GB RAM, EN/FR/DE meetings
30 min - 2 h, hard requirement 2 h in <= 10 min (>= 12x real time) and peak RAM <= 4 GB, commercial-OK licences.

---

## Key takeaways

1. **The starting hypothesis (Parakeet TDT 0.6B v3 int8) is accurate but probably too slow on 4 cores.**
   The same int8 encoder that reaches ~31x on an 8-core desktop Ryzen 9800X3D (onnx-asr benchmark) ran at only
   **~8-9x real time (encoder + mel only, no decoder loop)** on a 4-vCPU Xeon VM in this research session
   (chapter G; noisy shared VM, so indicative only). Parakeet alone may miss the 12x target on the laptop.
2. **New 2026 candidate that changes the ranking: IBM Granite Speech 5.0 470M TurboCTC**
   (`ibm-granite/granite-speech-5.0-470m-turboctc`, Apache-2.0, released 2026-10-21, EN/FR/DE/ES/PT/IT/JA,
   CTC, 16 Conformer blocks, native punctuation and capitalisation). A community single-file ONNX int8 export
   (553 MB, raw waveform in, token ids out, mel front-end inside the graph) ran **~31-38x real time on the same
   4-vCPU VM** (3.5-4x faster than Parakeet int8) and the exporter's own FLEURS-FR test shows WER 7.20 % vs
   7.02 % for Parakeet int8 at 2.2x the speed (i7-13700H). Caveats: community export (days old), self-reported
   numbers, no word timestamps (only one token per 80 ms frame), no DE/meeting WER found yet.
3. **Parakeet TDT 0.6B v3** is still the accuracy anchor: CC-BY-4.0, 25 languages, punctuation + capitalisation,
   word timestamps, AMI (meetings) WER 11.31 % vs 16.0 % for Canary-1b-v2 and 16.1 % for Whisper large-v3-turbo;
   FLEURS FR 5.15 %, DE 5.04 %, EN 4.85 %. It cannot be told which language to use, and one May 2026 blog
   report claims silent French-to-English drift on spontaneous speech (secondary source, **unverified**).
4. **Everything else is out on speed or languages:** Whisper large-v3-turbo runs at 3.9-5.4x on a fast desktop
   (onnx-asr), Canary-1b-v2 at 8-15x there, Moonshine has no French, Kyutai STT has no German, Qwen3-ASR /
   Voxtral / Granite-2B / Cohere Transcribe / Nemotron-3.5-Transcribe-30B are 2-34 B-parameter autoregressive
   models. Whisper is only a fallback.
5. **New licence flag:** NVIDIA's 2026 models (`nemotron-3.5-asr-streaming-0.6b`, `nemotron-labs-unified-asr-0.6b`,
   Nemotron-3.5-Transcribe-30B) use **OpenMDW-1.1** (permissive, "ready for commercial use", but not one of the
   licences on the allow list). `nvidia/parakeet-unified-en-0.6b` is `license: other`. Also not allowed:
   `nyrahealth/CrisperWhisper` (CC-BY-NC-4.0), Granite `...-turboctc-nc` (CC-BY-NC-SA-4.0), TEN VAD
   (Apache-2.0 plus a non-compete clause), Moonshine non-English legacy models (Moonshine Community License,
   non-commercial).
6. **Time budget:** 600 s total. Silero VAD over 2 h costs ~90 s single-threaded (RTF 0.0127), ffmpeg decode
   ~10 s, and sherpa-onnx's diarisation pipeline is documented at RTF 0.11-0.30 single-thread (= 13-36 min for
   2 h at default settings). ASR must therefore deliver >= 20-25x on its own. Diarisation must be configured
   cheaply (window shift 0.5-1.0, small embedding model, parallel workers) or replaced by the VAD-only fallback.
7. **VAD: Silero VAD v6.2.3** (MIT, 2.33 MB, input `[B, 64+512]` float, state `[2,B,128]`, `sr` int64 scalar,
   output `[B,1]`). Pin by tag `v6.2.3` (commit `5cd7945676eb32225748052e2e6a0580e4686a08`).
8. **Diarisation picks:** segmentation `pyannote/segmentation-3.0` ONNX (MIT; public re-exports by
   `onnx-community` and `csukuangfj`, 6.0 MB fp32 / 1.5 MB int8, 10 s window, 589 frames, 7 powerset classes);
   embedding **WeSpeaker CAM++ LM (VoxCeleb)** from the sherpa-onnx release (29.3 MB, 512-d, 1.15 GFLOPs vs 4.55
   for ResNet34); clustering = cosine agglomerative, complete linkage, threshold ~0.5 (sherpa default). Cheaper
   fallback: Silero segments, 1.5 s windows, CAM++ embeddings, same clustering, no overlap handling.
9. **ffmpeg:** BtbN `autobuild-2026-10-31-12-59` (a monthly build, kept two years) `ffmpeg-n8.1.3-27-g8dca1586f4-linux64-lgpl-8.1.tar.xz`,
   135 MB, LGPL-3 (`--enable-version3`, no `--enable-gpl`, no nonfree), native `aac` decoder present, MP4/M4A/
   MKV/OGG/WAV/FLAC/MP3 demuxers present; verified by running it (20 min AAC to 16 kHz mono in 1.4 s).
10. **ONNX Runtime:** `Microsoft.ML.OnnxRuntime` **1.31.1** (published 2026-10-28) is the latest stable. On AVX2
    machines without VNNI, `session.x64quantprecision` can matter for U8S8 int8 accuracy; set intra-op threads to
    the physical core count and share one `InferenceSession` across worker threads to keep RAM low.
11. **Punctuation/truecasing is not needed** for Parakeet v3 or Granite TurboCTC (both emit it natively);
    fallback `1-800-BAD-CODE/punct_cap_seg_47_language` (Apache-2.0, 233 MB ONNX).
12. **Recommended benchmark shortlist:** (1) Granite 470M TurboCTC int8, (2) Parakeet TDT 0.6B v3 int8
    (sherpa-onnx split export), (3) Parakeet Ultra int8 (post-trained v3, same graph and speed, better WER,
    community ONNX). Run all three on the real target laptop with real meeting audio before choosing.
13. **Parakeet quirks to code around:** decoder targets are **int32**, not int64; the model emits `<unk>` (id 0)
    for some characters (e.g. degree sign, `+`); features are 128-bin with per-utterance mean/variance
    normalisation; one encoder frame = 80 ms.
14. **Do not send long files to the encoder in one piece.** Parakeet uses full attention (NVIDIA: 24 min on an
    A100 80 GB); on CPU use VAD-cut chunks of 20-60 s (RSS 1.7 GB at 30 s, 2.3 GB at 60 s in my test).
15. **Open items for the team:** measure on the real laptop (AVX2 only?), verify DE/EN meeting WER of Granite
    TurboCTC, decide whether to self-export Granite ONNX from the IBM weights (scripts are in the community repo)
    rather than depend on a community upload, and get legal sign-off on VoxCeleb-derived speaker models.

---

## A. Speech recognition candidates (EN/FR/DE)

### A.0 Overview

| Model | Licence (quoted id) | Languages | Params | Best ONNX source | CPU speed evidence | Verdict |
|---|---|---|---|---|---|---|
| Parakeet TDT 0.6B v3 | `cc-by-4.0` | 25 EU incl. EN/FR/DE | 600 M | `istupakov/...-onnx`, `csukuangfj/...-int8` | 31 x (9800X3D, onnx-asr int8); 42 x (EPYC 9575F 8 cores, sherpa int8); 28 x (M2); ~8-9 x (4-vCPU VM, mine) | accuracy anchor, speed risk |
| Granite Speech 5.0 470M TurboCTC | `apache-2.0` | EN FR DE ES PT IT JA | 473 M | community `gagni/...-onnx` (int8) | 36.5 x (i7-13700H, exporter), 31-38 x (4-vCPU VM, mine) | speed leader, needs validation |
| Parakeet Ultra (moondream) | `cc-by-4.0` | same 25 | 627 M | community `Masterx/...-ultra-onnx` | same graph as v3 | better WER, same speed |
| Canary-1b-v2 | `cc-by-4.0` | same 25 | 979 M | `istupakov/canary-1b-v2-onnx` | 7.7-8.3 x fp32, 14.4-14.9 x int8 (9800X3D) | too slow, worse on AMI |
| Canary-1b-flash | `cc-by-4.0` | EN DE ES FR | 811 M (883 M card) | `istupakov/canary-1b-flash-onnx` | no CPU numbers found | not needed |
| Canary-180m-flash | `cc-by-4.0` | EN DE ES FR | 182 M | `istupakov/canary-180m-flash-onnx` | no CPU numbers found | speed reserve, weak WER |
| Whisper large-v3-turbo | `mit` | 99 | 809 M | `onnx-community/whisper-large-v3-turbo` | 2.9-5.4 x (9800X3D) | fallback only |
| distil-large-v3.5 | `mit` | English only | 756 M | `distil-whisper/distil-large-v3.5-ONNX` | ~1.5 x turbo | EN only, no |
| Moonshine | MIT (EN, streaming) | no French | 27-245 M | `onnx-community/moonshine-*-ONNX` | n/a | no |

### A.1 NVIDIA Parakeet TDT 0.6B v3 (lead candidate in the brief)

**Upstream.** `nvidia/parakeet-tdt-0.6b-v3`, sha `541d1f99c6b0c3cd0b11a95167540bb8edefd82b`, model card header
`license: cc-by-4.0`, "GOVERNING TERMS: Use of this model is governed by the CC-BY-4.0 license" and "This model
is ready for commercial/non-commercial use". FastConformer encoder (24 layers, hidden 1024, 8 heads, conv kernel 9,
subsampling factor 8) + TDT decoder, 600 M parameters, 25 languages (bg hr cs da nl en et fi fr de el hu it lv lt
mt pl pt ro sk sl es sv ru uk). Release date 2025-08-14. Sources:
https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 ,
https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3/resolve/main/config.json ,
technical report https://arxiv.org/abs/2509.14128 .

**Language behaviour.** "The model automatically detects the language of the audio and transcribes it without
requiring additional prompting" (model card). There is no language-prompt input in the NeMo/ONNX graph (the
exported graphs take only features and lengths). **A language cannot be forced** in the open model. A May 2026 blog
post (https://www.creativeainews.com/blog/parakeet-tdt-v3-french-translation-bug/, secondary, **unverified**) reports
English words in 31.3 % of utterances on a 40-minute spontaneous French interview and 0 % on scripted French.
Mitigation to test: detect language per segment (e.g. from the VAD segments with a tiny language-id model or
from Whisper-style language ids of a second model) and re-run a different model, or use the unified
Nemotron model that has a language-id prompt (A.6, licence caveat).

#### A.1.1 ONNX exports

| Repo | sha (main, 2026-11-10) | Licence tag | Layout | Notes |
|---|---|---|---|---|
| `istupakov/parakeet-tdt-0.6b-v3-onnx` | `8f23f0c03c8761650bdb5b40aaf3e40d2c15f1ce` (2026-02-17) | `cc-by-4.0` | encoder + combined decoder_joint + preprocessor | onnx-asr layout; 114 k downloads |
| `csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8` | `ed5d745623472f97cfc2e6c2916ee248798fc5a8` (2026-11-07) | `cc-by-4.0` | encoder + decoder + joiner (split) | sherpa-onnx layout; int8 only |
| `moondream/parakeet-ultra` ONNX re-exports (A.1.7) | see there | `cc-by-4.0` | onnx-asr layout | community |
| `onnx-community/parakeet-tdt-0.6b-v3-ONNX` | does not exist (API: "Invalid username or password") | - | - | - |

Files of `istupakov/parakeet-tdt-0.6b-v3-onnx` (sizes from the HF tree API, sha256 = LFS oid):

| File | MB | Precision | sha256 |
|---|---|---|---|
| `encoder-model.int8.onnx` | 652.2 | int8 dynamic (QUInt8 weights, `quantize_dynamic`) | `6139d2fa7e1b086097b277c7149725edbab89cc7c7ae64b23c741be4055aff09` |
| `decoder_joint-model.int8.onnx` | 18.2 | int8 (uses `com.microsoft` `DynamicQuantizeLSTM`) | `eea7483ee3d1a30375daedc8ed83e3960c91b098812127a0d99d1c8977667a70` |
| `encoder-model.onnx` + `encoder-model.onnx.data` | 41.8 + 2435.4 | fp32 (external data) | `98a74b21...` / `9a22d372...` |
| `decoder_joint-model.onnx` | 72.5 | fp32 | `e978ddf6688527182c10fde2eb4b83068421648985ef23f7a86be732be8706c1` |
| `nemo128.onnx` | 0.14 | fp32 mel preprocessor (128 bins) | `a9fde1486ebfcc08f328d75ad4610c67835fea58c73ba57e3209a6f6cf019e9f` |
| `vocab.txt` | 0.09 | 8193 lines `token id` | git blob `fc43e1c723e2` |
| `config.json` | tiny | `{"model_type":"nemo-conformer-tdt","features_size":128,"subsampling_factor":8}` | |

Files of `csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8`:

| File | MB | sha256 |
|---|---|---|
| `encoder.int8.onnx` | 652.3 | `1afbe69c1ffdabee94755f42e4daf1d93549131a3da11356bda84fcef0f2c479` |
| `decoder.int8.onnx` | 11.8 | `0222a86147299d4dee90edf95b6d13827edb42ad8b7c9cf0b2582f030937c0b5` |
| `joiner.int8.onnx` | 6.4 | `cbd02f3696e9944970111fb439d9c3fd365a80374565f13f2b9c7007d253fc98` |
| `tokens.txt` | 0.09 | identical vocabulary to `vocab.txt` (same git blob) |

The same bundle as a tarball: https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2 (487 MB, HEAD 200).
The unquantised sources: `nvidia/parakeet-tdt-0.6b-v3` has `model.safetensors` (2508 MB), the `.nemo` (2509 MB) and, new,
`parakeet-tdt-0.6b-v3.q8_0.gguf` (714 MB, for NVIDIA's NeMo-Speech.cpp, not usable by ORT).

#### A.1.2 Feature front-end (exact)

Sources: onnx-asr `preprocessors/nemo.py`
(https://raw.githubusercontent.com/istupakov/onnx-asr/main/preprocessors/nemo.py, which is also what generated
`nemo128.onnx`), HF `processor_config.json` of the NVIDIA repo, sherpa-onnx `offline-recognizer-transducer-nemo-impl.h`.

| Parameter | Value |
|---|---|
| Sample rate | 16000 Hz mono, float32 in [-1, 1] |
| Features | 128 log-mel (`features: 128`) |
| `n_fft` | 512 |
| Window | Hann, **symmetric** (`periodic=0`), length 400 (25 ms), zero-padded centred to 512 |
| Hop | 160 (10 ms) |
| Pre-emphasis | 0.97: `x[0]`, then `x[i] - 0.97*x[i-1]`; samples beyond the real length forced to 0 |
| Framing | STFT after **constant zero padding** of 256 samples each side (centre=True, pad_mode=constant) |
| Power | `mag_power = 2` (|STFT|^2, sum of re^2+im^2) |
| Mel filterbank | 128 bins, 0 - 8000 Hz, **Slaney** scale and Slaney norm (librosa `mel` defaults) |
| Log | `log(mel + 2^-24)` (natural log, `log_zero_guard_type: add`, value 2^-24) |
| Normalisation | `normalize: per_feature`: per mel bin, over the valid frames of the utterance, `(x - mean) / (std + 1e-5)`, std is **unbiased** (divide by n-1); frames beyond the valid length set to 0 |
| Dither | none at inference (0) |
| `pad_to` | 0 (no padding to a multiple); batch padding is masked by `length` |
| Valid frame count | `features_lens = waveform_len / 160` (integer division; the STFT yields `len/160 + 1` frames, the last one is cut by the length mask) |
| Frame splicing | none |

Note: onnx-asr's code states the pre-normalisation maths; `nemo_normalize_type = per_feature` is read from the
encoder metadata by sherpa (`normalize_type`, `feat_dim=128`, `subsampling_factor=8`, `vocab_size=8192`,
`pred_rnn_layers=2`, `pred_hidden=640`; I read these from the tail of `encoder.int8.onnx` with an HTTP range
request). Sherpa builds its own Kaldi-style fbank with `is_librosa=true`, `low_freq=0`, `remove_dc_offset=false`;
that path is not bit-identical to NeMo's, so for hand-written C# follow the onnx-asr formulas above, or simply run
`nemo128.onnx` (inputs `waveforms [B,N]` float, `waveforms_lens [B]` int64; outputs `features [B,128,T]`,
`features_lens [B]`) through ORT, which removes the need to port the mel code (cost: small, single thread).

#### A.1.3 Graph I/O contracts (read with the `onnx` Python package from the downloaded files)

istupakov layout:

| Graph | Inputs | Outputs |
|---|---|---|
| `encoder-model*.onnx` (opset 17) | `audio_signal` float `[B,128,T]`; `length` int64 `[B]` | `outputs` float `[B,1024,T']`; `encoded_lengths` int64 `[B]` |
| `decoder_joint-model*.onnx` | `encoder_outputs` float `[B,1024,T']`; `targets` **int32** `[B,U]`; `target_length` **int32** `[B]`; `input_states_1` float `[2,B,640]`; `input_states_2` float `[2,B,640]` | `outputs` float `[B,T',U',8198]`; `prednet_lengths` int32; `output_states_1` `[2,B,640]`; `output_states_2` `[2,B,640]` |

Sherpa-onnx layout:

| Graph | Inputs | Outputs |
|---|---|---|
| `encoder.int8.onnx` | `audio_signal [B,128,T]`, `length [B]` (names read from the session) | `outputs [B,1024,T']`, `encoded_lengths [B]` int64 |
| `decoder.int8.onnx` | `targets` int32 `[B,U]`, `target_length` int32 `[B]`, `states.1` float `[2,B,640]`, `onnx::Slice_3` float `[2,1,640]` | `outputs [B,640,U]`, `prednet_lengths`, `states`, `162` (the two LSTM states) |
| `joiner.int8.onnx` | `encoder_outputs [B,1024,T]`, `decoder_outputs [B,640,U]` | `outputs [..., 8198]` |

Constants: encoder frames `T' = (T - 1) / 8 + 1` (integer division, onnx-asr), 1 frame = 80 ms;
decoder is a 2-layer LSTM, hidden 640; initial states are zeros; vocabulary 8192 SentencePiece tokens + `<blk>`
= **8193 entries, blank id = 8192** (last line of `vocab.txt`: `<blk> 8192`); special tokens `<unk>`=0,
`<|nospeech|>`=1, `<pad>`=2, `<|endoftext|>`=3 and a block of control tokens up to ~id 40 (language ids etc.);
`▁` (U+2581) marks a word start. Joint output size 8198 = 8193 token logits (incl. blank) + **5 duration logits**;
**TDT durations = [0, 1, 2, 3, 4]** (HF `config.json`: `"durations":[0,1,2,3,4]`, `"max_symbols_per_step":10`,
`"blank_token_id":8192`, `"decoder_hidden_size":640`, `"vocab_size":8193`).

#### A.1.4 Greedy TDT decoding, exactly as the two implementations do it

onnx-asr (`src/onnx_asr/asr.py` + `models/nemo.py`, https://raw.githubusercontent.com/istupakov/onnx-asr/main/src/onnx_asr/asr.py):

```
enc = encoder_out[b]            # [T', 1024], transposed from [1024, T']
state = (zeros[2,1,640], zeros[2,1,640]); tokens = []; stamps = []
t = 0; emitted = 0
while t < enc_len:
    prev = tokens[-1] if tokens else BLANK(8192)
    out, new_state = decoder_joint(enc[t], targets=[[prev]], target_length=[1], state)   # out: 8198 floats
    token = argmax(out[0 : 8193])                    # includes blank at index 8192
    step  = argmax(out[8193 : 8198])                 # duration index == duration in frames (0..4)
    if token != BLANK:
        state = new_state                            # state advances ONLY when a token is emitted
        tokens.append(token); stamps.append(t); emitted += 1
    if step > 0:
        t += step; emitted = 0
    elif token == BLANK or emitted == max_tokens_per_step(10):
        t += 1; emitted = 0
    # step == 0 with a non-blank token: stay on the same frame and emit again
timestamp_seconds(token i) = stamps[i] * 0.08       # window_step 0.01 * subsampling 8
```

sherpa-onnx (`offline-transducer-greedy-search-nemo-decoder.cc`, `DecodeOneTDT`,
https://raw.githubusercontent.com/k2-fsa/sherpa-onnx/master/sherpa-onnx/csrc/offline-transducer-greedy-search-nemo-decoder.cc):

```
blank = vocab_size - 1 (= 8192);  dec_out, states = decoder(token=blank, zero states)   # decoder run ONCE per emitted token
t = 0; tokens_this_frame = 0; skip = 0; max_tokens_per_frame = 5
while t < T':
    logits = joiner(enc[t], dec_out)                 # 8198 floats; blank_penalty optional: logits[blank] -= p
    y    = argmax(logits[0:8193]);  skip = argmax(logits[8193:8198])
    if y != blank:
        emit(y, timestamp=t, duration=skip)
        dec_out, states = decoder(y, states)         # only now is the prediction network advanced
        tokens_this_frame += 1
    if skip > 0: tokens_this_frame = 0
    if tokens_this_frame >= 5: tokens_this_frame = 0; skip = 1
    if y == blank and skip == 0: tokens_this_frame = 0; skip = 1
    t += skip
```

Differences that matter for C#: (a) sherpa splits decoder and joiner, so the LSTM runs once per emitted token and
the joiner once per step; onnx-asr's combined `decoder_joint` re-runs the LSTM on every call (slower); (b) NeMo/
onnx-asr cap is 10 symbols per frame, sherpa 5; (c) onnx-asr's `targets` is `[[last_token]]` with a stale state, which
is equivalent. **Timestamps:** token start = `t * 0.08 s` (frame index at emission), the duration output (0-4 frames)
gives an end estimate `(t + max(duration,1)) * 0.08`; word start/end = first/last token of each `▁`-delimited group
(HF Transformers `processor.decode(..., durations=...)` does the same for NeMo-style word timestamps). Text assembly:
concatenate token strings, `▁` -> space, strip leading space (sherpa strips the first character if it is a space).
The decoder emits no language, punctuation or case tokens separately: they are ordinary vocabulary pieces.

#### A.1.5 Accuracy, speed, behaviour

WER (percent, no punctuation/case, greedy, from the NVIDIA model card, https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3):

| Set | EN | FR | DE |
|---|---|---|---|
| FLEURS | 4.85 | 5.15 | 5.04 |
| MLS | - | 4.97 | - |
| CoVoST 2 | 6.80 | 6.05 | 4.84 |
| Open ASR Leaderboard EN (avg 6.34): AMI 11.31, Earnings-22 11.42, GigaSpeech 9.59, LS-clean 1.93, LS-other 3.59, SPGI 3.97, TEDLIUM 2.75, VoxPopuli 6.14 | | | |
| Noise (avg WER 6.34 clean, 7.12 at 10 dB, 8.23 at 5 dB, 11.66 at 0 dB SNR; AMI 11.31 -> 13.99 / 17.59 / 24.44) | | | |

An independent re-score with the Sept-2026 leaderboard normaliser (moondream, https://huggingface.co/moondream/parakeet-ultra):
v3 FLEURS EN 4.25, FR 4.81, DE 4.13; AMI 10.86 (cleaned AMI 9.52). Meeting-style data is the weak spot (AMI ~11 %).

CPU speed (RTFx = audio seconds / processing seconds):

| Source | Hardware | Setup | RTFx |
|---|---|---|---|
| onnx-asr benchmarks https://istupakov.github.io/onnx-asr/benchmarks/ | Ryzen 7 9800X3D | v3 fp32 / int8 | 33.4-35.4 / 30.5-31.4 (int8 is not faster there) |
| same | Cortex-A53 (Orange Pi Zero 3) | int8 | 1.0-1.1 |
| moondream Redux card https://huggingface.co/moondream/parakeet-redux | EPYC 9575F, 8 cores (Zen 5, AVX-512) | sherpa-onnx int8 / onnx-asr int8 / parakeet.cpp q8 | 42 / 28 / 45 |
| same | MacBook Air M2 CPU | sherpa-onnx / onnx-asr int8 | 28 / 33 |
| exporter test https://huggingface.co/gagni/granite-speech-5.0-470m-turboctc-onnx | i7-13700H, 8 threads, ORT 1.23.2 | v3 int8, FLEURS-FR clips, one at a time | 16.9 (10 s clip: 498 ms) |
| this research (chapter G) | 4-vCPU Xeon 2.1 GHz VM (AVX-512 VNNI, AMX) | v3 int8 encoder + mel only, 30 s chunk | 8-9 |

Timestamps: word and segment level via token times as above. Punctuation and capitalisation: **yes, native**
("Punctuations and Capitalizations included"). Long audio: model card says "up to 24 minutes with full attention
(A100 80GB) or up to 3 hours with local attention"; HF config `max_position_embeddings: 5000` frames (400 s). On
CPU keep chunks <= 60 s (attention memory is quadratic: RSS 1.4 GB at 10 s, 1.7 GB at 30 s, 2.3 GB at 60 s).
Weaknesses: language auto-detect only; `<unk>` (id 0) emitted for characters outside the vocabulary, e.g. `°`,
`+`, some apostrophes (reported and patched in https://huggingface.co/Olicorne/parakeet-tdt-0.6b-v3-ultra-onnx by
setting the joint bias of `<unk>` to -1e4; you can simply mask logit 0 yourself); meeting audio WER ~11 %;
NVIDIA risk note: "If a word is not trained in the language model and not presented in vocabulary, the word is
not likely to be recognized". Silence: output is empty text (the NeMo card states output may be empty for no
speech); VAD still recommended.

#### A.1.6 Verdict for Parakeet v3

Best-documented accuracy and timestamps in the set, tiny decode cost, but its encoder is 24 layers of full-attention
FastConformer and the int8 graph here ran 4x slower than Granite's. Expect 8-15x on the laptop for ASR alone
(unmeasured there): not enough once VAD + diarisation are added unless several chunks are run in parallel
(RAM: one shared session, 2 workers x ~1.7 GB activations).

#### A.1.7 Parakeet Ultra (post-trained v3) and Redux (ternary)

- `moondream/parakeet-ultra`, sha `73175eb7aeb0d82f1e2a6b53b3aabc10a90bcd0b`, `license: cc-by-4.0`, same architecture
  and tokenizer, `model.safetensors` 1255 MB (HF `ParakeetForTDT` format, no ONNX from the author). Self-reported
  (their pipeline, Sept 2026): Open ASR EN 5.80 vs 6.26, FLEURS-25 9.55 vs 11.62, FR 4.32 vs 4.81, DE 3.61 vs 4.13, AMI 9.77 vs 10.86,
  TED-LIUM long-form 1.94 vs 2.71, noise 5.82 vs 6.72. Runs only on moondream's "Photon" runtime officially.
- Community ONNX re-exports (CC-BY-4.0, unverified provenance): `Masterx/parakeet-tdt-0.6b-ultra-onnx`
  (sha `99b09f030a5a6efeaa13cf2cf54592100ce2c3f1`; same graph names as istupakov: `encoder-model.int8.onnx` 652.2 MB,
  `decoder_joint-model.int8.onnx` 18.2 MB, fp16 1239 MB, fp32 + `.data`; the card says graphs are istupakov's with
  weights replaced and v3 initialisers reproduced to max rel. error 1.3e-7);
  `Olicorne/parakeet-tdt-0.6b-v3-ultra-onnx` (sha `b27f93c2238aa35e138996c005a62f1e8430f801`; int8 649 MB, w4a8 383 MB,
  `<unk>` ban, faster attention graph, reports FLEURS-25 greedy macro WER 10.95 % for fp32 and int8 alike).
  Same speed class as v3, so it does not fix the speed problem.
- `moondream/parakeet-redux` (sha `2bf128600aac4b16946f7ed8372e56117fe5e23b`, 178 MB ternary, 113x on EPYC 9575F with their proprietary
  runtime needing AVX-512 VNNI): no ONNX export, not usable with ORT. Listed so nobody wastes time on it.

### A.2 IBM Granite Speech 5.0 470M TurboCTC (new, not in the brief)

- Upstream `ibm-granite/granite-speech-5.0-470m-turboctc`, sha `91dcb0605daf65f88dbb04fef015c423241d941d`
  (2026-11-06), `license: apache-2.0`, multilingual update released 2026-10-21 (English-only first release
  2026-08-25). Languages: English, French, German, Spanish, Portuguese, Italian, Japanese. 473 M parameters,
  `model.safetensors` 946 MB (bf16). Technical report https://arxiv.org/abs/2609.20104 ;
  card https://huggingface.co/ibm-granite/granite-speech-5.0-470m-turboctc ;
  blog https://huggingface.co/blog/ibm-granite/granite-speech-5-0-470m-turboctc .
- Architecture (card): 16 Conformer blocks, hidden 1024, 8 heads, conv kernel 7, **chunked (block) attention with
  block size 128**, self-conditioned CTC from the middle layer, input = 80 log-mel + 80 deltas stacked x2 (320 dims),
  8x temporal subsampling -> **12.5 frames/s (80 ms/frame)**, 16384 multilingual BPE units, greedy CTC, no decoder
  loop, no language prompt. Output has punctuation and capitalisation. Trained on ~153 k h of public data incl.
  AMI (150 h), Earnings-22, VoxPopuli, MLS, CommonVoice-17, YODAS-Granary FR/DE, 2.5 k h synthetic overlapped
  speech and 43 h MUSAN non-speech with empty targets (reduces hallucination on silence; silence yields the
  `<|nospeech|>` token).
- **Licence flag:** the sibling `granite-speech-5.0-470m-turboctc-nc` is CC-BY-NC-SA-4.0. Use only the repo above.
- Accuracy: card says English average 5.51 % over 9 public Open ASR test sets for the multilingual version
  (5.78 % before) and shows multilingual FLEURS/CV/MLS charts (images only; **I could not extract FR/DE numbers**).
  Independent exporter test, FLEURS French (676 clips, 117 min), lower-case no-punctuation WER:
  Granite int8 **7.20 %**, Granite fp32 6.98 %, Parakeet v3 int8 7.02 %; numbers-normalised 6.35 / 6.13 / 6.43
  (the model sometimes writes "quatre-cent-trois" for 403). English: Apache-2.0 model scored 5.00 % aggregate
  on the public Open-ASR sets at release (blog, 2026-08-25, vs ~6.3 for Parakeet v3 under the older metric; not
  strictly comparable).
- ONNX: no official IBM ONNX for the 470M model (IBM published `ibm-granite/granite-speech-5.0-2b-ONNX`, the 2 B
  LLM-based model, q4). Community export (two identical uploads, same LFS oid):
  `gagni/granite-speech-5.0-470m-turboctc-onnx` sha `67e808153220ecd3d8614adab9379c0d17bbe024`
  (created 2026-11-10; Apache-2.0 with `LICENSE` and `NOTICE`) and
  `Nicolassuez/granite-speech-5.0-470m-turboctc-onnx` sha `11102885490765491bd7cb123305e2ea97f36bd8` (2026-11-07).
  Files: `model.int8.onnx` 552,434,296 B (**sha256 `3db95700883af468c8b78c09131ede9302b273e8388c1467a778a02f1d00c632`**,
  verified by my download), `tokenizer.json` 1.7 MB (copy of the original), `export/export_onnx.py`,
  `export/granite_onnx.py` (rebuild scripts, so the team can reproduce it from IBM's weights).
- **I/O contract** (author's README, and confirmed with the `onnx` package on the downloaded file; opset 17):
  input `waveforms` float32 `[1, samples]` at 16 kHz in [-1, 1] (the graph holds the feature extractor: reflect pad
  256, Hann 400 (periodic) in a 512 window, power spectrum, 80 mel bins, `log10(max(mel,1e-10))`, floor at
  `peak - 8.0` dB-style (`logmel_floor_db: 8.0`, relative to the **utterance maximum**), `/4 + 1`, deltas with a
  3-frame window, 2-frame stacking); when `samples` is an odd multiple of 160, append one zero sample.
  Output `token_ids` int64 `[1, frames]` (argmax already inside, one id per 80 ms).
  Decode: collapse repeats, drop id 0 (blank = `<unk>`), map ids through `tokenizer.json` (BPE with `▁` word marker,
  ByteFallback `<0xNN>` pieces, `Fuse`, strip leading space; special ids: 1 `<|nospeech|>`, 3 `<|endoftext|>`, 5 `<|pnc|>` ...
  skip all `<|...|>`), no probabilities are exposed, so no confidence scores unless the export is changed to emit
  logits. Timestamps: token start = frame index x 0.08 s (frame-level alignment only; words get start of their first
  BPE piece, end = start of the next non-repeat token).
- Chunking: normalisation relative to the utterance maximum means chunk each VAD segment on its own (20-30 s
  chunks are safe; HF config `max_position_embeddings: 512` suggests a hard limit near 512 encoder frames =
  41 s, **unverified**; the 300 s run in my test did execute at 40x with RSS 2.7 GB, so it is not a hard
  crash limit in the ONNX export).
- Speed and memory (chapter G): 4-vCPU VM, 30 s chunks, 31-38x; 60 s 25x; 120 s 56x (noisy); RSS 1.4 GB at 30 s;
  load 2.0-2.8 s.
- Risks: community artefact, young model, no word-level timestamps from the model (CTC alignment is coarse and
  peaky), no probability output, German and meeting WER unpublished in text form. Mitigation: self-export from
  IBM's weights with the scripts, and benchmark DE/EN/FR on in-house meetings.

### A.3 NVIDIA Canary

| Model | sha | Licence tag | Params | Languages | ONNX export (istupakov, onnx-asr layout) |
|---|---|---|---|---|---|
| `nvidia/canary-1b-v2` | `d455706339a6b32e1aa40f82c713a482a0c938e2` | `cc-by-4.0` | 979 M (32 enc + 8 dec layers, vocab 16,384) | 25 EU languages, ASR + AST | `istupakov/canary-1b-v2-onnx` sha `5ebc1520cef7b6b318b3526ad17adbfe00bc1bfc`: `encoder-model.int8.onnx` 859.1 MB, `decoder-model.int8.onnx` 170.0 MB, fp32 `encoder-model.onnx` 4.8 MB + `.data` 3284.7 MB, `decoder-model.onnx` 676.3 MB, `vocab.txt` |
| `nvidia/canary-1b-flash` | `2b6e4d2dacb11cc1b1724de31bb48fe68c26c12e` | `cc-by-4.0` | 811 M | EN DE ES FR | `istupakov/canary-1b-flash-onnx` sha `8876c67043cbb9cbcb69995cf3ea005709a51187`: encoder int8 859.4 MB, decoder int8 79.5 MB |
| `nvidia/canary-180m-flash` | `b12ab418510d093e83890178fd0e8b0d0f7918a6` | `cc-by-4.0` | 182 M | EN DE ES FR | `istupakov/canary-180m-flash-onnx` sha `92c2231a4e2b2524277fea759be967d2e6edfc49`: encoder int8 133.7 MB, decoder int8 79.5 MB |

I/O (onnx-asr `NemoConformerAED`): encoder `audio_signal [B,F,T]`, `length` -> `encoder_embeddings`, `encoder_mask`;
decoder `input_ids int64`, `encoder_embeddings`, `encoder_mask`, `decoder_mems` -> `logits`, `decoder_hidden_states`
(autoregressive with KV-like "mems"). Prompt prefix tokens: `[" ", <|startofcontext|>, <|startoftranscript|>,
<|emo:undefined|>, <|en|> (source), <|en|> (target), <|pnc|>, <|noitn|>, <|notimestamp|>, <|nodiarize|>]`; **language
can be forced** by replacing the two language slots (`<|fr|>`, `<|de|>`), unlike Parakeet. Max sequence length 1024.
Canary models "handle input audio smaller than 40 seconds"; longer audio needs chunking (NeMo uses 1 s overlap).

Accuracy: canary-1b-v2 FLEURS-25 8.40 %, CoVoST 8.85 %, MLS-6 7.27 %; Open-ASR EN mean 7.15 (AMI **16.01**,
Earnings22 11.79, GigaSpeech 10.82, LS-clean 2.18, LS-other 3.56, SPGI 2.28, TEDLIUM 4.29, VoxPopuli 6.25), RTFx 749
on GPU (batch 128). canary-1b-flash: MLS DE 4.36 / FR 4.47, MCV EN 6.99 / DE 4.09 / FR 6.15. canary-180m-flash: MLS DE 4.81 / FR 4.75,
MCV EN 9.53 / DE 5.94 / FR 8.19; AMI 14.86, Earnings22 13.33. Timestamps: word/segment (180m: "experimental"; v2 needs
an auxiliary CTC model). Punctuation: yes. Speed on CPU: onnx-asr: Canary-1b-v2 fp32 7.7-8.3x, int8 14.4-14.9x on the
9800X3D; no CPU numbers for flash/180m. Verdict: autoregressive decoder, 40 s window, worse on AMI than Parakeet;
canary-180m-flash int8 (≈134 + 80 MB) is a possible speed reserve if both main candidates fail, but its WER is clearly worse.

### A.4 Whisper family

| Model | sha | Licence | Params | ONNX source | Notes |
|---|---|---|---|---|---|
| `openai/whisper-large-v3-turbo` | `41f01f3fe87f28c78e2fbf8b568835947dd65ed9` | `mit` | 809 M (4 decoder layers) | `onnx-community/whisper-large-v3-turbo` sha `360ebcde2559d60bb474678be3c1de9ef347d01a` (no licence tag on the repo; derived from MIT): encoder_model_int8 644.8 MB (+ fp16 1274 MB, fp32 `.onnx_data` 2548 MB), decoder_model_merged_int8 439.9 MB, `decoder_with_past_model_int8` 426.5 MB; `csukuangfj/sherpa-onnx-whisper-turbo` sha `2ca6ff69fc878651b770880507669577ac41c2ff`: `turbo-encoder.int8.onnx` 674.7 MB, `turbo-decoder.int8.onnx` 361.1 MB, `turbo-tokens.txt` | 128 mel bins, 30 s windows |
| `distil-whisper/distil-large-v3.5` | `728a7691f3ff1d3d971528d3203a6e9559165d41` | `mit` | 756 M | `distil-whisper/distil-large-v3.5-ONNX` sha `c1f7de035d785f54bc80f47a9d1d8f9d58da68b1` (encoder int8 644.8 MB, decoder merged int8 385.3 MB) | **English only** ("newest addition to the Distil-Whisper English family") |
| `bofenghuang/whisper-large-v3-distil-fr-v0.2` | `a9e66d6b03c203f1c2730963bba9e96a80b26175` | `mit` | 756 M | no ONNX; CTranslate2, ggml | French only; per its card table (commented out in the README source) turbo FR short-form WER 12.25 (mcv17) vs 11.10 for v0.2 |
| `primeline/whisper-large-v3-turbo-german` | `9e7012dad9247f07553aac8a99c4d50ad7c3d36d` | `apache-2.0` | 809 M | no ONNX | German only; Tuda-De 6.44 vs 8.30 turbo, MLS 2.07 vs 3.20 |
| `nyrahealth/CrisperWhisper` | `5a513ffa...` | **`cc-by-nc-4.0` (not allowed)** | 1.6 B | - | excluded |

Whisper contract (turbo `generation_config.json` / `added_tokens.json`): prompt = `<|startoftranscript|>`(50258),
language (`<|en|>` 50259, `<|de|>` 50261, `<|fr|>` 50265), task `<|transcribe|>` 50360 (`<|translate|>` 50359),
then `<|notimestamps|>` 50364 or timestamps from `<|0.00|>` 50365 (step 0.02 s up to `<|30.00|>` 51865); `<|endoftext|>`
50257, `<|nospeech|>` 50363, `<|startofprev|>` 50362; features 128 mel, n_fft 400, hop 160, 30 s = 3000 frames
(`preprocessor_config.json`). Language **can be forced** (and should be, to avoid language confusion).
CPU speed: onnx-asr: large-v3-turbo 2.9-3.9x fp32 and 3.9-5.4x int8 on a 9800X3D; distil-v3.5 is ~1.5x faster than
turbo (card), still far below 12x at 4 cores. Accuracy EN (distil card, https://huggingface.co/distil-whisper/distil-large-v3.5):
turbo AMI 16.13, avg 7.25. Known weaknesses: hallucination on silence/music (needs VAD gating and `no_speech`
threshold), repetition loops, 30 s window bookkeeping, numbers/format variance. Verdict: fallback only for a
language Parakeet/Granite handle badly; not a lead.

### A.5 Moonshine

Repo https://github.com/moonshine-ai/moonshine : "Speech to text in English, Spanish, German, Mandarin, Japanese,
Korean, Vietnamese, Tagalog, Ukrainian, and Arabic" (README, checked 2026-11-10): **no French**. Licence: "The models
are MIT by default ... the only exceptions are the legacy non-streaming models for languages other than English,
which stay under the non-commercial Moonshine Community License." English models: `UsefulSensors/moonshine-base`
(`mit`, 61.5 M), `moonshine-ai/moonshine-streaming-{tiny,small,medium}` (2026-01-06); ONNX: `onnx-community/moonshine-base-ONNX`
(encoder int8 20.5 MB, decoder merged int8 42.4 MB). Verdict: **not viable** (no French, German only under a
non-commercial legacy licence or in the streaming line, which I did not evaluate).

### A.6 Other 2025-2026 models checked (one line each)

| Model | Licence | Viable for CPU ONNX at >= 12x? |
|---|---|---|
| `nvidia/nemotron-labs-unified-asr-0.6b` (sha `023ce907267a09a308a5cb69239447cb3d0c1597`, 2026-10-27) | `other` = OpenMDW-1.1 | Possibly: 600 M FastConformer-**RNNT** (not TDT), 25 langs, **language-id prompt** fused into the encoder (also `auto`), offline WER on its own suite 4.68 (EN, 7 cleaned sets) vs v3 4.93, FR 5.53, DE 4.32, 25-lang 9.49 vs 10.55. Same compute class as Parakeet (speed risk), no ONNX export yet (only MLX/CoreML), non-allow-listed licence. Watch list. |
| `nvidia/nemotron-3.5-asr-streaming-0.6b` (sha `ea30d66d...`) | `other` = OpenMDW-1.1 | Streaming, worse offline accuracy (multilingual 7.22 vs 4.61 unified offline); `onnx-community/...-onnx-int4` exists; same compute class. No. |
| `nvidia/NVIDIA-Nemotron-3.5-Transcribe-30B-A4B` (2026-10-29) | OpenMDW-1.1 | 33.7 B parameters (30B-A4B MoE). No. |
| `nvidia/parakeet-unified-en-0.6b` | `other` | English only. No. |
| `moondream/parakeet-ultra` / `-redux` | CC-BY-4.0 | See A.1.7: Ultra yes (same speed as v3), Redux no ONNX. |
| `ibm-granite/granite-speech-5.0-2b` (+ `-ONNX`, q4) | Apache-2.0 | 2.3 B LLM-based AR decoder (EN FR DE ES PT JA). No (speed). |
| `CohereLabs/cohere-transcribe-03-2026` | Apache-2.0 | 2.07 B AED, 14 languages incl. EN/FR/DE; sherpa-onnx docs list a "Cohere Transcribe" page. AR decoder; very unlikely >= 12x on 4 cores. No. |
| `Qwen/Qwen3-ASR-1.7B` | Apache-2.0 | 2.35 B LLM-style decoder. No. |
| `mistralai/Voxtral-Mini-3B-2507`, `Voxtral-Mini-4B-Realtime-2602` | Apache-2.0 | 4.7 B. No. |
| `kyutai/stt-1b-en_fr`, `stt-2.6b-en-trfs` | CC-BY-4.0 | 1 B decoder-only streaming, EN/FR only (no German). No. |
| `FunAudioLLM/SenseVoiceSmall` | `other` | zh/yue/en/ja/ko, no FR/DE. No. |
| Microsoft Phi-4-multimodal | not checked | multi-B LLM. No. |
| Zipformer multilingual (sherpa) | - | none covering EN+FR+DE found in this pass. |
| `microsoft/VibeVoice-ASR`, `Cactus-Compute/whistle`, `Confucius4` | not evaluated | out of scope. |

### A.7 Ranked shortlist (max 3 to benchmark) and download URLs

Rank = expected chance of meeting accuracy + 12x + licence at once. Download with `curl -L`; verify sha256.

**1. Granite Speech 5.0 470M TurboCTC, int8** (community export; self-export recommended for production)
- `https://huggingface.co/gagni/granite-speech-5.0-470m-turboctc-onnx/resolve/67e808153220ecd3d8614adab9379c0d17bbe024/model.int8.onnx` (552.4 MB, sha256 `3db95700883af468c8b78c09131ede9302b273e8388c1467a778a02f1d00c632`)
- `https://huggingface.co/gagni/granite-speech-5.0-470m-turboctc-onnx/resolve/67e808153220ecd3d8614adab9379c0d17bbe024/tokenizer.json` (1.7 MB)
- export scripts: `.../resolve/67e808153220ecd3d8614adab9379c0d17bbe024/export/export_onnx.py`, `.../export/granite_onnx.py`
- original tokenizer/config: `https://huggingface.co/ibm-granite/granite-speech-5.0-470m-turboctc/resolve/91dcb0605daf65f88dbb04fef015c423241d941d/tokenizer.json` and `.../processor_config.json`

**2. Parakeet TDT 0.6B v3, int8**, sherpa-onnx split layout (best for C#: decoder runs once per token)
- `https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/ed5d745623472f97cfc2e6c2916ee248798fc5a8/encoder.int8.onnx` (652.3 MB)
- `.../resolve/ed5d745623472f97cfc2e6c2916ee248798fc5a8/decoder.int8.onnx` (11.8 MB)
- `.../resolve/ed5d745623472f97cfc2e6c2916ee248798fc5a8/joiner.int8.onnx` (6.4 MB)
- `.../resolve/ed5d745623472f97cfc2e6c2916ee248798fc5a8/tokens.txt` (93,939 B)
- mel preprocessor if desired: `https://huggingface.co/istupakov/parakeet-tdt-0.6b-v3-onnx/resolve/8f23f0c03c8761650bdb5b40aaf3e40d2c15f1ce/nemo128.onnx` (139,764 B)
- alternative combined layout: `https://huggingface.co/istupakov/parakeet-tdt-0.6b-v3-onnx/resolve/8f23f0c03c8761650bdb5b40aaf3e40d2c15f1ce/{encoder-model.int8.onnx,decoder_joint-model.int8.onnx,vocab.txt}`

**3. Parakeet Ultra, int8** (accuracy upgrade over #2 if its extra WER gain holds on meetings; community ONNX)
- `https://huggingface.co/Masterx/parakeet-tdt-0.6b-ultra-onnx/resolve/99b09f030a5a6efeaa13cf2cf54592100ce2c3f1/encoder-model.int8.onnx` (652.2 MB)
- `.../decoder_joint-model.int8.onnx` (18.2 MB), `.../vocab.txt`; mel from `nemo128.onnx` above. Decoder_joint layout (use the onnx-asr combined decoding loop, A.1.4).
- Verify weights against `https://huggingface.co/moondream/parakeet-ultra/resolve/73175eb7aeb0d82f1e2a6b53b3aabc10a90bcd0b/model.safetensors` by re-export if the provenance matters.

Not shortlisted but kept as reserves: `istupakov/canary-180m-flash-onnx` (speed), `onnx-community/whisper-large-v3-turbo` (language fallback).

---

## B. Voice activity detection

### B.1 Silero VAD (recommended)

- Repo https://github.com/snakers4/silero-vad , licence **MIT** (LICENSE: "MIT License, Copyright (c) 2020-present Silero Team").
  Latest tag **v6.2.3**, commit `5cd7945676eb32225748052e2e6a0580e4686a08` (tags v6.0 ... v6.2.3 present; HEAD
  `066409f3d9db45a30d21dfcbf9147826ffdd7052`).
- File: `https://raw.githubusercontent.com/snakers4/silero-vad/v6.2.3/src/silero_vad/data/silero_vad.onnx`
  (2,327,524 B; sha256 `1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3`; opset 16; one graph for
  8 kHz and 16 kHz, `If` node switches on `sr`). Pin by commit: replace `v6.2.3` with the commit sha.
- **I/O (read from the graph, and `OnnxWrapper` in `src/silero_vad/utils_vad.py`):**
  `input` float32 `[B, 576]` = **64 samples of context from the previous chunk + 512 new samples** (16 kHz; for 8 kHz
  32 + 256); `state` float32 `[2, B, 128]` (zeros initially, take `stateN` from the previous call);
  `sr` int64 scalar (shape `[]`) = 16000; outputs `output` float32 `[B, 1]` (speech probability) and `stateN`
  `[2, B, 128]`. Context for the first chunk is zeros; after each call keep `x[..., -64:]` as the next context.
  Reset state and context between independent files. Input amplitude: float in [-1, 1].
- **Reference post-processing** (`get_speech_timestamps` defaults): `threshold=0.5`, `neg_threshold = max(threshold-0.15, 0.01)` (= 0.35),
  `min_speech_duration_ms=250`, `min_silence_duration_ms=100`, `speech_pad_ms=30`, `max_speech_duration_s=inf`,
  `min_silence_at_max_speech=98 ms`, `use_max_poss_sil_at_max_speech=True`, window 512 samples. Hysteresis: enter speech
  when `p >= threshold`; remember `temp_end` when `p < neg_threshold`; close the segment when the silence lasts >=
  `min_silence`; drop segments shorter than `min_speech`; when a segment exceeds `max_speech_duration_s` cut at the longest
  silence seen inside it; finally pad each segment by `speech_pad_ms`, and when two segments are less than
  2 x pad apart split the gap in the middle. For meetings use `min_silence_duration_ms` ~300-500 and
  `max_speech_duration_s` = chunk limit of the ASR (20-30 s for Granite, <= 60 s for Parakeet), `speech_pad_ms` ~100-200
  (the reference defaults cut quickly).
- Speed: README: "one audio chunk (30+ ms) takes less than 1 ms on a single CPU thread". Third-party measure (TEN VAD README):
  Silero RTF 0.0127 on a Xeon Gold 6348 (ONNX, single thread) = ~79x; 2 h = ~91 s single-threaded, so split the
  signal into 4-8 parts (1-2 s overlap to warm the state) and process in parallel, or batch.

### B.2 Alternatives

| Option | Licence | Verdict |
|---|---|---|
| TEN VAD (https://github.com/TEN-framework/ten-vad, tag `v1.0-ONNX`) | "Apache License v2.0 with the following additional conditions": you may not Deploy it in a way that competes with Agora's offerings | **Flag**: non-standard licence; claims better precision and lower RTF (0.0086-0.0150) than Silero; ~306 KB library |
| pyannote segmentation-3.0 used as VAD | MIT | already needed for diarisation (C.1); 10 s windows, speech = classes != 0; ~17 ms frames |
| WebRTC VAD | BSD-3 | no ONNX/neural; poor in noisy meeting rooms; not recommended |
| Parakeet-Ultra's built-in VAD head | CC-BY-4.0 | not in the ONNX exports (Masterx: "The VAD head ... is not included") |

---

## C. Speaker diarisation building blocks

### C.1 Segmentation: pyannote/segmentation-3.0 (recommended)

- Upstream `pyannote/segmentation-3.0`, sha `e66f3d3b9eb0873085418a7b813d3b369bf160bb`, `license: mit`; the HF repo is
  **gated "auto"** (a user-information form, approved automatically). The **ONNX re-exports are not gated** and tagged MIT:
  - `onnx-community/pyannote-segmentation-3.0` sha `733a93b6473d019a773298e08cefa686894b1854`, `license: mit`:
    `onnx/model.onnx` 5,986,908 B (sha256 `057ee564753071c0b09b5b611648b50ac188d50846bff5f01e9f7bbf1591ea25`),
    `onnx/model_int8.onnx` 1,542,304 B (`465d0975bf70fbf14fb77c0589a5d346a9c07c2170345f529cf774678446db76`), fp16 3.0 MB.
  - `csukuangfj/sherpa-onnx-pyannote-segmentation-3-0` sha `9403a6902bb58e3d5ae8c7e77c3422de279db2e0` (repo `LICENSE` = MIT,
    Copyright CNRS): `model.onnx` 5,992,913 B (`220ad67ca923bef2fa91f2390c786097bf305bceb5e261d4af67b38e938e1079`), `model.int8.onnx`
    1,540,506 B (`d582f4b4c6b48205de7e0643c57df0df5615a3c176189be3fc461e9d18827b5d`); release tarball
    https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2 (6.96 MB).
  Both re-exports are freely downloadable. Whether the MIT grant on the original is affected by the gating form: the
  repo carries the MIT licence file; get a legal read if the form's data-collection terms matter to you.
- **I/O.** onnx-community: input `input_values` float `[batch, 1, num_samples]` (16 kHz, mono; opset 17), output `logits`
  float `[batch, num_frames, 7]`. Sherpa: input `x [N,1,T]`, output `y [N, floor(...), 7]`; sherpa metadata:
  `window_size=160000` (10 s), `sample_rate=16000`, `num_classes=7`, `num_speakers=3`, `powerset_max_classes=2`,
  `receptive_field_size=991`, `receptive_field_shift=270`. For `T=160000` the output has **589 frames**, frame step
  270 samples = **16.875 ms**, receptive field 991 samples (~62 ms). The output of the model is log-probabilities
  over the 7 **powerset classes** (config `id2label`): 0 NO_SPEAKER, 1-3 single speakers, 4 = {1,2}, 5 = {1,3}, 6 = {2,3};
  take argmax per frame and map to a 3-speaker 0/1 activity matrix (pyannote `Powerset.to_multilabel`). Speaker indices
  are only local to a 10 s window.
- Speed: model is tiny (5.7 M params). sherpa-onnx docs (https://k2-fsa.github.io/sherpa/onnx/speaker-diarization/models.html),
  a 56.9 s 4-speaker file, **1 thread**, hardware not stated: whole pipeline RTF **0.297** (fp32 seg + ERes2Net-base),
  0.241 (int8 seg + ERes2Net-base), **0.119 / 0.110 (fp32 / int8 seg + TitaNet-small)**; Reverb segmentation 0.45 / 0.39 / 0.20 / 0.17.
  Default window shift is 10 % of the window (`pyannote-window-shift-ratio` default 0.1 = 1 s step, "Smaller values mean more
  overlap, higher quality, and slower processing"): set 0.5-1.0 for the 12x budget.
- Newer piece: `pyannote/speaker-diarization-community-1` (`license: cc-by-4.0`, gated, sha `3533c8cf8e369892e6b79ff1bf80f7b0286a54ee`)
  ships `segmentation/pytorch_model.bin` (5.9 MB), `embedding/pytorch_model.bin` (26.6 MB), PLDA files; uses **VBx** clustering
  (pyannote CHANGELOG). No ONNX export was found, so it would be your own conversion; not recommended before benchmarking.
- Alternative: `Revai/reverb-diarization-v1` converted by sherpa (`csukuangfj/sherpa-onnx-reverb-diarization-v1`, model.onnx 9.5 MB,
  int8 2.4 MB): licence for commercial use not verified (the original Reverb licence is non-commercial **(unverified)**), skip.

### C.2 Speaker embeddings (all from the sherpa-onnx `speaker-recongition-models` release)

Base URL: `https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/<file>` (sizes from HTTP HEAD; the release
page itself is not listable from this sandbox).

| Model file | MB | Dim | Training data / language | EER VoxCeleb1-O | Licence | sha256 (downloaded) |
|---|---|---|---|---|---|---|
| `wespeaker_en_voxceleb_CAM++_LM.onnx` | 29.3 | 512 | VoxCeleb2, multilingual celebrity speech (English-dominant) | CAM++ 0.803 (WeSpeaker table, no LM); LM row not isolated | WeSpeaker: "follows the license of its corresponding dataset"; for VoxCeleb "CC BY 4.0" (wespeaker docs) | `e197af7e9d473030cf486b3124149a19bf37014d0e4485e4c70c483b0ec10cb2` |
| `wespeaker_en_voxceleb_resnet34_LM.onnx` | 26.5 | 256 | same | ResNet34 0.867 (no LM), 0.797 with LM, 0.723 with LM + AS-norm | same | `e9848563da86f263117134dfd7ad63c92355b37de492b55e325400c9d9c39012` |
| `wespeaker_en_voxceleb_resnet34.onnx` | 26.5 | 256 | same | 0.867 | same | not downloaded |
| `wespeaker_en_voxceleb_CAM++.onnx` | 29.3 | 512 | same | 0.803 | same | not downloaded |
| `3dspeaker_speech_campplus_sv_en_voxceleb_16k.onnx` | 29.6 | 512 | VoxCeleb (en) | CAM++ 0.65 (3D-Speaker README) | 3D-Speaker repo Apache-2.0 | `357a834f702b80161e5b981182c038e18553c1f2ca752ed6cec2052365d4129b` |
| `3dspeaker_speech_eres2net_sv_en_voxceleb_16k.onnx` | 26.5 | 192 | VoxCeleb (en) | ERes2Net-base 0.84 (large 0.52) | Apache-2.0 | `c59158379255ad66e161679cca6af8d52d51e389e3224ab7d7a7baae295c2db5` |
| `nemo_en_titanet_small.onnx` | 40.3 | 192 | VoxCeleb1/2 + Fisher + Switchboard + LibriSpeech etc. (English-centric) | TitaNet-large 0.66 (HF card); small not isolated | `cc-by-4.0` (HF card of titanet_large; NGC for small) | `ad4a1802485d8b34c722d2a9d04249662f2ece5d28a7a039063ca22f515a789e` |
| `nemo_en_titanet_large.onnx` | 101.4 | 192 | same | 0.66 | CC-BY-4.0 | not downloaded |
| `3dspeaker_speech_campplus_sv_zh-cn_16k-common.onnx` | 28.3 | 192? | **Chinese-only** (200 k Mandarin speakers) | CN-trained | Apache-2.0 | - |
| `3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx` | 28.3 | - | zh + en | - | Apache-2.0 | - |
| `3dspeaker_speech_eres2net_*_zh-cn_*.onnx` (base 39.6 MB, large 116 MB, common 220.6 MB) | | | **Chinese-only** | | | - |
| `wespeaker_zh_cnceleb_resnet34(_LM).onnx` | 26.5 | 256 | **Chinese-only (CNCeleb)** | | | - |

**Flag Chinese-only models (3D-Speaker zh-cn, CNCeleb) as weaker for EN/FR/DE; do not use them.** Licence note for all
VoxCeleb-trained models: the code is Apache-2.0, the weights inherit the dataset terms, and VoxCeleb's audio comes from YouTube;
WeSpeaker states CC BY 4.0 (https://github.com/wenet-e2e/wespeaker/blob/master/docs/pretrained.md), pyannote's copy
`pyannote/wespeaker-voxceleb-resnet34-LM` is tagged `cc-by-4.0` (sha `837717ddb9ff5507820346191109dc79c958d614`). Treat as acceptable but
ask legal to confirm, because of the training-data provenance. Compute: WeSpeaker table: ResNet34 6.63 M params 4.55 GFLOPs,
CAM++ 7.18 M params **1.15 GFLOPs** (per 2 s-class input, as listed), ResNet221 21.3 GFLOPs.

**Input features (all four WeSpeaker/3D-Speaker/NeMo variants differ, read the ONNX metadata):**
- WeSpeaker (`infer_onnx.py`): Kaldi fbank, **80 mel**, frame length 25 ms, shift 10 ms, `dither=0.0`, `window_type='hamming'`,
  `use_energy=False`, waveform **multiplied by 32768** (sherpa metadata `normalize_samples=0`), then **CMN without CVN**
  (subtract the per-utterance mean of every mel bin over time). Input `feats` float `[B,T,80]` -> `embs [B,256|512]`.
- 3D-Speaker: input `x [N,T,80]` -> `embedding`; metadata `normalize_samples=1` (samples in [-1,1]), `feature_normalize_type=global-mean`
  (mean subtraction over time).
- NeMo TitaNet: inputs `audio_signal [B,80,T]`, `length [B]` int64 -> `logits`, `embs [B,192]`; `feature_normalize_type=per_feature`,
  25 ms window, 10 ms stride, hann, 80 mel. Slightly more code, no clear advantage.
- Always L2-normalise embeddings before cosine comparison (sherpa does `rowwise().normalize()`).

### C.3 Clustering as done by sherpa-onnx and pyannote

- sherpa-onnx (`fast-clustering.cc`, `offline-speaker-diarization.*`): per (10 s window, local speaker) embedding on the
  speech frames of that speaker -> L2 normalise -> full cosine-dissimilarity matrix (N x N doubles) -> **fastcluster
  agglomerative, complete linkage**, cut either at `num_clusters` or at distance **threshold 0.5** (default `FastClusteringConfig.threshold = 0.5`,
  smaller = more clusters), then relabel windows, resolve overlapped frames by powerset, merge: `min_duration_on = 0.3 s`
  (drop shorter segments), `min_duration_off = 0.5 s` (merge gaps of the same speaker shorter than that).
  Memory is O(N^2): 10 k embeddings = 400 MB for doubles; use window shift 0.5-1.0 and cap N (e.g. cluster
  on a subsample then assign the rest to nearest centroid).
- pyannote 3.1 pipeline: WeSpeaker ResNet34-LM embeddings + **agglomerative clustering with centroid linkage**, threshold about 0.70 on
  cosine distance **(from memory, unverified: the pipeline config is gated)**; `community-1` uses **VBx** with PLDA, which is
  more robust on long recordings but needs the PLDA transform and a more complex implementation.
- Spectral clustering with eigengap (3D-Speaker/NeMo) exists but is not used by the references above; it is a later optimisation.

### C.4 Recommendation

- **Primary:** pyannote-segmentation-3.0 int8 (1.5 MB), windows of 10 s with 5-10 s step, powerset to 3 local speakers,
  embeddings of >= 0.5-1 s speech per (window, speaker) with **WeSpeaker CAM++ LM** (29.3 MB, 512-d), L2-normalised, complete-linkage
  cosine clustering threshold ~0.5 (tune on your meetings; typical sherpa sweet spot 0.5-0.8 for CAM++/ERes2Net, lower values
  split speakers), `min_duration_on/off` 0.3/0.5 s. Align speaker turns to ASR word timestamps by overlap.
- **Cheaper fallback** (no segmentation model, no overlap handling): Silero speech segments -> slice into 1.5 s windows with 0.75 s hop
  (merge <1 s tails) -> CAM++ embeddings -> same clustering -> smooth labels (median over 3 windows) -> assign ASR tokens by time.
  Cost estimate: 2 h = ~9,600 windows x 1.15 GFLOPs = ~11 TFLOP (about 1-2 minutes at 100-150 GFLOPS effective on 4 cores).
  With ResNet34 (4.55 GFLOPs) it is 4x more; avoid.
- Budget warning again: the documented sherpa defaults (RTF 0.11-0.30 per thread) mean 13-36 min for 2 h; you must set a window shift
  >= 0.5 (and measure on the target laptop) or use the fallback.

---

## D. Punctuation and truecasing

Not needed for the shortlist: Parakeet v3 ("Punctuations and Capitalizations included", NVIDIA card) and Granite TurboCTC (card: "outputs
punctuated and capitalized transcripts"; PER 21-24 on LibriSpeech-PC style sets in the 2B card) both write them. Canary and Whisper do too.
If a model without them is chosen: `1-800-BAD-CODE/punct_cap_seg_47_language` (sha `1b9d51fc7989ebc61e844d407d9dadd08ff4ba28`, `apache-2.0`,
`punct_cap_seg_47lang.onnx` 233 MB + `spe_unigram_64k_lowercase_47lang.model` 1.2 MB; 6-layer transformer, SentencePiece 64 k, predicts punctuation, true-case
and sentence boundaries for 47 languages incl. EN/FR/DE) or `1-800-BAD-CODE/xlm-roberta_punctuation_fullstop_truecase` (`apache-2.0`, `model.onnx` 1,112 MB).
Sources: https://huggingface.co/1-800-BAD-CODE/punct_cap_seg_47_language . German noun capitalisation is a documented strength of CTC/transducer models
trained on cased text (Granite 2B card: Cap-F1 99.51 on German CommonVoice), so a separate truecaser is only a fallback.

---

## E. ffmpeg bundling (Linux x64 LGPL, optional Windows x64)

Repo https://github.com/BtbN/FFmpeg-Builds ("Static Windows (x86_64) and Linux (x86_64) Builds of ffmpeg master and latest release branch").
Variant `lgpl` = "Lacking libraries that are GPL-only. Most prominently libx264 and libx265"; `linux64` requires glibc >= 2.28.

**Retention policy** (README, and `util/prunetags.sh`: `KEEP_LATEST=14`, `KEEP_MONTHLY=24`): "The last build of each month is kept for two
years. The last 14 daily builds are kept. The special 'latest' build floats". So `latest` URLs move, and a **monthly build tag is the pinnable choice
for 24 months**. Each release carries `checksums.sha256`. Tags observed: `autobuild-2026-09-30-13-08` (September monthly),
`autobuild-2026-10-31-12-59` (October monthly), daily ones through `autobuild-2026-11-10-12-39`. Dated asset names embed the git describe string
(`ffmpeg-n8.1.3-27-g8dca1586f4-...`), unlike the `-latest-` aliases.

| Artefact | URL | Size | sha256 |
|---|---|---|---|
| **Linux x64 LGPL static, n8.1.3 (recommended pin)** | `https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-31-12-59/ffmpeg-n8.1.3-27-g8dca1586f4-linux64-lgpl-8.1.tar.xz` | 135,188,284 B | `a64f3171e6ad1f7d358d28890a907ee478c331d1177ec696a93418f79ac3f1f3` (verified by download) |
| Windows x64 LGPL static, n8.1.3 (optional) | `https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-31-12-59/ffmpeg-n8.1.3-27-g8dca1586f4-win64-lgpl-8.1.zip` | 167,778,744 B | `d4c2a42673917b4a97104a3e9f082729a9730796b98664e37c0b7eba6c6c613d` (from `checksums.sha256`) |
| Linux x64 LGPL shared (libav* .so, smaller exes) | same tag, `ffmpeg-n8.1.3-27-g8dca1586f4-linux64-lgpl-shared-8.1.tar.xz` | 65.7 MB (latest alias) | `493e89a48da02b9a7d217b60f7f60c3c917c9bed73459d8a106e5e987da082ef` |
| Floating alias (not pinnable) | `https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-linux64-lgpl-8.1.tar.xz` | 134.8 MB, replaced daily | changes |
| n9.0.2 branch also built | `ffmpeg-n9.0.2-39-g7adfbe40dc-linux64-lgpl-9.0.tar.xz` (same tag) | - | `6046d6731b26f58887e953561551d254a6eec9fbca4100dfeab56e19af8328d5` |
| Master snapshot | `ffmpeg-N-128227-g4811625b5f-linux64-lgpl.tar.xz` | - | `f7ce163eca978dc3ab761e6c288f5f764ec4326d17c926c8d4f75ed95e77ee4b` |

Verification (I ran the Linux binary, `/tmp/research-models/ff`):
- `ffmpeg -version`: `n8.1.3-27-g8dca1586f4-20261031`, configuration contains `--enable-version3`, **no** `--enable-gpl`, no `--enable-nonfree`,
  explicit `--disable-libx264 --disable-libx265 --disable-libfdk-aac`; `LICENSE.txt` in the tarball is the **GNU LGPL v3**. So the binary is LGPL-3.0-or-later
  (note: it links many libraries, e.g. openssl, libsoxr, libopus, lame; keep the tarball's licence text and offer source per LGPL; ship `ffmpeg` only, drop `ffplay`).
- `ffmpeg -decoders` lists the **native `aac` decoder** (LGPL) plus `aac_fixed`, `aac_latm`, `alac`, `flac`, `mp3`/`mp3float`, `opus`, `libopus`, `vorbis`, `pcm_s16le`,
  `wmav2`; `-demuxers` lists `mov,mp4,m4a,3gp,3g2,mj2`, `matroska,webm`, `ogg`, `wav`, `mp3`, `flac`, `asf`. AAC in MP4/M4A is therefore decoded without any GPL/nonfree part
  (patent aspects of AAC are outside the copyright licence question).
- Functional test: 20 min pink noise encoded to AAC 128 kbit/s M4A with the same build, decoded `-ac 1 -ar 16000 -f s16le` in **1.4 s** (~860x real time), so a 2 h file costs ~8-10 s.
- Size: `bin/ffmpeg` is **134 MB** (all-in-one static, includes Vulkan, hardware encoders, etc.). If installer size matters, build a minimal LGPL ffmpeg (`--disable-everything` plus
  the demuxers/decoders/`aresample`) with the same `build.sh` approach, or use the `shared` tarball only for `ffmpeg` + needed libs. Windows exe is also ~130-170 MB zipped.
- Alternatives with permanent URLs: the monthly tags above (2 years); GyanD/codexffmpeg (gyan.dev) builds are GPL for "full" and have an LGPL "shared" variant, not verified here;
  BtbN's `latest` for development only. For long-term pinning copy the verified tarball to your own artifact store.

---

## F. ONNX Runtime

- **Latest stable `Microsoft.ML.OnnxRuntime`: 1.31.1, published 2026-10-28** (NuGet flat container index: ... 1.29.0, 1.30.0 (2026-09-10), 1.31.0 (2026-10-09), 1.31.1; no later stable).
  GitHub tags v1.31.0 and v1.31.1 exist. `Microsoft.ML.OnnxRuntime.Managed` 1.31.1 is the dependency; package licence file `LICENSE` (MIT).
  https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime/1.31.1 . The release notes page was not fetchable here (403); behaviour notes below come from the
  `onnxruntime_session_options_config_keys.h` of tag v1.31.1.
- Session config keys relevant to your case (exact strings, header at tag v1.31.1,
  https://raw.githubusercontent.com/microsoft/onnxruntime/v1.31.1/include/onnxruntime/core/session/onnxruntime_session_options_config_keys.h):

| Key | Meaning / recommendation |
|---|---|
| `session.intra_op.allow_spinning` | "1": threads spin before blocking. Default is "0" only for ORT built with `ORT_CLIENT_PACKAGE_BUILD`, else "1" (the NuGet desktop package is a normal build, so default spinning is **on**). Spinning helps latency but burns CPU when you run several sessions or workers; try "0" when oversubscribed. Same for `session.inter_op.allow_spinning`. `session.force_spinning_stop` stops spinning after a Run. |
| `session.dynamic_block_base` | "specify any positive integer, e.g. 4, to enable": work items are split as N / (threads x base) with shrinking granularity; "helps to reduce the variance of E2E latency and boost performance for some models". Try 4 on the conformer encoders; unmeasured here. |
| `session.x64quantprecision` (`kOrtSessionOptionsAvx2PrecisionMode`) | "x64 SSE4.1/AVX2/AVX512 (with no VNNI) has overflow problem with quantized matrix multiplication with U8S8"; "1" switches to slower, safe U8U8. **Test int8 accuracy on the AVX2 laptop with and without it** (the Parakeet int8 weights are QUInt8 dynamic; Granite is QInt8 per channel). |
| `session.intra_op_thread_affinities` | must list `intra_op_num_threads - 1` affinities; useful to pin to physical cores and avoid SMT siblings. |
| `session.disable_prepacking` / `session.prepack.enable_parallel` | prepacking (weight re-layout) happens at load; the parallel option reduces load time for MatMulNBits-heavy models (4-bit); int8 MatMulInteger graphs gain little. |
| `session.use_device_allocator_for_initializers` | "1": allocate initialisers with malloc rather than the arena (avoids arena over-reservation, less RSS). |
| `session.enable_cpu_mem_arena` | "0" disables the CPU arena (lower peak RSS, slightly slower); also `SessionOptions.EnableCpuMemArena`. |
| `session.optimized_model_filepath` | dump the optimised graph once so later loads skip most graph optimisation; do it per machine class (layout optimisations are CPU specific; use `ORT_ENABLE_EXTENDED` for a portable file). |
| `session.save_external_prepacked_constant_initializers` | save pre-packed weights next to the model so they can be memory-mapped on load. |
| `session.set_denormal_as_zero` | flush-to-zero; can speed float kernels slightly (check accuracy). |
| `mlas.use_lut_gemm`, `mlas.qnbit.force_fp32` | only for 2/4-bit MatMulNBits models. |

- **Threads:** set `IntraOpNumThreads` to the physical core count (4), `InterOpNumThreads = 1`, `ExecutionMode = ORT_SEQUENTIAL`. In my VM test, Granite
  at 3 threads (31.6x) was about as fast as 4 threads (31-38x) and Parakeet 2 threads (10x) matched 4 threads (9x), i.e. these graphs scale poorly beyond 2-3
  threads: **prefer 2 worker threads x 2 intra-op threads over one 8-thread session** (ORT `Run` is thread-safe; share **one** `InferenceSession` so the 0.55-0.65 GB of
  int8 weights are loaded once; activations per Run add 0.8-1.7 GB at 30 s chunks).
- **Model load cost:** from the OS page cache: Parakeet int8 encoder 2.7-4.3 s, Granite int8 2.0-2.8 s (4-vCPU VM; protobuf parse + prepack of a 0.55-0.65 GB single-file model).
  A cold disk adds read time (0.65 GB). Models with external data (`encoder-model.onnx` + 2.4 GB `.data` fp32) are memory-mapped by ORT on supported platforms and avoid the
  protobuf copy, but you do not want fp32 here (RAM). The int8 Parakeet single file is parsed in memory (peak during load ~2x file size transiently) - still inside 4 GB.
- **AVX2 / AVX-VNNI notes:** MLAS selects kernels at run time (AVX2, AVX512, AVX-VNNI, AMX where available). onnx-asr's benchmark shows int8 *not* faster than fp32
  on a Zen 5 desktop (30.5 vs 33.4-35.4x) for Parakeet while it is ~2x faster for Canary (14.4 vs 7.7) and Whisper; Granite's own numbers show int8 2.4x faster than fp32
  (36.5 vs 15.3 on an i7-13700H). So always benchmark int8 vs fp16/fp32 on the actual laptop; fp16 weights are not accelerated by the CPU EP on AVX2 machines.
- Float inputs: keep tensors contiguous and reuse `OrtValue`s (C# `OrtValue.CreateTensorValueFromMemory`) to avoid per-call allocations; for the Parakeet decoder loop
  (tens of thousands of tiny `Run` calls per hour of audio) the per-call overhead (~0.1-0.3 ms) is noticeable: use `IoBinding` or pre-allocated inputs, and run the joiner
  for several time steps with batched frames only if you accept a non-greedy variant.

---

## G. My own measurements (indicative, read the caveats)

Environment: sandbox VM, 4 vCPU, Intel Xeon @ 2.1 GHz (AVX2, AVX-512 F, **AVX-512 VNNI, AVX-VNNI, AMX-int8**), 15 GB RAM, ORT Python 1.31.1 CPU EP, `intra_op_num_threads=N`,
`inter_op_num_threads=1`, best of 4 timed runs after one warm-up, synthetic Gaussian-noise input (the graphs' cost is content-independent except for decoder loops, which
are not measured), 30 s chunk unless stated. A numpy 2048x2048 SGEMM measured ~161 GFLOPS on the same VM. Shared VM: absolute numbers fluctuate +/-30 %.

| Model (file) | What was timed | Threads | Audio | Best time | RTFx | Peak RSS (whole process) |
|---|---|---|---|---|---|---|
| Parakeet v3 int8 (`encoder-model.int8.onnx` + `nemo128.onnx`) | mel (1 thread) + encoder, no decode loop | 4 | 30 s | 3.27-3.31 s | **9.1-9.2** | 1.7 GB |
| same | same | 3 | 30 s | 3.64 s | 8.2 | 1.74 GB |
| same | same | 2 | 30 s | 2.99 s | 10.0 (one noisy run) | 1.74 GB |
| same | same | 4 | 10 s | 0.85 s | 11.7 | 1.43 GB |
| same | same | 4 | 60 s | 6.46 s | 9.3 | 2.31 GB |
| Granite TurboCTC int8 (`model.int8.onnx`) | whole graph (mel + encoder + argmax) | 4 | 30 s | 0.78-0.97 s | **31-38** | 1.42 GB |
| same | same | 3 | 30 s | 0.95 s | 31.6 | 1.42 GB |
| same | same | 4 | 10 s | 0.61 s | 16.4 | 1.39 GB |
| same | same | 4 | 60 s | 2.37 s | 25.3 | 1.80 GB |
| same | same | 4 | 120 s | 2.14 s | 56 (outlier, rerun suspected noisy) | 2.09 GB |
| same | same | 4 | 300 s | 7.56 s | 39.7 | 2.69 GB |

Reading: on this VM Parakeet int8 costs ~3.5-4x more per second of audio than Granite int8. Parakeet's end-to-end figure will be somewhat lower than shown
(decoder loop adds time) and the target laptop is probably slower per core than this Sapphire-Rapids-class VM (AVX2 vs AVX-512/AMX). The published 31x on a
9800X3D and 42x on an EPYC 9575F are for 8 high-clock cores with AVX-512; they should not be extrapolated to a 4-core AVX2 laptop. **Do the real benchmark on the target machine.**

## H. Open questions

1. Granite TurboCTC: German and meeting-room (distant microphone, overlapped speech) WER on in-house audio; numbers are only in images on the model card. The Open ASR leaderboard
   multilingual tab (https://huggingface.co/spaces/hf-audio/open_asr_leaderboard, datasets FLEURS/MLS/CommonVoice) can be queried by the benchmark harness for a like-for-like score.
2. Whether `gagni`/`Nicolassuez` exports will remain available; mirror the file (sha256 above) and keep the export scripts.
3. Language-confusion safeguard for Parakeet on FR/DE spontaneous speech (A.1): needs an in-house test with real meetings.
4. Legal sign-off for VoxCeleb-trained speaker models and for the gated pyannote repo's MIT grant.
5. pyannote clustering thresholds were taken from sherpa defaults (0.5) and from memory (pyannote 3.1); tune on the target data.
6. Real laptop numbers: ORT threads, `allow_spinning`, `dynamic_block_base`, `x64quantprecision` effects were not measurable here.

## Source index (main ones)

- NVIDIA Parakeet v3: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 ; ONNX https://huggingface.co/istupakov/parakeet-tdt-0.6b-v3-onnx ; sherpa https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8
- onnx-asr: https://github.com/istupakov/onnx-asr ; benchmarks https://istupakov.github.io/onnx-asr/benchmarks/
- sherpa-onnx source: https://github.com/k2-fsa/sherpa-onnx/tree/master/sherpa-onnx/csrc ; diarisation docs https://k2-fsa.github.io/sherpa/onnx/speaker-diarization/models.html
- Granite: https://huggingface.co/ibm-granite/granite-speech-5.0-470m-turboctc ; ONNX https://huggingface.co/gagni/granite-speech-5.0-470m-turboctc-onnx
- Parakeet Ultra/Redux: https://huggingface.co/moondream/parakeet-ultra , https://huggingface.co/moondream/parakeet-redux
- Canary: https://huggingface.co/nvidia/canary-1b-v2 , https://huggingface.co/istupakov/canary-1b-v2-onnx
- Whisper: https://huggingface.co/openai/whisper-large-v3-turbo , https://huggingface.co/distil-whisper/distil-large-v3.5
- Moonshine: https://github.com/moonshine-ai/moonshine
- Silero: https://github.com/snakers4/silero-vad ; TEN VAD: https://github.com/TEN-framework/ten-vad
- pyannote ONNX: https://huggingface.co/onnx-community/pyannote-segmentation-3.0 ; WeSpeaker: https://github.com/wenet-e2e/wespeaker ; 3D-Speaker: https://github.com/modelscope/3D-Speaker
- ffmpeg builds: https://github.com/BtbN/FFmpeg-Builds
- ONNX Runtime: https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime ; config keys header at tag v1.31.1
