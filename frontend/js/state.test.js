// state.test.js: unit tests for the store's transitions and selectors, run with
// `node --test "frontend/**/*.test.js"`. They need no DOM and no bridge.
import test from "node:test";
import assert from "node:assert/strict";
import { buildRequest, canStart, createStore, initialState, outputFolder, reduce, startProblem } from "./state.js";

const selection = { path: "/home/me/meetings/standup.m4a", name: "standup.m4a" };

function withFile(state = initialState()) {
  return reduce(state, { type: "fileSelected", selection });
}

function readyInfo(state = initialState()) {
  return reduce(state, { type: "info", info: { version: "1.2.3", extensions: [".wav"], models: { ready: true } } });
}

test("the initial form has English, all formats, speakers on and no file", () => {
  const state = initialState();
  assert.equal(state.language, "en");
  assert.deepEqual(state.formats, { vtt: true, json: true, txt: true });
  assert.equal(state.speakerSeparation, true);
  assert.equal(state.speakerCountText, "");
  assert.equal(state.rawFiles, false);
  assert.equal(state.file, null);
  assert.equal(state.phase, "idle");
});

test("info stores the version and a ready models check", () => {
  const state = readyInfo();
  assert.equal(state.version, "1.2.3");
  assert.deepEqual(state.models, { checked: true, ready: true, message: null });
});

test("missing models disable Start and carry the host's message", () => {
  const state = withFile(
    reduce(initialState(), { type: "info", info: { version: "1", models: { ready: false, message: "Models are missing." } } }),
  );
  assert.equal(canStart(state), false);
  assert.equal(startProblem(state), "Models are missing.");
});

test("picking a file selects it, clears errors and defaults the output folder to its folder", () => {
  const refused = reduce(initialState(), { type: "fileRefused", reason: "bad" });
  const state = reduce(refused, { type: "fileSelected", selection });
  assert.deepEqual(state.file, selection);
  assert.equal(state.fileError, null);
  assert.equal(outputFolder(state), "/home/me/meetings");
});

test("a refused file keeps the previous selection and shows the reason", () => {
  const state = reduce(withFile(), { type: "fileRefused", reason: '"a.pdf" cannot be used' });
  assert.equal(state.file.name, "standup.m4a");
  assert.equal(state.fileError, '"a.pdf" cannot be used');
});

test("a dropped selection carries its note", () => {
  const state = reduce(initialState(), { type: "fileSelected", selection: { ...selection, note: "3 files were dropped." } });
  assert.equal(state.fileNote, "3 files were dropped.");
});

test("a chosen folder overrides the default and survives a new file", () => {
  let state = reduce(withFile(), { type: "folderChosen", path: "/data/out" });
  state = reduce(state, { type: "fileSelected", selection: { path: "/x/y.wav", name: "y.wav" } });
  assert.equal(outputFolder(state), "/data/out");
});

test("a cancelled folder dialog changes nothing", () => {
  const before = withFile();
  assert.equal(reduce(before, { type: "folderChosen", path: null }).chosenFolder, null);
});

test("Start needs a file, a format and a valid speaker count", () => {
  const ready = withFile(readyInfo());
  assert.equal(canStart(ready), true);
  assert.equal(canStart(readyInfo()), false);
  let none = ready;
  for (const format of ["vtt", "json", "txt"]) {
    none = reduce(none, { type: "formatToggled", format, checked: false });
  }
  assert.equal(canStart(none), false);
  assert.match(startProblem(none), /at least one output format/);
  const bad = reduce(ready, { type: "speakerCountChanged", text: "11" });
  assert.equal(canStart(bad), false);
  assert.match(startProblem(bad), /1 to 10/);
});

test("buildRequest follows the form and omits what is automatic", () => {
  const base = withFile(readyInfo());
  assert.deepEqual(buildRequest(base), {
    inputPath: selection.path,
    language: "en",
    formats: ["vtt", "json", "txt"],
    speakerSeparation: true,
    rawFiles: false,
  });
  let state = reduce(base, { type: "languageChanged", language: "de" });
  state = reduce(state, { type: "formatToggled", format: "json", checked: false });
  state = reduce(state, { type: "speakerCountChanged", text: " 4 " });
  state = reduce(state, { type: "rawFilesChanged", value: true });
  state = reduce(state, { type: "folderChosen", path: "/out" });
  assert.deepEqual(buildRequest(state), {
    inputPath: selection.path,
    language: "de",
    formats: ["vtt", "txt"],
    speakerSeparation: true,
    rawFiles: true,
    speakerCount: 4,
    outputFolder: "/out",
  });
});

test("a speaker count is not sent while separation is off", () => {
  let state = reduce(withFile(readyInfo()), { type: "speakerCountChanged", text: "3" });
  state = reduce(state, { type: "separationChanged", value: false });
  assert.equal("speakerCount" in buildRequest(state), false);
  assert.equal(buildRequest(state).speakerSeparation, false);
});

test("start puts the page in the running phase and clears old results", () => {
  const state = reduce(withFile(readyInfo()), { type: "startRequested" });
  assert.equal(state.phase, "running");
  assert.equal(state.outcome, null);
  assert.deepEqual(state.progress, { stage: "", fraction: 0 });
  assert.equal(startProblem(state), "running");
});

test("a rejected start returns to idle with the host's sentence", () => {
  const state = reduce(reduce(withFile(readyInfo()), { type: "startRequested" }), { type: "startRejected", message: "No output format is chosen." });
  assert.equal(state.phase, "idle");
  assert.equal(state.error, "No output format is chosen.");
});

test("a stale rejection after the run finished changes nothing", () => {
  let state = reduce(withFile(readyInfo()), { type: "startRequested" });
  state = reduce(state, { type: "finished", outcome: { kind: "completed", outputFiles: [] } });
  assert.equal(reduce(state, { type: "startRejected", message: "late" }), state);
});

test("progress never decreases and is clamped", () => {
  let state = reduce(withFile(readyInfo()), { type: "startRequested" });
  const seen = [];
  for (const fraction of [0.2, 0.1, 0.5, 1.7, -1, Number.NaN]) {
    state = reduce(state, { type: "progress", progress: { stage: "Recognising", fraction } });
    seen.push(state.progress.fraction);
  }
  assert.deepEqual(seen, [0.2, 0.2, 0.5, 1, 1, 1]);
  assert.equal(state.progress.stage, "Recognising");
});

test("progress outside a run is ignored", () => {
  const state = withFile(readyInfo());
  assert.equal(reduce(state, { type: "progress", progress: { stage: "x", fraction: 0.5 } }), state);
});

test("cancel marks the run as cancelling until the terminal event", () => {
  let state = reduce(withFile(readyInfo()), { type: "startRequested" });
  state = reduce(state, { type: "cancelRequested" });
  assert.equal(state.cancelling, true);
  state = reduce(state, { type: "finished", outcome: { kind: "cancelled", message: "Cancelled." } });
  assert.equal(state.phase, "done");
  assert.equal(state.cancelling, false);
  assert.equal(canStart(state), true);
});

test("a cancel request when idle does nothing", () => {
  const state = withFile(readyInfo());
  assert.equal(reduce(state, { type: "cancelRequested" }), state);
});

for (const kind of ["completed", "noSpeech", "cancelled", "failed"]) {
  test(`finish with kind ${kind} ends the run and keeps the outcome`, () => {
    const running = reduce(withFile(readyInfo()), { type: "startRequested" });
    const outcome = { kind, message: "m", outputFiles: [] };
    const state = reduce(running, { type: "finished", outcome });
    assert.equal(state.phase, "done");
    assert.equal(state.outcome, outcome);
    assert.equal(state.progress.fraction, kind === "completed" ? 1 : 0);
  });
}

test("inputs ignore file events while a run is active", () => {
  const running = reduce(withFile(readyInfo()), { type: "startRequested" });
  assert.equal(reduce(running, { type: "fileSelected", selection: { path: "/z.wav", name: "z.wav" } }), running);
  assert.equal(reduce(running, { type: "fileRefused", reason: "x" }), running);
});

test("a new file after a finished run starts a fresh round", () => {
  let state = reduce(withFile(readyInfo()), { type: "startRequested" });
  state = reduce(state, { type: "finished", outcome: { kind: "completed", outputFiles: [] } });
  state = reduce(state, { type: "fileSelected", selection: { path: "/z.wav", name: "z.wav" } });
  assert.equal(state.phase, "idle");
  assert.equal(state.outcome, null);
});

test("another clears the file and the outcome", () => {
  let state = reduce(withFile(readyInfo()), { type: "startRequested" });
  state = reduce(state, { type: "finished", outcome: { kind: "completed", outputFiles: [] } });
  state = reduce(state, { type: "another" });
  assert.equal(state.file, null);
  assert.equal(state.outcome, null);
  assert.equal(state.phase, "idle");
});

test("host errors and failures set the error line and dismiss clears it", () => {
  let state = reduce(initialState(), { type: "hostError" });
  assert.match(state.error, /could not describe/);
  state = reduce(state, { type: "failure", message: "Boom." });
  assert.equal(state.error, "Boom.");
  assert.equal(reduce(state, { type: "dismissError" }).error, null);
});

test("dragging is shown only while idle", () => {
  assert.equal(reduce(initialState(), { type: "dragging", value: true }).dragging, true);
  const running = reduce(withFile(readyInfo()), { type: "startRequested" });
  assert.equal(reduce(running, { type: "dragging", value: true }).dragging, false);
});

test("reduce never mutates the previous state", () => {
  const before = initialState();
  const frozen = JSON.stringify(before);
  reduce(before, { type: "formatToggled", format: "vtt", checked: false });
  reduce(before, { type: "fileSelected", selection });
  assert.equal(JSON.stringify(before), frozen);
});

test("the store notifies subscribers only when the state changes and supports unsubscribe", () => {
  const store = createStore();
  let calls = 0;
  const off = store.subscribe(() => {
    calls += 1;
  });
  store.dispatch({ type: "unknown" });
  assert.equal(calls, 0);
  store.dispatch({ type: "languageChanged", language: "fr" });
  assert.equal(calls, 1);
  assert.equal(store.getState().language, "fr");
  off();
  store.dispatch({ type: "languageChanged", language: "de" });
  assert.equal(calls, 1);
});
