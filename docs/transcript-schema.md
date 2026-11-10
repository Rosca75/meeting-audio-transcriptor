# The JSON transcript

The `.json` transcript is the one file that carries everything a run produced, the cleaned text and the recogniser's raw text of every segment. The formal description is `docs/transcript.schema.json` (JSON Schema draft 2020-12); this file explains it.

## 1. Rules

- The file is UTF-8 without a byte order mark, indented with two spaces, with `\n` line endings and a final newline. Accented characters are written literally.
- Keys appear in the order of the tables below. A key that may be absent is omitted, and a JSON `null` is never written.
- Every time is in seconds from the start of the recording. Times, durations and confidences are rounded to three decimals.
- Segments are in time order and numbered from 1. A segment whose cleaned `text` is empty was removed whole by cleaning: `removed` then names the reason, and `rawText` still holds what was recognised. A cleaning that keeps part of a segment leaves `removed` absent.
- In `words`, a word with `removed: true` is not in the cleaned `text`; the key is absent for a word cleaning kept.
- A segment's `speaker` is the `id` of an entry of `speakers`. When speaker separation was off, `speakers` is empty and no segment has a `speaker`.
- A reader ignores keys it does not know, and `schemaVersion` is raised when a field changes meaning.

## 2. Fields

### Root

| Key | Type | Notes |
|---|---|---|
| `schemaVersion` | string | the shape's version |
| `metadata` | object | the facts about the run |
| `speakers` | array | the speakers told apart; empty when separation was off |
| `segments` | array | the segments in time order; empty for a recording with no speech |

### `metadata`

| Key | Type | Notes |
|---|---|---|
| `application` | string | the application's name |
| `applicationVersion` | string | its version |
| `sourceFile` | string | the recording's file name, without its folder |
| `durationSeconds` | number | the recording's length, seconds |
| `speechSeconds` | number | seconds in which speech was detected |
| `language` | string | `en`, `fr` or `de`, as the user chose |
| `createdUtc` | string | when the run finished, UTC, ISO 8601 |
| `processingSeconds` | number | how long the run took, decoding and writing included |
| `models` | array | `{ role, name }` per model: `recognition`, `voice-activity` or `speaker-embedding`, and the name the models lock lists |
| `settings` | object | see below |

### `metadata.settings`

| Key | Type | Notes |
|---|---|---|
| `speakerSeparation` | boolean | whether speakers were told apart |
| `speakerCountHint` | integer | the speaker count the user stated; absent when none was |
| `threads` | integer | processor threads used |
| `formats` | array of string | the formats written: `vtt`, `json`, `txt` |
| `rawFiles` | boolean | whether raw transcript files were written beside the cleaned ones |

### `speakers[]`

| Key | Type | Notes |
|---|---|---|
| `id` | string | what segments refer to, such as `S1` |
| `label` | string | what the text formats show, such as `Speaker 1` |
| `speakingSeconds` | number | the seconds the speaker spoke, summed over their segments |

### `segments[]`

| Key | Type | Notes |
|---|---|---|
| `id` | integer | from 1, in time order |
| `start`, `end` | number | seconds |
| `speaker` | string | a speaker `id`; absent when separation was off |
| `text` | string | the cleaned text; empty when cleaning removed the segment |
| `rawText` | string | the text as the recogniser wrote it |
| `removed` | string | why cleaning removed the whole segment, such as `backchannel`; absent otherwise |
| `confidence` | number | mean of the word confidences, 0 to 1 |
| `words` | array | the raw words with their times; absent when the recogniser gives none |

### `segments[].words[]`

| Key | Type | Notes |
|---|---|---|
| `text` | string | the word as recognised |
| `start`, `end` | number | seconds |
| `confidence` | number | the recogniser's probability, 0 to 1 |
| `removed` | boolean | `true` when cleaning removed the word from `text`; absent otherwise |

## 3. Example

```json
{
  "schemaVersion": "1",
  "metadata": {
    "application": "meeting-audio-transcriptor",
    "applicationVersion": "0.1.0",
    "sourceFile": "réunion.wav",
    "durationSeconds": 12.4,
    "speechSeconds": 6.1,
    "language": "fr",
    "createdUtc": "2026-11-10T18:30:00Z",
    "processingSeconds": 3.2,
    "models": [
      { "role": "recognition", "name": "whisper-small" }
    ],
    "settings": {
      "speakerSeparation": true,
      "threads": 4,
      "formats": ["vtt", "json", "txt"],
      "rawFiles": true
    }
  },
  "speakers": [
    { "id": "S1", "label": "Speaker 1", "speakingSeconds": 3.1 },
    { "id": "S2", "label": "Speaker 2", "speakingSeconds": 0.4 }
  ],
  "segments": [
    {
      "id": 1,
      "start": 0.5,
      "end": 3.6,
      "speaker": "S1",
      "text": "Bonjour à tous.",
      "rawText": "euh bonjour à tous",
      "confidence": 0.93,
      "words": [
        { "text": "euh", "start": 0.5, "end": 0.9, "confidence": 0.71, "removed": true },
        { "text": "bonjour", "start": 1.0, "end": 1.6, "confidence": 0.97 }
      ]
    },
    {
      "id": 2,
      "start": 3.7,
      "end": 4.1,
      "speaker": "S2",
      "text": "",
      "rawText": "Mm-hmm.",
      "removed": "backchannel",
      "confidence": 0.88
    }
  ]
}
```
