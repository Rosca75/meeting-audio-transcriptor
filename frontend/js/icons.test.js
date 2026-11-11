// icons.test.js: unit tests for the drawn icons (the flags, the gear and the logo) over a recording
// stand-in for the document, run with `node --test "frontend/**/*.test.js"`.
import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { FLAG_CODES, flagIcon, gearIcon, logoIcon } from "./icons.js";
import { LANGUAGES } from "./model.js";

const here = dirname(fileURLToPath(import.meta.url));
const SVG = "http://www.w3.org/2000/svg";

// A document that records what was built: the namespace, the tag, the attributes and the children.
function fakeDocument() {
  return {
    createElementNS(namespace, tag) {
      return {
        namespace,
        tag,
        attributes: {},
        children: [],
        setAttribute(name, value) {
          this.attributes[name] = String(value);
        },
        append(...nodes) {
          this.children.push(...nodes);
        },
      };
    },
  };
}

function everyNode(node, found = []) {
  found.push(node);
  for (const child of node.children) {
    everyNode(child, found);
  }
  return found;
}

function allIcons() {
  const doc = fakeDocument();
  return [...LANGUAGES.map((code) => flagIcon(doc, code)), gearIcon(doc), logoIcon(doc)];
}

test("there is a flag for every language and no flag without a language", () => {
  assert.deepEqual([...FLAG_CODES].sort(), [...LANGUAGES].sort());
});

test("an unknown flag is an error and never an empty picture", () => {
  assert.throws(() => flagIcon(fakeDocument(), "it"), /no flag/);
});

test("every icon is an SVG picture with shapes, hidden from assistive technology", () => {
  for (const icon of allIcons()) {
    assert.equal(icon.tag, "svg");
    assert.equal(icon.attributes["aria-hidden"], "true");
    assert.equal(icon.attributes.focusable, "false");
    assert.match(icon.attributes.viewBox, /^0 0 \d+ \d+$/);
    assert.ok(icon.children.length > 0, "an icon with no shape draws nothing");
    for (const node of everyNode(icon)) {
      assert.equal(node.namespace, SVG, node.tag);
    }
  }
});

test("no icon carries an inline style, a script hook or an address", () => {
  for (const icon of allIcons()) {
    for (const node of everyNode(icon)) {
      for (const [name, value] of Object.entries(node.attributes)) {
        assert.equal(/^(style|href|xlink:href|src)$|^on/i.test(name), false, node.tag + " " + name);
        assert.equal(/url\(|https?:|javascript:/i.test(value), false, node.tag + " " + name);
      }
    }
  }
});

test("the three flags carry their national colours", () => {
  const colours = (code) =>
    new Set(
      everyNode(flagIcon(fakeDocument(), code))
        .flatMap((node) => [node.attributes.fill, node.attributes.stroke])
        .filter((value) => value && value !== "none"),
    );
  assert.deepEqual(colours("en"), new Set(["#012169", "#FFFFFF", "#C8102E"]));
  assert.deepEqual(colours("fr"), new Set(["#0055A4", "#FFFFFF", "#EF4135"]));
  assert.deepEqual(colours("de"), new Set(["#000000", "#DD0000", "#FFCE00"]));
});

test("the gear takes the colour of its button and the logo is the orange tile with a white glyph", () => {
  const doc = fakeDocument();
  const gear = everyNode(gearIcon(doc)).flatMap((node) => [node.attributes.fill, node.attributes.stroke]).filter(Boolean);
  assert.deepEqual([...new Set(gear)].sort(), ["currentColor", "none"]);
  const logo = everyNode(logoIcon(doc)).map((node) => node.attributes.fill).filter(Boolean);
  assert.deepEqual([...new Set(logo)].sort(), ["#FD5108", "#FFFFFF"]);
});

test("only the icons module writes a colour in a script", () => {
  for (const file of ["api.js", "bridge.js", "controller.js", "copy.js", "main.js", "model.js", "state.js", "view.js"]) {
    const text = readFileSync(join(here, file), "utf8");
    assert.equal(/#[0-9a-f]{6}\b/i.test(text), false, file);
  }
});
