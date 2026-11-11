// Tests of build/make-app-icon.mjs: the drawn mark has the colours written out here at known pixels,
// the committed icon holds every size and shows exactly what the geometry draws today, and the
// geometry is the same as the page's logo, so the executable and the page show one mark.
// Run with: node --test build/*.test.mjs
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { inflateSync } from "node:zlib";
import { test } from "node:test";
import { ICON_PATH, SIZES, buildIcon, renderMark } from "./make-app-icon.mjs";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");

function pixel(pixels, size, column, row) {
  const at = (row * size + column) * 4;
  return [...pixels.subarray(at, at + 4)];
}

// Reads an icon file back into one straight RGBA picture per size, top row first, whatever the
// encoding of each entry, so the comparison does not depend on the compressor's exact bytes.
function decodeIcon(bytes) {
  assert.deepEqual([...bytes.subarray(0, 4)], [0, 0, 1, 0], "an icon file starts with reserved 0 and type 1");
  const count = bytes.readUInt16LE(4);
  const pictures = new Map();
  for (let index = 0; index < count; index += 1) {
    const at = 6 + index * 16;
    const size = bytes[at] === 0 ? 256 : bytes[at];
    const image = bytes.subarray(bytes.readUInt32LE(at + 12), bytes.readUInt32LE(at + 12) + bytes.readUInt32LE(at + 8));
    const pixels = new Uint8Array(size * size * 4);
    if (image[0] === 0x89) {
      const data = [];
      for (let offset = 8; offset < image.length; ) {
        const length = image.readUInt32BE(offset);
        if (image.toString("ascii", offset + 4, offset + 8) === "IDAT") {
          data.push(image.subarray(offset + 8, offset + 8 + length));
        }
        offset += 12 + length;
      }
      const rows = inflateSync(Buffer.concat(data));
      for (let row = 0; row < size; row += 1) {
        assert.equal(rows[row * (size * 4 + 1)], 0, "every PNG row uses filter 0");
        pixels.set(rows.subarray(row * (size * 4 + 1) + 1, (row + 1) * (size * 4 + 1)), row * size * 4);
      }
    } else {
      assert.equal(image.readUInt16LE(14), 32, "the bitmap entries hold 32 bits per pixel");
      for (let row = 0; row < size; row += 1) {
        for (let column = 0; column < size; column += 1) {
          const from = 40 + ((size - 1 - row) * size + column) * 4;
          pixels.set([image[from + 2], image[from + 1], image[from], image[from + 3]], (row * size + column) * 4);
        }
      }
    }
    pictures.set(size, pixels);
  }
  return pictures;
}

test("the mark is a transparent corner, an orange tile and a white middle bar", () => {
  const pixels = renderMark(32);
  assert.deepEqual(pixel(pixels, 32, 0, 0), [0, 0, 0, 0]);
  assert.deepEqual(pixel(pixels, 32, 2, 16), [0xfd, 0x51, 0x08, 255]);
  assert.deepEqual(pixel(pixels, 32, 15, 16), [255, 255, 255, 255]);
});

test("the icon lists the title bar, taskbar and large sizes", () => {
  assert.deepEqual(SIZES, [16, 20, 24, 32, 40, 48, 64, 256]);
  assert.deepEqual([...decodeIcon(buildIcon()).keys()], SIZES);
});

test("the committed icon shows what the geometry draws; run node build/make-app-icon.mjs after a change", () => {
  const committed = decodeIcon(readFileSync(ICON_PATH));
  assert.deepEqual([...committed.keys()], SIZES);
  for (const size of SIZES) {
    assert.deepEqual(committed.get(size), renderMark(size), `the ${size} pixel image differs from the mark`);
  }
});

test("the icon's geometry is the page's logo", () => {
  const page = readFileSync(join(root, "frontend", "js", "icons.js"), "utf8");
  const logo = page.slice(page.indexOf("export function logoIcon"));
  for (const fragment of ["width: 32, height: 32, rx: 7", '"#FD5108"', "[6, 14, 20, 10, 6]", "x: 5.7 + index * 4.5", "width: 2.6", '"#FFFFFF"']) {
    assert.ok(logo.includes(fragment), `logoIcon in frontend/js/icons.js no longer holds ${fragment}; change build/make-app-icon.mjs to match and run it`);
  }
});
