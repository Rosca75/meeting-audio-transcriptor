# THROWAWAY RESEARCH SPIKE. Inspects the tail of Granite's ONNX graph, lists quantisation ops, adds the tensor that feeds
# the final ArgMax as an extra graph output (written to /tmp), runs it with onnxruntime and reports frame-level
# score statistics for confidence use. Run (venv active, nothing else running): python granite_graph.py > granite_graph.txt
import collections, numpy as np, onnx, onnxruntime as ort
from onnx import helper, TensorProto
import asrlib as L
M = f"{L.CACHE}/granite-470m-turboctc/model.int8.onnx"
m = onnx.load(M); g = m.graph
print("ir_version", m.ir_version, "opsets", [(o.domain or "ai.onnx", o.version) for o in m.opset_import])
print("n_nodes", len(g.node), "inputs", [i.name for i in g.input], "outputs", [o.name for o in g.output])
ops = collections.Counter(n.op_type for n in g.node)
print("op counts (top 25)", ops.most_common(25))
for k in ("MatMulInteger", "ConvInteger", "DynamicQuantizeLinear", "QLinearMatMul", "MatMul", "DequantizeLinear", "QuantizeLinear"):
    print(k, ops.get(k, 0))
print("LAST 6 NODES")
for n in g.node[-6:]: print(" ", n.op_type, n.name, "in", list(n.input), "out", list(n.output), [(a.name, a.i if a.type == 2 else '') for a in n.attribute])
am = [n for n in g.node if n.op_type == "ArgMax"][-1]
feed_name = am.input[0]
prod = {o: n for n in g.node for o in n.output}
print("ArgMax", am.name, "input", feed_name, "attrs", [(a.name, a.i) for a in am.attribute])
n = prod[feed_name]; print("producer of ArgMax input:", n.op_type, n.name, list(n.input))
chain = []
cur = n
for _ in range(4):
    chain.append((cur.op_type, cur.name, list(cur.input), list(cur.output)))
    nxt = [prod[i] for i in cur.input if i in prod]
    if not nxt: break
    cur = nxt[0]
print("producer chain", *chain, sep="\n  ")
if "Softmax" in [c[0] for c in chain[:2]] or "LogSoftmax" in [c[0] for c in chain[:2]]: print("NOTE: softmax-type node directly before ArgMax")
g.output.append(helper.make_tensor_value_info(feed_name, TensorProto.FLOAT, None))
out = "/tmp/granite-with-scores.onnx"; onnx.save(m, out); print("saved", out)
s = ort.InferenceSession(out, ort.SessionOptions())
def stats(a, label):
    ids, sc = s.run(["token_ids", feed_name], {"waveforms": a[None]})
    sc = sc[0]; ids = ids[0]
    print(label, "token_ids", ids.shape, "score tensor", sc.shape, sc.dtype)
    lse = np.log(np.exp(sc - sc.max(-1, keepdims=True)).sum(-1)) + sc.max(-1)
    print("  logsumexp over vocab: mean %.3f min %.3f max %.3f (0 means already log-softmax)" % (lse.mean(), lse.min(), lse.max()))
    p = np.exp(sc - lse[:, None]); mx = p.max(-1)
    keep = np.concatenate([[True], ids[1:] != ids[:-1]]) & (ids != 0)
    print("  argmax==scores.argmax:", bool((sc.argmax(-1) == ids).all()), "| max-softmax all frames mean %.3f" % mx.mean(),
          "| emitted-token frames n=%d mean %.3f p10 %.3f min %.3f" % (keep.sum(), mx[keep].mean() if keep.any() else float('nan'),
                                                                      np.percentile(mx[keep], 10) if keep.any() else float('nan'), mx[keep].min() if keep.any() else float('nan')),
          "| blank frames mean %.3f" % mx[ids == 0].mean())
    return mx[keep]
c = lambda p, s0=0, d=28: L.load_wav(p)[int(s0 * 16000):int((s0 + d) * 16000)]
stats(c(f"{L.ROOT}/clips/en/en-single-01.wav"), "en read speech 28s")
stats(c(f"{L.ROOT}/clips/en/en-ami-01.wav"), "AMI headset 28s")
stats(c(f"{L.ROOT}/clips/en/en-ami-sdm-01.wav"), "AMI distant mic 28s")
stats(c(f"{L.ROOT}/noise/noise-office-60s.wav"), "office noise 28s")
stats(c(f"{L.ROOT}/noise/silence-60s.wav"), "silence 28s")
