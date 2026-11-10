// api.test.js: unit tests for request ids, reply matching, rejection and event routing over a fake
// transport, run with `node --test "frontend/**/*.test.js"`.
import test from "node:test";
import assert from "node:assert/strict";
import { createApi } from "./api.js";

function fakeTransport(available = true) {
  const listeners = [];
  const sent = [];
  const drops = [];
  return {
    available,
    sent,
    drops,
    send: (text) => sent.push(JSON.parse(text)),
    onMessage: (callback) => listeners.push(callback),
    sendDrop: (files) => drops.push(files),
    // Simulates the host posting a message to the page.
    post: (message) => listeners.forEach((listener) => listener(JSON.stringify(message))),
  };
}

test("each call carries a fresh id, its method and positional args", async () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const first = api.appInfo();
  const second = api.pickFolder("/x");
  assert.deepEqual(transport.sent, [
    { id: 1, method: "app.info", args: [] },
    { id: 2, method: "folder.pick", args: ["/x"] },
  ]);
  transport.post({ id: 2, result: "/chosen" });
  transport.post({ id: 1, result: { version: "1" } });
  assert.deepEqual(await first, { version: "1" });
  assert.equal(await second, "/chosen");
});

test("replies match by id whatever the order", async () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const a = api.pickFile();
  const b = api.cancelRun();
  transport.post({ id: 2, result: null });
  transport.post({ id: 1, result: { path: "/a.wav", name: "a.wav" } });
  assert.equal(await b, null);
  assert.deepEqual(await a, { path: "/a.wav", name: "a.wav" });
});

test("an error reply rejects with its sentence", async () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const promise = api.startRun({});
  transport.post({ id: 1, error: "A transcription is already running." });
  await assert.rejects(promise, { message: "A transcription is already running." });
});

test("a reply without a result resolves to null", async () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const promise = api.openFolder("/x");
  transport.post({ id: 1 });
  assert.equal(await promise, null);
});

test("unknown ids and garbage are ignored", async () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const promise = api.appInfo();
  transport.post({ id: 99, result: 1 });
  transport.post(42);
  transport.post(null);
  transport.post({ id: 1, result: "ok" });
  assert.equal(await promise, "ok");
});

test("events reach their subscribers with the payload and unsubscribe works", () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const seen = [];
  const off = api.on("run:progress", (payload) => seen.push(payload));
  api.on("other", () => seen.push("wrong"));
  transport.post({ event: "run:progress", payload: { stage: "s", fraction: 0.5 } });
  off();
  transport.post({ event: "run:progress", payload: { stage: "s", fraction: 0.6 } });
  assert.deepEqual(seen, [{ stage: "s", fraction: 0.5 }]);
});

test("a throwing event handler does not stop the others", () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const seen = [];
  const originalError = console.error;
  console.error = () => {};
  try {
    api.on("host:error", () => {
      throw new Error("boom");
    });
    api.on("host:error", (payload) => seen.push(payload));
    transport.post({ event: "host:error", payload: { code: "internal_error" } });
  } finally {
    console.error = originalError;
  }
  assert.deepEqual(seen, [{ code: "internal_error" }]);
});

test("without a bridge every call rejects with a readable sentence and never throws synchronously", async () => {
  const api = createApi(fakeTransport(false));
  let promise;
  assert.doesNotThrow(() => {
    promise = api.appInfo();
  });
  await assert.rejects(promise, /cannot reach the application/);
});

test("a transport that throws on send rejects the call and leaves nothing pending", async () => {
  const transport = fakeTransport();
  transport.send = () => {
    throw new Error("post failed");
  };
  const api = createApi(transport);
  await assert.rejects(api.cancelRun(), { message: "post failed" });
});

test("dropFiles hands the file objects over first and sends their names", async () => {
  const transport = fakeTransport();
  const api = createApi(transport);
  const files = [{ name: "a.wav" }, { name: "b.pdf" }];
  const promise = api.dropFiles(files);
  assert.deepEqual(transport.drops, [files]);
  assert.deepEqual(transport.sent, [{ id: 1, method: "files.dropped", args: [["a.wav", "b.pdf"]] }]);
  transport.post({ id: 1, result: null });
  assert.equal(await promise, null);
});

test("folder.pick sends null when there is no initial folder", () => {
  const transport = fakeTransport();
  createApi(transport).pickFolder();
  assert.deepEqual(transport.sent[0].args, [null]);
});
