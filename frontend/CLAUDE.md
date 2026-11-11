# CLAUDE.md: frontend

The charter of the page. The root `CLAUDE.md` overrides it where they conflict.

## 1. What this folder is

The page of the desktop application: one window, one screen, plain ES modules with no build step, no framework and no npm package. It is a leaf: no project writes into it, and every file is pinned by `frontend-hashes.txt`. The hosts serve it from the embedded copy under a strict `default-src 'self'` policy, so a file that needs the network, an inline script or an inline style cannot work here. The hosts embed the pages, scripts and styles, and leave the tests, the markdown files, `package.json` and the hash file out of the executable.

## 2. Seam to the backend

`js/api.js` is the only module that calls `js/bridge.js`, and `bridge.js` is the only module that knows which host it runs in. The contract with the hosts is `BRIDGE.md`. Every wrapper in `api.js` is async, so a missing bridge is a rejected promise with a readable sentence rather than a synchronous throw on page load.

## 3. Module map

| File | Owns |
|---|---|
| `index.html` | the static markup of the three views; no inline script or style, no picture |
| `css/tokens.css` | every colour, space and size, light and dark; the colours are the palette's alone |
| `css/style.css` | layout and components; declares no token and no colour |
| `js/main.js` | boot and the DOM event wiring; no logic |
| `js/api.js` | the bridge wrappers, request ids and the event subscription |
| `js/bridge.js` | the transport over WebView2 or WebKitGTK |
| `js/state.js` | the only page state: the store, the reducer and the selectors |
| `js/controller.js` | gestures and host events into actions and calls |
| `js/model.js` | pure helpers: file names, speaker count, speaker names, paths, the cut of a long line, formatting |
| `js/view.js` | renders the state into the markup; keeps none |
| `js/icons.js` | the pictures, built as SVG elements: the flag of each language, the gear, the logo |
| `js/copy.js` | every user-visible string |
| `js/*.test.js` | the page suite |

## 4. Discipline rules

- **`state.js` holds the only state.** The view and the controller keep none, because two copies of a flag drift apart and the screen then shows the wrong one.
- **State changes are pure reducer actions.** A flow is then a list of actions a Node test can replay without a DOM or a bridge.
- **The view sets properties from the state and never reads them back.** The one exception is a text field (the speaker count and the speaker names), compared before writing so the caret does not jump.
- **All user-visible text lives in `copy.js`, in English, with no em dash.** One file is reviewed for wording, and a test scans it.
- **The host's sentences are shown as they are.** The page does not rewrite a rejection, because the host knows the cause and the page does not.
- **Nothing leaves the machine.** No `fetch`, no `XMLHttpRequest`, no WebSocket, no remote font or image, no inline script or style; `copy.test.js` checks the markup and every script.
- **A refusal reads the same on both sides.** `copy.refusal` and the host's sentence are identical for the same names, so a repeated refusal is one message.
- **Locked while running.** Every control that changes the request is disabled during a run, the gear that opens the settings included, and file events are ignored, so the displayed settings are the ones the run uses.
- **Only `tokens.css` names a colour, and only a colour of the palette.** White, black, the oranges, the gray-blues and their tints and shades; `copy.test.js` holds the token file to the list and the stylesheet and the markup to none. The pictures of `icons.js` are the exception: a flag carries its country's colours and the logo its own, as SVG presentation attributes, because a picture is not themed.
- **A picture is built by `icons.js`, never loaded.** The content policy allows no inline style and Windows draws no flag emoji, so each picture is SVG elements made with `createElementNS`; an unknown flag throws, and a test holds the flags to the languages both ways.
- **What the host would reject is stopped before Start.** A speaker name over 40 characters, with a control character or an angle bracket, or equal to another ignoring case, shows one sentence and keeps Start off, as an invalid speaker count does.

## 5. Layout contract

**The page is a frame of the window's height and the document never scrolls.** The header keeps its size and the view under it takes the rest. The hosts open the window at 560 by 440 logical pixels and refuse a smaller one, and every state fits that size: idle, a chosen file with a long path, running, a cancelled or failed run with its sentence, the summary with five files, the settings with ten speaker names and their error. The view's own scrolling, and the file list's inside the summary, are the last resort of content longer than that.

**The header holds the logo, the title, the version and one button.** The button is the gear, which opens the settings, or Done, which leaves them; Escape leaves them too, and the focus follows the swap.

**Exactly one of three views shows.** The main view holds the recording card (the choose button, the drop hint, the chosen file), the output folder card and the run row: Start with the sentence naming the setting that keeps it off, or Cancel with the progress bar and its stage. Start without a recording is disabled with no sentence. The settings panel replaces the main view inside the same window: the language as three flags, the output formats and the raw files on the left, speaker separation, the number of speakers and one name field per speaker of that number, in two columns, on the right. The summary replaces the main view when a run completes. One flag in the state, `settingsOpen`, chooses the settings; a completed outcome chooses the summary.

**A long text is cut, never wrapped, and its whole is the tooltip.** A file name and a folder path are cut in the middle so the end that tells them apart shows; a sentence in the run row is cut at its end.

**A language is its flag.** Each flag is a radio button named by the language (`aria-label` and `title` from `copy.js`); the chosen one wears an orange ring and the focused one a second ring.

Focus order follows the visual order. Colours come from the tokens and respect `prefers-color-scheme`; text keeps 4.5 to 1 contrast on its background in both schemes, and the primary button is black on orange for that reason. Errors use `role="alert"` and the progress is a polite live region.

## 6. Comment rules

A file opens with a comment saying what it is for. A comment states the reason for the code as it is now and names no document or change.

## 7. Testing

```
node --test "frontend/**/*.test.js"
```

The quoted recursive glob is required: a flat glob silently skips the `js/` folder. A test sits beside the module it covers, uses `node:test` and `node:assert/strict`, and needs no DOM. `PageTestsPresentTests` in the guards suite fails when no test file exists. A page edit moves its line in `frontend-hashes.txt` in the same commit:

```
node build/update-frontend-hashes.mjs
```

## 8. Where to look next

The contract with the hosts: `BRIDGE.md`. The bound surface it calls: `src/MeetingAudioTranscriptor.App/Bridge/`. The tiers and guards: `docs/TESTING.md`.
