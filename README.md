# meeting-audio-transcriptor

Transcribes recorded meetings in English, French and German entirely offline, writing `.vtt`, `.json` and `.txt` files. One engine serves a command-line tool and a basic desktop window, on Windows x64 and Linux x64, and runs ONNX models on the CPU.

Status: under construction. The solution builds and its tests pass, and no transcription exists yet.

## Build and test

```
dotnet build -warnaserror
dotnet build src/MeetingAudioTranscriptor.Shell -r win-x64 -warnaserror
dotnet test --filter "Tier!=deep&Tier!=integration&Tier!=models"
```

The second command builds the Windows desktop host; the first builds the Linux one on Linux. See `docs/TESTING.md` for the test tiers.

## License

MIT, see `LICENSE`.
