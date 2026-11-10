#!/bin/bash
# THROWAWAY RESEARCH SPIKE. Runs the accuracy + noise passes for every candidate, strictly one after the other.
# Run: bash accuracy_all.sh > accuracy.log 2>&1
cd "$(dirname "$0")"; source /home/user/spike-venv/bin/activate
H=/home/user/meeting-audio-transcriptor/bench/results/2026-11-10-phase1-hypotheses
for c in C A D B; do
  python run.py --cand $c --set clips,long --workers 4 --intra 1 --chunk 28 --tag acc-$c --hyp $H --hypname $c
  python run.py --cand $c --set noise --workers 4 --intra 1 --chunk 28 --tag noise-$c --hyp $H --hypname $c
done
python run.py --cand E --set noise --workers 1 --intra 4 --chunk 28 --tag noise-E --hyp $H --hypname E
python run.py --cand E --set clips,long --workers 1 --intra 4 --chunk 28 --tag acc-E --hyp $H --hypname E
echo ALLDONE
