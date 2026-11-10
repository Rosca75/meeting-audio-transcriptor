// Tests of build/fetch-models.mjs against a loopback HTTP server the tests start:
// a verified download, an idempotent second run, a hash or size mismatch, offline
// mode, a missing file, a transient failure that is retried, the set selection,
// optional models, the cache folder, and a file derived from a source by a
// graph patch. Run with: node --test build/*.test.mjs
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import { after, before, describe, test } from "node:test";
import { fileURLToPath } from "node:url";
import { parseArguments, readLock, run, selectModels } from "./fetch-models.mjs";
import { derive } from "./onnx-patch.mjs";
import { tinyCtcModel } from "./tiny-onnx.mjs";

const sha = (buffer) => createHash("sha256").update(buffer).digest("hex");
const alpha = Buffer.from("alpha model bytes");
const beta = Buffer.from("beta tokenizer bytes");
const gamma = Buffer.from("gamma candidate bytes");
const sourceModel = tinyCtcModel();
let derivedEntry;

let server;
let base;
let work;
const hits = new Map();
let flakyLeft = 0;

before(async () => {
  work = mkdtempSync(join(tmpdir(), "fetch-models-test-"));
  server = createServer((request, response) => {
    hits.set(request.url, (hits.get(request.url) ?? 0) + 1);
    const send = (status, body) => {
      response.writeHead(status);
      response.end(body);
    };
    if (request.url === "/alpha.bin") send(200, alpha);
    else if (request.url === "/beta.bin") send(200, beta);
    else if (request.url === "/gamma.bin") send(200, gamma);
    else if (request.url === "/source.onnx") send(200, sourceModel);
    else if (request.url === "/flaky.bin") {
      if (flakyLeft-- > 0) send(503, "busy");
      else send(200, alpha);
    } else if (request.url === "/wrong.bin") send(200, Buffer.from("not what the lock says"));
    else send(404, "gone");
  });
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  base = `http://127.0.0.1:${server.address().port}`;
  const sourcePath = join(work, "reference-source.onnx");
  writeFileSync(sourcePath, sourceModel);
  const reference = await derive(sourcePath, join(work, "reference-derived.onnx"), "ctc-token-scores");
  derivedEntry = { path: "d/model.scores.onnx", derive: { from: "d/model.onnx", patch: "ctc-token-scores" }, sha256: reference.sha256, bytes: reference.bytes, role: "model" };
});

after(() => {
  server.close();
  rmSync(work, { recursive: true, force: true });
});

/** Writes a lock for the given model list and returns its path. */
function writeLock(name, models) {
  const path = join(work, `${name}.lock`);
  writeFileSync(path, JSON.stringify({ models }));
  return path;
}

const file = (path, url, buffer, role = "model") => ({ path, url: `${base}${url}`, sha256: sha(buffer), bytes: buffer.length, role });
const model = (name, set, files, extra = {}) => ({ name, set, licence: "MIT", source: "test", attribution: "test", files, ...extra });
const quiet = () => {};
const fast = { retryDelayMs: 1 };

describe("run", () => {
  test("downloads and verifies, then a second run downloads nothing", async () => {
    const lock = writeLock("basic", [model("m", "default", [file("m/alpha.bin", "/alpha.bin", alpha), file("m/beta.bin", "/beta.bin", beta, "tokenizer")])]);
    const into = join(work, "basic-out");
    hits.clear();
    assert.deepEqual(await run({ lock, into, models: [], set: "default", ...fast }, quiet), []);
    assert.equal(readFileSync(join(into, "m", "alpha.bin")).toString(), alpha.toString());
    assert.equal(existsSync(join(into, "m", "alpha.bin.part")), false);
    assert.equal(hits.get("/alpha.bin"), 1);

    const lines = [];
    assert.deepEqual(await run({ lock, into, models: [], set: "default", ...fast }, (line) => lines.push(line)), []);
    assert.equal(hits.get("/alpha.bin"), 1, "the second run must not touch the network");
    assert.ok(lines.every((line) => line.startsWith("ok ")), `got ${lines.join(" | ")}`);
  });

  test("a hash mismatch deletes the download and names both hashes", async () => {
    const served = Buffer.from("not what the lock says");
    const entry = { path: "w/wrong.bin", url: `${base}/wrong.bin`, sha256: sha(Buffer.from("something else")), bytes: served.length, role: "model" };
    const lock = writeLock("mismatch", [model("w", "default", [entry])]);
    const into = join(work, "mismatch-out");
    const problems = await run({ lock, into, models: [], set: "default", ...fast }, quiet);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /sha256 [0-9a-f]{64}, wanted [0-9a-f]{64}/);
    assert.equal(existsSync(join(into, "w", "wrong.bin")), false);
    assert.equal(existsSync(join(into, "w", "wrong.bin.part")), false);
  });

  test("a size mismatch is reported as a size", async () => {
    const entry = { ...file("s/alpha.bin", "/alpha.bin", alpha), bytes: alpha.length + 5 };
    const lock = writeLock("size", [model("s", "default", [entry])]);
    const problems = await run({ lock, into: join(work, "size-out"), models: [], set: "default", ...fast }, quiet);
    assert.match(problems[0], /bytes, wanted/);
  });

  test("an existing file with the wrong content is replaced", async () => {
    const lock = writeLock("replace", [model("r", "default", [file("r/alpha.bin", "/alpha.bin", alpha)])]);
    const into = join(work, "replace-out");
    mkdirSync(join(into, "r"), { recursive: true });
    writeFileSync(join(into, "r", "alpha.bin"), "stale");
    assert.deepEqual(await run({ lock, into, models: [], set: "default", ...fast }, quiet), []);
    assert.equal(readFileSync(join(into, "r", "alpha.bin")).toString(), alpha.toString());
  });

  test("offline mode downloads nothing and lists what is missing", async () => {
    const lock = writeLock("offline", [model("o", "default", [file("o/alpha.bin", "/alpha.bin", alpha)])]);
    const into = join(work, "offline-out");
    hits.clear();
    const problems = await run({ lock, into, models: [], set: "default", offline: true }, quiet);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /o\/alpha\.bin is missing/);
    assert.equal(hits.size, 0);

    mkdirSync(join(into, "o"), { recursive: true });
    writeFileSync(join(into, "o", "alpha.bin"), alpha);
    assert.deepEqual(await run({ lock, into, models: [], set: "default", offline: true }, quiet), []);
  });

  test("a missing remote file fails once, naming the status", async () => {
    const entry = { ...file("g/alpha.bin", "/nothing.bin", alpha) };
    const lock = writeLock("missing", [model("g", "default", [entry])]);
    hits.clear();
    const problems = await run({ lock, into: join(work, "missing-out"), models: [], set: "default", ...fast }, quiet);
    assert.match(problems[0], /answered 404/);
    assert.equal(hits.get("/nothing.bin"), 1, "a 404 is not retried");
  });

  test("a transient server error is retried", async () => {
    const lock = writeLock("flaky", [model("f", "default", [file("f/alpha.bin", "/flaky.bin", alpha)])]);
    flakyLeft = 2;
    hits.clear();
    assert.deepEqual(await run({ lock, into: join(work, "flaky-out"), models: [], set: "default", ...fast }, quiet), []);
    assert.equal(hits.get("/flaky.bin"), 3);
  });

  test("candidates are fetched only with --set all or --model", async () => {
    const lock = writeLock("sets", [
      model("d", "default", [file("d/alpha.bin", "/alpha.bin", alpha)]),
      model("c", "candidate", [file("c/gamma.bin", "/gamma.bin", gamma)]),
    ]);
    const into = join(work, "sets-out");
    assert.deepEqual(await run({ lock, into, models: [], set: "default", ...fast }, quiet), []);
    assert.equal(existsSync(join(into, "c", "gamma.bin")), false);
    assert.deepEqual(await run({ lock, into, models: ["c"], set: "default", ...fast }, quiet), []);
    assert.equal(existsSync(join(into, "c", "gamma.bin")), true);
    assert.deepEqual(selectModels(readLock(lock), { models: [], set: "all" }).map((m) => m.name), ["d", "c"]);
    assert.throws(() => selectModels(readLock(lock), { models: ["nope"] }), /unknown model nope/);
  });

  test("an optional model that cannot be fetched does not fail the run", async () => {
    const lock = writeLock("optional", [model("p", "default", [file("p/x.bin", "/nothing.bin", alpha)], { optional: true })]);
    const lines = [];
    assert.deepEqual(await run({ lock, into: join(work, "optional-out"), models: [], set: "default", ...fast }, (line) => lines.push(line)), []);
    assert.match(lines[0], /^skipped/);
  });

  test("a verified file in the cache is copied instead of downloaded", async () => {
    const lock = writeLock("cache", [model("k", "default", [file("k/alpha.bin", "/alpha.bin", alpha)])]);
    const cache = join(work, "cache-in");
    mkdirSync(join(cache, "k"), { recursive: true });
    writeFileSync(join(cache, "k", "alpha.bin"), alpha);
    hits.clear();
    const lines = [];
    assert.deepEqual(await run({ lock, into: join(work, "cache-out"), cache, models: [], set: "default", ...fast }, (line) => lines.push(line)), []);
    assert.equal(hits.size, 0);
    assert.match(lines[0], /^copied/);
  });
});

describe("derived files", () => {
  const sourceEntry = () => ({ ...file("d/model.onnx", "/source.onnx", sourceModel), role: "source" });
  const derivedModel = (entry = derivedEntry) => model("d", "default", [entry], { sources: [sourceEntry()] });

  test("a derived file is made from its downloaded source, which is not left in the folder", async () => {
    const lock = writeLock("derive", [derivedModel()]);
    const into = join(work, "derive-out");
    hits.clear();
    const lines = [];
    assert.deepEqual(await run({ lock, into, models: [], set: "default", ...fast }, (line) => lines.push(line)), []);
    assert.equal(existsSync(join(into, "d", "model.scores.onnx")), true);
    assert.equal(existsSync(join(into, "d", "model.onnx")), false, "the source is not shipped");
    assert.match(lines[0], /^derived/);
    assert.equal(hits.get("/source.onnx"), 1);

    hits.clear();
    assert.deepEqual(await run({ lock, into, models: [], set: "default", ...fast }, quiet), []);
    assert.equal(hits.size, 0, "a verified derived file needs no source");
  });

  test("--keep-sources installs the source and derives from it", async () => {
    const lock = writeLock("keep", [derivedModel()]);
    const into = join(work, "keep-out");
    assert.deepEqual(await run({ lock, into, models: [], set: "default", keepSources: true, ...fast }, quiet), []);
    assert.equal(readFileSync(join(into, "d", "model.onnx")).length, sourceModel.length);
    assert.equal(existsSync(join(into, "d", "model.scores.onnx")), true);
  });

  test("with a cache the downloaded source is kept there for the next derivation", async () => {
    const lock = writeLock("derive-cache", [derivedModel()]);
    const cache = join(work, "derive-cache");
    hits.clear();
    assert.deepEqual(await run({ lock, into: join(work, "derive-cache-out-1"), cache, models: [], set: "default", ...fast }, quiet), []);
    assert.equal(existsSync(join(cache, "d", "model.onnx")), true);
    assert.deepEqual(await run({ lock, into: join(work, "derive-cache-out-2"), cache, models: [], set: "default", ...fast }, quiet), []);
    assert.equal(hits.get("/source.onnx"), 1, "the second derivation reads the cached source");
  });

  test("a derived file that differs from the lock fails naming both hashes and is removed", async () => {
    const lock = writeLock("derive-wrong", [derivedModel({ ...derivedEntry, sha256: "0".repeat(64) })]);
    const into = join(work, "derive-wrong-out");
    const problems = await run({ lock, into, models: [], set: "default", ...fast }, quiet);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /has \d+ bytes and sha256 [0-9a-f]{64}, wanted \d+ bytes and 0{64}/);
    assert.equal(existsSync(join(into, "d", "model.scores.onnx")), false);
    assert.equal(existsSync(join(into, "d", "model.onnx")), false);
  });

  test("offline, a derived file is checked and its source is not asked for", async () => {
    const lock = writeLock("derive-offline", [derivedModel()]);
    const into = join(work, "derive-offline-out");
    const problems = await run({ lock, into, models: [], set: "default", offline: true }, quiet);
    assert.deepEqual(problems.map((p) => p.split(" ")[0]), ["d/model.scores.onnx"]);
  });

  test("a derived file must name one of the model's sources", () => {
    const path = writeLock("derive-bad", [model("d", "default", [{ ...derivedEntry, derive: { from: "d/other.onnx", patch: "ctc-token-scores" } }], { sources: [sourceEntry()] })]);
    assert.throws(() => readLock(path), /"from" must be the path of one of the model's sources/);
  });
});

describe("readLock and parseArguments", () => {
  test("a lock entry without a hash is refused", () => {
    const path = writeLock("bad", [model("b", "default", [{ path: "b/x", url: "u", bytes: 1, role: "model" }])]);
    assert.throws(() => readLock(path), /lacks "sha256"/);
  });

  test("the shipped lock parses and every url is pinned", () => {
    const lock = readLock(join(fileURLToPath(new URL(".", import.meta.url)), "models.lock"));
    const entries = lock.models.flatMap((m) => [...m.files, ...(m.sources ?? [])]);
    for (const entry of entries.filter((e) => e.derive === undefined)) {
      assert.doesNotMatch(entry.url, /\/(main|master)\//, `${entry.path} must pin a commit or a tag`);
    }
    assert.ok(entries.some((e) => e.derive !== undefined), "the shipped lock derives the Granite graph with token scores");
  });

  test("flags are parsed and an unknown flag is refused", () => {
    const options = parseArguments(["--into", "d", "--model", "a", "--model", "b", "--set", "all", "--offline", "--keep-sources"]);
    assert.deepEqual(options, { models: ["a", "b"], set: "all", offline: true, keepSources: true, into: "d" });
    assert.throws(() => parseArguments(["--bogus"]), /unknown argument --bogus/);
    assert.throws(() => parseArguments(["--set", "some"]), /default or all/);
  });

  test("the command line exits non-zero offline with a missing file", () => {
    const lock = writeLock("cli", [model("l", "default", [file("l/alpha.bin", "/alpha.bin", alpha)])]);
    const tool = fileURLToPath(new URL("./fetch-models.mjs", import.meta.url));
    const result = spawnSync(process.execPath, [tool, "--offline", "--lock", lock, "--into", join(work, "cli-out")], { encoding: "utf8" });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /l\/alpha\.bin is missing/);
  });
});
