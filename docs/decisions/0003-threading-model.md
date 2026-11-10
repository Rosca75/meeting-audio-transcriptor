# 0003. Threading: parallel chunk workers over one shared model

## Context

Recognition is the dominant cost. ONNX Runtime can spread one inference over
several threads (intra-op), or several inferences can run at once.

## Options

1. One worker, one session with 4 intra-op threads.
2. Two workers, one session with 2 intra-op threads.
3. Four workers sharing one session created with 1 intra-op thread.
4. Batching several chunks into one inference.

## Evidence

Python spike, 10-minute file, times real time (two runs each, shared machine):

| Arrangement | Granite int8 | Parakeet v3 int8 |
|---|---|---|
| 1 worker x 4 threads | 38 to 84 | 14.4 to 14.6 |
| 2 workers x 2 threads | 83 to 88 | 18.1 to 18.4 |
| 4 workers x 1 thread | 95 to 126 | 22 to 28 |
| 8 workers x 1 thread (oversubscribed) | 97 to 125 | 26 |

Thread spinning on or off made no measurable difference with one thread per
worker. Chunk length between 10 and 28 seconds did not change throughput
beyond noise. Batching was not pursued: chunks have different lengths, padding
wastes work, and option 3 already uses every core.

## Decision

Option 3. One `InferenceSession` per model, created with one intra-op thread
and spinning off, shared by N recognition workers; N defaults to the smaller of
the logical processor count and 4. One more thread produces chunks (decode,
resample, voice activity). The weights are loaded once, so memory does not grow
with N.

## Consequences

- A hyper-thread adds little, so the default does not go above 4; `--threads`
  overrides it.
- The pipeline is a bounded producer/consumer queue: the producer blocks when
  the workers fall behind, which keeps memory flat for any recording length.
