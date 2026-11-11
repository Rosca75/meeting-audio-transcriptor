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
  strings.push(copy.speakerLabel(3), copy.speakerNameLabel(3));
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

test("the sentences the window no longer shows are gone from the copy", () => {
  const strings = allStrings(Object.values(copy).filter((value) => typeof value !== "function"));
  for (const gone of [
    "Turn a meeting recording into a transcript. Everything stays on this computer.",
    "Leave empty to detect the number automatically, or enter 1 to 10.",
    "Choose a recording before starting.",
  ]) {
    assert.equal(strings.includes(gone), false, gone);
  }
  assert.equal("tagline" in copy.APP, false);
  assert.equal("speakerCountHelp" in copy.OPTIONS, false);
  assert.equal("needFile" in copy.ERRORS, false);
  // The sentence of the output folder line stays.
  assert.equal(copy.OUTPUT.nothingYet, "Choose a recording first.");
});

test("a speaker's default label and its field's name carry the number", () => {
  assert.equal(copy.speakerLabel(3), "Speaker 3");
  assert.equal(copy.speakerNameLabel(10), "Name of speaker 10");
});

test("the markup holds no colour and no picture: the icons are built by the script", () => {
  const html = readFileSync(join(frontend, "index.html"), "utf8");
  assert.equal(/<(img|svg|picture|object|embed|iframe)\b/i.test(html), false);
  assert.equal(/#[0-9a-f]{3,8}\b/i.test(html), false);
});

test("style.css declares no colour and no token; tokens.css owns them", () => {
  const style = readFileSync(join(frontend, "css", "style.css"), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
  assert.equal(/#[0-9a-f]{3,8}\b|\brgba?\(|\bhsla?\(/i.test(style), false, "a colour literal in style.css");
  assert.equal(/^\s*--[a-z0-9-]+\s*:/im.test(style), false, "a token declared in style.css");
});

test("every colour of tokens.css is one of the palette", () => {
  const palette = new Set(
    (
      "FFFFFF 000000 EBEBEB FD5108 FE7C39 FFAA72 A1A8B3 B5BCC4 CBD1D6 FFCDA8 FFE8D4 FFF5ED DFE3E6 EEEFF1 F5F7F8 " +
      "F2F2F2 D8D8D8 C6C6C6 BBBBBB A5A5A5 7F7F7F 595959 464646 3B3B3B 262626 0C0C0C " +
      "D3D3D3 B0B0B0 868686 6D6D6D 3A3A3A 171717 FEDCCD FEB99C FDA078 F5895B C23A01 812701 " +
      "FEE4D7 FECAAF FEB793 FBA275 E84F01 9A3500 FFEEE2 FFDDC6 FFD1B2 FFBF95 FF7215 B84900 " +
      "ECEDEF D9DCE0 CBCFD5 BABFC7 717C8D 4B535E F0F1F3 E1E4E7 D6DADE C6CBD2 808C9A 535D68 " +
      "B3BCC3 909DA8 6A7883 54616C 2D343A 121517"
    ).split(" "),
  );
  const tokens = readFileSync(join(frontend, "css", "tokens.css"), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
  const used = [...tokens.matchAll(/#([0-9a-f]{3,8})\b/gi)].map((match) => match[1].toUpperCase());
  assert.ok(used.length >= 20, "got " + used.length + " colours, wanted both schemes");
  for (const colour of used) {
    assert.ok(palette.has(colour), "#" + colour + " is not a colour of the palette");
  }
  assert.equal(/\brgba?\(|\bhsla?\(/i.test(tokens), false, "a colour written as a function escapes the palette check");
});
