# THROWAWAY RESEARCH SPIKE. Runs the speed matrix on bench/data/long/en-10min.wav, one run (own process) at a time,
# each repeated twice, then the chunk-length accuracy runs on the three 10-min files. Each run waits for an idle CPU
# (run.py) so only one benchmark process ever exists.
# Run: python sweep.py > sweep.log 2>&1      (venv active; results land in runs/*.json)
import json, os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
H = "/home/user/meeting-audio-transcriptor/bench/results/2026-11-10-phase1-hypotheses"
CONF = {"1x4": (1, 4), "4x1": (4, 1), "2x2": (2, 2), "8x1": (8, 1)}


def run(cand, setname, conf, chunk, tag, nospin=False, hyp=False):
    w, i = CONF[conf]
    cmd = [sys.executable, "run.py", "--cand", cand, "--set", setname, "--workers", str(w), "--intra", str(i),
           "--chunk", str(chunk), "--tag", tag] + (["--nospin"] if nospin else []) + (["--hyp", H] if hyp else [])
    subprocess.run(cmd, cwd=HERE, check=False)
    p = f"{HERE}/runs/{tag}.json"
    return json.load(open(p))["rtfx"] if os.path.exists(p) else 0.0


def two(cand, conf, chunk=28, nospin=False, suffix=""):
    base = f"sp-{cand}-{conf}-c{chunk}{'-nospin' if nospin else ''}{suffix}"
    r = [run(cand, "en-10min", conf, chunk, f"{base}-r{k}", nospin) for k in (1, 2)]
    return sum(r) / 2


def matrix(cand, chunks):
    sc = {c: two(cand, c) for c in ("1x4", "4x1", "2x2")}
    best = max(sc, key=sc.get); print("BEST", cand, best, sc, flush=True)
    two(cand, best, nospin=True)
    for ch in chunks: two(cand, best, chunk=ch)
    two(cand, "8x1")
    return best


for cand, chunks in (("C", (10, 20, 40)), ("A", (10, 20)), ("B", ())):
    best = matrix(cand, chunks)
    if cand == "A": bestA = best
run("D", "en-10min", bestA, 28, "sp-D-single-r1")
run("E", "en-10min", "1x4", 28, "sp-E-1x4-r1")
# chunk-length accuracy on the three 10-min files (28 s is the main accuracy run)
for ch in (10, 20, 40): run("C", "long", "4x1", ch, f"chunk-C-c{ch}", hyp=True)
for ch in (10, 20): run("A", "long", "4x1", ch, f"chunk-A-c{ch}", hyp=True)
print("SWEEPDONE")
