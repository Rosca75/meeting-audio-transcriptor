// The tool that builds the speech test set the transcription benchmarks read. It
// reads bench/testset/testset.lock, which pins every source file by address,
// size and hash and holds the clip recipes (which utterances, in which order,
// with which gaps), downloads the sources into a cache folder, verifies each
// against the lock before a byte of it is trusted, and writes the test set into
// bench/data/ with ffmpeg: 16 kHz mono PCM16 WAV clips with their reference
// text and RTTM ground truth, ten-minute meeting-style files per language,
// synthetic and recorded noise, an AAC copy of the English long file as .m4a
// and .mp4, and bench/data/manifest.json listing every file.
//
//   node bench/testset/fetch-testset.mjs [--cache <dir>] [--out <dir>]
//        [--long <minutes>]... [--long-lang en,fr,de] [--offline] [--verify]
//        [--force] [--pin] [--plan] [--lock <path>]
//
//   --cache      where sources are downloaded to; bench/.cache by default.
//   --out        where the test set is written; bench/data by default.
//   --long       also build <lang>-<minutes>min.wav (and its .ref.txt and
//                .rttm) by looping the ten-minute recipe; repeatable. 30 and
//                120 are the sizes the benchmarks use.
//   --long-lang  the languages --long builds; every language when absent.
//   --offline    download nothing: verify the cache against the lock, then build
//                from it, and exit non-zero listing every missing or mismatched
//                source.
//   --verify     check the data folder against the manifest (and the cache
//                against the lock when it is present) and build nothing.
//   --force      build again even when the data folder is already up to date.
//   --pin        maintainer tool: fill the size and hash of every lock source
//                that has none, from the cache or from the network, and write
//                the lock. A source that has a hash is never rewritten, because
//                a pin that moves silently is not a pin.
//   --plan       maintainer tool: choose the utterances, turns and gaps of every
//                clip from the "plan" section of the lock with the seeded
//                generator below and write them back into the lock as recipes.
//                A build never plans: it replays the recipes, so the output
//                depends on the lock and on nothing else.
//
// A second run with nothing changed reads the manifest, hashes every output and
// exits without touching the cache or running ffmpeg, which is the fast no-op.
//
// Why the output is byte-identical between runs: every source is a pinned file;
// every decode is ffmpeg to raw 16-bit samples; gaps are listed in the lock in
// milliseconds; the WAV container is written by this file rather than by a muxer
// (a muxer's header can carry a version string); synthetic noise is ffmpeg's
// generator with a fixed seed; the AAC and H.264 copies are encoded with
// -bitexact and one thread. The only tool is ffmpeg on PATH. Node 22, no
// dependency.
//
// Downloads go through the global fetch with no proxy setting of this tool's
// own: a machine behind a proxy sets NODE_USE_ENV_PROXY=1 for the process, as it
// does for any Node program, and a proxy that re-signs TLS needs
// NODE_EXTRA_CA_CERTS or `node --use-system-ca`.
import { createHash } from "node:crypto";
import {
  createReadStream, createWriteStream, existsSync, mkdirSync, readFileSync,
  renameSync, rmSync, statSync, writeFileSync,
} from "node:fs";
import { once } from "node:events";
import http from "node:http";
import { spawnSync } from "node:child_process";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createGunzip, inflateRawSync } from "node:zlib";

/** Bumped whenever this file changes what a build writes, so an old data folder is rebuilt rather than trusted. */
export const builderRevision = 1;

const toolDir = dirname(fileURLToPath(import.meta.url));
const benchDir = resolve(toolDir, "..");
export const defaultLockPath = join(toolDir, "testset.lock");
export const sampleRate = 16000;
const bytesPerSample = 2;

// ---------------------------------------------------------------------------
// Command line
// ---------------------------------------------------------------------------

/** Parses the arguments; an unknown one is refused with the list of the known ones. */
export function parseArgs(argv) {
  const options = {
    cache: join(benchDir, ".cache"), out: join(benchDir, "data"), lock: defaultLockPath,
    long: [], longLang: null, offline: false, verify: false, force: false, pin: false, plan: false,
  };
  const known = "--cache, --out, --long, --long-lang, --offline, --verify, --force, --pin, --plan, --lock, --help";
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => {
      if (i + 1 >= argv.length) throw new Error(`${arg} needs a value; run with --help for the usage`);
      return argv[++i];
    };
    switch (arg) {
      case "--cache": options.cache = resolve(value()); break;
      case "--out": options.out = resolve(value()); break;
      case "--lock": options.lock = resolve(value()); break;
      case "--long": {
        const minutes = Number(value());
        if (!Number.isInteger(minutes) || minutes < 1) throw new Error("--long takes a whole number of minutes, such as 30 or 120");
        if (!options.long.includes(minutes)) options.long.push(minutes);
        break;
      }
      case "--long-lang": options.longLang = value().split(",").map((s) => s.trim()).filter(Boolean); break;
      case "--offline": options.offline = true; break;
      case "--verify": options.verify = true; break;
      case "--force": options.force = true; break;
      case "--pin": options.pin = true; break;
      case "--plan": options.plan = true; break;
      case "--help": case "-h": options.help = true; break;
      default: throw new Error(`unknown argument ${arg}; the known ones are ${known}`);
    }
  }
  return options;
}

// ---------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------

const sha256Of = (buffer) => createHash("sha256").update(buffer).digest("hex");

function sha256OfFile(path) {
  return sha256Of(readFileSync(path));
}

function ensureDir(path) {
  mkdirSync(path, { recursive: true });
}

/** Writes through a temporary name so an interrupted run never leaves a half-written output. */
function writeAtomic(path, data) {
  ensureDir(dirname(path));
  const temporary = `${path}.part`;
  writeFileSync(temporary, data);
  renameSync(temporary, path);
}

const round3 = (value) => Math.round(value * 1000) / 1000;
const fmt3 = (value) => round3(value).toFixed(3);
const log = (message) => process.stdout.write(`${message}\n`);

/**
 * A seeded 32-bit generator (mulberry32). It is implemented here because the
 * recipes must not depend on a library's idea of randomness, and a Math.random
 * cannot be seeded.
 */
export function makeRandom(seed) {
  let state = seed >>> 0;
  const next = () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  return {
    next,
    /** A real in [low, high], rounded to the millisecond, which is the resolution the lock stores. */
    between: (low, high) => round3(low + next() * (high - low)),
    /** A whole number in [low, high]. */
    int: (low, high) => low + Math.floor(next() * (high - low + 1)),
  };
}

// ---------------------------------------------------------------------------
// The lock
// ---------------------------------------------------------------------------

const sourceFields = ["id", "dataset", "url", "file"];

/** Parses the lock and refuses one whose sections are not the shape this tool reads. */
export function readLock(path) {
  let text;
  try {
    text = readFileSync(path, "utf8");
  } catch (error) {
    throw new Error(`cannot read the lock ${path} (${error.message}); it is bench/testset/testset.lock unless --lock names another`);
  }
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch (error) {
    throw new Error(`${path} is not valid JSON (${error.message}); fix the lock rather than the tool`);
  }
  if (!parsed || !Array.isArray(parsed.sources) || !parsed.datasets || !parsed.plan) {
    throw new Error(`${path} needs "sources", "datasets" and "plan"; they are the pinned files, their licences and the recipe settings`);
  }
  for (const source of parsed.sources) {
    for (const field of sourceFields) {
      if (!(field in source)) throw new Error(`${path}: the source ${source.id ?? "(no id)"} has no "${field}"; every source carries ${sourceFields.join(", ")}`);
    }
    if (!(source.dataset in parsed.datasets)) throw new Error(`${path}: the source ${source.id} names the dataset ${source.dataset}, which "datasets" does not describe`);
  }
  parsed.clips ??= [];
  parsed.long ??= {};
  return { lock: parsed, text, sha256: sha256Of(text) };
}

/** Serialises the lock with each recipe item on one line, so a diff of a replanned lock reads. */
export function formatLock(lock) {
  const json = JSON.stringify(lock, null, 2);
  // ["utterance-id", gap, duration] items collapse onto one line each.
  const items = json.replace(/\[\s+("[^"\n]+"),\s+(-?[\d.]+),\s+(-?[\d.]+)\s+\]/g, "[$1, $2, $3]");
  return `${items}\n`;
}

// ---------------------------------------------------------------------------
// Sources: download, verify, cache
// ---------------------------------------------------------------------------

function cachePathOf(cacheDir, source) {
  return join(cacheDir, source.file);
}

/** Streams a download to a .part file while hashing it, so a 350 MB file is never held in memory. */
async function download(source, destination) {
  let response;
  try {
    // Zenodo refuses the runtime's default user agent with a 403, so the tool names itself.
    response = await fetch(source.url, { redirect: "follow", headers: { "user-agent": "fetch-testset/1 (speech test set builder)" } });
  } catch (error) {
    throw new Error(
      `cannot reach ${source.url} (${error.cause?.message ?? error.message}); ` +
      `behind a proxy start the tool with NODE_USE_ENV_PROXY=1 (and NODE_EXTRA_CA_CERTS=<bundle> when it re-signs TLS), ` +
      `or place the file at ${destination} by hand and run again`);
  }
  if (!response.ok) {
    throw new Error(`${source.url} answered HTTP ${response.status}; the pinned address may have moved, so check it in a browser and update the lock's "url" (the size and hash then say whether it is the same file)`);
  }
  ensureDir(dirname(destination));
  const temporary = `${destination}.part`;
  const out = createWriteStream(temporary);
  const hash = createHash("sha256");
  const total = Number(response.headers.get("content-length")) || source.bytes || 0;
  let received = 0;
  let nextReport = 0.25;
  for await (const chunk of response.body) {
    hash.update(chunk);
    received += chunk.length;
    if (!out.write(chunk)) await once(out, "drain");
    if (total > 50e6 && received / total >= nextReport) {
      log(`    ${source.id}: ${Math.round((received / total) * 100)}%`);
      nextReport += 0.25;
    }
  }
  out.end();
  await once(out, "finish");
  return { temporary, bytes: received, sha256: hash.digest("hex") };
}

/**
 * Makes one source present in the cache and equal to the lock. A cached file
 * is trusted only after its size and hash match; a download is kept only after
 * both match. With no pinned hash (the --pin run) the measured values are
 * returned for the caller to record.
 */
async function ensureSource(source, cacheDir, { offline, allowUnpinned = false }) {
  const destination = cachePathOf(cacheDir, source);
  const pinned = Boolean(source.sha256);
  if (!pinned && !allowUnpinned) {
    throw new Error(`the source ${source.id} has no size and hash in the lock; run \`node bench/testset/fetch-testset.mjs --pin\` to fill them, then review the diff`);
  }
  if (existsSync(destination)) {
    const bytes = statSync(destination).size;
    if (!pinned) return { destination, bytes, sha256: sha256OfFile(destination) };
    if (bytes === source.bytes && sha256OfFile(destination) === source.sha256) return { destination, bytes, sha256: source.sha256 };
    log(`  ${source.id}: the cached file differs from the lock; downloading it again`);
    rmSync(destination);
  }
  if (offline) {
    throw new Error(`--offline: ${destination} is missing or differs from the lock (${source.id}); run once without --offline or place the file there by hand`);
  }
  log(`  downloading ${source.id} (${source.bytes ? `${(source.bytes / 1e6).toFixed(1)} MB` : "size not pinned yet"})`);
  const result = await download(source, destination);
  if (pinned && (result.bytes !== source.bytes || result.sha256 !== source.sha256)) {
    rmSync(result.temporary, { force: true });
    throw new Error(
      `${source.id}: ${source.url} delivered ${result.bytes} bytes with sha256 ${result.sha256}, but the lock pins ${source.bytes} bytes and ${source.sha256}; ` +
      `the file at that address changed, so nothing was kept. If the change is intended, run --pin after emptying the source's "sha256" and "bytes", and review the new recording and its licence`);
  }
  renameSync(result.temporary, destination);
  return { destination, bytes: result.bytes, sha256: result.sha256 };
}

// ---------------------------------------------------------------------------
// Archives: a gzip tar read as a stream, and a zip read from memory
// ---------------------------------------------------------------------------

const readString = (block, start, end) => {
  const slice = block.subarray(start, end);
  const zero = slice.indexOf(0);
  return slice.subarray(0, zero < 0 ? slice.length : zero).toString("utf8");
};

function parsePax(content) {
  const fields = {};
  let offset = 0;
  while (offset < content.length) {
    const space = content.indexOf(0x20, offset);
    if (space < 0) break;
    const length = Number(content.subarray(offset, space).toString());
    if (!length) break;
    const record = content.subarray(space + 1, offset + length - 1).toString("utf8");
    const equals = record.indexOf("=");
    fields[record.slice(0, equals)] = record.slice(equals + 1);
    offset += length;
  }
  return fields;
}

/**
 * Reads a .tar.gz as a stream and returns the regular files `accept` says yes
 * to, as a Map from entry path to bytes. Every other entry is skipped as it
 * goes by, so a 346 MB archive costs the memory of the few files wanted.
 */
export async function readTarGz(path, accept) {
  const found = new Map();
  let buffer = Buffer.alloc(0);
  let header = null;
  let nameOverride = null;
  for await (const chunk of createReadStream(path).pipe(createGunzip())) {
    buffer = buffer.length ? Buffer.concat([buffer, chunk]) : chunk;
    for (;;) {
      if (!header) {
        if (buffer.length < 512) break;
        const block = buffer.subarray(0, 512);
        buffer = buffer.subarray(512);
        if (block.every((b) => b === 0)) continue;
        let name = readString(block, 0, 100);
        if (readString(block, 257, 263).startsWith("ustar")) {
          const prefix = readString(block, 345, 500);
          if (prefix) name = `${prefix}/${name}`;
        }
        const sizeField = block.subarray(124, 136);
        const size = sizeField[0] & 0x80
          ? Number(sizeField.readBigUInt64BE(4))
          : parseInt(readString(block, 124, 136).trim() || "0", 8);
        header = { name, size, type: String.fromCharCode(block[156] || 0x30), padded: Math.ceil(size / 512) * 512 };
      }
      if (buffer.length < header.padded) break;
      const content = buffer.subarray(0, header.size);
      buffer = buffer.subarray(header.padded);
      if (header.type === "x") {
        nameOverride = parsePax(content).path ?? nameOverride;
      } else if (header.type === "L") {
        nameOverride = readString(content, 0, content.length);
      } else {
        const name = nameOverride ?? header.name;
        nameOverride = null;
        if ((header.type === "0" || header.type === "\0") && accept(name)) found.set(name, Buffer.from(content));
      }
      header = null;
    }
  }
  return found;
}

/** Reads a zip into memory and returns a function that inflates one entry by name; zip64 is not needed by any source. */
export function openZip(path) {
  const data = readFileSync(path);
  let eocd = data.length - 22;
  while (eocd >= 0 && data.readUInt32LE(eocd) !== 0x06054b50) eocd--;
  if (eocd < 0) throw new Error(`${path} is not a zip file (no end-of-directory record); the cached file is damaged, delete it and run again`);
  const count = data.readUInt16LE(eocd + 10);
  let offset = data.readUInt32LE(eocd + 16);
  const entries = new Map();
  for (let i = 0; i < count; i++) {
    if (data.readUInt32LE(offset) !== 0x02014b50) throw new Error(`${path}: the central directory is damaged; delete the cached file and run again`);
    const nameLength = data.readUInt16LE(offset + 28);
    const extraLength = data.readUInt16LE(offset + 30);
    const commentLength = data.readUInt16LE(offset + 32);
    entries.set(data.toString("utf8", offset + 46, offset + 46 + nameLength), {
      method: data.readUInt16LE(offset + 10),
      compressed: data.readUInt32LE(offset + 20),
      local: data.readUInt32LE(offset + 42),
    });
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return {
    names: [...entries.keys()],
    read(name) {
      const entry = entries.get(name);
      if (!entry) throw new Error(`${path} has no entry ${name}; the lock names an entry the archive does not hold`);
      const start = entry.local + 30 + data.readUInt16LE(entry.local + 26) + data.readUInt16LE(entry.local + 28);
      const body = data.subarray(start, start + entry.compressed);
      if (entry.method === 0) return Buffer.from(body);
      if (entry.method === 8) return inflateRawSync(body);
      throw new Error(`${path}: entry ${name} uses zip method ${entry.method}, which this tool does not read`);
    },
  };
}

// ---------------------------------------------------------------------------
// ffmpeg and WAV
// ---------------------------------------------------------------------------

const exactFlags = ["-hide_banner", "-v", "error", "-nostdin", "-bitexact", "-fflags", "+bitexact"];

function requireFfmpeg() {
  const probe = spawnSync("ffmpeg", ["-version"], { encoding: "utf8" });
  if (probe.error || probe.status !== 0) {
    throw new Error("ffmpeg was not found on PATH; install ffmpeg 6 or later (apt install ffmpeg, brew install ffmpeg or winget install ffmpeg) and run again");
  }
}

function runFfmpeg(args, input) {
  const result = spawnSync("ffmpeg", [...exactFlags, ...args], { input, maxBuffer: 2 ** 31 - 1 });
  if (result.status !== 0) {
    throw new Error(`ffmpeg ${args.join(" ")} failed: ${result.stderr?.toString().trim() || result.error?.message}; the source file may be damaged, so delete it from the cache and run again`);
  }
  return result.stdout;
}

/** Decodes any audio ffmpeg reads (from memory) to raw 16 kHz mono signed 16-bit samples. */
function decodeToPcm(input) {
  return runFfmpeg(["-i", "pipe:0", "-map_metadata", "-1", "-ac", "1", "-ar", String(sampleRate), "-f", "s16le", "pipe:1"], input);
}

/** A 16 kHz mono PCM16 WAV with the canonical 44-byte header and no other chunk, so equal samples are equal bytes. */
export function wavFile(pcm) {
  const header = Buffer.alloc(44);
  header.write("RIFF", 0);
  header.writeUInt32LE(36 + pcm.length, 4);
  header.write("WAVEfmt ", 8);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(sampleRate, 24);
  header.writeUInt32LE(sampleRate * bytesPerSample, 28);
  header.writeUInt16LE(bytesPerSample, 32);
  header.writeUInt16LE(16, 34);
  header.write("data", 36);
  header.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([header, pcm]);
}

const secondsOf = (pcm) => pcm.length / bytesPerSample / sampleRate;
const silence = (seconds) => Buffer.alloc(Math.round(seconds * sampleRate) * bytesPerSample);

// ---------------------------------------------------------------------------
// Datasets: how an utterance id becomes samples and text
// ---------------------------------------------------------------------------

const speakerOf = {
  librispeech: (id) => id.split("-")[0],
  mls: (id) => id.split("_")[0],
};

/** Natural order of ids, so utterance 9 comes before utterance 10 within a chapter. */
const compareIds = (a, b) => a.localeCompare(b, "en", { numeric: true });

/**
 * Loads utterances of one language: the decoded samples and the dataset's
 * transcript text, untouched, for every id the filter accepts. The filter is
 * either {ids: Set} or {speakers: Set}.
 */
async function loadUtterances(lock, cacheDir, lang, filter) {
  const sources = lock.sources.filter((s) => s.lang === lang && s.dataset !== "ami" && s.role !== "noise");
  const dataset = sources[0]?.dataset;
  const speakerFn = speakerOf[dataset];
  if (!speakerFn) throw new Error(`the lock has no utterance source for the language ${lang}`);
  const wantsId = (id) => (filter.ids ? filter.ids.has(id) : filter.speakers.has(speakerFn(id)));
  const wantsSpeaker = (speaker) => (filter.ids ? [...filter.ids].some((id) => speakerFn(id) === speaker) : filter.speakers.has(speaker));
  const flacs = new Map();
  const transcripts = new Map();

  if (dataset === "librispeech") {
    for (const source of sources.filter((s) => s.role === "audio")) {
      const archive = await readTarGz(cachePathOf(cacheDir, source), (name) => {
        const parts = name.split("/");
        return parts.length >= 4 && wantsSpeaker(parts[parts.length - 3]) && (name.endsWith(".flac") || name.endsWith(".trans.txt"));
      });
      for (const [name, content] of archive) {
        if (name.endsWith(".trans.txt")) {
          for (const line of content.toString("utf8").split("\n")) {
            const space = line.indexOf(" ");
            if (space > 0) transcripts.set(line.slice(0, space), line.slice(space + 1).trim());
          }
        } else {
          const id = basename(name, ".flac");
          if (wantsId(id)) flacs.set(id, content);
        }
      }
    }
  } else {
    for (const source of sources.filter((s) => s.role === "transcripts")) {
      for (const line of readFileSync(cachePathOf(cacheDir, source), "utf8").split("\n")) {
        const tab = line.indexOf("\t");
        if (tab > 0) transcripts.set(line.slice(0, tab), line.slice(tab + 1).trim());
      }
    }
    for (const source of sources.filter((s) => s.role === "audio")) {
      if (!wantsSpeaker(source.speaker)) continue;
      const archive = await readTarGz(cachePathOf(cacheDir, source), (name) => name.endsWith(".flac") && wantsId(basename(name, ".flac")));
      for (const [name, content] of archive) flacs.set(basename(name, ".flac"), content);
    }
  }

  const utterances = new Map();
  for (const id of [...flacs.keys()].sort(compareIds)) {
    if (!transcripts.has(id)) throw new Error(`the utterance ${id} has audio but no transcript line in the pinned transcripts; the lock and the archive disagree`);
    utterances.set(id, { id, speaker: speakerFn(id), text: transcripts.get(id), pcm: decodeToPcm(flacs.get(id)) });
  }
  if (filter.ids) {
    const missing = [...filter.ids].filter((id) => !utterances.has(id));
    if (missing.length) throw new Error(`the lock's recipes name utterances the pinned archives do not hold: ${missing.slice(0, 5).join(", ")}${missing.length > 5 ? ", ..." : ""}; run --plan again or restore the lock`);
  }
  return utterances;
}

// ---------------------------------------------------------------------------
// AMI: word-level annotations to reference text and RTTM
// ---------------------------------------------------------------------------

const xmlText = (raw) => raw
  .replace(/&#x([0-9a-fA-F]+);/g, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
  .replace(/&#(\d+);/g, (_, decimal) => String.fromCodePoint(Number(decimal)))
  .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, "&");

/** Words of one AMI meeting, per speaker, with each punctuation token attached to the word before it. */
function readAmiWords(zip, meeting) {
  const decoder = new TextDecoder("latin1");
  const meetings = decoder.decode(zip.read("corpusResources/meetings.xml"));
  const block = meetings.match(new RegExp(`<meeting [^>]*observation="${meeting}"[\\s\\S]*?</meeting>`));
  if (!block) throw new Error(`corpusResources/meetings.xml has no meeting ${meeting}; the lock names a meeting the annotation archive does not hold`);
  const speakers = [];
  for (const m of block[0].matchAll(/<speaker [^>]*nxt_agent="(\w)"[^>]*global_name="(\w+)"/g)) speakers.push({ agent: m[1], id: m[2] });
  speakers.sort((a, b) => a.agent.localeCompare(b.agent));
  const words = [];
  for (const { agent, id } of speakers) {
    const xml = decoder.decode(zip.read(`words/${meeting}.${agent}.words.xml`));
    let last = null;
    for (const m of xml.matchAll(/<w ([^>]*?)>([^<]*)<\/w>/g)) {
      const attrs = Object.fromEntries([...m[1].matchAll(/([\w:]+)="([^"]*)"/g)].map((a) => [a[1], a[2]]));
      const text = xmlText(m[2]);
      if (attrs.punc === "true") {
        if (last) last.text += text;
        continue;
      }
      last = { speaker: `ami-${id}`, start: Number(attrs.starttime), end: Number(attrs.endtime), text };
      words.push(last);
    }
  }
  return words;
}

/** Speech intervals of every speaker merged, and the midpoint of each silence of at least `minimum` seconds. */
function silenceMidpoints(words, minimum) {
  const spans = words.map((w) => [w.start, w.end]).sort((a, b) => a[0] - b[0]);
  const midpoints = [];
  let reach = spans[0][1];
  for (const [start, end] of spans) {
    if (start - reach >= minimum) midpoints.push(round3((start + reach) / 2));
    reach = Math.max(reach, end);
  }
  return midpoints;
}

/** Reference text and RTTM segments of the words fully inside [start, end]; times in the RTTM are relative to start. */
function amiWindow(words, start, end, bridgeGap = 0.5) {
  const inside = words.filter((w) => w.start >= start && w.end <= end).sort((a, b) => a.start - b.start || a.end - b.end);
  const segments = [];
  const open = new Map();
  for (const w of inside) {
    const current = open.get(w.speaker);
    if (current && w.start - current.end <= bridgeGap) {
      current.end = Math.max(current.end, w.end);
    } else {
      const segment = { speaker: w.speaker, start: w.start, end: w.end };
      segments.push(segment);
      open.set(w.speaker, segment);
    }
  }
  segments.sort((a, b) => a.start - b.start || a.speaker.localeCompare(b.speaker));
  return {
    text: inside.map((w) => w.text).join(" "),
    segments: segments.map((s) => ({ speaker: s.speaker, start: round3(s.start - start), duration: round3(s.end - s.start) })),
    speakers: [...new Set(inside.map((w) => w.speaker))].sort(),
  };
}

// ---------------------------------------------------------------------------
// Planning: choose utterances, turns and gaps (a maintainer step)
// ---------------------------------------------------------------------------

const cloneSections = (lock) => JSON.parse(JSON.stringify(lock));

/** Fills clips, long recipes and AMI windows of the lock from its "plan" section. */
async function planLock(lock, cacheDir) {
  const planSection = lock.plan;
  const random = makeRandom(planSection.seed);
  const clips = [];
  const longRecipes = {};
  const { gapMin, gapMax } = planSection;

  for (const [lang, spec] of Object.entries(planSection.languages)) {
    const speakers = new Set([...spec.single.map((c) => c.speaker), ...spec.multi.flatMap((c) => c.speakers), ...spec.long.speakers]);
    log(`  planning ${lang}: decoding every utterance of ${speakers.size} speakers`);
    const pool = await loadUtterances(lock, cacheDir, lang, { speakers });
    const queues = new Map();
    for (const speaker of speakers) queues.set(speaker, [...pool.values()].filter((u) => u.speaker === speaker));
    const take = (speaker) => {
      const queue = queues.get(speaker);
      if (!queue.length) throw new Error(`plan: the speaker ${speaker} of ${lang} ran out of utterances; give the clip fewer seconds or the language more speakers`);
      return queue.shift();
    };
    const dataset = lock.sources.find((s) => s.lang === lang && s.dataset !== "ami" && s.role !== "noise").dataset;
    const makeItem = (utterance, previous) => [utterance.id, previous ? random.between(gapMin, gapMax) : 0, round3(secondsOf(utterance.pcm))];
    const total = (items) => items.reduce((sum, [, gap, duration]) => sum + gap + duration, 0);

    for (const spec1 of spec.single) {
      const items = [];
      while (total(items) < spec1.seconds) items.push(makeItem(take(spec1.speaker), items.length > 0));
      clips.push({ id: spec1.id, lang, kind: "single", dataset, speakers: [spec1.speaker], items });
    }
    for (const spec2 of spec.multi) {
      const items = [];
      let turn = 0;
      while (total(items) < spec2.seconds) {
        const speaker = spec2.speakers[turn++ % spec2.speakers.length];
        const length = random.int(spec2.turn[0], spec2.turn[1]);
        for (let k = 0; k < length && total(items) < spec2.seconds; k++) items.push(makeItem(take(speaker), items.length > 0));
      }
      clips.push({ id: spec2.id, lang, kind: "multi", dataset, speakers: spec2.speakers, items });
    }
    const items = [];
    let previous = null;
    const live = () => spec.long.speakers.filter((s) => queues.get(s).length > 0);
    while (total(items) < planSection.longSeconds) {
      const candidates = live().filter((s) => s !== previous);
      const options = candidates.length ? candidates : live();
      if (!options.length) throw new Error(`plan: the long file of ${lang} ran out of utterances at ${Math.round(total(items))} s; add speakers to its list`);
      const speaker = options[random.int(0, options.length - 1)];
      const length = random.int(planSection.turn[0], planSection.turn[1]);
      for (let k = 0; k < length && total(items) < planSection.longSeconds && queues.get(speaker).length; k++) {
        items.push(makeItem(take(speaker), items.length > 0));
      }
      previous = speaker;
    }
    longRecipes[lang] = { dataset, speakers: spec.long.speakers, items };
  }

  if (planSection.ami?.length) {
    const annotations = openZip(cachePathOf(cacheDir, lock.sources.find((s) => s.id === "ami-annotations")));
    for (const spec3 of planSection.ami) {
      const words = readAmiWords(annotations, spec3.meeting);
      const midpoints = silenceMidpoints(words, 0.3);
      const start = midpoints.find((m) => m >= spec3.approxStart);
      const end = midpoints.find((m) => m >= start + spec3.seconds);
      if (start === undefined || end === undefined) throw new Error(`plan: ${spec3.meeting} has no window of ${spec3.seconds} s after ${spec3.approxStart} s; lower approxStart`);
      const window = amiWindow(words, start, end);
      log(`  planning ${spec3.id}: ${spec3.meeting} ${start}-${end} s, ${window.speakers.length} speakers, ${window.text.split(" ").length} words`);
      clips.push({
        id: spec3.id, lang: "en", kind: "ami", dataset: "ami", meeting: spec3.meeting, audio: spec3.audio,
        channel: spec3.channel, start, end, speakers: window.speakers,
      });
    }
  }

  const planned = cloneSections(lock);
  planned.clips = clips;
  planned.long = longRecipes;
  return planned;
}

// ---------------------------------------------------------------------------
// Building
// ---------------------------------------------------------------------------

/** Concatenates the recipe's utterances with its gaps and returns samples, reference text and speaker segments. */
function assemble(items, utterances, labelOf) {
  const parts = [];
  const texts = [];
  const segments = [];
  let samples = 0;
  for (const [id, gap, expected] of items) {
    const utterance = utterances.get(id);
    if (gap > 0) {
      const pad = silence(gap);
      parts.push(pad);
      samples += pad.length / bytesPerSample;
    }
    const duration = secondsOf(utterance.pcm);
    if (Math.abs(duration - expected) > 0.002) {
      log(`  warning: ${id} decodes to ${fmt3(duration)} s but the lock recorded ${fmt3(expected)} s; the decoder differs from the one the lock was planned with`);
    }
    segments.push({ speaker: labelOf(utterance.speaker), start: round3(samples / sampleRate), duration: round3(duration) });
    parts.push(utterance.pcm);
    samples += utterance.pcm.length / bytesPerSample;
    texts.push(utterance.text);
  }
  return { pcm: Buffer.concat(parts), text: texts.join(" "), segments };
}

const rttmLines = (clipId, segments) =>
  segments.map((s) => `SPEAKER ${clipId} 1 ${fmt3(s.start)} ${fmt3(s.duration)} <NA> <NA> ${s.speaker} <NA> <NA>`).join("\n") + "\n";

/** Repeats the ten-minute recipe, with a one-second gap at each seam, until it reaches the wanted length. */
function loopItems(items, minutes) {
  const wanted = minutes * 60;
  const looped = [];
  let length = 0;
  for (let round = 0; length < wanted; round++) {
    for (let index = 0; index < items.length && length < wanted; index++) {
      const [id, gap, duration] = items[index];
      const seam = index === 0 && round > 0 ? 1 : 0;
      const effective = round3(index === 0 && round === 0 ? 0 : seam || gap);
      looped.push([id, effective, duration]);
      length += effective + duration;
    }
  }
  return looped;
}

function entryFor(out, relativePath, extra) {
  const full = join(out, relativePath);
  const data = readFileSync(full);
  return { path: relativePath.split("\\").join("/"), bytes: data.length, sha256: sha256Of(data), ...extra };
}

/** The attribution fields every manifest entry carries, from the lock's dataset table. */
function datasetFields(lock, dataset) {
  const d = lock.datasets[dataset];
  return { dataset: d.name, licence: d.licence };
}

function buildWavSet(out, relativeStem, id, wav, text, rttm) {
  writeAtomic(join(out, `${relativeStem}.wav`), wavFile(wav));
  writeAtomic(join(out, `${relativeStem}.ref.txt`), `${text}\n`);
  if (rttm) writeAtomic(join(out, `${relativeStem}.rttm`), rttm);
}

/** Encodes the English long file as .m4a and as .mp4 with a tiny black video track, bit-exact and single-threaded. */
function buildContainers(out, wavPath, stem, seconds) {
  const m4a = join(out, `${stem}.m4a`);
  const mp4 = join(out, `${stem}.mp4`);
  const audio = ["-map_metadata", "-1", "-c:a", "aac", "-b:a", "64k", "-flags:a", "+bitexact", "-fflags", "+bitexact"];
  runFfmpeg(["-y", "-i", wavPath, ...audio, "-f", "ipod", `${m4a}.part`]);
  renameSync(`${m4a}.part`, m4a);
  runFfmpeg([
    "-y", "-i", wavPath, "-f", "lavfi", "-i", `color=c=black:s=64x64:r=2:d=${Math.ceil(seconds) + 1}`,
    "-map", "1:v", "-map", "0:a", ...audio, "-c:v", "libx264", "-preset", "medium", "-crf", "40", "-pix_fmt", "yuv420p",
    "-threads", "1", "-x264-params", "threads=1:sliced-threads=0", "-flags:v", "+bitexact", "-shortest", "-movflags", "+faststart",
    "-f", "mp4", `${mp4}.part`,
  ]);
  renameSync(`${mp4}.part`, mp4);
}

function durationOfWav(path) {
  return round3((statSync(path).size - 44) / bytesPerSample / sampleRate);
}

/** Everything the plan names, built from the cache. Returns the manifest entries. */
async function build(lock, options, longRequests) {
  requireFfmpeg();
  const { out, cache } = options;
  const entries = [];

  // Sources first: all verified before any output is written.
  log("sources:");
  const zips = {};
  for (const source of lock.sources) await ensureSource(source, cache, options);
  zips.ami = openZip(cachePathOf(cache, lock.sources.find((s) => s.id === "ami-annotations")));

  // Utterances per language, loaded once for every recipe that names them.
  const languages = [...new Set([...lock.clips.filter((c) => c.items).map((c) => c.lang), ...Object.keys(lock.long)])];
  const utterancesOf = new Map();
  for (const lang of languages) {
    const ids = new Set();
    for (const clip of lock.clips.filter((c) => c.lang === lang && c.items)) clip.items.forEach(([id]) => ids.add(id));
    lock.long[lang]?.items.forEach(([id]) => ids.add(id));
    log(`decoding ${ids.size} utterances of ${lang}`);
    utterancesOf.set(lang, await loadUtterances(lock, cache, lang, { ids }));
  }

  // Clips.
  log("clips:");
  const amiPcm = new Map();
  for (const clip of lock.clips) {
    const stem = `clips/${clip.lang}/${clip.id}`;
    let wav, text, segments;
    if (clip.kind === "ami") {
      const source = lock.sources.find((s) => s.id === clip.audio);
      if (!amiPcm.has(source.id)) amiPcm.set(source.id, decodeToPcm(readFileSync(cachePathOf(cache, source))));
      const whole = amiPcm.get(source.id);
      const from = Math.round(clip.start * sampleRate) * bytesPerSample;
      const to = Math.round(clip.end * sampleRate) * bytesPerSample;
      wav = whole.subarray(from, to);
      const window = amiWindow(readAmiWords(zips.ami, clip.meeting), clip.start, clip.end);
      text = window.text;
      segments = window.segments;
    } else {
      const assembled = assemble(clip.items, utterancesOf.get(clip.lang), (speaker) => `${clip.lang}-${speaker}`);
      ({ pcm: wav, text, segments } = assembled);
    }
    const rttm = clip.speakers.length > 1 ? rttmLines(clip.id, segments) : null;
    buildWavSet(out, stem, clip.id, wav, text, rttm);
    entries.push(entryFor(out, `${stem}.wav`, {
      id: clip.id, type: "clip", language: clip.lang, durationSeconds: durationOfWav(join(out, `${stem}.wav`)),
      speakers: clip.speakers.length, ...datasetFields(lock, clip.dataset),
      reference: `${stem}.ref.txt`, rttm: rttm ? `${stem}.rttm` : null,
      ...(clip.kind === "ami" ? { meeting: clip.meeting, channel: clip.channel, startSeconds: clip.start, endSeconds: clip.end } : {}),
    }));
    log(`  ${clip.id}: ${entries.at(-1).durationSeconds} s, ${clip.speakers.length} speaker(s)`);
  }

  // Long files.
  log("long files:");
  const buildLong = (lang, minutes) => {
    const recipe = lock.long[lang];
    const items = minutes === 10 ? recipe.items : loopItems(recipe.items, minutes);
    const stem = `long/${lang}-${minutes}min`;
    const { pcm, text, segments } = assemble(items, utterancesOf.get(lang), (speaker) => `${lang}-${speaker}`);
    buildWavSet(out, stem, `${lang}-${minutes}min`, pcm, text, rttmLines(`${lang}-${minutes}min`, segments));
    entries.push(entryFor(out, `${stem}.wav`, {
      id: `${lang}-${minutes}min`, type: "long", language: lang, durationSeconds: durationOfWav(join(out, `${stem}.wav`)),
      speakers: recipe.speakers.length, ...datasetFields(lock, recipe.dataset),
      reference: `${stem}.ref.txt`, rttm: `${stem}.rttm`, utterances: items.length,
      ...(minutes === 10 ? {} : { loopedFrom: `${lang}-10min` }),
    }));
    log(`  ${lang}-${minutes}min: ${entries.at(-1).durationSeconds} s, ${items.length} utterances`);
    return stem;
  };
  for (const lang of Object.keys(lock.long)) buildLong(lang, 10);
  for (const { lang, minutes } of longRequests) buildLong(lang, minutes);

  // Containers from the English ten-minute file.
  const containerStem = "long/en-10min";
  buildContainers(out, join(out, `${containerStem}.wav`), containerStem, entries.find((e) => e.id === "en-10min").durationSeconds);
  for (const extension of ["m4a", "mp4"]) {
    entries.push(entryFor(out, `${containerStem}.${extension}`, {
      id: `en-10min-${extension}`, type: "container", language: "en", durationSeconds: entries.find((e) => e.id === "en-10min").durationSeconds,
      speakers: lock.long.en.speakers.length, ...datasetFields(lock, lock.long.en.dataset),
      reference: `${containerStem}.ref.txt`, rttm: `${containerStem}.rttm`, encodedFrom: "en-10min",
      codec: extension === "m4a" ? "AAC-LC 64 kbit/s" : "AAC-LC 64 kbit/s with a 64x64 black H.264 video track",
    }));
    log(`  en-10min.${extension}`);
  }

  // Noise.
  log("noise:");
  const noise = lock.noise;
  const synthetic = (name, source) => {
    const pcm = runFfmpeg(["-f", "lavfi", "-i", source, "-ac", "1", "-ar", String(sampleRate), "-f", "s16le", "pipe:1"]);
    return pcm;
  };
  const noiseSet = [
    ["silence-60s", "digital silence (ffmpeg anullsrc)", synthetic("silence", `anullsrc=r=${sampleRate}:cl=mono:d=${noise.seconds}`)],
    ["noise-white-60s", `white noise at about ${noise.levelDbfs} dBFS RMS (ffmpeg anoisesrc, seed ${noise.seed})`,
      synthetic("white", `anoisesrc=color=white:amplitude=${noise.whiteAmplitude}:seed=${noise.seed}:sample_rate=${sampleRate}:duration=${noise.seconds}`)],
    ["noise-pink-60s", `pink noise at about ${noise.levelDbfs} dBFS RMS (ffmpeg anoisesrc, seed ${noise.seed})`,
      synthetic("pink", `anoisesrc=color=pink:amplitude=${noise.pinkAmplitude}:seed=${noise.seed}:sample_rate=${sampleRate}:duration=${noise.seconds}`)],
  ];
  const demand = lock.sources.find((s) => s.id === noise.recorded.source);
  const demandZip = openZip(cachePathOf(cache, demand));
  const recording = decodeToPcm(demandZip.read(noise.recorded.entry));
  noiseSet.push(["noise-office-60s", `office room tone, DEMAND ${noise.recorded.entry}, the first ${noise.seconds} s`,
    recording.subarray(0, noise.seconds * sampleRate * bytesPerSample)]);
  for (const [name, description, pcm] of noiseSet) {
    if (pcm.length !== noise.seconds * sampleRate * bytesPerSample) throw new Error(`${name} came out ${secondsOf(pcm)} s long, not ${noise.seconds} s; the source is shorter than the recipe needs`);
    const path = `noise/${name}.wav`;
    writeAtomic(join(out, path), wavFile(pcm));
    const recorded = name === "noise-office-60s";
    entries.push(entryFor(out, path, {
      id: name, type: "noise", language: null, durationSeconds: noise.seconds, speakers: 0,
      ...datasetFields(lock, recorded ? "demand" : "synthetic"), reference: null, rttm: null, description,
    }));
    log(`  ${name}`);
  }
  return entries;
}

// ---------------------------------------------------------------------------
// Manifest, up-to-date check and verification
// ---------------------------------------------------------------------------

const manifestPath = (out) => join(out, "manifest.json");

function readManifest(out) {
  try {
    return JSON.parse(readFileSync(manifestPath(out), "utf8"));
  } catch {
    return null;
  }
}

/** Files of the manifest that are missing or differ from the recorded hash. */
function damagedEntries(out, manifest) {
  const bad = [];
  for (const entry of manifest.entries) {
    const path = join(out, entry.path);
    if (!existsSync(path) || statSync(path).size !== entry.bytes || sha256OfFile(path) !== entry.sha256) bad.push(entry.path);
  }
  return bad;
}

/** The ids a run must find in the manifest to call itself a no-op. */
function wantedIds(lock, longRequests) {
  return [
    ...lock.clips.map((c) => c.id),
    ...Object.keys(lock.long).map((l) => `${l}-10min`),
    "en-10min-m4a", "en-10min-mp4",
    "silence-60s", "noise-white-60s", "noise-pink-60s", "noise-office-60s",
    ...longRequests.map(({ lang, minutes }) => `${lang}-${minutes}min`),
  ];
}

function writeManifest(lock, lockSha, out, entries, previous) {
  const regenerated = new Set(entries.map((e) => e.id));
  const kept = (previous?.entries ?? []).filter((e) => !regenerated.has(e.id) && existsSync(join(out, e.path)) && sha256OfFile(join(out, e.path)) === e.sha256);
  const all = [...entries, ...kept].sort((a, b) => a.path.localeCompare(b.path));
  const manifest = {
    comment: "Written by bench/testset/fetch-testset.mjs; do not edit. Every path is relative to this folder.",
    builderRevision,
    lockSha256: lockSha,
    sampleRate,
    format: "WAV, 16 kHz, mono, PCM16",
    datasets: Object.fromEntries(Object.entries(lock.datasets).map(([k, d]) => [k, { name: d.name, licence: d.licence, attribution: d.attribution }])),
    entries: all,
  };
  writeAtomic(manifestPath(out), `${JSON.stringify(manifest, null, 2)}\n`);
  return manifest;
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

const usage = () => readFileSync(fileURLToPath(import.meta.url), "utf8").split("\n").slice(0, 36).filter((l) => l.startsWith("//")).map((l) => l.slice(3)).join("\n");

async function main() {
  const options = parseArgs(process.argv.slice(2));
  if (options.help) {
    log(usage());
    return 0;
  }
  if (typeof http.setGlobalProxyFromEnv === "function") http.setGlobalProxyFromEnv();
  const started = Date.now();
  const { lock, sha256: lockSha } = readLock(options.lock);
  const defaultLong = lock.defaultLong ?? [];
  const requested = [...defaultLong];
  for (const minutes of options.long) {
    for (const lang of options.longLang ?? Object.keys(lock.long)) {
      if (!lock.long[lang]) throw new Error(`--long-lang names ${lang}, which the lock has no long recipe for; the languages are ${Object.keys(lock.long).join(", ")}`);
      if (minutes !== 10 && !requested.some((r) => r.lang === lang && r.minutes === minutes)) requested.push({ lang, minutes });
    }
  }

  if (options.pin) {
    let changed = 0;
    for (const source of lock.sources) {
      if (source.sha256) continue;
      const result = await ensureSource(source, options.cache, { ...options, allowUnpinned: true });
      source.bytes = result.bytes;
      source.sha256 = result.sha256;
      changed++;
      log(`  pinned ${source.id}: ${source.bytes} bytes ${source.sha256}`);
    }
    writeFileSync(options.lock, formatLock(lock));
    log(`${changed} source(s) pinned in ${options.lock}`);
    return 0;
  }

  if (options.plan) {
    requireFfmpeg();
    log("sources:");
    for (const source of lock.sources) await ensureSource(source, options.cache, options);
    const planned = await planLock(lock, options.cache);
    writeFileSync(options.lock, formatLock(planned));
    log(`planned ${planned.clips.length} clips and ${Object.keys(planned.long).length} long recipes into ${options.lock}`);
    return 0;
  }

  if (options.verify) {
    const manifest = readManifest(options.out);
    if (!manifest) throw new Error(`${manifestPath(options.out)} is missing or unreadable; run the tool without --verify to build the test set`);
    const bad = damagedEntries(options.out, manifest);
    if (manifest.lockSha256 !== lockSha) bad.push("(manifest was built from a different lock)");
    for (const source of lock.sources) {
      const path = cachePathOf(options.cache, source);
      if (existsSync(path) && (statSync(path).size !== source.bytes || sha256OfFile(path) !== source.sha256)) bad.push(`cache: ${source.file}`);
    }
    if (bad.length) {
      process.stderr.write(`verification failed for ${bad.length} item(s):\n  ${bad.join("\n  ")}\nrun the tool without --verify (add --force to rebuild everything)\n`);
      return 1;
    }
    log(`verified ${manifest.entries.length} files against the manifest`);
    return 0;
  }

  // The fast path: nothing to do when the data folder is what the lock describes.
  const previous = readManifest(options.out);
  if (!options.force && previous && previous.builderRevision === builderRevision && previous.lockSha256 === lockSha) {
    const have = new Set(previous.entries.map((e) => e.id));
    if (wantedIds(lock, requested).every((id) => have.has(id)) && damagedEntries(options.out, previous).length === 0) {
      log(`up to date: ${previous.entries.length} files in ${options.out} verified against the manifest (${((Date.now() - started) / 1000).toFixed(1)} s)`);
      return 0;
    }
  }

  if (!lock.clips.length) throw new Error("the lock has no clip recipes; run with --plan first (a maintainer step), or restore testset.lock");
  const entries = await build(lock, options, requested);
  const manifest = writeManifest(lock, lockSha, options.out, entries, previous && previous.lockSha256 === lockSha ? previous : null);
  const total = manifest.entries.filter((e) => e.type === "clip" || e.type === "long").reduce((sum, e) => sum + e.durationSeconds, 0);
  log(`built ${manifest.entries.length} files (${(total / 60).toFixed(1)} min of speech clips and long files) in ${options.out}, ${((Date.now() - started) / 1000).toFixed(0)} s`);
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().then((code) => { process.exitCode = code; }, (error) => {
    process.stderr.write(`error: ${error.message}\n`);
    process.exitCode = 1;
  });
}
