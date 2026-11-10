#!/bin/bash
# THROWAWAY RESEARCH SPIKE. Two extra repetitions (r3, r4) of the 4x1 spin / no-spin timings of A, B and C because the VM is
# noisy. Sequential. Run: bash extra_reps.sh > extra.log 2>&1
cd "$(dirname "$0")"; source /home/user/spike-venv/bin/activate
for c in C A B; do for r in 3 4; do
  python run.py --cand $c --set en-10min --workers 4 --intra 1 --chunk 28 --tag sp-$c-4x1-c28-r$r
  python run.py --cand $c --set en-10min --workers 4 --intra 1 --chunk 28 --nospin --tag sp-$c-4x1-c28-nospin-r$r
done; done
echo EXTRADONE
