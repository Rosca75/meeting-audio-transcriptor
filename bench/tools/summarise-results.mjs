// Prints the tables of docs/BENCHMARKS.md from the committed acceptance result files, so no published number is typed by hand.
//
// Usage: node bench/tools/summarise-results.mjs [results-folder] [date]
//   results-folder  where the result files are (default: bench/results next to this script's repository root)
//   date            the date prefix of the acceptance files (default: 2026-11-10)
//
// It reads `<date>-accept-<label>.json` and `<date>-phase1-candidates.json`, sums the per-file
// substitution, deletion, insertion and reference-word counts, and prints Markdown. It uses only
// the Node standard library and writes nothing.
import { readFileSync, existsSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const folder = resolve(process.argv[2] ?? join(root, "bench", "results"));
const date = process.argv[3] ?? "2026-11-10";

/** Reads one acceptance file by its label; null when it does not exist. */
function accept(label) {
  const path = join(folder, `${date}-accept-${label}.json`);
  return existsSync(path) ? JSON.parse(readFileSync(path, "utf8")) : null;
}

const pct = (x, digits = 2) => (x * 100).toFixed(digits);
const fixed = (x, digits = 1) => Number(x).toFixed(digits);
const mb = (bytes) => (bytes / 1e9).toFixed(2);

/** Sums the error counts of a list of files for one of the two word error rates ("werRaw" or "werCleaned"). */
function sumWer(files, key) {
  const total = { s: 0, d: 0, i: 0, n: 0 };
  for (const file of files) {
    const wer = file[key];
    if (!wer) continue;
    total.s += wer.substitutions;
    total.d += wer.deletions;
    total.i += wer.insertions;
    total.n += wer.referenceWords;
  }
  return { ...total, wer: total.n ? (total.s + total.d + total.i) / total.n : null };
}

const scored = (files) => files.filter((file) => file.werRaw);
const english = (files) => files.filter((file) => file.group === "en-read" || file.group === "en-ami");
const inGroup = (files, group) => files.filter((file) => file.group === group);
const ofType = (files, ...types) => files.filter((file) => types.includes(file.type));

/** All the files of an acceptance mask: the clips and the 10-minute files, whichever result file holds them. */
function maskFiles(mask) {
  const labels = { avx2: ["clips", "long"], "avx2vnni": ["long"], unmasked: ["long"] }[mask].map((l) => `${mask}-${l}`);
  return labels.flatMap((label) => accept(label)?.files ?? []).filter((file) => file.type === "clip" || file.type === "long");
}

function header(text) {
  console.log(`\n### ${text}\n`);
}

// ---------------------------------------------------------------------------------------------
// 1. The two-hour headline.
// ---------------------------------------------------------------------------------------------
header("Two-hour recording, end to end (the file with `type: extra`)");
console.log("| Configuration | Mask | Threads | Speakers | Audio s | Wall s | Times real time | Wall (min) | Peak GB |");
console.log("|---|---|---|---|---|---|---|---|---|");
const headline = [
  ["avx2-120min-m4a", "AVX2 only, with speakers"],
  ["avx2-120min-wav-nospk", "AVX2 only, without speakers (WAV)"],
  ["avx2vnni-120min-m4a", "AVX2 + AVX-VNNI"],
  ["unmasked-120min-m4a", "unmasked"],
  ["avx2-120min-m4a-8threads", "AVX2 only, 8 threads"],
];
for (const [label, name] of headline) {
  const r = accept(label);
  const f = r.files.find((file) => file.type === "extra");
  const wall = f.wallSeconds[0];
  console.log(`| ${name} | ${r.machine.cpuIdMask ?? "none"} | ${r.settings.threads} | ${r.settings.speakerSeparation ? "yes" : "no"} | ${fixed(f.audioSeconds, 1)} | ${fixed(wall, 1)} | ${fixed(f.throughput, 1)} | ${fixed(wall / 60, 1)} | ${mb(f.peakWorkingSetBytes)} |`);
}

header("Scaling: the same recording looped to 10, 30 and 120 minutes (AVX2 only, with speakers)");
console.log("| Length | File | Audio s | Wall s | Times real time | Peak GB | Result file |");
console.log("|---|---|---|---|---|---|---|");
for (const [label, id, name] of [["avx2-long", "en-10min", "10 minutes"], ["avx2-30min", "en-30min", "30 minutes"], ["avx2-120min-m4a", "en-120min", "120 minutes (m4a)"]]) {
  const r = accept(label);
  const f = r.files.find((file) => file.id === id);
  console.log(`| ${name} | ${id} | ${fixed(f.audioSeconds, 1)} | ${fixed(f.wallSeconds[0], 1)} | ${fixed(f.throughput, 1)} | ${mb(f.peakWorkingSetBytes)} | ${date}-accept-${label}.json |`);
}

header("Per-stage seconds of the two-hour files");
console.log("| Label | Pipeline s | Streaming s | Decode and voice activity s (producer) | Recognition s (summed over workers) | Recognition per worker s | Workers busy | Speakers s | Cleaning s | Writing s |");
console.log("|---|---|---|---|---|---|---|---|---|---|");
for (const [label] of headline) {
  const r = accept(label);
  const f = r.files.find((file) => file.type === "extra");
  const s = f.stageSeconds;
  const perWorker = s.recognitionWorkers / r.settings.threads;
  console.log(`| ${label} | ${fixed(s.pipeline)} | ${fixed(s.streaming)} | ${fixed(s.decodeAndVad)} | ${fixed(s.recognitionWorkers)} | ${fixed(perWorker)} | ${pct(perWorker / s.streaming, 0)} % | ${fixed(s.speakers, 2)} | ${fixed(s.cleaning, 2)} | ${fixed(s.writing, 2)} |`);
}

// ---------------------------------------------------------------------------------------------
// 2. Accuracy.
// ---------------------------------------------------------------------------------------------
function werRow(name, files) {
  const raw = sumWer(files, "werRaw");
  const cleaned = sumWer(files, "werCleaned");
  return `| ${name} | ${raw.n} | ${raw.s} / ${raw.d} / ${raw.i} | ${pct(raw.wer)} | ${pct(cleaned.wer)} |`;
}

function accuracyTable(files) {
  console.log("| Language | Reference words | S / D / I (raw) | WER raw % | WER cleaned % |");
  console.log("|---|---|---|---|---|");
  const scoredFiles = scored(files);
  console.log(werRow("English read", inGroup(scoredFiles, "en-read")));
  console.log(werRow("English meetings (AMI)", inGroup(scoredFiles, "en-ami")));
  console.log(werRow("English combined", english(scoredFiles)));
  console.log(werRow("French", inGroup(scoredFiles, "fr")));
  console.log(werRow("German", inGroup(scoredFiles, "de")));
}

const avx2 = maskFiles("avx2");
header("Accuracy under the AVX2-only mask: clips (16 files, `avx2-clips`)");
accuracyTable(ofType(avx2, "clip"));
header("Accuracy under the AVX2-only mask: 10-minute files (`avx2-long`; English meetings have none)");
accuracyTable(ofType(avx2, "long"));
header("Accuracy under the AVX2-only mask: clips and 10-minute files combined");
accuracyTable(avx2);

header("Accuracy by mask, clips and 10-minute files combined (word error rate in percent, raw / cleaned)");
console.log("| Language | AVX2 only | AVX2 + AVX-VNNI | Unmasked |");
console.log("|---|---|---|---|");
const byMask = { avx2, "avx2 + vnni": maskFiles("avx2vnni"), unmasked: maskFiles("unmasked") };
for (const [name, pick] of [
  ["English read", (f) => inGroup(f, "en-read")],
  ["English meetings (AMI)", (f) => inGroup(f, "en-ami")],
  ["English combined", english],
  ["French", (f) => inGroup(f, "fr")],
  ["German", (f) => inGroup(f, "de")],
]) {
  const cells = Object.values(byMask).map((files) => {
    const picked = scored(pick(files));
    return `${pct(sumWer(picked, "werRaw").wer)} / ${pct(sumWer(picked, "werCleaned").wer)}`;
  });
  console.log(`| ${name} | ${cells.join(" | ")} |`);
}

header("The 10-minute files with the runtime's fast 8-bit kernels on the AVX2-only mask (`avx2-fastkernels-long`) against the exact kernels (`avx2-long`), raw WER %");
console.log("| Kernels | English | French | German | Times real time (group) |");
console.log("|---|---|---|---|---|");
for (const [name, label] of [["fast", "avx2-fastkernels-long"], ["exact (shipped)", "avx2-long"]]) {
  const r = accept(label);
  const cells = ["en-read", "fr", "de"].map((g) => pct(sumWer(inGroup(r.files, g), "werRaw").wer));
  const wall = r.files.reduce((sum, f) => sum + f.wallSeconds[0], 0);
  const audio = r.files.reduce((sum, f) => sum + f.audioSeconds, 0);
  console.log(`| ${name} | ${cells.join(" | ")} | ${fixed(audio / wall, 1)} |`);
}

// ---------------------------------------------------------------------------------------------
// 3. Cleaning.
// ---------------------------------------------------------------------------------------------
header("Cleaning, AVX2-only mask, clips and 10-minute files combined");
console.log("| Language | Content words kept | Of | Retention | Words read | Words removed | Share removed % |");
console.log("|---|---|---|---|---|---|---|");
const languageOf = (code) => avx2.filter((f) => f.language === code);
for (const [name, files] of [["English", languageOf("en")], ["French", languageOf("fr")], ["German", languageOf("de")], ["All", avx2]]) {
  const retained = files.reduce((s, f) => s + f.retention.retained, 0);
  const total = files.reduce((s, f) => s + f.retention.total, 0);
  const words = files.reduce((s, f) => s + f.words, 0);
  const removed = files.reduce((s, f) => s + f.wordsRemoved, 0);
  console.log(`| ${name} | ${retained} | ${total} | ${(retained / total).toFixed(4)} | ${words} | ${removed} | ${pct(removed / words)} |`);
}
header("Noise-only inputs (`avx2-noise`)");
const noise = accept("avx2-noise");
console.log("| File | Seconds | Words produced | Outcome |");
console.log("|---|---|---|---|");
for (const f of noise.files.filter((file) => file.type === "noise")) {
  console.log(`| ${f.id} | ${fixed(f.audioSeconds, 0)} | ${f.words} | ${f.outcome} |`);
}
console.log(`\nTotal ${noise.noise.words} words over ${noise.noise.files} files, gate ${noise.noise.gate}.`);
header("Container copies of the English 10-minute file (`avx2-noise`)");
console.log("| File | Wall s | Times real time | Words |");
console.log("|---|---|---|---|");
for (const f of noise.files.filter((file) => file.type === "container")) {
  console.log(`| ${f.id} | ${fixed(f.wallSeconds[0], 1)} | ${fixed(f.throughput, 1)} | ${f.words} |`);
}

// ---------------------------------------------------------------------------------------------
// 4. Speakers.
// ---------------------------------------------------------------------------------------------
function speakerTable(label, types) {
  const r = accept(label);
  console.log("| Group | Files | Error rate, overlap excluded | Error rate, overlap included | Confusion share | Attribution accuracy | Count exact | Count error (mean absolute) | Estimated / reference per file |");
  console.log("|---|---|---|---|---|---|---|---|---|");
  for (const g of r.groups.filter((group) => group.speakers)) {
    const s = g.speakers;
    const files = r.files.filter((f) => f.group === g.group && types.includes(f.type) && f.speakerScore);
    const counts = files.map((f) => `${f.speakerScore.estimatedSpeakers}/${f.speakerScore.referenceSpeakers}`).join(", ");
    console.log(`| ${g.group} | ${s.files} | ${pct(s.derOverlapExcluded, 1)} % | ${pct(s.derOverlapIncluded, 1)} % | ${pct(s.confusionOverlapExcluded, 1)} % | ${pct(s.attributionAccuracy, 1)} % | ${s.speakerCountExact} of ${s.files} | ${fixed(s.speakerCountMeanAbsoluteError, 2)} | ${counts} |`);
  }
}
header("Speakers, clips (`avx2-clips`)");
speakerTable("avx2-clips", ["clip"]);
header("Speakers, 10-minute files (`avx2-long`)");
speakerTable("avx2-long", ["long"]);

// ---------------------------------------------------------------------------------------------
// 5. The recogniser candidates of the Python spike.
// ---------------------------------------------------------------------------------------------
const spikePath = join(folder, `${date}-phase1-candidates.json`);
if (existsSync(spikePath)) {
  const spike = JSON.parse(readFileSync(spikePath, "utf8"));
  const sum = (c, groups) => {
    const t = { S: 0, D: 0, I: 0, N: 0 };
    for (const g of groups) for (const k of Object.keys(t)) t[k] += spike.candidates[c].wer_per_group[g][k];
    return t;
  };
  const rate = (t) => (t.S + t.D + t.I) / t.N;
  header("Spike candidates, word error rate in percent (`phase1-candidates.json`, `wer_per_group`)");
  console.log("| Candidate | English read | English meetings | English combined | French | German |");
  console.log("|---|---|---|---|---|---|");
  const best = { "en-read": 1, "en-ami": 1, en: 1, fr: 1, de: 1 };
  const bestWho = {};
  const bestCounts = {};
  const rows = Object.keys(spike.candidates).map((c) => {
    const parts = { "en-read": ["en-read"], "en-ami": ["en-ami"], en: ["en-read", "en-ami"], fr: ["fr"], de: ["de"] };
    const values = {};
    for (const [key, groups] of Object.entries(parts)) {
      const t = sum(c, groups);
      values[key] = rate(t);
      if (values[key] < best[key]) {
        best[key] = values[key];
        bestWho[key] = c;
        bestCounts[key] = `${t.S + t.D + t.I} / ${t.N}`;
      }
    }
    return [c, spike.candidates[c].name, values];
  });
  for (const [c, name, v] of rows) {
    console.log(`| ${c} ${name} | ${pct(v["en-read"])} | ${pct(v["en-ami"])} | ${pct(v.en)} | ${pct(v.fr)} | ${pct(v.de)} |`);
  }
  console.log(`| best of the five | ${pct(best["en-read"])} | ${pct(best["en-ami"])} | ${pct(best.en)} | ${pct(best.fr)} | ${pct(best.de)} |`);

  header("Quality gate: the engine under the AVX2-only mask (raw, clips and 10-minute files) against the best spike candidate, in points");
  console.log("| Language | Engine errors / words | Engine % | Best candidate | Its errors / words | Its % | Engine minus best (points) | Within one point |");
  console.log("|---|---|---|---|---|---|---|---|");
  const engine = scored(avx2);
  for (const [name, key, files] of [
    ["English read", "en-read", inGroup(engine, "en-read")],
    ["English meetings (AMI)", "en-ami", inGroup(engine, "en-ami")],
    ["English combined", "en", english(engine)],
    ["French", "fr", inGroup(engine, "fr")],
    ["German", "de", inGroup(engine, "de")],
  ]) {
    const t = sumWer(files, "werRaw");
    const diff = (t.wer - best[key]) * 100;
    console.log(`| ${name} | ${t.s + t.d + t.i} / ${t.n} | ${pct(t.wer)} | ${bestWho[key]} | ${bestCounts[key]} | ${pct(best[key])} | ${diff.toFixed(2)} | ${diff <= 1 ? "yes" : "no"} |`);
  }
}

// ---------------------------------------------------------------------------------------------
// 6. Provenance.
// ---------------------------------------------------------------------------------------------
header("Provenance of the acceptance files");
console.log("| File | Started (UTC) | Mask | Threads | Speakers | Logical cores | ONNX Runtime | Framework |");
console.log("|---|---|---|---|---|---|---|---|");
for (const name of readdirSync(folder).filter((n) => n.startsWith(`${date}-accept-`)).sort()) {
  const r = JSON.parse(readFileSync(join(folder, name), "utf8"));
  console.log(`| ${name} | ${r.startedUtc} | ${r.machine.cpuIdMask ?? "none"} | ${r.settings.threads} | ${r.settings.speakerSeparation ? "yes" : "no"} | ${r.machine.logicalCores} | ${r.machine.onnxRuntime} | ${r.machine.framework} |`);
}
