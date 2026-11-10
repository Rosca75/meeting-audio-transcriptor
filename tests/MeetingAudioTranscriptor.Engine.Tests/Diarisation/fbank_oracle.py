# The origin of the reference constants in KaldiFbankTests.cs: Kaldi fbank values computed by
# kaldi-native-fbank (pip install kaldi-native-fbank numpy) for the synthetic signal the C# test
# generates with the same formula. Not run by any suite; rerun it by hand only to regenerate them.
import math
import numpy as np
import kaldi_native_fbank as knf

def signal(n):
    state = 12345
    out = []
    for i in range(n):
        state = (state * 1664525 + 1013904223) % 4294967296
        noise = state / 4294967296.0 * 2.0 - 1.0
        t = i / 16000.0
        v = 0.3 * math.sin(2 * math.pi * 440.0 * t) + 0.2 * math.sin(2 * math.pi * 1234.5 * t) + 0.05 * noise + 0.01
        out.append(np.float32(v))
    return np.array(out, dtype=np.float32)

def fbank(x, window):
    o = knf.FbankOptions()
    o.frame_opts.dither = 0.0
    o.frame_opts.samp_freq = 16000
    o.frame_opts.window_type = window
    o.frame_opts.snip_edges = True
    o.mel_opts.num_bins = 80
    o.mel_opts.low_freq = 20
    o.mel_opts.high_freq = 0
    o.use_energy = False
    f = knf.OnlineFbank(o)
    f.accept_waveform(16000, (x.astype(np.float64) * 32768.0).tolist())
    f.input_finished()
    return np.array([f.get_frame(i) for i in range(f.num_frames_ready)])

x = signal(4000)
for w in ["povey", "hamming"]:
    F = fbank(x, w)
    print(w, F.shape)
    picks = [(0,0),(0,1),(0,10),(0,39),(0,79),(5,3),(5,20),(5,47),(5,60),(10,0),(10,15),(10,33),(10,55),(10,79),(17,7),(17,28),(17,44),(17,71),(22,2),(22,25),(22,50),(22,78),(21,12),(21,66)]
    print(" ".join(f"({r}, {c}, {F[r,c]:.5f}f)" for r,c in picks))
    if w == "hamming":
        cm = F - F.mean(axis=0)
        print("cmn", " ".join(f"({r}, {c}, {cm[r,c]:.5f}f)" for r,c in picks[:8]))
