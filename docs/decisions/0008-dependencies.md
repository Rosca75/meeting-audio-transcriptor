# 0008. Dependencies

## Context

The rule: write the code unless a library would take more than about 5 hours
of coding plus long test sessions to replace.

## Decision

| Dependency | Where | Why it passes the rule |
|---|---|---|
| `Microsoft.ML.OnnxRuntime` | Inference | pre-approved; a tensor runtime is months of work |
| `Microsoft.Web.WebView2` | Shell, Windows host | pre-approved; the reference's host component |
| GTK 3 and WebKitGTK 4.1 (system libraries, P/Invoke) | Shell, Linux host | the operating system's web view; no package |
| Media Foundation (system, raw COM calls) | Media | the operating system's media API |
| ffmpeg (LGPL build, child process) | Media | pre-approved |
| xunit.v3 | tests only | the reference's test framework |
| Node 22 or later | build tools and page tests only | the reference's tooling; no npm package |

Hand-written instead of a library: WAV parsing, resampling, FFT, filterbank
features, CTC token decoding and BPE detokenisation, the voice-activity
wrapper and chunker, clustering, the cleaning rules, the three writers, word
error rate and diarisation error rate scoring, command-line parsing, the
bridge, the protobuf edit of the model graph.

## Consequences

`Directory.Packages.props` lists three packages. A fourth needs a new record.
