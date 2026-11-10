# 0011. Deviations from the build requirements not recorded elsewhere

## Context

The requirements name a set of outputs, a packaging model and a reference
repository to mirror. Most departures from them are recorded where they were
decided (0002 for the recogniser and its quality gate, 0004 for the benchmark
machine, 0005 for speaker separation, 0007 for packaging, 0009 for decoding).
This record lists the remaining ones in one place, so a reader comparing the
repository with the requirements finds each with its reason.

## Decision

- **The packages are not trimmed and not compiled ahead of time.** The
  reference trims its portable build. Here both executables share one
  self-contained runtime in one folder, and trimming each would cut its copy of
  the framework differently, so trimming is off for the whole package
  (`build/publish.ps1`). ReadyToRun is off because its cross-compiler packs
  differ per operating system and the Windows package may be built from Linux.
  The cost is a larger package and a slower first start, both small beside the
  models.
- **The JSON names each model by role and name, without its revision.** The
  revision, address and hash of every shipped file are in `build/models.lock`
  and the package carries exactly those files, so a transcript's model names
  identify the build's models through the release that wrote it.
- **The language choice selects the cleaning rules only.** The recogniser is
  multilingual and cannot be told the language (0002), so a file transcribed
  under the wrong language choice gives the same words with another language's
  cleaning rules.
- **Word times have the recogniser's 80 ms frame resolution.** The CTC model
  emits one token per 80 ms frame, so a word's start and end are frame
  boundaries, and a cue or turn timestamp is accurate to about a tenth of a
  second.
- **The German repetition rule has no name guard.** English and French keep a
  repeated capitalised word as a probable name; German capitalises every noun,
  so there a repeated name collapses like any other immediate repetition, and
  a repeated phrase such as `denkt und denkt` loses its first copy.
- **Speaker separation tells at most 10 voices apart** (0005), the same range
  as the speaker-count hint of both front ends.

## Consequences

- A change to any of these is a change to this record in the same commit.
- `README.md` states the user-visible ones (the speaker bound, the name guard).
