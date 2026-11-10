// bridge.test.js: unit tests for the transport abstraction over fake window objects for each host,
// run with `node --test "frontend/**/*.test.js"`.
import test from "node:test";
import assert from "node:assert/strict";
import { createTransport } from "./bridge.js";

function fakeWebView2() {
  const handlers = [];
  const posted = [];
  const drops = [];
  const webview = {
    postMessage: (text) => posted.push(text),
    postMessageWithAdditionalObjects: (text, files) => drops.push({ text, files }),
    addEventListener: (name, handler) => {
      assert.equal(name, "message");
      handlers.push(handler);
    },
  };
  return { win: { chrome: { webview } }, handlers, posted, drops };
}

function fakeGtk() {
  const posted = [];
  return { win: { webkit: { messageHandlers: { host: { postMessage: (text) => posted.push(text) } } } }, posted };
}

test("WebView2 is detected, sends strings and receives string messages", () => {
  const host = fakeWebView2();
  const transport = createTransport(host.win);
  const received = [];
  transport.onMessage((text) => received.push(text));
  transport.send('{"id":1}');
  host.handlers[0]({ data: '{"event":"x"}' });
  assert.equal(transport.kind, "webview2");
  assert.equal(transport.available, true);
  assert.deepEqual(host.posted, ['{"id":1}']);
  assert.deepEqual(received, ['{"event":"x"}']);
});

test("WebView2 messages that arrive as parsed objects are turned back into JSON text", () => {
  const host = fakeWebView2();
  const transport = createTransport(host.win);
  const received = [];
  transport.onMessage((text) => received.push(text));
  host.handlers[0]({ data: { id: 3, result: null } });
  assert.deepEqual(received, ['{"id":3,"result":null}']);
});

test("WebView2 drops go through postMessageWithAdditionalObjects with the drop marker", () => {
  const host = fakeWebView2();
  const transport = createTransport(host.win);
  const files = [{ name: "a.wav" }];
  transport.sendDrop(files);
  assert.equal(host.drops.length, 1);
  assert.equal(host.drops[0].text, '{"drop":true}');
  assert.equal(host.drops[0].files, files);
});

test("WebKitGTK is detected, sends through the host handler and receives through __hostReceive", () => {
  const host = fakeGtk();
  const transport = createTransport(host.win);
  const received = [];
  transport.onMessage((text) => received.push(text));
  transport.send("hello");
  host.win.__hostReceive('{"event":"y"}');
  assert.equal(transport.kind, "gtk");
  assert.deepEqual(host.posted, ["hello"]);
  assert.deepEqual(received, ['{"event":"y"}']);
});

test("WebKitGTK drops are the host's to intercept, so the page sends nothing", () => {
  const host = fakeGtk();
  const transport = createTransport(host.win);
  transport.sendDrop([{ name: "a.wav" }]);
  assert.deepEqual(host.posted, []);
});

test("window.__hostReceive is defined on both hosts", () => {
  assert.equal(typeof createTransport(fakeWebView2().win).kind, "string");
  const win = fakeWebView2().win;
  createTransport(win);
  assert.equal(typeof win.__hostReceive, "function");
});

test("with no host the transport is unavailable and sending throws", () => {
  const transport = createTransport({});
  assert.equal(transport.kind, "none");
  assert.equal(transport.available, false);
  assert.throws(() => transport.send("x"), /no host transport/);
});

test("a failing listener does not stop the others", () => {
  const host = fakeGtk();
  const transport = createTransport(host.win);
  const received = [];
  const originalError = console.error;
  console.error = () => {};
  try {
    transport.onMessage(() => {
      throw new Error("boom");
    });
    transport.onMessage((text) => received.push(text));
    host.win.__hostReceive("m");
  } finally {
    console.error = originalError;
  }
  assert.deepEqual(received, ["m"]);
});

test("onMessage returns an unsubscribe function", () => {
  const host = fakeGtk();
  const transport = createTransport(host.win);
  const received = [];
  const off = transport.onMessage((text) => received.push(text));
  host.win.__hostReceive("1");
  off();
  host.win.__hostReceive("2");
  assert.deepEqual(received, ["1"]);
});
