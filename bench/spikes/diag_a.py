# THROWAWAY RESEARCH SPIKE. Diagnoses the deletions of candidate A: per-chunk word counts of int8 vs fp32 Parakeet on one file,
# and whether the int8 output changes between thread settings. Run: python diag_a.py <file-id>
import sys, numpy as np, asrlib as L
fid = sys.argv[1]; e = [x for x in L.manifest() if x["id"] == fid][0]
chunks = L.chunk_audio(L.load_wav(f"{L.ROOT}/{e['path']}"), 28)
for tag, cand, intra in (("A intra4", "A", 4), ("A intra1", "A", 1), ("B intra4", "B", 4)):
    m = L.build(cand, intra, True, L.Timer())
    print(tag, [len(m.transcribe(c, None).split()) for c in chunks], "dur", [round(len(c) / 16000, 1) for c in chunks], flush=True)
