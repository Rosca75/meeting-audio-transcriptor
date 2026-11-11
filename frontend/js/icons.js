// icons.js: the pictures of the page, drawn as SVG elements: the flag of each language, the gear of
// the settings button and the logo beside the title. They are built with createElementNS and coloured
// with SVG presentation attributes, because the hosts' content policy allows no image file from
// elsewhere, no inline style and no inline script, and Windows draws no flag emoji. A flag's colours
// are its country's and the logo's are the mark's own, so they live here and not among the page's
// colour tokens; the gear takes the text colour of the button that holds it.

const SVG = "http://www.w3.org/2000/svg";

function shape(doc, tag, attributes) {
  const element = doc.createElementNS(SVG, tag);
  for (const [name, value] of Object.entries(attributes)) {
    element.setAttribute(name, value);
  }
  return element;
}

// An empty picture of the given size. It is decoration: the control that holds it carries the name.
function picture(doc, width, height) {
  return shape(doc, "svg", { viewBox: `0 0 ${width} ${height}`, "aria-hidden": "true", focusable: "false" });
}

function rect(doc, x, y, width, height, fill) {
  return shape(doc, "rect", { x, y, width, height, fill });
}

// Lines drawn with a stroke; the picture's own frame cuts what runs past its edge.
function lines(doc, d, stroke, width) {
  return shape(doc, "path", { d, fill: "none", stroke, "stroke-width": width });
}

// Every flag is drawn in the same 60 by 40 frame so the three sit in one row at one size.
const FLAGS = {
  // The United Kingdom: the white saltire and its red one, then the white-edged red cross.
  en(doc) {
    const diagonals = "M0,0 L60,40 M60,0 L0,40";
    const cross = "M30,0 V40 M0,20 H60";
    return [
      rect(doc, 0, 0, 60, 40, "#012169"),
      lines(doc, diagonals, "#FFFFFF", 8),
      lines(doc, diagonals, "#C8102E", 3),
      lines(doc, cross, "#FFFFFF", 13),
      lines(doc, cross, "#C8102E", 8),
    ];
  },
  // France: three upright bands.
  fr(doc) {
    return [rect(doc, 0, 0, 20, 40, "#0055A4"), rect(doc, 20, 0, 20, 40, "#FFFFFF"), rect(doc, 40, 0, 20, 40, "#EF4135")];
  },
  // Germany: three level bands.
  de(doc) {
    return [rect(doc, 0, 0, 60, 40, "#000000"), rect(doc, 0, 13.33, 60, 13.34, "#DD0000"), rect(doc, 0, 26.67, 60, 13.33, "#FFCE00")];
  },
};

// The language codes that have a flag; a test holds this list to the languages of the model.
export const FLAG_CODES = Object.keys(FLAGS);

// The flag of a language code. An unknown code is an error, because an empty picture would leave a
// choice on screen that shows nothing.
export function flagIcon(doc, code) {
  if (!Object.hasOwn(FLAGS, code)) {
    throw new Error("there is no flag for the language " + code + "; add one to icons.js or remove the language");
  }
  const svg = picture(doc, 60, 40);
  svg.append(...FLAGS[code](doc));
  return svg;
}

// A gear: a ring with eight teeth, in the colour of the text around it.
export function gearIcon(doc) {
  const svg = picture(doc, 24, 24);
  svg.append(shape(doc, "circle", { cx: 12, cy: 12, r: 5.5, fill: "none", stroke: "currentColor", "stroke-width": 3 }));
  for (let tooth = 0; tooth < 8; tooth++) {
    svg.append(
      shape(doc, "rect", { x: 10.25, y: 1.5, width: 3.5, height: 5, rx: 1, fill: "currentColor", transform: `rotate(${tooth * 45} 12 12)` }),
    );
  }
  return svg;
}

// The application's mark: an orange rounded tile holding a white sound wave of five bars.
export function logoIcon(doc) {
  const svg = picture(doc, 32, 32);
  svg.append(shape(doc, "rect", { x: 0, y: 0, width: 32, height: 32, rx: 7, fill: "#FD5108" }));
  const heights = [6, 14, 20, 10, 6];
  heights.forEach((height, index) => {
    svg.append(shape(doc, "rect", { x: 5.7 + index * 4.5, y: 16 - height / 2, width: 2.6, height, rx: 1.3, fill: "#FFFFFF" }));
  });
  return svg;
}
