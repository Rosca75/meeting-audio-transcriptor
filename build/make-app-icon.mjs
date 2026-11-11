// make-app-icon.mjs: draws the application's mark (the orange tile with a white waveform that the page
// shows beside its title, `logoIcon` in frontend/js/icons.js) into the Windows icon file the desktop
// executable embeds, src/MeetingAudioTranscriptor.Shell/Windows/app.ico. It uses Node alone, so the
// icon is reproducible from its geometry with no drawing application. Run it after the mark changes:
// `node build/make-app-icon.mjs`.
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { deflateSync } from "node:zlib";

// The mark in its 32 by 32 frame, the same numbers as logoIcon: a tile with rounded corners, then five
// rounded bars centred on the middle line.
export const MARK = {
  frame: 32,
  tile: { radius: 7, colour: [0xfd, 0x51, 0x08] },
  bars: { heights: [6, 14, 20, 10, 6], left: 5.7, step: 4.5, width: 2.6, colour: [0xff, 0xff, 0xff] },
};

// The sizes Windows asks for: the small title bar and list icons at 100 to 250 percent scaling, the
// taskbar and desktop sizes, and the large size Explorer shows in its biggest views.
export const SIZES = [16, 20, 24, 32, 40, 48, 64, 256];

// Each pixel is the average of a grid of samples, which smooths the edges at every size.
const SAMPLES = 8;

function insideRoundedRect(x, y, left, top, width, height, radius) {
  const nearestX = Math.min(Math.max(x, left + radius), left + width - radius);
  const nearestY = Math.min(Math.max(y, top + radius), top + height - radius);
  const dx = x - nearestX;
  const dy = y - nearestY;
  return x >= left && x <= left + width && y >= top && y <= top + height && dx * dx + dy * dy <= radius * radius;
}

// The colour of one point of the frame, or null outside the tile.
function colourAt(x, y) {
  const { frame, tile, bars } = MARK;
  if (!insideRoundedRect(x, y, 0, 0, frame, frame, tile.radius)) {
    return null;
  }
  for (let index = 0; index < bars.heights.length; index += 1) {
    const height = bars.heights[index];
    const left = bars.left + index * bars.step;
    if (insideRoundedRect(x, y, left, frame / 2 - height / 2, bars.width, height, bars.width / 2)) {
      return bars.colour;
    }
  }
  return tile.colour;
}

// The mark at one size as straight (not premultiplied) RGBA rows, top row first.
export function renderMark(size) {
  const pixels = new Uint8Array(size * size * 4);
  const unit = MARK.frame / size;
  for (let row = 0; row < size; row += 1) {
    for (let column = 0; column < size; column += 1) {
      let red = 0;
      let green = 0;
      let blue = 0;
      let covered = 0;
      for (let sy = 0; sy < SAMPLES; sy += 1) {
        for (let sx = 0; sx < SAMPLES; sx += 1) {
          const colour = colourAt((column + (sx + 0.5) / SAMPLES) * unit, (row + (sy + 0.5) / SAMPLES) * unit);
          if (colour) {
            red += colour[0];
            green += colour[1];
            blue += colour[2];
            covered += 1;
          }
        }
      }
      const at = (row * size + column) * 4;
      if (covered > 0) {
        pixels[at] = Math.round(red / covered);
        pixels[at + 1] = Math.round(green / covered);
        pixels[at + 2] = Math.round(blue / covered);
        pixels[at + 3] = Math.round((covered * 255) / (SAMPLES * SAMPLES));
      }
    }
  }
  return pixels;
}

// A 32-bit device-independent bitmap as an icon stores it: the header with the height doubled, the
// colour rows bottom first in blue, green, red, alpha order, then the one-bit mask that marks the
// fully transparent pixels for the renderers that ignore alpha.
function encodeBitmap(size, pixels) {
  const maskStride = Math.ceil(size / 32) * 4;
  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8);
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  header.writeUInt32LE(size * size * 4 + maskStride * size, 20);
  const colour = Buffer.alloc(size * size * 4);
  const mask = Buffer.alloc(maskStride * size);
  for (let row = 0; row < size; row += 1) {
    const stored = size - 1 - row;
    for (let column = 0; column < size; column += 1) {
      const from = (row * size + column) * 4;
      const to = (stored * size + column) * 4;
      colour[to] = pixels[from + 2];
      colour[to + 1] = pixels[from + 1];
      colour[to + 2] = pixels[from];
      colour[to + 3] = pixels[from + 3];
      if (pixels[from + 3] === 0) {
        mask[stored * maskStride + (column >> 3)] |= 0x80 >> (column & 7);
      }
    }
  }
  return Buffer.concat([header, colour, mask]);
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k += 1) {
    c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  }
  return c >>> 0;
});

function crc32(bytes) {
  let c = 0xffffffff;
  for (const byte of bytes) {
    c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

function pngChunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length, 0);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);
  return Buffer.concat([length, body, crc]);
}

// The largest size is stored as PNG, which Windows reads inside an icon and which keeps the file small.
function encodePng(size, pixels) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8;
  header[9] = 6;
  const rows = Buffer.alloc(size * (size * 4 + 1));
  for (let row = 0; row < size; row += 1) {
    Buffer.from(pixels.buffer, pixels.byteOffset + row * size * 4, size * 4).copy(rows, row * (size * 4 + 1) + 1);
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk("IHDR", header),
    pngChunk("IDAT", deflateSync(rows, { level: 9 })),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

// The icon file: a directory of one entry per size, then each image.
export function buildIcon(sizes = SIZES) {
  const images = sizes.map((size) => (size >= 256 ? encodePng(size, renderMark(size)) : encodeBitmap(size, renderMark(size))));
  const directory = Buffer.alloc(6 + 16 * sizes.length);
  directory.writeUInt16LE(0, 0);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  sizes.forEach((size, index) => {
    const at = 6 + index * 16;
    directory[at] = size >= 256 ? 0 : size;
    directory[at + 1] = size >= 256 ? 0 : size;
    directory.writeUInt16LE(1, at + 4);
    directory.writeUInt16LE(32, at + 6);
    directory.writeUInt32LE(images[index].length, at + 8);
    directory.writeUInt32LE(offset, at + 12);
    offset += images[index].length;
  });
  return Buffer.concat([directory, ...images]);
}

export const ICON_PATH = join(dirname(fileURLToPath(import.meta.url)), "..", "src", "MeetingAudioTranscriptor.Shell", "Windows", "app.ico");

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const icon = buildIcon();
  writeFileSync(ICON_PATH, icon);
  console.log(`wrote ${icon.length} bytes to src/MeetingAudioTranscriptor.Shell/Windows/app.ico (${SIZES.join(", ")} pixels)`);
}
