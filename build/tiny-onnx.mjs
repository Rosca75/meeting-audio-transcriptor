// A hand-encoded ONNX-shaped model for the tests of the graph patch and the fetch
// tool: a CTC tail (logits from an Add, optionally through a LogSoftmax, into an
// ArgMax producing token_ids) with an initializer standing for the weights and
// an opset import after the graph, so a patch must copy bytes on both sides of
// the graph. Built from the same field encoders the patch writes with, plus the
// few message kinds only a source model holds.
import { nodeField, outputField, varint } from "./onnx-patch.mjs";

const key = (number, wire) => varint(number * 8 + wire);
const bytesField = (number, payload) => Buffer.concat([key(number, 2), varint(payload.length), payload]);
const stringField = (number, text) => bytesField(number, Buffer.from(text, "utf8"));
const varintField = (number, value) => Buffer.concat([key(number, 0), varint(value)]);

/** A shape [batch, frames] as TensorShapeProto bytes, both dimensions symbolic. */
export const tinyShape = Buffer.concat([
  bytesField(1, stringField(2, "batch")),
  bytesField(1, stringField(2, "frames")),
]);

/** An int64 graph output of the given shape, as the field bytes. */
export const tinyIdsOutput = outputField("token_ids", 7, tinyShape);

/**
 * Builds the model. Options: logSoftmax puts a LogSoftmax between the logits and
 * the ArgMax; weightBytes sizes the initializer (a TensorProto with raw data).
 */
export function tinyCtcModel({ logSoftmax = false, weightBytes = 4096, withArgMax = true } = {}) {
  const initializer = bytesField(5, Buffer.concat([
    varintField(1, 2),
    varintField(2, weightBytes / 4),
    varintField(2, 1),
    varintField(2, 1),
    stringField(8, "weights"),
    bytesField(9, Buffer.alloc(weightBytes, 0x5a)),
  ]));
  const nodes = [nodeField({ inputs: ["features", "weights"], outputs: ["logits"], name: "/head/Add", opType: "Add" })];
  let scored = "logits";
  if (logSoftmax) {
    nodes.push(nodeField({ inputs: ["logits"], outputs: ["log_probs"], name: "/head/LogSoftmax", opType: "LogSoftmax" }));
    scored = "log_probs";
  }
  if (withArgMax) {
    nodes.push(nodeField({ inputs: [scored], outputs: ["token_ids"], name: "/ArgMax", opType: "ArgMax" }));
  }
  const graph = Buffer.concat([
    nodes[0],
    stringField(2, "tiny"),
    initializer,
    ...nodes.slice(1),
    bytesField(11, stringField(1, "features")),
    tinyIdsOutput,
  ]);
  return Buffer.concat([
    varintField(1, 8),
    stringField(2, "tiny-test"),
    bytesField(7, graph),
    bytesField(8, Buffer.concat([stringField(1, ""), varintField(2, 17)])),
  ]);
}
