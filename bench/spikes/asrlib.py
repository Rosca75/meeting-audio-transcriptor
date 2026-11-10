# THROWAWAY RESEARCH SPIKE (phase 1 candidate benchmark). Not production code.
# Shared pieces: data lookup, energy-based chunker, and the recogniser classes for
# Parakeet TDT (raw onnxruntime, greedy TDT loop), Granite TurboCTC (raw onnxruntime) and
# Whisper (sherpa-onnx). Imported by run.py; no command line of its own.
import json, os, threading, time
import numpy as np
import onnxruntime as ort
import soundfile as sf

ROOT = "/home/user/meeting-audio-transcriptor/bench/data"
CACHE = "/home/user/model-cache"
SR = 16000


def manifest():
    return json.load(open(f"{ROOT}/manifest.json"))["entries"]


def load_wav(path):
    a, sr = sf.read(path, dtype="float32")
    assert sr == SR and a.ndim == 1
    return a


def chunk_audio(a, max_s=28.0, min_frac=18 / 28):
    """Split into chunks <= max_s. Each cut is at the start of the lowest-energy 20 ms frame
    between min_frac*max_s (18 s for 28) and max_s after the previous cut."""
    mx, mn, fr = int(max_s * SR), int(max_s * min_frac * SR), 320
    out, pos = [], 0
    while len(a) - pos > mx:
        lo, hi = pos + mn, pos + mx
        seg = a[lo:hi]
        nfr = len(seg) // fr
        e = (seg[: nfr * fr].reshape(nfr, fr) ** 2).sum(1)
        cut = lo + int(np.argmin(e)) * fr
        out.append(a[pos:cut]); pos = cut
    out.append(a[pos:])
    return out


class Timer:
    """Thread-safe accumulator of seconds spent inside session.run, per stage."""
    def __init__(self):
        self.lock = threading.Lock(); self.t = {}
    def add(self, k, dt):
        with self.lock: self.t[k] = self.t.get(k, 0.0) + dt


def opts(intra, spin=True):
    o = ort.SessionOptions()
    o.intra_op_num_threads = intra; o.inter_op_num_threads = 1
    if not spin: o.add_session_config_entry("session.intra_op.allow_spinning", "0")
    return o


def timed_run(sess, timer, key, names, feed):
    t = time.perf_counter(); r = sess.run(names, feed); timer.add(key, time.perf_counter() - t); return r


# ---------------------------------------------------------------- Parakeet
class Parakeet:
    BLANK = 8192
    def __init__(self, d, enc, dec, intra, spin, timer, mel_dir=f"{CACHE}/parakeet-tdt-0.6b-v3"):
        self.timer = timer
        self.mel = ort.InferenceSession(f"{mel_dir}/nemo128.onnx", opts(1, spin))
        self.enc = ort.InferenceSession(f"{d}/{enc}", opts(intra, spin))
        self.dec = ort.InferenceSession(f"{d}/{dec}", opts(1, spin))
        self.vocab = {}
        for line in open(f"{d}/vocab.txt", encoding="utf-8"):
            tok, i = line.rstrip("\n").rsplit(" ", 1); self.vocab[int(i)] = tok
        self.unk = 0
        self.unk_lock = threading.Lock()
    def transcribe(self, a, lang=None):
        T = self.timer
        f, fl = timed_run(self.mel, T, "mel", ["features", "features_lens"],
                          {"waveforms": a[None], "waveforms_lens": np.array([len(a)], np.int64)})
        e, el = timed_run(self.enc, T, "enc", ["outputs", "encoded_lengths"],
                          {"audio_signal": f, "length": fl})
        encT = np.ascontiguousarray(e[0].T)       # [T',1024]
        n = int(el[0]); s1 = np.zeros((2, 1, 640), np.float32); s2 = np.zeros((2, 1, 640), np.float32)
        tl = np.array([1], np.int32); toks = []; t = 0; emitted = 0
        t0 = time.perf_counter(); inrun = 0.0
        names = ["outputs", "output_states_1", "output_states_2"]
        while t < n:
            prev = toks[-1] if toks else self.BLANK
            r0 = time.perf_counter()
            out, n1, n2 = self.dec.run(names, {"encoder_outputs": encT[t].reshape(1, 1024, 1),
                                               "targets": np.array([[prev]], np.int32), "target_length": tl,
                                               "input_states_1": s1, "input_states_2": s2})
            inrun += time.perf_counter() - r0
            o = out[0, 0, 0]
            tok = int(np.argmax(o[:8193])); step = int(np.argmax(o[8193:8198]))
            if tok != self.BLANK:
                s1, s2 = n1, n2; toks.append(tok); emitted += 1
            if step > 0: t += step; emitted = 0
            elif tok == self.BLANK or emitted == 10: t += 1; emitted = 0
        T.add("dec", inrun); T.add("dec_loop_total", time.perf_counter() - t0)
        parts = []
        for i in toks:
            s = self.vocab[i]
            if i == 0:
                with self.unk_lock: self.unk += 1
            elif s.startswith("<|"): continue
            parts.append(s)
        return "".join(parts).replace("▁", " ").strip()


# ---------------------------------------------------------------- Granite
class Granite:
    def __init__(self, intra, spin, timer, model=f"{CACHE}/granite-470m-turboctc/model.int8.onnx"):
        from tokenizers import Tokenizer
        self.timer = timer
        self.s = ort.InferenceSession(model, opts(intra, spin))
        self.tok = Tokenizer.from_file(f"{CACHE}/granite-470m-turboctc/tokenizer.json")
    def transcribe(self, a, lang=None):
        if len(a) % 160 == 0 and (len(a) // 160) % 2: a = np.append(a, np.float32(0))
        ids = timed_run(self.s, self.timer, "enc", ["token_ids"], {"waveforms": a[None]})[0][0]
        keep = np.concatenate([[True], ids[1:] != ids[:-1]]) & (ids != 0)
        return self.tok.decode(ids[keep].tolist(), skip_special_tokens=True).strip()


# ---------------------------------------------------------------- Whisper (sherpa-onnx)
class Whisper:
    def __init__(self, threads, timer, d=f"{CACHE}/whisper-turbo-sherpa-int8"):
        import sherpa_onnx
        self.timer = timer; self.d = d; self.threads = threads; self.sh = sherpa_onnx; self.rec = {}
    def transcribe(self, a, lang):
        lang = lang or "en"   # noise files have no language: forced English
        if lang not in self.rec:
            d = self.d
            self.rec[lang] = self.sh.OfflineRecognizer.from_whisper(
                encoder=f"{d}/turbo-encoder.int8.onnx", decoder=f"{d}/turbo-decoder.int8.onnx",
                tokens=f"{d}/turbo-tokens.txt", language=lang, task="transcribe", num_threads=self.threads)
        r = self.rec[lang]
        t = time.perf_counter()
        s = r.create_stream(); s.accept_waveform(SR, a); r.decode_stream(s)
        self.timer.add("all", time.perf_counter() - t)
        return s.result.text.strip()


def build(cand, intra, spin, timer):
    P = f"{CACHE}/parakeet-tdt-0.6b-v3"
    if cand == "A": return Parakeet(P, "encoder-model.int8.onnx", "decoder_joint-model.int8.onnx", intra, spin, timer)
    if cand == "B": return Parakeet(P, "encoder-model.onnx", "decoder_joint-model.onnx", intra, spin, timer)
    if cand == "D": return Parakeet(f"{CACHE}/parakeet-ultra-int8", "encoder-model.int8.onnx",
                                    "decoder_joint-model.int8.onnx", intra, spin, timer)
    if cand == "C": return Granite(intra, spin, timer)
    if cand == "E": return Whisper(intra, timer)
    raise SystemExit("unknown candidate " + cand)
