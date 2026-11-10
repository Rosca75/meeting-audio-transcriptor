# THROWAWAY RESEARCH SPIKE. Scores every run in runs/*.json with wer.py and writes the machine-readable results file
# (../results/2026-11-10-phase1-candidates.json) plus a markdown tables fragment on stdout.
# Run (after accuracy_all.sh and sweep.py finished): python aggregate.py > tables.md
import glob, json, os, re, subprocess, platform
import wer, asrlib as L
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = "/home/user/meeting-audio-transcriptor/bench/results/2026-11-10-phase1-candidates.json"
ents = {e["id"]: e for e in L.manifest()}
ref = {i: open(f"{L.ROOT}/{e['reference']}", encoding="utf-8").read() for i, e in ents.items() if e.get("reference")}
def group(i):
    e = ents[i]
    if e["type"] not in ("clip", "long"): return None
    if e["language"] == "en": return "en-ami" if "ami" in i else "en-read"
    return e["language"]
runs = {os.path.basename(p)[:-5]: json.load(open(p)) for p in sorted(glob.glob(f"{HERE}/runs/*.json"))}

def score_run(r, only=None):
    per = {}; grp = {}
    for i, f in r["files"].items():
        g = group(i)
        if g is None or (only and not only(i)): continue
        s = wer.score(ref[i], f["text"]); per[i] = s; grp.setdefault(g, []).append(s)
    return per, {g: wer.micro(v) for g, v in grp.items()}

def md(x): return f"{x*100:.2f}"
cands = {"A": "Parakeet TDT 0.6B v3 int8", "B": "Parakeet TDT 0.6B v3 fp32", "C": "Granite Speech 5.0 470M TurboCTC int8",
         "D": "Parakeet Ultra int8 (Masterx re-export)", "E": "Whisper large-v3-turbo int8 (sherpa-onnx, language forced)"}
def sha_dir(d, names):
    return {n: subprocess.run(["sha256sum", f"{L.CACHE}/{d}/{n}"], capture_output=True, text=True).stdout.split()[0] for n in names}
files = {"A": ("parakeet-tdt-0.6b-v3", ["encoder-model.int8.onnx", "decoder_joint-model.int8.onnx", "nemo128.onnx", "vocab.txt"]),
         "B": ("parakeet-tdt-0.6b-v3", ["encoder-model.onnx", "encoder-model.onnx.data", "decoder_joint-model.onnx", "nemo128.onnx", "vocab.txt"]),
         "C": ("granite-470m-turboctc", ["model.int8.onnx", "tokenizer.json"]),
         "D": ("parakeet-ultra-int8", ["encoder-model.int8.onnx", "decoder_joint-model.int8.onnx", "vocab.txt"]),
         "E": ("whisper-turbo-sherpa-int8", ["turbo-encoder.int8.onnx", "turbo-decoder.int8.onnx", "turbo-tokens.txt"])}
cpu = [l for l in open("/proc/cpuinfo") if l.startswith("model name")][0].split(":")[1].strip()
flags = [l for l in open("/proc/cpuinfo") if l.startswith("flags")][0].split(":")[1].split()
mem = int([l for l in open("/proc/meminfo") if l.startswith("MemTotal")][0].split()[1]) // 1024
import onnxruntime
res = {"date": "2026-11-10", "machine": {"cpu": cpu, "cores": os.cpu_count(), "ram_mb": mem, "onnxruntime": onnxruntime.__version__,
       "flags": {k: (k in flags) for k in ("avx2", "avx512f", "avx512_vnni", "avx_vnni", "amx_int8")}, "kernel": platform.release()},
       "method": {"chunking": "<=28 s, cut at lowest-energy 20 ms frame in [18,28] s after previous cut (window scales with max chunk: 0.643*max .. max)",
                  "wer": "wer.py: NFKC, lower, non-alnum->space, split; micro-average per group"},
       "candidates": {}}
tables = []
for c, name in cands.items():
    d = {"name": name, "files": {n: {"sha256": h} for n, h in sha_dir(*files[c]).items()}}
    r = runs.get(f"acc-{c}")
    if r:
        per, grp = score_run(r)
        d["wer_per_file"] = per; d["wer_per_group"] = grp
        d["accuracy_run"] = {k: r[k] for k in ("workers", "intra", "chunk_max_s", "wall_s", "audio_s", "rtfx", "unk_count", "n_chunks", "errors")}
        if len(per) < 19: d["accuracy_run"]["note"] = "files scored: %d" % len(per)
    n = runs.get(f"noise-{c}")
    if n: d["noise"] = {i: {"text": f["text"], "words": len(f["text"].split())} for i, f in n["files"].items()}
    d["speed_runs"] = [{k: v for k, v in r2.items() if k != "files"} | {"en10_wer": wer.score(ref["en-10min"], r2["files"]["en-10min"]["text"])}
                       for t, r2 in runs.items() if t.startswith(f"sp-{c}-") and "en-10min" in r2["files"]]
    d["chunk_length_wer_long"] = {}
    for t, r2 in runs.items():
        if t.startswith(f"chunk-{c}-") or t == f"acc-{c}":
            ch = r2["chunk_max_s"]; p, gm = score_run(r2, lambda i: ents[i]["type"] == "long")
            tot = wer.micro(list(p.values())) if p else None
            d["chunk_length_wer_long"][str(int(ch))] = {"per_file": p, "per_group": gm, "all_three": tot}
    res["candidates"][c] = d
json.dump(res, open(OUT, "w"), ensure_ascii=False, indent=1)

# ---- tables
print("## WER (%) per group, 28 s chunks"); print("| cand | en-read | en-ami | fr | de | RTFx(acc run) |\n|---|---|---|---|---|---|")
for c, d in res["candidates"].items():
    g = d.get("wer_per_group")
    if g: print(f"| {c} | " + " | ".join(md(g[k]["wer"]) if k in g else "-" for k in ("en-read", "en-ami", "fr", "de")) + f" | {d['accuracy_run']['rtfx']:.1f} |")
print("\n## Speed runs (en-10min)\n| tag | workers x intra | spin | chunk | wall s | RTFx | load s | peak RSS MB | in-run s | load1 |\n|---|---|---|---|---|---|---|---|---|---|")
for c, d in res["candidates"].items():
    for r in d["speed_runs"]:
        print(f"| {r['tag']} | {r['workers']}x{r['intra']} | {r['allow_spinning']} | {r['chunk_max_s']:g} | {r['wall_s']:.1f} | {r['rtfx']:.1f} | {r['load_s']:.1f} | {r['peak_rss_mb']:.0f} | {({k: round(v) for k, v in r['in_run_seconds'].items()})} | {r['loadavg1_at_start']} |")
print("\n## Chunk length: WER % on the three 10-min files (all three micro / en / fr / de)")
for c, d in res["candidates"].items():
    for ch, v in sorted(d["chunk_length_wer_long"].items(), key=lambda x: int(x[0])):
        pf = v["per_file"]
        print(f"- {c} chunk {ch}: all {md(v['all_three']['wer'])} | " + " | ".join(f"{i} {md(s['wer'])}" for i, s in pf.items()))
print("\n## Noise outputs")
for c, d in res["candidates"].items():
    for i, v in d.get("noise", {}).items(): print(f"- {c} {i}: {v['words']} words: {v['text'][:200]!r}")
# error-type probes
fill = {"um", "uh", "mm", "hmm", "mhm", "uh-huh", "yeah", "okay", "ah", "oh"}
print("\n## Probes")
for c, d in res["candidates"].items():
    r = runs.get(f"acc-{c}")
    if not r: continue
    def toks(i): return wer.normalise(r["files"][i]["text"])
    ami = [i for i in r["files"] if "ami" in i]
    rf = sum(w in fill for i in ami for w in wer.normalise(ref[i])); hf = sum(w in fill for i in ami for w in toks(i))
    dig_r = sum(any(ch.isdigit() for ch in w) for i in r["files"] if group(i) for w in wer.normalise(ref[i]))
    dig_h = sum(any(ch.isdigit() for ch in w) for i in r["files"] if group(i) for w in toks(i))
    en_sw = {"the", "and", "of", "is", "that", "you", "it", "to", "was"}
    drift = {g: sum(w in en_sw for i in r["files"] if group(i) == g for w in toks(i)) for g in ("fr", "de")}
    punct = sum(ch in ".,?!" for i in r["files"] for ch in r["files"][i]["text"]); up = sum(ch.isupper() for i in r["files"] for ch in r["files"][i]["text"])
    print(f"- {c}: AMI filler words ref {rf} vs hyp {hf}; digit-words ref {dig_r} vs hyp {dig_h}; English stopwords in fr/de hyps {drift}; "
          f"punct chars {punct}, upper-case chars {up}; <unk> {r.get('unk_count')}")
