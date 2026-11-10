// Tests of build/onnx-patch.mjs over a hand-encoded model: the graph is found and
// read, the derived file keeps every byte outside the graph and the graph's own
// bytes, appends the nodes and the output the patch promises, is the same bytes
// on every run, and a graph of the wrong shape is refused by name.
// Run with: node --test build/*.test.mjs
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, before, describe, test } from "node:test";
import { derive, describeModel, varint } from "./onnx-patch.mjs";
import { tinyCtcModel, tinyShape } from "./tiny-onnx.mjs";

let work;

before(() => {
  work = mkdtempSync(join(tmpdir(), "onnx-patch-test-"));
});

after(() => {
  rmSync(work, { recursive: true, force: true });
});

/** Writes a model to the work folder and returns its path. */
function place(name, bytes) {
  const path = join(work, name);
  writeFileSync(path, bytes);
  return path;
}

describe("varint", () => {
  test("encodes small, multi-byte and negative values as protobuf does", () => {
    assert.deepEqual([...varint(1)], [1]);
    assert.deepEqual([...varint(300)], [0xac, 0x02]);
    assert.deepEqual([...varint(552434296)], [0xf8, 0xf4, 0xb5, 0x87, 0x02]);
    const minusOne = varint(-1);
    assert.equal(minusOne.length, 10, "a negative int64 takes ten bytes");
    assert.deepEqual([...minusOne], [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01]);
  });
});

describe("describeModel", () => {
  test("finds the graph, its nodes and its outputs", () => {
    const path = place("plain.onnx", tinyCtcModel());
    const model = describeModel(path);
    assert.deepEqual(model.nodes.map((node) => node.opType), ["Add", "ArgMax"]);
    assert.deepEqual(model.nodes[1].inputs, ["logits"]);
    assert.deepEqual(model.outputs.map((output) => output.name), ["token_ids"]);
    assert.equal(model.outputs[0].elemType, 7);
    assert.deepEqual(model.outputs[0].shape, tinyShape);
    assert.ok(model.graph.end < model.size, "the opset import follows the graph");
  });

  test("refuses a file that is not a model", () => {
    const path = place("junk.onnx", Buffer.from([0x3a, 0xff, 0xff, 0x01]));
    assert.throws(() => describeModel(path), /truncated or is not an ONNX model/);
  });
});

describe("derive ctc-token-scores", () => {
  test("appends Softmax and ReduceMax over the logits and a float output of the id output's shape", async () => {
    const sourceBytes = tinyCtcModel({ weightBytes: 3 << 20 });
    const source = place("logits.onnx", sourceBytes);
    const target = join(work, "logits.scores.onnx");
    const result = await derive(source, target, "ctc-token-scores");

    const derived = readFileSync(target);
    assert.equal(result.bytes, derived.length);
    assert.equal(result.sha256, createHash("sha256").update(derived).digest("hex"));
    assert.equal(existsSync(`${target}.part`), false);
    assert.equal(result.plan.scoresInput, "logits");
    assert.equal(result.plan.logProbabilities, false);

    const before = describeModel(source);
    const after = describeModel(target);
    assert.deepEqual(after.nodes.map((node) => node.opType), ["Add", "ArgMax", "Softmax", "ReduceMax"]);
    assert.deepEqual(after.nodes[2].inputs, ["logits"]);
    assert.deepEqual(after.nodes[3].outputs, ["token_scores"]);
    assert.deepEqual(after.outputs.map((output) => output.name), ["token_ids", "token_scores"]);
    assert.equal(after.outputs[1].elemType, 1, "token_scores is float32");
    assert.deepEqual(after.outputs[1].shape, tinyShape);

    // The bytes before the graph's length, the graph's own bytes and the bytes after it are unchanged.
    assert.deepEqual(derived.subarray(0, before.graph.keyEnd), sourceBytes.subarray(0, before.graph.keyEnd));
    const graphBody = sourceBytes.subarray(before.graph.dataStart, before.graph.end);
    assert.deepEqual(derived.subarray(after.graph.dataStart, after.graph.dataStart + graphBody.length), graphBody);
    assert.deepEqual(derived.subarray(after.graph.end), sourceBytes.subarray(before.graph.end));
  });

  test("the same source gives the same bytes every time", async () => {
    const source = place("again.onnx", tinyCtcModel());
    const first = await derive(source, join(work, "again-1.onnx"), "ctc-token-scores");
    const second = await derive(source, join(work, "again-2.onnx"), "ctc-token-scores");
    assert.equal(first.sha256, second.sha256);
    assert.equal(first.bytes, second.bytes);
  });

  test("over log-probabilities the peak is ReduceMax then Exp", async () => {
    const source = place("logsoftmax.onnx", tinyCtcModel({ logSoftmax: true }));
    const target = join(work, "logsoftmax.scores.onnx");
    const result = await derive(source, target, "ctc-token-scores");
    assert.equal(result.plan.logProbabilities, true);
    const after = describeModel(target);
    assert.deepEqual(after.nodes.slice(-2).map((node) => node.opType), ["ReduceMax", "Exp"]);
    assert.deepEqual(after.nodes.at(-2).inputs, ["log_probs"]);
  });

  test("a graph without an in-graph ArgMax is refused and nothing is written", async () => {
    const source = place("noargmax.onnx", tinyCtcModel({ withArgMax: false }));
    const target = join(work, "noargmax.scores.onnx");
    await assert.rejects(derive(source, target, "ctc-token-scores"), /no ArgMax node produces "token_ids"/);
    assert.equal(existsSync(target), false);
  });

  test("a graph patched already is refused", async () => {
    const source = place("twice.onnx", tinyCtcModel());
    const once = join(work, "twice-1.onnx");
    await derive(source, once, "ctc-token-scores");
    await assert.rejects(derive(once, join(work, "twice-2.onnx"), "ctc-token-scores"), /already has a tensor named "token_scores"/);
  });

  test("an unknown patch is refused by name", async () => {
    const source = place("unknown.onnx", tinyCtcModel());
    await assert.rejects(derive(source, join(work, "unknown-out.onnx"), "nope"), /unknown patch "nope"/);
  });
});
