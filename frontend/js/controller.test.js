// controller.test.js: unit tests for the user flows (pick, drop, start, cancel, finish, models
// missing) over a fake api and the real store, run with `node --test "frontend/**/*.test.js"`.
import test from "node:test";
import assert from "node:assert/strict";
import { createController } from "./controller.js";
import { createStore } from "./state.js";

// A fake api that records calls, answers from a script and lets a test fire host events.
function fakeApi(script = {}) {
  const handlers = new Map();
  const calls = [];
  const answer = (name, fallback) => async (...args) => {
    calls.push([name, ...args]);
    const value = script[name] ?? fallback;
    if (value instanceof Error) {
      throw value;
    }
    return typeof value === "function" ? value(...args) : value;
  };
  return {
    calls,
    appInfo: answer("appInfo", { version: "9.9.9", models: { ready: true } }),
    pickFile: answer("pickFile", null),
    pickFolder: answer("pickFolder", null),
    openFolder: answer("openFolder", null),
    startRun: answer("startRun", null),
    cancelRun: answer("cancelRun", null),
    dropFiles: answer("dropFiles", null),
    on: (name, callback) => handlers.set(name, callback),
    emit: (name, payload) => handlers.get(name)(payload),
  };
}

async function ready(script) {
  const api = fakeApi(script);
  const store = createStore();
  const controller = createController({ api, store });
  await controller.init();
  return { api, store, controller };
}

const selection = { path: "/m/a.wav", name: "a.wav" };

test("init reads the info and subscribes to every host event", async () => {
  const { store } = await ready();
  assert.equal(store.getState().version, "9.9.9");
  assert.equal(store.getState().models.ready, true);
});

test("init with the models missing shows the host's message and blocks Start", async () => {
  const { store } = await ready({ appInfo: { version: "1", models: { ready: false, message: "Install the models." } } });
  assert.equal(store.getState().models.ready, false);
  assert.equal(store.getState().models.message, "Install the models.");
});

test("init without a bridge blocks Start with the readable sentence", async () => {
  const { store } = await ready({ appInfo: new Error("The page cannot reach the application.") });
  assert.equal(store.getState().models.ready, false);
  assert.match(store.getState().models.message, /cannot reach/);
});

test("pick selects the chosen file and a cancelled dialog changes nothing", async () => {
  const picked = await ready({ pickFile: selection });
  await picked.controller.pick();
  assert.equal(picked.store.getState().file.name, "a.wav");
  const cancelled = await ready({ pickFile: null });
  await cancelled.controller.pick();
  assert.equal(cancelled.store.getState().file, null);
});

test("a failing pick shows the error line", async () => {
  const { store, controller } = await ready({ pickFile: new Error("The file dialog could not be opened.") });
  await controller.pick();
  assert.equal(store.getState().error, "The file dialog could not be opened.");
});

test("the host's file events update the selection and the refusal", async () => {
  const { api, store } = await ready();
  api.emit("file:selected", { path: "/m/b.mp4", name: "b.mp4", note: "2 files were dropped." });
  assert.equal(store.getState().file.name, "b.mp4");
  assert.equal(store.getState().fileNote, "2 files were dropped.");
  api.emit("file:refused", { reason: "refused" });
  assert.equal(store.getState().fileError, "refused");
});

test("dropping only unsupported files is refused at once and still reported to the host", async () => {
  const { api, store, controller } = await ready();
  await controller.drop([{ name: "notes.pdf" }]);
  assert.match(store.getState().fileError, /"notes\.pdf" cannot be used/);
  assert.deepEqual(api.calls.at(-1)[0], "dropFiles");
});

test("dropping a supported file leaves the selection to the host's event", async () => {
  const { api, store, controller } = await ready();
  await controller.drop([{ name: "a.wav" }]);
  assert.equal(store.getState().fileError, null);
  assert.equal(store.getState().file, null);
  api.emit("file:selected", selection);
  assert.equal(store.getState().file.path, "/m/a.wav");
});

test("choose folder asks the host starting at the current output folder", async () => {
  const { api, store, controller } = await ready({ pickFolder: "/out" });
  api.emit("file:selected", selection);
  await controller.chooseFolder();
  assert.deepEqual(api.calls.find((call) => call[0] === "pickFolder"), ["pickFolder", "/m"]);
  assert.equal(store.getState().chosenFolder, "/out");
});

test("start sends the request and the page runs until the terminal event", async () => {
  const { api, store, controller } = await ready();
  api.emit("file:selected", selection);
  await controller.start();
  assert.equal(store.getState().phase, "running");
  assert.deepEqual(api.calls.find((call) => call[0] === "startRun")[1].inputPath, "/m/a.wav");
  api.emit("run:progress", { stage: "Recognising", fraction: 0.4 });
  api.emit("run:progress", { stage: "Recognising", fraction: 0.3 });
  assert.equal(store.getState().progress.fraction, 0.4);
  api.emit("run:finished", { kind: "completed", message: "done", outputFolder: "/m", outputFiles: ["/m/a.vtt"], speakers: 2, words: 10 });
  assert.equal(store.getState().phase, "done");
  assert.equal(store.getState().outcome.kind, "completed");
});

test("start does nothing without a file and does not call the host", async () => {
  const { api, controller } = await ready();
  await controller.start();
  assert.equal(api.calls.some((call) => call[0] === "startRun"), false);
});

test("a rejected start shows the host's sentence and returns to idle", async () => {
  const { api, store, controller } = await ready({ startRun: new Error("No output format is chosen.") });
  api.emit("file:selected", selection);
  await controller.start();
  assert.equal(store.getState().phase, "idle");
  assert.equal(store.getState().error, "No output format is chosen.");
});

test("cancel calls the host once and waits for the terminal event", async () => {
  const { api, store, controller } = await ready();
  api.emit("file:selected", selection);
  await controller.start();
  await controller.cancel();
  assert.equal(store.getState().cancelling, true);
  assert.equal(api.calls.filter((call) => call[0] === "cancelRun").length, 1);
  api.emit("run:finished", { kind: "cancelled", message: "Cancelled." });
  assert.equal(store.getState().phase, "done");
  assert.equal(store.getState().cancelling, false);
});

test("cancel when no run is active does not call the host", async () => {
  const { api, controller } = await ready();
  await controller.cancel();
  assert.equal(api.calls.some((call) => call[0] === "cancelRun"), false);
});

test("while running, pick, drop and folder choice are ignored", async () => {
  const { api, controller } = await ready({ pickFile: selection, pickFolder: "/out" });
  api.emit("file:selected", selection);
  await controller.start();
  const before = api.calls.length;
  await controller.pick();
  await controller.drop([{ name: "a.wav" }]);
  await controller.chooseFolder();
  assert.equal(api.calls.length, before);
});

test("Open output folder uses the outcome's folder", async () => {
  const { api, store, controller } = await ready();
  api.emit("file:selected", selection);
  await controller.start();
  api.emit("run:finished", { kind: "completed", outputFolder: "/elsewhere", outputFiles: [] });
  await controller.openOutputFolder();
  assert.deepEqual(api.calls.find((call) => call[0] === "openFolder"), ["openFolder", "/elsewhere"]);
  assert.equal(store.getState().error, null);
});

test("a failing Open output folder shows the error line", async () => {
  const { api, store, controller } = await ready({ openFolder: new Error("The folder does not exist.") });
  api.emit("file:selected", selection);
  await controller.openOutputFolder();
  assert.equal(store.getState().error, "The folder does not exist.");
});

test("a host error event shows the generic error line", async () => {
  const { api, store } = await ready();
  api.emit("host:error", { code: "internal_error" });
  assert.match(store.getState().error, /could not describe/);
});

test("a failed run keeps its sentence in the outcome", async () => {
  const { api, store, controller } = await ready();
  api.emit("file:selected", selection);
  await controller.start();
  api.emit("run:finished", { kind: "failed", message: "The audio could not be decoded. Convert it to WAV." });
  assert.equal(store.getState().outcome.message, "The audio could not be decoded. Convert it to WAV.");
});
