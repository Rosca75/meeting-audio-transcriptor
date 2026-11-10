# CLAUDE.md: frontend

The charter of the page. The root `CLAUDE.md` overrides it where they conflict.

## 1. What this folder is

The page of the desktop application: one window, one screen, plain ES modules with no build step, no framework and no npm package. It is a leaf: no project writes into it, and every file is pinned by `frontend-hashes.txt`. The hosts serve it from the embedded copy under a strict `default-src 'self'` policy, so a file that needs the network, an inline script or an inline style cannot work here. The hosts embed the pages, scripts and styles, and leave the tests, the markdown files, `package.json` and the hash file out of the executable.

## 2. Seam to the backend

`js/api.js` is the only module that calls `js/bridge.js`, and `bridge.js` is the only module that knows which host it runs in. The contract with the hosts is `BRIDGE.md`. Every wrapper in `api.js` is async, so a missing bridge is a rejected promise with a readable sentence rather than a synchronous throw on page load.

## 3. Module map

| File | Owns |
|---|---|
| `index.html` | the static markup; no inline script or style |
| `css/tokens.css` | every colour, space and size, light and dark |
| `css/style.css` | layout and components; declares no token |
| `js/main.js` | boot and the DOM event wiring; no logic |
| `js/api.js` | the bridge wrappers, request ids and the event subscription |
| `js/bridge.js` | the transport over WebView2 or WebKitGTK |
| `js/state.js` | the only page state: the store, the reducer and the selectors |
| `js/controller.js` | gestures and host events into actions and calls |
| `js/model.js` | pure helpers: file names, speaker count, paths, formatting |
| `js/view.js` | renders the state into the markup; keeps none |
| `js/copy.js` | every user-visible string |
| `js/*.test.js` | the page suite |

## 4. Discipline rules

- **`state.js` holds the only state.** The view and the controller keep none, because two copies of a flag drift apart and the screen then shows the wrong one.
- **State changes are pure reducer actions.** A flow is then a list of actions a Node test can replay without a DOM or a bridge.
- **The view sets properties from the state and never reads them back.** The one exception is the speaker count field, compared before writing so the caret does not jump.
- **All user-visible text lives in `copy.js`, in English, with no em dash.** One file is reviewed for wording, and a test scans it.
- **The host's sentences are shown as they are.** The page does not rewrite a rejection, because the host knows the cause and the page does not.
- **Nothing leaves the machine.** No `fetch`, no `XMLHttpRequest`, no WebSocket, no remote font or image, no inline script or style; `copy.test.js` checks the markup and every script.
- **A refusal reads the same on both sides.** `copy.refusal` and the host's sentence are identical for the same names, so a repeated refusal is one message.
- **Locked while running.** Every control that changes the request is disabled during a run, and file events are ignored, so the displayed settings are the ones the run uses.

## 5. Layout contract

One centred column, at most 44rem wide, of cards: Recording, Options, Output folder, then the actions, the progress card while running and the summary when done. Focus order follows the visual order. Colours come from the tokens and respect `prefers-color-scheme`; text keeps 4.5 to 1 contrast in both schemes. Errors use `role="alert"` and the progress card is a polite live region.

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
