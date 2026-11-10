// The one tool that downloads the models. It reads build/models.lock, which pins
// every file by address, size and SHA-256, and puts the files where the
// application and the test suites read them: a models folder holding one
// sub-folder per model, each file at the relative path the lock names. The
// application itself never downloads anything.
//
//   node build/fetch-models.mjs [--into <dir>] [--lock <file>] [--set default|all]
//                               [--model <name>]... [--offline] [--cache <dir>]
//
//   --into     the models folder; ./models when absent.
//   --lock     another lock file, for a test that serves files of its own.
//   --set      default fetches the models a release ships (the default); all
//              also fetches the candidates only benchmarks read.
//   --model    one model from the lock; may be repeated; it overrides --set.
//   --offline  download nothing: verify what is in the folder against the lock
//              and exit non-zero listing every missing or mismatched file.
//   --cache    a folder of files already downloaded; a file whose size and hash
//              match the lock is copied from it instead of being fetched. It is
//              searched as <cache>/<lock path>, <cache>/<file name> and
//              <cache>/<model name>/<file name>.
//
// Every file is verified by size and SHA-256, whether downloaded, copied or
// already present. A download is streamed to <file>.part and renamed only after
// it verifies, so a half-written file never reads as installed, and a file that
// is already right is left alone, which makes a second run a fast no-op. A
// mismatch deletes the file and fails naming the expected and the actual hash.
//
// Downloads honour HTTPS_PROXY and the system certificate store: both are
// process-start options of Node, so a run that downloads re-executes itself
// once with them set. Transient failures (network errors, 429, 5xx) are retried
// with a growing delay. Node 22 or later, no dependency.
import { createHash } from "node:crypto";
import { copyFileSync, createReadStream, createWriteStream, existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync } from "node:fs";
import { dirname, join, resolve, basename } from "node:path";
import { spawnSync } from "node:child_process";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";
import { fileURLToPath } from "node:url";

/** The lock beside this tool, which is the lock unless a test hands in another. */
export const defaultLockPath = join(dirname(fileURLToPath(import.meta.url)), "models.lock");

/** The fields every file entry of the lock carries. */
export const entryFields = ["path", "url", "sha256", "bytes", "role"];

/** A neutral client name: the tool is a plain downloader and says nothing about its user. */
const userAgent = "model-fetch/1";

/** Parses the lock and refuses one whose entries are not the shape the tool reads. */
export function readLock(path) {
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(path, "utf8"));
  } catch (error) {
    throw new Error(`${path} is not valid JSON (${error.message}); fix the lock rather than the tool`);
  }
  if (!parsed || !Array.isArray(parsed.models)) {
    throw new Error(`${path} has no "models" array; every model is one element of it`);
  }
  const names = new Set();
  for (const model of parsed.models) {
    for (const field of ["name", "set", "licence", "source", "attribution"]) {
      if (typeof model[field] !== "string" || model[field].length === 0) {
        throw new Error(`${path}: a model has no "${field}" string; every model carries name, set, licence, source and attribution`);
      }
    }
    if (model.set !== "default" && model.set !== "candidate") {
      throw new Error(`${path}: the model ${model.name} has set "${model.set}"; use "default" or "candidate"`);
    }
    if (names.has(model.name)) {
      throw new Error(`${path}: the model ${model.name} appears twice; names are unique`);
    }
    names.add(model.name);
    if (!Array.isArray(model.files) || model.files.length === 0) {
      throw new Error(`${path}: the model ${model.name} has no "files" array; every model lists its files`);
    }
    for (const entry of model.files) {
      for (const field of entryFields) {
        if (!(field in entry)) {
          throw new Error(`${path}: a file of ${model.name} lacks "${field}"; every file has ${entryFields.join(", ")}`);
        }
      }
      if (!/^[0-9a-f]{64}$/.test(entry.sha256) || !Number.isInteger(entry.bytes) || entry.bytes < 0) {
        throw new Error(`${path}: ${entry.path} has a malformed sha256 or bytes; write the lowercase hex hash and the integer size`);
      }
      if (entry.path.startsWith("/") || entry.path.split("/").includes("..")) {
        throw new Error(`${path}: ${entry.path} is not a relative path inside the models folder`);
      }
    }
  }
  return parsed;
}

/** SHA-256 of a file, streamed because a model file can exceed a gigabyte. */
export async function sha256Of(path) {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(path)) {
    hash.update(chunk);
  }
  return hash.digest("hex");
}

/** Checks a file against its entry: {ok} or {ok:false, reason: missing|size|hash, actual}. */
export async function verify(path, entry) {
  if (!existsSync(path)) {
    return { ok: false, reason: "missing" };
  }
  const bytes = statSync(path).size;
  if (bytes !== entry.bytes) {
    return { ok: false, reason: "size", actual: String(bytes) };
  }
  const actual = await sha256Of(path);
  return actual === entry.sha256 ? { ok: true } : { ok: false, reason: "hash", actual };
}

/** A generous timeout that grows with the file: a floor of half a megabyte a second, at least ten minutes. */
export function timeoutFor(entry) {
  return Math.max(10 * 60 * 1000, Math.ceil((entry.bytes / (512 * 1024)) * 1000));
}

/** Whether an HTTP status is worth another attempt. */
function isTransientStatus(status) {
  return status === 408 || status === 429 || status >= 500;
}

/** One download attempt into <target>.part; returns {ok} or a verification failure. Throws on transport errors. */
async function downloadOnce(entry, target) {
  const part = `${target}.part`;
  const response = await fetch(entry.url, {
    headers: { "User-Agent": userAgent },
    signal: AbortSignal.timeout(timeoutFor(entry)),
  });
  if (!response.ok || !response.body) {
    const error = new Error(`downloading ${entry.path} from ${entry.url} answered ${response.status}; check the url in the lock and the proxy settings`);
    error.transient = isTransientStatus(response.status);
    throw error;
  }
  const hash = createHash("sha256");
  let bytes = 0;
  const source = Readable.fromWeb(response.body);
  source.on("data", (chunk) => {
    hash.update(chunk);
    bytes += chunk.length;
  });
  try {
    await pipeline(source, createWriteStream(part));
  } catch (error) {
    rmSync(part, { force: true });
    error.transient = true;
    throw error;
  }
  const actual = hash.digest("hex");
  if (bytes !== entry.bytes || actual !== entry.sha256) {
    rmSync(part, { force: true });
    return bytes !== entry.bytes
      ? { ok: false, reason: "size", actual: String(bytes) }
      : { ok: false, reason: "hash", actual };
  }
  renameSync(part, target);
  return { ok: true };
}

/**
 * Downloads one entry with retries on transient errors. A verified file is
 * renamed into place; a file that fails verification is deleted and reported,
 * and is never retried, because the same bytes would come again.
 */
export async function download(entry, target, { attempts = 4, retryDelayMs = 2000 } = {}) {
  mkdirSync(dirname(target), { recursive: true });
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      return await downloadOnce(entry, target);
    } catch (error) {
      lastError = error;
      const transient = error.transient !== false;
      if (!transient || attempt === attempts) {
        break;
      }
      await new Promise((done) => setTimeout(done, retryDelayMs * 2 ** (attempt - 1)));
    }
  }
  const cause = lastError.cause?.code ?? lastError.cause?.message ?? "";
  throw new Error(`${lastError.message}${cause ? ` (${cause})` : ""}`);
}

/** Describes a failed verification in one actionable sentence. */
function describeFailure(entry, result) {
  if (result.reason === "missing") {
    return `${entry.path} is missing; run node build/fetch-models.mjs without --offline, or place the file there (sha256 ${entry.sha256})`;
  }
  if (result.reason === "size") {
    return `${entry.path} has ${result.actual} bytes, wanted ${entry.bytes}; delete it and fetch it again`;
  }
  return `${entry.path} has sha256 ${result.actual}, wanted ${entry.sha256}; delete it and fetch it again, and check the url in the lock if it keeps failing`;
}

/** Looks for a verified copy of an entry in the cache folder; returns its path or null. */
async function findInCache(cache, modelName, entry) {
  if (!cache) {
    return null;
  }
  const candidates = [join(cache, entry.path), join(cache, basename(entry.path)), join(cache, modelName, basename(entry.path))];
  for (const candidate of candidates) {
    if (existsSync(candidate) && (await verify(candidate, entry)).ok) {
      return candidate;
    }
  }
  return null;
}

/** The models the options select: --model names win, else the default set, or everything with --set all. */
export function selectModels(lock, { models = [], set = "default" }) {
  if (models.length > 0) {
    const known = new Map(lock.models.map((model) => [model.name, model]));
    const unknown = models.filter((name) => !known.has(name));
    if (unknown.length > 0) {
      throw new Error(`unknown model ${unknown.join(", ")}; the lock holds ${[...known.keys()].join(", ")}`);
    }
    return models.map((name) => known.get(name));
  }
  return lock.models.filter((model) => set === "all" || model.set === "default");
}

/** Installs or verifies every selected model; returns the list of problems (empty on success). */
export async function run(options, log = (line) => console.log(line)) {
  const lock = readLock(options.lock ?? defaultLockPath);
  const into = resolve(options.into ?? "models");
  const problems = [];
  for (const model of selectModels(lock, options)) {
    for (const entry of model.files) {
      const target = join(into, ...entry.path.split("/"));
      const optional = model.optional === true;
      const present = await verify(target, entry);
      if (present.ok) {
        log(`ok       ${entry.path} (${entry.bytes} bytes, already verified)`);
        continue;
      }
      if (options.offline) {
        const text = describeFailure(entry, present);
        if (optional) {
          log(`skipped  ${entry.path} (optional model ${model.name}: ${text})`);
        } else {
          problems.push(text);
        }
        continue;
      }
      if (present.reason !== "missing") {
        rmSync(target, { force: true });
      }
      const cached = await findInCache(options.cache, model.name, entry);
      if (cached) {
        mkdirSync(dirname(target), { recursive: true });
        const part = `${target}.part`;
        copyFileSync(cached, part);
        renameSync(part, target);
        log(`copied   ${entry.path} (${entry.bytes} bytes, from the cache)`);
        continue;
      }
      try {
        const result = await download(entry, target, options);
        if (result.ok) {
          log(`fetched  ${entry.path} (${entry.bytes} bytes)`);
        } else {
          const text = describeFailure(entry, result);
          if (optional) {
            log(`skipped  ${entry.path} (optional model ${model.name}: ${text})`);
          } else {
            problems.push(text);
          }
        }
      } catch (error) {
        if (optional) {
          log(`skipped  ${entry.path} (optional model ${model.name}: ${error.message})`);
        } else {
          problems.push(`${entry.path}: ${error.message}`);
        }
      }
    }
  }
  return problems;
}

/** Reads the command line into options; an unknown flag is an error that names the usage. */
export function parseArguments(argv) {
  const options = { models: [], set: "default", offline: false };
  const usage = "usage: node build/fetch-models.mjs [--into <dir>] [--lock <file>] [--set default|all] [--model <name>]... [--offline] [--cache <dir>]";
  const value = (index, flag) => {
    if (index + 1 >= argv.length) {
      throw new Error(`${flag} needs a value; ${usage}`);
    }
    return argv[index + 1];
  };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case "--into": options.into = value(i++, "--into"); break;
      case "--lock": options.lock = value(i++, "--lock"); break;
      case "--cache": options.cache = value(i++, "--cache"); break;
      case "--model": options.models.push(value(i++, "--model")); break;
      case "--set": {
        options.set = value(i++, "--set");
        if (options.set !== "default" && options.set !== "all") {
          throw new Error(`--set takes default or all, got ${options.set}; ${usage}`);
        }
        break;
      }
      case "--offline": options.offline = true; break;
      default: throw new Error(`unknown argument ${argv[i]}; ${usage}`);
    }
  }
  return options;
}

/**
 * Re-executes the calling tool once with the proxy and system certificate options
 * Node reads at start-up; returns the child's exit code, or null when the options
 * are already set. fetch-ffmpeg.mjs shares it.
 */
export function reexecWithNetworkOptions(scriptUrl = import.meta.url) {
  const hasCa = process.execArgv.includes("--use-system-ca");
  const hasProxy = process.env.NODE_USE_ENV_PROXY === "1";
  if (hasCa && hasProxy) {
    return null;
  }
  const result = spawnSync(
    process.execPath,
    [...process.execArgv, "--use-system-ca", fileURLToPath(scriptUrl), ...process.argv.slice(2)],
    { stdio: "inherit", env: { ...process.env, NODE_USE_ENV_PROXY: "1" } },
  );
  return result.status ?? 1;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  if (!options.offline) {
    const code = reexecWithNetworkOptions();
    if (code !== null) {
      process.exit(code);
    }
  }
  const problems = await run(options);
  if (problems.length > 0) {
    console.error(options.offline
      ? "these files are missing or differ from build/models.lock; fetch them without --offline or place each one by hand:"
      : "these files could not be installed:");
    for (const problem of problems) {
      console.error("  " + problem);
    }
    process.exit(1);
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error(error.message);
    process.exit(1);
  });
}
