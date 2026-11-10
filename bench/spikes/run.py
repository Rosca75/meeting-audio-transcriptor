# THROWAWAY RESEARCH SPIKE (phase 1 candidate benchmark). Not production code.
# Measures: transcribes a set of audio files with one candidate (A Parakeet int8, B Parakeet fp32,
# C Granite TurboCTC int8, D Parakeet Ultra int8, E Whisper large-v3-turbo via sherpa-onnx), recording hypothesis
# texts, wall time (excluding model load), time inside session.run per stage, model load time and peak RSS.
# Run (venv active):  python run.py --cand A --set clips,long --workers 4 --intra 1 --chunk 28 --tag A
#   --set: comma list of groups (clips, long, noise) or file ids (e.g. en-10min).
#   Workers = Python threads sharing ONE set of sessions (4x1 = --workers 4 --intra 1).
#   Writes runs/<tag>.json and, with --hyp, hypotheses to <hyp-dir>/<tag>/<id>.txt
import argparse, json, os, queue, threading, time
import numpy as np
import asrlib as L

ap = argparse.ArgumentParser()
ap.add_argument("--cand", required=True); ap.add_argument("--set", required=True)
ap.add_argument("--workers", type=int, default=1); ap.add_argument("--intra", type=int, default=4)
ap.add_argument("--chunk", type=float, default=28.0); ap.add_argument("--nospin", action="store_true")
ap.add_argument("--tag", required=True); ap.add_argument("--hyp", default="")
ap.add_argument("--no-wait", action="store_true"); ap.add_argument("--hypname", default="")
a = ap.parse_args()
HERE = os.path.dirname(os.path.abspath(__file__))


def cpu_busy(sec=3.0):
    def rd():
        v = list(map(int, open("/proc/stat").readline().split()[1:])); return sum(v), v[3] + v[4]
    t0, i0 = rd(); time.sleep(sec); t1, i1 = rd(); return 1 - (i1 - i0) / max(1, t1 - t0)


def wait_quiet():
    # waits until the machine is idle (own previous process gone). 1-min loadavg is recorded, not waited on:
    # it still carries the previous benchmark and would add ~2 min per run.
    for _ in range(60):
        b = cpu_busy()
        if b < 0.05: break
    return b


def vmhwm_mb():
    for l in open("/proc/self/status"):
        if l.startswith("VmHWM"): return int(l.split()[1]) / 1024


ents = L.manifest()
sel = []
groups = {"clips": lambda e: e["type"] == "clip", "long": lambda e: e["type"] == "long" and e["id"] != "en-30min",
          "noise": lambda e: e["type"] == "noise"}
want = a.set.split(",")
for w in want:
    if w in groups: sel += [e for e in ents if groups[w](e)]
    else: sel += [e for e in ents if e["id"] == w]

busy = None if a.no_wait else wait_quiet()
load1 = float(open("/proc/loadavg").read().split()[0])
timer = L.Timer()
t0 = time.perf_counter(); model = L.build(a.cand, a.intra, not a.nospin, timer); load_s = time.perf_counter() - t0
rss_load = vmhwm_mb()
# warm-up (untimed): 3 s of low noise, so lazy allocation / first-run costs are not counted
rng = np.random.default_rng(0); w = (rng.standard_normal(48000) * 0.01).astype(np.float32)
t0 = time.perf_counter()
for lang in ({"en", "fr", "de"} if a.cand == "E" else {None}): model.transcribe(w, lang)
warm_s = time.perf_counter() - t0
timer.t.clear()
if hasattr(model, "unk"): model.unk = 0

audio = {e["id"]: L.load_wav(f"{L.ROOT}/{e['path']}") for e in sel}
t_start = time.perf_counter()
items = []
for e in sel:
    for k, c in enumerate(L.chunk_audio(audio[e["id"]], a.chunk)): items.append((e["id"], k, e.get("language"), c))
q = queue.Queue()
for it in items: q.put(it)
res = {}; lock = threading.Lock(); err = []
def worker():
    while True:
        try: fid, k, lang, c = q.get_nowait()
        except queue.Empty: return
        try: txt = model.transcribe(c, lang)
        except Exception as ex:
            err.append(repr(ex)); txt = ""
        with lock: res[(fid, k)] = txt
ths = [threading.Thread(target=worker) for _ in range(a.workers)]
[t.start() for t in ths]; [t.join() for t in ths]
wall = time.perf_counter() - t_start
if err: print("ERRORS", err[:3])

files = {}
for e in sel:
    n = sum(1 for it in items if it[0] == e["id"])
    txt = " ".join(res[(e["id"], k)] for k in range(n) if res[(e["id"], k)])
    files[e["id"]] = {"text": txt, "audio_s": e["durationSeconds"], "chunks": n}
    if a.hyp:
        hn = a.hypname or a.tag; os.makedirs(f"{a.hyp}/{hn}", exist_ok=True)
        open(f"{a.hyp}/{hn}/{e['id']}.txt", "w", encoding="utf-8").write(txt + "\n")
tot_audio = sum(e["durationSeconds"] for e in sel)
out = {"tag": a.tag, "cand": a.cand, "set": a.set, "workers": a.workers, "intra": a.intra, "chunk_max_s": a.chunk,
       "allow_spinning": not a.nospin, "load_s": load_s, "warmup_s": warm_s, "peak_rss_mb": vmhwm_mb(),
       "rss_after_load_mb": rss_load, "wall_s": wall, "audio_s": tot_audio, "rtfx": tot_audio / wall,
       "loadavg1_at_start": load1, "cpu_busy_before": busy, "in_run_seconds": timer.t,
       "unk_count": getattr(model, "unk", None), "n_chunks": len(items), "errors": err[:3], "files": files,
       "date": time.strftime("%Y-%m-%d %H:%M:%S")}
os.makedirs(f"{HERE}/runs", exist_ok=True)
json.dump(out, open(f"{HERE}/runs/{a.tag}.json", "w"), ensure_ascii=False, indent=1)
print(f"{a.tag}: wall {wall:.1f}s audio {tot_audio:.0f}s RTFx {tot_audio/wall:.2f} load {load_s:.1f}s "
      f"rss {out['peak_rss_mb']:.0f}MB in-run {({k: round(v,1) for k,v in timer.t.items()})} load1 {load1}")
