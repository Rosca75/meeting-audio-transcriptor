# Phase 1 candidate spike (throwaway)

Python + onnxruntime 1.31.1 comparison of speech-recognition candidates (speed and WER). Not production code and not needed to build or ship the application; the final
engine is C#. The scripts, `runs/` and the tables are kept because they back the report; console logs and bytecode caches are ignored (`.gitignore`). Report: `docs/build/phase1-candidates.md`. Results: `bench/results/2026-11-10-phase1-candidates.json`,
hypotheses in `bench/results/2026-11-10-phase1-hypotheses/<candidate>/<file-id>.txt` (`<candidate>/noise-*` and
`silence-60s` are the noise runs).

| File | Role |
|---|---|
| `wer.py` | the one WER normalisation and scorer (documented at its top) |
| `asrlib.py` | chunker, Parakeet greedy TDT loop, Granite CTC runner, Whisper (sherpa-onnx) wrapper |
| `run.py` | one benchmark process: candidate, file set, workers x intra-op threads, chunk length; writes `runs/<tag>.json` |
| `accuracy_all.sh`, `rest.sh`, `sweep.py`, `extra_reps.sh` | the strictly sequential drivers that were run (accuracy + noise, speed matrix, chunk-length accuracy, extra repetitions) |
| `granite_graph.py` | graph tail, quantisation ops, scores-as-output experiment (output in `granite_graph.txt`) |
| `diag_a.py`, `diag_a2.py` | diagnosis of the dropped spans of Parakeet v3 int8 |
| `aggregate.py` | scores all `runs/*.json`, writes the results JSON and `tables.md` |

Run: `source /home/user/spike-venv/bin/activate; python run.py --cand C --set en-10min --workers 4 --intra 1 --tag x`
(`--cand` A Parakeet v3 int8, B Parakeet v3 fp32, C Granite, D Parakeet Ultra int8, E Whisper turbo; `--set` clips, long,
noise or file ids). Nothing else may run while timing. `aggregate.py` rewrites the results JSON from scratch; the
`granite_graph`, `parakeet_int8_dropped_spans` and E load-note keys were added to it by hand afterwards.
