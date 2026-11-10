# 0001. Repository layout and conventions

## Context

The build prompt names the reference repository `doc-anonymiser-for-.NET` as
the model for everything it does not specify. The reference is a Windows-only
application; this one ships a command-line tool and a desktop window on
Windows x64 and Linux x64.

## Options

1. Copy the reference layout unchanged, with a Windows-only shell.
2. Mirror the reference layout and conventions, and change only what a second
   operating system and a second front end force.

## Evidence

`docs/build/reference-digest.md` chapters 1, 2, 12 and 13.

## Decision

Option 2. Mirrored: .NET 10, central package management, warnings as errors,
purpose headers, the `Contracts <- Engine <- Inference <- App <- Shell` graph,
an engine on the framework library alone behind a guard, one project naming
ONNX Runtime, xunit.v3 on Microsoft.Testing.Platform with tier traits, guard
tests, lock files with a dependency-free Node fetch tool, a tag-triggered
release workflow.

Deliberate differences:

- No repository-wide Windows platform attribute: every project targets
  `net10.0` and runs on both operating systems.
- A `Media` project beside `Inference` holds audio file decoding (the ffmpeg
  child process and Media Foundation interop), so the engine keeps receiving
  samples and never a path.
- A `Cli` project beside `Shell`; both sit on `App`, which composes the engine.
- `Shell` is one project holding one desktop host per operating system behind
  `IDesktopHost`; the `ShellHost` build property, derived from the runtime
  identifier, selects which host is compiled, so the Windows host compiles from
  Linux with `-r win-x64`.
- `global.json` rolls forward within the major version (`latestFeature`), so
  any 10.0 SDK at or above the pinned band builds the repository.
- `main` is updated by a fast-forward push at every phase gate; the work
  branch is `claude/admiring-bell-dajief`.

## Consequences

Windows-only code is confined to `Shell/Windows/` and to the Media Foundation
decoder, and is compiled, not executed, by the Linux suites.
