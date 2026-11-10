#!/bin/bash
# THROWAWAY RESEARCH SPIKE. Remaining passes, strictly sequential: Whisper noise rerun, Granite graph inspection, speed sweep.
# Run: bash rest.sh > rest.log 2>&1
cd "$(dirname "$0")"; source /home/user/spike-venv/bin/activate
H=/home/user/meeting-audio-transcriptor/bench/results/2026-11-10-phase1-hypotheses
python run.py --cand E --set noise --workers 1 --intra 4 --chunk 28 --tag noise-E --hyp $H --hypname E
python granite_graph.py > granite_graph.txt 2>&1
python sweep.py
