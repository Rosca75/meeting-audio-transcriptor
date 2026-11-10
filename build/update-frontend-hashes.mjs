// update-frontend-hashes.mjs: rewrites frontend/frontend-hashes.txt, one `<sha256> *<path>` line per
// file under frontend/ except the hash file itself, so a deliberate page edit moves its hash line in
// the same commit. Run it from anywhere: `node build/update-frontend-hashes.mjs`.
import { createHash } from "node:crypto";
import { readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "frontend");
const hashFile = join(root, "frontend-hashes.txt");

function filesUnder(folder) {
  const found = [];
  for (const entry of readdirSync(folder).sort()) {
    const path = join(folder, entry);
    if (statSync(path).isDirectory()) {
      if (entry !== "node_modules") {
        found.push(...filesUnder(path));
      }
    } else if (path !== hashFile) {
      found.push(path);
    }
  }
  return found;
}

const lines = filesUnder(root)
  .map((path) => relative(root, path).split("\\").join("/"))
  .sort()
  .map((name) => {
    const digest = createHash("sha256").update(readFileSync(join(root, name))).digest("hex");
    return `${digest} *${name}`;
  });

writeFileSync(hashFile, lines.join("\n") + "\n");
console.log(`wrote ${lines.length} lines to frontend/frontend-hashes.txt`);
