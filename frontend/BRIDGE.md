# The host to page contract

This file is the contract between the page and the desktop hosts (the Windows host over WebView2 and the Linux host over WebKitGTK). A host engineer needs this file and the C# signatures in `src/MeetingAudioTranscriptor.App/Bridge/`, and nothing else. Every message is a JSON text.

## 1. Transport

The page speaks through `js/bridge.js`, which detects its host.

| | Page to host | Host to page |
|---|---|---|
| WebView2 | `window.chrome.webview.postMessage(string)` | `window.chrome.webview` `message` event, `e.data` is the JSON string (a parsed object is accepted too) |
| WebKitGTK | `window.webkit.messageHandlers.host.postMessage(string)` | the host calls `window.__hostReceive(string)` |

The host passes every string it receives from the page to `Bridge.Receive(string)`, and implements `IHostServices.Emit(string)` as the post to the page. `Receive` returns at once and never throws; replies and events leave through `Emit` from any thread, so `Emit` must be callable from any thread and must not throw.

The host injects these globals before the first navigation. Outside a host (a plain browser) no transport exists and every call rejects with a sentence saying so.

## 2. Envelopes

| Direction | Shape |
|---|---|
| Page to host, call | `{"id": 1, "method": "run.start", "args": [ ... ]}` |
| Host to page, reply | `{"id": 1, "result": ...}` or `{"id": 1, "error": "sentence"}` |
| Host to page, event | `{"event": "run:progress", "payload": ...}` |
| Host to bridge, resolved drop | `{"drop": {"paths": ["/full/path.wav"]}}` |

Ids are integers chosen by the page, one per call. A method with nothing to return replies `"result": null`. A rejection carries one plain sentence the page shows as it is. Property names are camelCase and absent optional properties are omitted, never `null`.

A message that is not JSON, not an object, or neither a call nor a drop produces no reply (there is no id to answer) and the event `host:error` with code `message_unreadable`. A call whose id can be read is always answered.

## 3. Methods

Arguments are positional and the count is exact.

| Method | Arguments | Resolves to | Rejects when |
|---|---|---|---|
| `app.info` | none | `{version, extensions, models: {ready, message?}}` | never |
| `file.pick` | none | `{path, name}` or `null` when cancelled or refused | the dialog fails |
| `files.dropped` | `[names]`, an array of file names | `null` | never |
| `folder.pick` | `initialFolder`, a path or `null` | the chosen path or `null` when cancelled | the dialog fails |
| `folder.open` | `path` | `null` | the folder does not exist or cannot be shown |
| `run.start` | `request` (section 4) | `null` once the run is accepted | a run is active, or the request is invalid |
| `run.cancel` | none | `null` (also when nothing runs) | never |

`app.info.extensions` is `[".m4a", ".mp4", ".wav"]`. When `models.ready` is false the page shows `models.message` and disables Start. `file.pick` refused means the extension is unsupported or the file is gone: the call resolves `null` and the event `file:refused` carries the sentence. An unknown method is rejected with `unknown method <name>`.

`run.start` returns as soon as the run is accepted. Progress and the result arrive as events, and a rejected start emits no event, because a run that never started has no terminal event.

## 4. The run request

```json
{
  "inputPath": "/full/path/meeting.m4a",
  "language": "en",
  "formats": ["vtt", "json", "txt"],
  "speakerSeparation": true,
  "speakerCount": 3,
  "rawFiles": false,
  "outputFolder": "/full/path/out"
}
```

`language` is `en`, `fr` or `de`. `formats` holds at least one of `vtt`, `json`, `txt`. `speakerCount` is 1 to 10 and is omitted for automatic. `outputFolder` is omitted for the input file's folder; when given it is a full path that exists or whose nearest existing ancestor is a folder. The bridge validates all of it and hands `ITranscriber.RunAsync` a normalised copy: lower-case, distinct formats, no speaker count when separation is off, and `OutputFolder` always set. Validation creates nothing; the transcriber creates the folder.

## 5. Events

| Event | Payload | When |
|---|---|---|
| `run:progress` | `{stage, fraction}` | during a run; `fraction` is 0 to 1 and never decreases; at most about ten per second, a stage change and completion always pass |
| `run:finished` | `{kind, message, outputFolder, outputFiles, durationSeconds, processingSeconds, speedFactor, speakers, words}` | exactly once per accepted run, however it ends, and after the last progress event |
| `file:selected` | `{path, name, note?}` | a drop resolved to a supported file |
| `file:refused` | `{reason}` | a pick or a drop named an unsupported or missing file |
| `host:error` | `{code}` | a failure with no waiting call; `message_unreadable`, `internal_error` or `drop_failed`; no content rides on it |

`kind` is `completed`, `noSpeech`, `cancelled` or `failed`. `message` is always a sentence. `outputFiles` are full paths and may be empty. The numbers are zero when unknown.

## 6. Dropped files

The page cannot read a dropped file's path, so the host resolves it natively.

1. On drop the page calls `files.dropped` with the dropped file names. When none has a supported extension the bridge emits `file:refused` at once, naming what was refused and what is supported.
2. On WebView2 the page also calls `window.chrome.webview.postMessageWithAdditionalObjects(JSON.stringify({drop: true}), files)` inside the drop event, before anything is awaited. The host receives the file objects, resolves their full paths and calls `Bridge.Receive` with `{"drop": {"paths": [...]}}`. The `{drop: true}` message is the host's own marker and is ignored if it reaches `Bridge.Receive`.
3. On WebKitGTK the host intercepts the drop natively and calls `Bridge.Receive` with the same resolved message. The page sends nothing extra.
4. The bridge turns the resolved message into `file:selected` or `file:refused`. With several paths it takes the first supported one, and `note` says how many were dropped and which was used. A missing file and an empty list are refused.

Steps 1 and 3 may both name the same unsupported file; the page treats a repeated refusal as one message, so the order of arrival does not matter.

## 7. Rules for changing the contract

A method is added in this order: a constant in `BridgeMethods`, its shapes and a `[JsonSerializable]` entry in `BridgeJsonContext`, a case in `Dispatcher`, a wrapper in `js/api.js`, a section here and a test on each side. Property order in a record is the wire order. A renamed method or event is a new one: the old name stays until the page stops calling it.
