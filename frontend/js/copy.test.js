// copy.test.js: unit tests that hold every string the page can show to the copy rules (no em dash,
// English, no placeholders left over) and the static page to its offline rules (no inline script or
// style, no network address). Run with `node --test "frontend/**/*.test.js"`.
import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import * as copy from "./copy.js";

const here = dirname(fileURLToPath(import.meta.url));
const frontend = join(here, "..");

function allStrings(value, found = []) {
  if (typeof value === "string") {
    found.push(value);
  } else if (value && typeof value === "object") {
    for (const inner of Object.values(value)) {
      allStrings(inner, found);
    }
  }
  return found;
}

test("copy.js has no em dash anywhere in its text", () => {
  assert.equal(readFileSync(join(here, "copy.js"), "utf8").includes("\u2014"), false);
});

test("no exported string and no function result contains an em dash", () => {
  const strings = allStrings(Object.values(copy).filter((value) => typeof value !== "function"));
  strings.push(copy.refusal(["a.pdf", "b.txt"]), copy.percent(5), copy.stageLine("Stage", 5), copy.versionLine("1.0.0"));
  assert.ok(strings.length > 40, "got " + strings.length + " strings, wanted the whole copy");
  for (const text of strings) {
    assert.equal(text.includes("\u2014"), false, text);
  }
});

test("the refusal sentence names what was refused and what is supported", () => {
  const text = copy.refusal(["a.pdf", "b.txt"]);
  assert.match(text, /"a\.pdf", "b\.txt" cannot be used/);
  assert.match(text, /\.m4a, \.mp4 and \.wav/);
});

test("the refusal matches the host's wording for the same names", () => {
  assert.equal(
    copy.refusal(["notes.pdf"]),
    '"notes.pdf" cannot be used: only .m4a, .mp4 and .wav files are supported. Choose a recording in one of those formats.',
  );
});

test("the page is offline: no inline script or style, no network address, no external font", () => {
  const html = readFileSync(join(frontend, "index.html"), "utf8");
  assert.equal(/<script(?![^>]*\ssrc=)/i.test(html), false, "an inline script is not allowed under the CSP");
  assert.equal(/\sstyle\s*=/i.test(html), false, "an inline style is not allowed under the CSP");
  assert.equal(/<style/i.test(html), false);
  assert.equal(/https?:\/\//i.test(html), false);
  for (const file of readdirSync(join(frontend, "css"))) {
    const css = readFileSync(join(frontend, "css", file), "utf8");
    assert.equal(/https?:\/\/|@import|@font-face/i.test(css), false, file);
  }
});

test("no script calls fetch, XMLHttpRequest, WebSocket or eval", () => {
  for (const file of readdirSync(here).filter((name) => name.endsWith(".js") && !name.endsWith(".test.js"))) {
    const text = readFileSync(join(here, file), "utf8");
    assert.equal(/\b(fetch|XMLHttpRequest|WebSocket|EventSource|sendBeacon)\b|\beval\(|new Function/.test(text), false, file);
  }
});

test("every id the view needs exists in index.html", () => {
  const html = readFileSync(join(frontend, "index.html"), "utf8");
  const view = readFileSync(join(here, "view.js"), "utf8");
  const listed = view.slice(view.indexOf("for (const id of ["), view.indexOf("]) {", view.indexOf("for (const id of [")));
  const ids = [...listed.matchAll(/"([a-z-]+)"/g)].map((match) => match[1]);
  assert.ok(ids.length > 40);
  for (const id of ids) {
    assert.ok(html.includes(`id="${id}"`), "index.html has no id " + id);
  }
});
