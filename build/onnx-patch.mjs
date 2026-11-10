// Deterministic edits of an ONNX model file, applied by the fetch tool to derive
// a shipped graph from a pinned source graph. The edit is a protobuf edit done by
// hand: the top-level ModelProto is walked field by field to find the graph
// (field 7), the graph's own fields are walked to find the nodes and outputs the
// edit needs, new NodeProto (graph field 1) and ValueInfoProto output (graph
// field 12) messages are appended at the end of the graph message, and the
// graph's length prefix is rewritten. Protobuf allows a repeated field to be
// split across the message, and ONNX requires only that nodes be topologically
// sorted, so nodes appended last are valid. The file is streamed through, never
// loaded whole, because a model can exceed half a gigabyte, and the same input
// always yields the same bytes, which is what lets the lock pin the result.
//
// The one patch, "ctc-token-scores", exposes the per-frame peak probability of
// a CTC graph whose only output is the argmax inside the graph: it finds the
// ArgMax node producing the named id output, takes its input (the logits, or the
// log-probabilities when that tensor comes from a LogSoftmax), and appends
//   Softmax(axis=-1) -> ReduceMax(axes=[-1], keepdims=0)          for logits
//   ReduceMax(axes=[-1], keepdims=0) -> Exp                       for log-probabilities
// producing a float32 output with the id output's shape. Node 22 or later, no dependency.
import { createHash } from "node:crypto";
import { closeSync, createReadStream, createWriteStream, fstatSync, openSync, readSync, renameSync, rmSync } from "node:fs";
import { once } from "node:events";

/** Protobuf wire types. */
const VARINT = 0;
const FIXED64 = 1;
const LEN = 2;
const FIXED32 = 5;

/** Field numbers of the ONNX messages the edit reads or writes (onnx.proto). */
const MODEL_GRAPH = 7;
const GRAPH_NODE = 1;
const GRAPH_OUTPUT = 12;
const NODE_INPUT = 1;
const NODE_OUTPUT = 2;
const NODE_NAME = 3;
const NODE_OP_TYPE = 4;
const NODE_ATTRIBUTE = 5;
const VALUE_INFO_NAME = 1;
const VALUE_INFO_TYPE = 2;
const TYPE_TENSOR = 1;
const TENSOR_ELEM_TYPE = 1;
const TENSOR_SHAPE = 2;
const ATTRIBUTE_NAME = 1;
const ATTRIBUTE_I = 3;
const ATTRIBUTE_INTS = 8;
const ATTRIBUTE_TYPE = 20;
const ATTRIBUTE_TYPE_INT = 2;
const ATTRIBUTE_TYPE_INTS = 7;
const ELEM_FLOAT = 1;

/** A random-access reader over a file with a small window, so walking headers costs few system calls. */
class FileWindow {
  constructor(path) {
    this.path = path;
    this.fd = openSync(path, "r");
    this.size = fstatSync(this.fd).size;
    this.buffer = Buffer.alloc(1 << 20);
    this.start = 0;
    this.length = 0;
  }

  /** Returns the bytes [position, position + count), reading through the window. */
  bytes(position, count) {
    if (position < 0 || position + count > this.size) {
      throw new Error(`${this.path} ends at byte ${this.size} inside a protobuf field that claims bytes up to ${position + count}; the file is truncated or is not an ONNX model`);
    }
    if (count > this.buffer.length) {
      const out = Buffer.alloc(count);
      readSync(this.fd, out, 0, count, position);
      return out;
    }
    if (position < this.start || position + count > this.start + this.length) {
      this.start = position;
      this.length = readSync(this.fd, this.buffer, 0, Math.min(this.buffer.length, this.size - position), position);
    }
    return this.buffer.subarray(position - this.start, position - this.start + count);
  }

  /** Reads a varint at a position: {value, next}. The value is a Number, exact below 2^53. */
  varint(position) {
    let value = 0;
    let scale = 1;
    for (let i = 0; i < 10; i++) {
      const byte = this.bytes(position + i, 1)[0];
      value += (byte & 0x7f) * scale;
      scale *= 128;
      if ((byte & 0x80) === 0) {
        return { value, next: position + i + 1 };
      }
    }
    throw new Error(`${this.path} holds a varint longer than ten bytes at byte ${position}; the file is not an ONNX model`);
  }

  close() {
    closeSync(this.fd);
  }
}

/**
 * Walks the fields of one message lying in [start, end) of a reader with the
 * same varint() and bytes() methods; yields {number, wire, start, dataStart, end}
 * where [dataStart, end) is the payload of a length-delimited field.
 */
function* fields(reader, start, end, what) {
  let position = start;
  while (position < end) {
    const key = reader.varint(position);
    const number = Math.floor(key.value / 8);
    const wire = key.value % 8;
    let dataStart = key.next;
    let next;
    if (wire === VARINT) {
      next = reader.varint(dataStart).next;
    } else if (wire === FIXED64) {
      next = dataStart + 8;
    } else if (wire === FIXED32) {
      next = dataStart + 4;
    } else if (wire === LEN) {
      const length = reader.varint(dataStart);
      dataStart = length.next;
      next = dataStart + length.value;
    } else {
      throw new Error(`${what} holds a field of wire type ${wire} at byte ${position}; ONNX uses no protobuf groups, so the file is not an ONNX model`);
    }
    if (next > end) {
      throw new Error(`${what} has a field running past its message at byte ${position}; the file is truncated or is not an ONNX model`);
    }
    yield { number, wire, start: position, dataStart, end: next };
    position = next;
  }
}

/** The same random access over a buffer already in memory, for the small messages (nodes, outputs). */
class BufferWindow {
  constructor(buffer) {
    this.buffer = buffer;
  }

  bytes(position, count) {
    if (position + count > this.buffer.length) {
      throw new Error("a protobuf message ends inside one of its fields; the file is not an ONNX model");
    }
    return this.buffer.subarray(position, position + count);
  }

  varint(position) {
    let value = 0;
    let scale = 1;
    for (let i = 0; i < 10; i++) {
      const byte = this.bytes(position + i, 1)[0];
      value += (byte & 0x7f) * scale;
      scale *= 128;
      if ((byte & 0x80) === 0) {
        return { value, next: position + i + 1 };
      }
    }
    throw new Error("a protobuf varint is longer than ten bytes; the file is not an ONNX model");
  }
}

/** Reads the parts of a NodeProto the edit needs: inputs, outputs, name and operator. */
function readNode(buffer) {
  const window = new BufferWindow(buffer);
  const node = { inputs: [], outputs: [], name: "", opType: "" };
  for (const field of fields(window, 0, buffer.length, "a node")) {
    if (field.wire !== LEN) {
      continue;
    }
    const text = () => buffer.toString("utf8", field.dataStart, field.end);
    if (field.number === NODE_INPUT) node.inputs.push(text());
    else if (field.number === NODE_OUTPUT) node.outputs.push(text());
    else if (field.number === NODE_NAME) node.name = text();
    else if (field.number === NODE_OP_TYPE) node.opType = text();
  }
  return node;
}

/** Reads a ValueInfoProto's name and, for a tensor type, its element type and the raw bytes of its shape. */
function readValueInfo(buffer) {
  const window = new BufferWindow(buffer);
  const info = { name: "", elemType: 0, shape: null };
  for (const field of fields(window, 0, buffer.length, "a graph output")) {
    if (field.number === VALUE_INFO_NAME && field.wire === LEN) {
      info.name = buffer.toString("utf8", field.dataStart, field.end);
    } else if (field.number === VALUE_INFO_TYPE && field.wire === LEN) {
      for (const typeField of fields(window, field.dataStart, field.end, "a type")) {
        if (typeField.number !== TYPE_TENSOR || typeField.wire !== LEN) {
          continue;
        }
        for (const tensorField of fields(window, typeField.dataStart, typeField.end, "a tensor type")) {
          if (tensorField.number === TENSOR_ELEM_TYPE && tensorField.wire === VARINT) {
            info.elemType = window.varint(tensorField.dataStart).value;
          } else if (tensorField.number === TENSOR_SHAPE && tensorField.wire === LEN) {
            info.shape = Buffer.from(buffer.subarray(tensorField.dataStart, tensorField.end));
          }
        }
      }
    }
  }
  return info;
}

/**
 * Locates the graph inside a model file and reads its nodes and outputs.
 * Returns {size, graph: {start, keyEnd, dataStart, end}, nodes, outputs}, where
 * [start, keyEnd) is the graph field's key and [keyEnd, dataStart) its length prefix.
 */
export function describeModel(path) {
  const reader = new FileWindow(path);
  try {
    let graph = null;
    for (const field of fields(reader, 0, reader.size, path)) {
      if (field.number !== MODEL_GRAPH) {
        continue;
      }
      if (field.wire !== LEN || graph) {
        throw new Error(`${path} has ${graph ? "more than one graph field" : "a graph field that is not a message"}; the patch reads models with exactly one graph`);
      }
      graph = { start: field.start, keyEnd: reader.varint(field.start).next, dataStart: field.dataStart, end: field.end };
    }
    if (!graph) {
      throw new Error(`${path} has no graph (ModelProto field 7); it is not an ONNX model`);
    }
    const nodes = [];
    const outputs = [];
    for (const field of fields(reader, graph.dataStart, graph.end, `the graph of ${path}`)) {
      if (field.wire !== LEN) {
        continue;
      }
      if (field.number === GRAPH_NODE) {
        nodes.push(readNode(Buffer.from(reader.bytes(field.dataStart, field.end - field.dataStart))));
      } else if (field.number === GRAPH_OUTPUT) {
        outputs.push(readValueInfo(Buffer.from(reader.bytes(field.dataStart, field.end - field.dataStart))));
      }
    }
    return { size: reader.size, graph, nodes, outputs };
  } finally {
    reader.close();
  }
}

/** Encodes an unsigned or negative integer as a protobuf varint; a negative int64 takes ten bytes. */
export function varint(value) {
  let rest = BigInt.asUintN(64, BigInt(value));
  const bytes = [];
  do {
    let byte = Number(rest & 0x7fn);
    rest >>= 7n;
    if (rest !== 0n) {
      byte |= 0x80;
    }
    bytes.push(byte);
  } while (rest !== 0n);
  return Buffer.from(bytes);
}

const key = (number, wire) => varint(number * 8 + wire);
const varintField = (number, value) => Buffer.concat([key(number, VARINT), varint(value)]);
const bytesField = (number, payload) => Buffer.concat([key(number, LEN), varint(payload.length), payload]);
const stringField = (number, text) => bytesField(number, Buffer.from(text, "utf8"));

/** An integer attribute: AttributeProto {name, i, type = INT}. */
function intAttribute(name, value) {
  return bytesField(NODE_ATTRIBUTE, Buffer.concat([stringField(ATTRIBUTE_NAME, name), varintField(ATTRIBUTE_I, value), varintField(ATTRIBUTE_TYPE, ATTRIBUTE_TYPE_INT)]));
}

/** An integer-list attribute: AttributeProto {name, ints (unpacked, as onnx.proto's proto2 encoding writes them), type = INTS}. */
function intsAttribute(name, values) {
  return bytesField(NODE_ATTRIBUTE, Buffer.concat([stringField(ATTRIBUTE_NAME, name), ...values.map((v) => varintField(ATTRIBUTE_INTS, v)), varintField(ATTRIBUTE_TYPE, ATTRIBUTE_TYPE_INTS)]));
}

/** A graph node field: NodeProto {input..., output..., name, op_type, attribute...} in the field order ONNX writers use. */
export function nodeField({ inputs, outputs, name, opType, attributes = [] }) {
  const payload = Buffer.concat([
    ...inputs.map((input) => stringField(NODE_INPUT, input)),
    ...outputs.map((output) => stringField(NODE_OUTPUT, output)),
    stringField(NODE_NAME, name),
    stringField(NODE_OP_TYPE, opType),
    ...attributes,
  ]);
  return bytesField(GRAPH_NODE, payload);
}

/** A graph output field: ValueInfoProto {name, type = tensor {elem_type, shape}} with the shape bytes copied from another output. */
export function outputField(name, elemType, shapeBytes) {
  const tensor = Buffer.concat([varintField(TENSOR_ELEM_TYPE, elemType), ...(shapeBytes ? [bytesField(TENSOR_SHAPE, shapeBytes)] : [])]);
  const type = bytesField(TYPE_TENSOR, tensor);
  return bytesField(GRAPH_OUTPUT, Buffer.concat([stringField(VALUE_INFO_NAME, name), bytesField(VALUE_INFO_TYPE, type)]));
}

/**
 * Plans the "ctc-token-scores" patch over a described model: the bytes to append
 * to the graph and what the plan found. Throws when the graph is not the shape
 * the patch expects, naming what was found.
 */
export function planCtcTokenScores(model, { idsOutput = "token_ids", scoresOutput = "token_scores" } = {}) {
  const argmax = model.nodes.find((node) => node.opType === "ArgMax" && node.outputs.includes(idsOutput));
  if (!argmax) {
    throw new Error(`no ArgMax node produces "${idsOutput}"; the ctc-token-scores patch needs a graph whose id output is an in-graph ArgMax`);
  }
  const ids = model.outputs.find((output) => output.name === idsOutput);
  if (!ids) {
    throw new Error(`"${idsOutput}" is not a graph output; the outputs are ${model.outputs.map((o) => o.name).join(", ")}`);
  }
  const taken = new Set([...model.nodes.flatMap((node) => node.outputs), ...model.outputs.map((o) => o.name)]);
  const prefix = `${scoresOutput}/`;
  if (taken.has(scoresOutput) || [...taken].some((name) => name.startsWith(prefix))) {
    throw new Error(`the graph already has a tensor named "${scoresOutput}" or under "${prefix}"; it is patched already, or the source is not the one the lock pins`);
  }
  const scoresInput = argmax.inputs[0];
  const producer = model.nodes.find((node) => node.outputs.includes(scoresInput));
  const logProbabilities = producer?.opType === "LogSoftmax";
  const nodes = logProbabilities
    ? [
      nodeField({ inputs: [scoresInput], outputs: [`${prefix}max_log_probability`], name: `${prefix}ReduceMax`, opType: "ReduceMax", attributes: [intsAttribute("axes", [-1]), intAttribute("keepdims", 0)] }),
      nodeField({ inputs: [`${prefix}max_log_probability`], outputs: [scoresOutput], name: `${prefix}Exp`, opType: "Exp" }),
    ]
    : [
      nodeField({ inputs: [scoresInput], outputs: [`${prefix}probabilities`], name: `${prefix}Softmax`, opType: "Softmax", attributes: [intAttribute("axis", -1)] }),
      nodeField({ inputs: [`${prefix}probabilities`], outputs: [scoresOutput], name: `${prefix}ReduceMax`, opType: "ReduceMax", attributes: [intsAttribute("axes", [-1]), intAttribute("keepdims", 0)] }),
    ];
  return {
    append: Buffer.concat([...nodes, outputField(scoresOutput, ELEM_FLOAT, ids.shape)]),
    scoresInput,
    producer: producer?.opType ?? "graph input or initializer",
    logProbabilities,
  };
}

/** The patches a lock entry may name, each a planner over a described model. */
export const patches = {
  "ctc-token-scores": planCtcTokenScores,
};

/** Streams [start, end) of a file into a writable, feeding a hash on the way. */
async function copyRange(path, start, end, output, hash) {
  if (end <= start) {
    return;
  }
  for await (const chunk of createReadStream(path, { start, end: end - 1, highWaterMark: 4 << 20 })) {
    hash.update(chunk);
    if (!output.write(chunk)) {
      await once(output, "drain");
    }
  }
}

async function writePiece(piece, output, hash) {
  hash.update(piece);
  if (!output.write(piece)) {
    await once(output, "drain");
  }
}

/**
 * Applies a named patch to a model file and writes the result to target through
 * target.part, renamed only once complete. Returns {sha256, bytes, plan}.
 */
export async function derive(sourcePath, targetPath, patchName, options = {}) {
  const planner = patches[patchName];
  if (!planner) {
    throw new Error(`unknown patch "${patchName}"; the patches are ${Object.keys(patches).join(", ")}`);
  }
  const model = describeModel(sourcePath);
  const plan = planner(model, options);
  const { graph } = model;
  const newLength = graph.end - graph.dataStart + plan.append.length;
  const part = `${targetPath}.part`;
  const output = createWriteStream(part);
  const hash = createHash("sha256");
  try {
    // Everything before the graph's length prefix, the key included, is copied as it is.
    await copyRange(sourcePath, 0, graph.keyEnd, output, hash);
    await writePiece(varint(newLength), output, hash);
    await copyRange(sourcePath, graph.dataStart, graph.end, output, hash);
    await writePiece(plan.append, output, hash);
    // The fields after the graph (the opset imports and the rest) follow unchanged.
    await copyRange(sourcePath, graph.end, model.size, output, hash);
    output.end();
    await once(output, "finish");
  } catch (error) {
    output.destroy();
    rmSync(part, { force: true });
    throw error;
  }
  renameSync(part, targetPath);
  const bytes = model.size - (graph.dataStart - graph.keyEnd) + varint(newLength).length + plan.append.length;
  return { sha256: hash.digest("hex"), bytes, plan };
}
