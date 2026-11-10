// model.test.js: unit tests for the pure helpers (file names, speaker count, paths, formatting), run
// with `node --test "frontend/**/*.test.js"`.
import test from "node:test";
import assert from "node:assert/strict";
import {
  baseName, checkDroppedNames, directoryOf, extensionOf, formatDuration, formatSpeed, isSupportedName,
  parseSpeakerCount, selectedFormats, toPercent,
} from "./model.js";

test("supported names are .m4a, .mp4 and .wav in any case", () => {
  for (const name of ["a.m4a", "B.MP4", "c.Wav", "/x/y/z.wav", "C:\\x\\y.m4a"]) {
    assert.equal(isSupportedName(name), true, name);
  }
  for (const name of ["a.mp3", "a.pdf", "wav", ".wav", "a.wav.txt", ""]) {
    assert.equal(isSupportedName(name), false, name);
  }
});

test("extensionOf and baseName handle both separators", () => {
  assert.equal(extensionOf("C:\\a\\b.MP4"), ".mp4");
  assert.equal(baseName("C:\\a\\b.mp4"), "b.mp4");
  assert.equal(baseName("/a/b.mp4"), "b.mp4");
  assert.equal(baseName("b.mp4"), "b.mp4");
});

test("directoryOf keeps roots and handles both separators", () => {
  assert.equal(directoryOf("/home/me/a.wav"), "/home/me");
  assert.equal(directoryOf("C:\\Users\\me\\a.wav"), "C:\\Users\\me");
  assert.equal(directoryOf("C:\\a.wav"), "C:\\");
  assert.equal(directoryOf("/a.wav"), "/");
  assert.equal(directoryOf("a.wav"), "");
});

test("dropped names pass when any is supported and are refused with a sentence otherwise", () => {
  assert.deepEqual(checkDroppedNames(["a.pdf", "b.wav"]), { ok: true, reason: null });
  const refused = checkDroppedNames(["a.pdf", "b.docx"]);
  assert.equal(refused.ok, false);
  assert.match(refused.reason, /"a\.pdf", "b\.docx" cannot be used/);
  assert.match(refused.reason, /\.m4a, \.mp4 and \.wav/);
  assert.equal(checkDroppedNames([]).ok, false);
});

test("speaker count: empty is automatic, 1 to 10 is valid, anything else is not", () => {
  assert.deepEqual(parseSpeakerCount(""), { valid: true, value: null });
  assert.deepEqual(parseSpeakerCount("  "), { valid: true, value: null });
  assert.deepEqual(parseSpeakerCount("1"), { valid: true, value: 1 });
  assert.deepEqual(parseSpeakerCount(" 10 "), { valid: true, value: 10 });
  for (const text of ["0", "11", "-1", "2.5", "abc", "1e1", "+3"]) {
    assert.equal(parseSpeakerCount(text).valid, false, text);
  }
});

test("selectedFormats lists ticked formats in a fixed order", () => {
  assert.deepEqual(selectedFormats({ txt: true, vtt: true, json: false }), ["vtt", "txt"]);
  assert.deepEqual(selectedFormats({}), []);
});

test("toPercent rounds and clamps", () => {
  assert.equal(toPercent(0.456), 46);
  assert.equal(toPercent(2), 100);
  assert.equal(toPercent(-1), 0);
  assert.equal(toPercent(Number.NaN), 0);
});

test("durations read as hours, minutes and seconds", () => {
  assert.equal(formatDuration(0), "0 s");
  assert.equal(formatDuration(59.6), "1 min 0 s");
  assert.equal(formatDuration(125), "2 min 5 s");
  assert.equal(formatDuration(3723), "1 h 2 min 3 s");
  assert.equal(formatDuration(-5), "0 s");
});

test("speed reads as a multiple of real time", () => {
  assert.equal(formatSpeed(12.34), "12.3 times real time");
  assert.equal(formatSpeed(undefined), "0.0 times real time");
});
