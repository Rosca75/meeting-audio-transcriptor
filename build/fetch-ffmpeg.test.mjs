// Tests of build/fetch-ffmpeg.mjs: a small archive built with the system tar is
// served from a loopback HTTP server, and the tests cover a verified download
// and extraction of the binary and licence only, an idempotent second run, a
// wrong archive hash, offline mode, a missing archive and a local --archive.
// They skip when tar or xz is not installed. Run with: node --test build/*.test.mjs
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import { after, before, describe, test } from "node:test";
import { fileURLToPath } from "node:url";
import { parseArguments, readLock, run } from "./fetch-ffmpeg.mjs";

const sha = (buffer) => createHash("sha256").update(buffer).digest("hex");
const binary = Buffer.from("#!/bin/sh\necho fake ffmpeg\n");
const licence = Buffer.from("GNU LESSER GENERAL PUBLIC LICENSE test text\n");
const root = "ffmpeg-test-linux64-lgpl";

const tarWorks = spawnSync("tar", ["--version"]).status === 0 && spawnSync("xz", ["--version"]).status === 0;

let server;
let work;
let archiveBytes;
let lockPath;
let hits = 0;

before(async () => {
  work = mkdtempSync(join(tmpdir(), "fetch-ffmpeg-test-"));
  if (!tarWorks) {
    return;
  }
  // The archive holds the binary, the licence and files the tool must not extract.
  const staging = join(work, "staging", root);
  mkdirSync(join(staging, "bin"), { recursive: true });
  mkdirSync(join(staging, "doc"), { recursive: true });
  writeFileSync(join(staging, "bin", "ffmpeg"), binary);
  writeFileSync(join(staging, "bin", "ffplay"), "player");
  writeFileSync(join(staging, "doc", "readme.html"), "docs");
  writeFileSync(join(staging, "LICENSE.txt"), licence);
  const archive = join(work, "ffmpeg-test.tar.xz");
  assert.equal(spawnSync("tar", ["-cJf", archive, "-C", join(work, "staging"), root]).status, 0);
  archiveBytes = readFileSync(archive);

  server = createServer((request, response) => {
    hits++;
    if (request.url === "/ffmpeg-test.tar.xz") {
      response.writeHead(200);
      response.end(archiveBytes);
    } else {
      response.writeHead(404);
      response.end("gone");
    }
  });
  await new Promise((done) => server.listen(0, "127.0.0.1", done));
  const base = `http://127.0.0.1:${server.address().port}`;
  lockPath = join(work, "ffmpeg.lock");
  writeFileSync(lockPath, JSON.stringify({
    builds: [{
      rid: "linux-x64",
      version: "test",
      licence: "LGPL-3.0-or-later",
      source: "test",
      url: `${base}/ffmpeg-test.tar.xz`,
      bytes: archiveBytes.length,
      sha256: sha(archiveBytes),
      archiveRoot: root,
      binary: { member: "bin/ffmpeg", name: "ffmpeg", bytes: binary.length, sha256: sha(binary) },
      licenceFiles: [{ member: "LICENSE.txt", name: "LICENSE.txt", bytes: licence.length, sha256: sha(licence) }],
    }],
  }));
});

after(() => {
  server?.close();
  rmSync(work, { recursive: true, force: true });
});

const quiet = () => {};
const options = (into, extra = {}) => ({ lock: lockPath, rid: "linux-x64", into, retryDelayMs: 1, ...extra });

describe("run", { skip: !tarWorks && "needs tar and xz on PATH" }, () => {
  test("downloads, extracts only the binary and the licence, and is a no-op the second time", async () => {
    const into = join(work, "out1");
    hits = 0;
    assert.deepEqual(await run(options(into), quiet), []);
    assert.deepEqual(readdirSync(join(into, "linux-x64")).sort(), ["LICENSE.txt", "ffmpeg"]);
    assert.ok((statSync(join(into, "linux-x64", "ffmpeg")).mode & 0o111) !== 0, "the binary must be executable");
    assert.equal(hits, 1);

    const lines = [];
    assert.deepEqual(await run(options(into), (line) => lines.push(line)), []);
    assert.equal(hits, 1, "the second run must not download");
    assert.match(lines[0], /already verified/);
  });

  test("a tampered binary is detected and replaced", async () => {
    const into = join(work, "out2");
    assert.deepEqual(await run(options(into), quiet), []);
    writeFileSync(join(into, "linux-x64", "ffmpeg"), "tampered");
    assert.equal((await run(options(into, { offline: true }), quiet)).length, 1);
    assert.deepEqual(await run(options(into), quiet), []);
    assert.equal(readFileSync(join(into, "linux-x64", "ffmpeg")).toString(), binary.toString());
  });

  test("an archive with the wrong hash is refused and nothing is extracted", async () => {
    const bad = JSON.parse(readFileSync(lockPath, "utf8"));
    bad.builds[0].sha256 = "0".repeat(64);
    const badLock = join(work, "bad.lock");
    writeFileSync(badLock, JSON.stringify(bad));
    const into = join(work, "out3");
    const problems = await run({ ...options(into), lock: badLock }, quiet);
    assert.match(problems[0], /wrong hash/);
    assert.equal(existsSync(join(into, "linux-x64", "ffmpeg")), false);
    assert.deepEqual(readdirSync(join(into, "linux-x64")), [], "the work folder is removed");
  });

  test("offline mode names what is missing", async () => {
    const problems = await run(options(join(work, "out4"), { offline: true }), quiet);
    assert.match(problems[0], /ffmpeg is missing/);
  });

  test("a missing archive fails with the status", async () => {
    const bad = JSON.parse(readFileSync(lockPath, "utf8"));
    bad.builds[0].url = bad.builds[0].url.replace("ffmpeg-test", "nothing");
    const badLock = join(work, "missing.lock");
    writeFileSync(badLock, JSON.stringify(bad));
    await assert.rejects(run({ ...options(join(work, "out5")), lock: badLock }, quiet), /answered 404/);
  });

  test("a local --archive is verified and used without the network", async () => {
    const into = join(work, "out6");
    hits = 0;
    assert.deepEqual(await run(options(into, { archive: join(work, "ffmpeg-test.tar.xz") }), quiet), []);
    assert.equal(hits, 0);
    const problems = await run(options(join(work, "out7"), { archive: join(work, "staging", root, "LICENSE.txt") }), quiet);
    assert.match(problems[0], /does not match the lock/);
  });
});

describe("lock and arguments", () => {
  test("the shipped lock has both platforms with a pinned binary hash", () => {
    const lock = readLock(join(fileURLToPath(new URL(".", import.meta.url)), "ffmpeg.lock"));
    assert.deepEqual(lock.builds.map((b) => b.rid).sort(), ["linux-x64", "win-x64"]);
    for (const build of lock.builds) {
      assert.match(build.binary.sha256, /^[0-9a-f]{64}$/);
      assert.equal(build.licence, "LGPL-3.0-or-later");
    }
  });

  test("flags are parsed and an unknown flag is refused", () => {
    assert.deepEqual(parseArguments(["--rid", "win-x64", "--into", "d", "--offline"]), { offline: true, rid: "win-x64", into: "d" });
    assert.throws(() => parseArguments(["--x"]), /unknown argument --x/);
  });
});
