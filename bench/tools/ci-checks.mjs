// The checks the end-to-end CI job runs over what the transcriber and the test set builder wrote,
// so a job that skipped its work, or two decoders or two systems that disagree, fails by name.
//
// Usage:
//   node bench/tools/ci-checks.mjs transcripts <a.json> <b.json> [--max-percent 2]
//       the word difference between two JSON transcripts of one recording (the recogniser's raw
//       text, lower-cased, punctuation removed), as word edits over the longer transcript's words;
//       exits 1 above the limit
//   node bench/tools/ci-checks.mjs tests <results-folder> --suite <name> --min-run <n>
//       reads the xUnit TRX files under the folder, counts the tests of the suite that ran (passed
//       or failed) and the tests skipped because the benchmark audio was absent; exits 1 when fewer
//       than n ran or any skipped for that reason
//   node bench/tools/ci-checks.mjs hashes <data-folder>
//       prints every file of a test set folder with its SHA-256, as JSON
//   node bench/tools/ci-checks.mjs parity <a.json> <b.json>
//       compares two such listings made on two systems: every WAV, reference and RTTM file must
//       be byte-identical; the .m4a and .mp4 copies (and the manifest, which records their
//       hashes) are reported only, because their bytes depend on the ffmpeg build's encoders
//
// Node standard library only; it writes nothing but its report.
import { createHash } from "node:crypto";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

const log = (line) => process.stdout.write(`${line}\n`);

/** The value after a flag, or the fallback when the flag is absent. */
function flag(args, name, fallback) {
  const index = args.indexOf(name);
  return index >= 0 && index + 1 < args.length ? args[index + 1] : fallback;
}

/** The words of a transcript's raw text: lower case, letters, digits and apostrophes only. */
export function wordsOf(document) {
  const text = document.segments.map((segment) => segment.rawText ?? segment.text ?? "").join(" ");
  return text.toLowerCase().replace(/[^\p{L}\p{N}' ]+/gu, " ").split(/\s+/).filter(Boolean);
}

/** The word-level edit distance (substitutions, deletions, insertions) between two word lists. */
export function wordEdits(a, b) {
  let previous = Array.from({ length: b.length + 1 }, (_, j) => j);
  for (let i = 1; i <= a.length; i++) {
    const current = [i];
    for (let j = 1; j <= b.length; j++) {
      current[j] = Math.min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
    }
    previous = current;
  }
  return previous[b.length];
}

function transcripts(args) {
  const [first, second] = args;
  const limit = Number(flag(args, "--max-percent", "2"));
  const a = wordsOf(JSON.parse(readFileSync(first, "utf8")));
  const b = wordsOf(JSON.parse(readFileSync(second, "utf8")));
  const edits = wordEdits(a, b);
  const percent = (100 * edits) / Math.max(a.length, b.length, 1);
  log(`${first}: ${a.length} words; ${second}: ${b.length} words; ${edits} word edits, ${percent.toFixed(2)} % (limit ${limit} %)`);
  if (percent > limit) {
    log(`::error::the two transcripts differ by ${percent.toFixed(2)} % of their words, over ${limit} %; the decoders hand the recogniser different audio, so compare their sample counts and levels`);
    return 1;
  }
  return 0;
}

/** Every file under a folder whose name ends with the suffix. */
function filesUnder(folder, suffix) {
  const found = [];
  for (const name of readdirSync(folder)) {
    const path = join(folder, name);
    if (statSync(path).isDirectory()) found.push(...filesUnder(path, suffix));
    else if (name.endsWith(suffix)) found.push(path);
  }
  return found;
}

function tests(args) {
  const [folder] = args;
  const suite = flag(args, "--suite", "");
  const minimum = Number(flag(args, "--min-run", "1"));
  // A TRX file names its test assembly in the storage attribute of each test definition.
  const files = filesUnder(folder, ".trx").filter((path) => readFileSync(path, "utf8").includes(`${suite}.dll`));
  if (files.length === 0) {
    log(`::error::no TRX file of ${suite} under ${folder}; the test run did not write its report (--report-xunit-trx)`);
    return 1;
  }
  let ran = 0;
  let skipped = 0;
  const absent = [];
  for (const path of files) {
    const xml = readFileSync(path, "utf8");
    for (const match of xml.matchAll(/<UnitTestResult\b([^>]*?)(\/>|>([\s\S]*?)<\/UnitTestResult>)/g)) {
      const outcome = /outcome="(\w+)"/.exec(match[1])?.[1];
      const name = (/testName="([^"]*)"/.exec(match[1])?.[1] ?? "(unnamed)").replaceAll("&quot;", '"').replaceAll("&amp;", "&");
      if (outcome === "Passed" || outcome === "Failed") ran++;
      else {
        skipped++;
        // Every skip for a missing test set file names the builder or says so.
        if (/needs the benchmark audio|fetch-testset/.test(match[3] ?? "")) absent.push(name);
      }
    }
  }
  log(`${suite}: ${ran} tests ran, ${skipped} skipped, ${absent.length} of them for absent benchmark audio (${files.length} report file(s))`);
  for (const name of absent) log(`  skipped for absent audio: ${name}`);
  if (absent.length > 0 || ran < minimum) {
    log(`::error::${suite} ran ${ran} tests (wanted at least ${minimum}) and skipped ${absent.length} for absent benchmark audio (wanted none); check MAT_BENCH_DATA and the test set step`);
    return 1;
  }
  return 0;
}

/** Every file of a data folder with its SHA-256, by its path relative to the folder with forward slashes. */
function hashes(args) {
  const [folder] = args;
  const listing = {};
  for (const path of filesUnder(folder, "").sort()) {
    const relative = path.slice(folder.length + 1).split("\\").join("/");
    listing[relative] = createHash("sha256").update(readFileSync(path)).digest("hex");
  }
  log(JSON.stringify(listing, null, 2));
  return 0;
}

function parity(args) {
  const [first, second] = args;
  const a = JSON.parse(readFileSync(first, "utf8"));
  const b = JSON.parse(readFileSync(second, "utf8"));
  const failures = [];
  let same = 0;
  for (const path of [...new Set([...Object.keys(a), ...Object.keys(b)])].sort()) {
    // The manifest records the encoded copies' hashes, so it follows them.
    const encoded = path.endsWith(".m4a") || path.endsWith(".mp4") || path === "manifest.json";
    if (!(path in a) || !(path in b)) {
      failures.push(`${path} was built on one system only`);
    } else if (a[path] === b[path]) {
      same++;
    } else if (encoded) {
      log(`  ${path} differs; expected, because the encoded copies depend on the ffmpeg build's encoders`);
    } else {
      failures.push(`${path} differs between the systems: ${a[path]} and ${b[path]}; the builder must write the same samples and LF text everywhere`);
    }
  }
  log(`${same} of ${Object.keys(a).length} files are byte-identical on both systems`);
  for (const failure of failures) log(`::error::${failure}`);
  return failures.length ? 1 : 0;
}

const commands = { transcripts, tests, hashes, parity };
const [command, ...rest] = process.argv.slice(2);
if (!(command in commands)) {
  log(`usage: node bench/tools/ci-checks.mjs ${Object.keys(commands).join("|")} ...; the header of this file describes each`);
  process.exitCode = 1;
} else {
  process.exitCode = commands[command](rest);
}
