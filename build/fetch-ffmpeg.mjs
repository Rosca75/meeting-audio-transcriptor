// The tool that fetches the pinned LGPL ffmpeg build. It reads build/ffmpeg.lock,
// downloads the archive of one platform, verifies its size and SHA-256, extracts
// only the ffmpeg binary and the licence text, and verifies those against the
// hashes the lock records, so a later run confirms the binary without the
// archive and does nothing when it is already right.
//
//   node build/fetch-ffmpeg.mjs [--rid linux-x64|win-x64] [--into <dir>]
//                               [--lock <file>] [--archive <file>] [--offline]
//
//   --rid      the platform build; the current operating system when absent.
//   --into     the folder holding one sub-folder per platform; tools/ffmpeg when
//              absent, so the binary lands in tools/ffmpeg/<rid>/.
//   --lock     another lock file, for a test that serves an archive of its own.
//   --archive  an archive already on disk, used instead of downloading it; it is
//              verified against the lock like a download.
//   --offline  download nothing: verify the extracted files and exit non-zero
//              naming what is missing.
//
// Extraction uses the system tar (a .tar.xz with `tar -xJf`, a .zip with
// `tar -xf`, which is bsdtar on Windows). A GNU tar cannot read a zip, so on a
// machine whose tar fails on one the tool falls back to unzip. tar runs in the
// target folder and is given the archive as a relative path with forward
// slashes, because a GNU tar (Git for Windows ships one) misreads a path with a
// drive letter, taking "D:" for a remote host. Node 22 or later, no dependency.
import { chmodSync, copyFileSync, existsSync, mkdirSync, readFileSync, renameSync, rmSync } from "node:fs";
import { join, relative, resolve, sep } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { download, reexecWithNetworkOptions, sha256Of, verify } from "./fetch-models.mjs";

/** The lock beside this tool. */
export const defaultLockPath = join(resolve(fileURLToPath(import.meta.url), ".."), "ffmpeg.lock");

/** The platform identifier of the machine running the tool, or null when it has no pinned build. */
export function currentRid() {
  if (process.arch !== "x64") {
    return null;
  }
  return process.platform === "win32" ? "win-x64" : process.platform === "linux" ? "linux-x64" : null;
}

/** Parses the lock and refuses one whose builds are not the shape the tool reads. */
export function readLock(path) {
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(path, "utf8"));
  } catch (error) {
    throw new Error(`${path} is not valid JSON (${error.message}); fix the lock rather than the tool`);
  }
  if (!parsed || !Array.isArray(parsed.builds)) {
    throw new Error(`${path} has no "builds" array; every platform is one element of it`);
  }
  for (const build of parsed.builds) {
    for (const field of ["rid", "version", "licence", "source", "url", "sha256", "archiveRoot"]) {
      if (typeof build[field] !== "string") {
        throw new Error(`${path}: a build lacks "${field}"; every build carries it`);
      }
    }
    for (const file of [build.binary, ...(build.licenceFiles ?? [])]) {
      if (!file || typeof file.member !== "string" || typeof file.name !== "string" || !/^[0-9a-f]{64}$/.test(file.sha256) || !Number.isInteger(file.bytes)) {
        throw new Error(`${path}: a file of build ${build.rid} needs member, name, bytes and sha256; fix the lock`);
      }
    }
  }
  return parsed;
}

/** The files a build leaves in its folder: the binary first, then the licence texts. */
function outputsOf(build) {
  return [build.binary, ...(build.licenceFiles ?? [])];
}

/** Runs a command, in a working folder when one is given, and returns its exit status; a missing program gets an actionable message. */
function runTool(program, args, cwd = undefined) {
  const result = spawnSync(program, args, { cwd, stdio: ["ignore", "ignore", "pipe"], encoding: "utf8" });
  if (result.error?.code === "ENOENT") {
    throw new Error(`the program "${program}" was not found on PATH; install it (tar is part of every supported operating system, and unzip is needed only where tar cannot read a zip)`);
  }
  return { status: result.status, stderr: result.stderr ?? "" };
}

/** Extracts the named members of an archive into a folder with the system tar, falling back to unzip for a zip. */
function extractMembers(archive, folder, members) {
  const isZip = archive.toLowerCase().endsWith(".zip");
  const archiveFromFolder = relative(resolve(folder), resolve(archive)).split(sep).join("/");
  const first = runTool("tar", [isZip ? "-xf" : "-xJf", archiveFromFolder, ...members], resolve(folder));
  if (first.status === 0) {
    return;
  }
  if (isZip) {
    const second = runTool("unzip", ["-q", "-o", archive, ...members, "-d", folder]);
    if (second.status === 0) {
      return;
    }
    throw new Error(`extracting ${archive} failed: tar said "${first.stderr.trim()}" and unzip said "${second.stderr.trim()}"; install bsdtar or unzip and run the tool again`);
  }
  throw new Error(`extracting ${archive} failed: ${first.stderr.trim()}; install tar with xz support and run the tool again`);
}

/** Verifies the files of a build in its folder; returns the list of problems. */
async function verifyOutputs(build, folder) {
  const problems = [];
  for (const file of outputsOf(build)) {
    const result = await verify(join(folder, file.name), { bytes: file.bytes, sha256: file.sha256 });
    if (!result.ok) {
      problems.push(`${join(folder, file.name)} ${result.reason === "missing" ? "is missing" : `differs from the lock (${result.reason}, got ${result.actual})`}`);
    }
  }
  return problems;
}

/** Installs or verifies one build; returns the problems (empty on success). */
export async function run(options, log = (line) => console.log(line)) {
  const lock = readLock(options.lock ?? defaultLockPath);
  const rid = options.rid ?? currentRid();
  if (!rid) {
    throw new Error(`no pinned ffmpeg build for ${process.platform}/${process.arch}; pass --rid linux-x64 or --rid win-x64`);
  }
  const build = lock.builds.find((candidate) => candidate.rid === rid);
  if (!build) {
    throw new Error(`the lock has no build for ${rid}; it holds ${lock.builds.map((b) => b.rid).join(", ")}`);
  }
  const folder = resolve(options.into ?? join("tools", "ffmpeg"), rid);

  const existing = await verifyOutputs(build, folder);
  if (existing.length === 0) {
    log(`ok       ${join(folder, build.binary.name)} (${build.version}, already verified)`);
    return [];
  }
  if (options.offline) {
    return existing;
  }

  // The work folder sits beside the target so the final move is a rename on one device.
  const work = join(folder, `.work-${process.pid}`);
  rmSync(work, { recursive: true, force: true });
  mkdirSync(work, { recursive: true });
  try {
    const archiveEntry = { path: build.url.split("/").pop(), url: build.url, sha256: build.sha256, bytes: build.bytes };
    let archive = join(work, archiveEntry.path);
    if (options.archive) {
      const given = await verify(resolve(options.archive), archiveEntry);
      if (!given.ok) {
        return [`${options.archive} does not match the lock (${given.reason}${given.actual ? `, got ${given.actual}` : ""}); pass the archive the lock names or omit --archive`];
      }
      archive = resolve(options.archive);
      log(`using    ${archive}`);
    } else {
      const result = await download(archiveEntry, archive, options);
      if (!result.ok) {
        return [`${build.url} downloaded with the wrong ${result.reason} (got ${result.actual}, wanted ${result.reason === "size" ? build.bytes : build.sha256}); the pin in the lock or the network is wrong`];
      }
      log(`fetched  ${archiveEntry.path} (${build.bytes} bytes)`);
    }

    const extracted = join(work, "out");
    mkdirSync(extracted);
    const members = outputsOf(build).map((file) => `${build.archiveRoot}/${file.member}`);
    extractMembers(archive, extracted, members);

    mkdirSync(folder, { recursive: true });
    for (const file of outputsOf(build)) {
      const source = join(extracted, build.archiveRoot, ...file.member.split("/"));
      const actual = existsSync(source) ? await sha256Of(source) : "missing";
      if (actual !== file.sha256) {
        return [`${file.member} extracted with sha256 ${actual}, wanted ${file.sha256}; the archive does not hold the pinned build`];
      }
      const target = join(folder, file.name);
      const part = `${target}.part`;
      copyFileSync(source, part);
      renameSync(part, target);
    }
    if (process.platform !== "win32") {
      chmodSync(join(folder, build.binary.name), 0o755);
    }
    log(`extracted ${join(folder, build.binary.name)} and ${build.licenceFiles?.length ?? 0} licence file(s)`);
    return [];
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
}

/** Reads the command line into options; an unknown flag is an error that names the usage. */
export function parseArguments(argv) {
  const options = { offline: false };
  const usage = "usage: node build/fetch-ffmpeg.mjs [--rid linux-x64|win-x64] [--into <dir>] [--lock <file>] [--archive <file>] [--offline]";
  const value = (index, flag) => {
    if (index + 1 >= argv.length) {
      throw new Error(`${flag} needs a value; ${usage}`);
    }
    return argv[index + 1];
  };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case "--rid": options.rid = value(i++, "--rid"); break;
      case "--into": options.into = value(i++, "--into"); break;
      case "--lock": options.lock = value(i++, "--lock"); break;
      case "--archive": options.archive = value(i++, "--archive"); break;
      case "--offline": options.offline = true; break;
      default: throw new Error(`unknown argument ${argv[i]}; ${usage}`);
    }
  }
  return options;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  if (!options.offline && !options.archive) {
    const code = reexecWithNetworkOptions(import.meta.url);
    if (code !== null) {
      process.exit(code);
    }
  }
  const problems = await run(options);
  if (problems.length > 0) {
    console.error("ffmpeg is not installed:");
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
