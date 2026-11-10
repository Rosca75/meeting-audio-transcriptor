# THROWAWAY RESEARCH SPIKE. Which half of candidate A drops words: mixes int8/fp32 encoder with int8/fp32 decoder_joint
# on the chunks of one file. Run: python diag_a2.py <file-id>
import sys, asrlib as L
fid = sys.argv[1]; e = [x for x in L.manifest() if x["id"] == fid][0]
chunks = L.chunk_audio(L.load_wav(f"{L.ROOT}/{e['path']}"), 28)
P = f"{L.CACHE}/parakeet-tdt-0.6b-v3"
for enc, dec in (("encoder-model.int8.onnx", "decoder_joint-model.onnx"), ("encoder-model.onnx", "decoder_joint-model.int8.onnx")):
    m = L.Parakeet(P, enc, dec, 4, True, L.Timer())
    print(enc, "+", dec, [len(m.transcribe(c, None).split()) for c in chunks], flush=True)
