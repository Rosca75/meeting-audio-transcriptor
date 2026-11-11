// view.js: renders the store's state into the static markup of index.html. It keeps no state of its
// own: createView() creates the repeated controls and the pictures once, render(state) sets every
// property from the state. Of the three views (main, settings, summary) exactly one is shown.
import { APP, FILE, OPTIONS, OUTPUT, RUN, SUMMARY, speakerLabel, speakerNameLabel, stageLine, versionLine } from "./copy.js";
import { flagIcon, gearIcon, logoIcon } from "./icons.js";
import {
  FORMATS, LANGUAGES, SPEAKER_MAX, SPEAKER_NAME_MAX, baseName, formatDuration, formatSpeed, parseSpeakerCount,
  speakerNamesValid, splitForCut, toPercent,
} from "./model.js";
import { canStart, namedSpeakers, outputFolder, startProblem } from "./state.js";

function byId(doc, id) {
  const element = doc.getElementById(id);
  if (!element) {
    throw new Error("index.html has no element with id " + id + "; the page and its script are out of step");
  }
  return element;
}

// A labelled checkbox in the page's choice style.
function choice(doc, name, value, text) {
  const label = doc.createElement("label");
  label.className = "choice";
  const input = doc.createElement("input");
  input.type = "checkbox";
  input.name = name;
  input.value = value;
  const span = doc.createElement("span");
  span.textContent = text;
  label.append(input, span);
  return { label, input };
}

// A language shown as its flag: a radio button the eye does not see, named by the language for
// assistive technology and for the tooltip, and a frame around the flag that shows the choice.
function flagChoice(doc, code, name) {
  const label = doc.createElement("label");
  label.className = "flag";
  label.title = name;
  const input = doc.createElement("input");
  input.type = "radio";
  input.name = "language";
  input.value = code;
  input.className = "visually-hidden";
  input.setAttribute("aria-label", name);
  const frame = doc.createElement("span");
  frame.className = "flag-frame";
  frame.append(flagIcon(doc, code));
  label.append(input, frame);
  return { label, input };
}

// The field for the name of speaker `number`; it shows the default label until a name is typed.
function nameField(doc, number) {
  const input = doc.createElement("input");
  input.type = "text";
  input.maxLength = SPEAKER_NAME_MAX;
  input.autocomplete = "off";
  input.spellcheck = false;
  input.placeholder = speakerLabel(number);
  input.setAttribute("aria-label", speakerNameLabel(number));
  input.setAttribute("aria-describedby", "names-error");
  return input;
}

function setText(element, text) {
  if (element.textContent !== text) {
    element.textContent = text;
  }
}

// The text of a line that may be cut at its end, with the whole of it as the tooltip.
function setCutText(element, text) {
  setText(element, text);
  element.title = text;
}

// How many characters stay whole at the end of a file name and of a folder path cut in the middle.
const NAME_TAIL = 12;
const PATH_TAIL = 24;

// Fills an element with a line that is cut in its middle: a head that gives way with an ellipsis
// and a tail that always shows. Returns the function that writes a text and its tooltip into it.
function cutLine(doc, element, keep) {
  const head = doc.createElement("span");
  head.className = "cut-head";
  const tail = doc.createElement("span");
  tail.className = "cut-tail";
  element.replaceChildren(head, tail);
  return (text, whole) => {
    const [first, last] = splitForCut(text, keep);
    setText(head, first);
    setText(tail, last);
    element.title = whole;
  };
}

function setShown(element, shown) {
  element.hidden = !shown;
}

export function createView(doc) {
  const el = {};
  for (const id of [
    "logo", "app-title", "open-settings", "close-settings", "models-message", "main-view", "inputs", "inputs-legend",
    "file-heading", "drop-zone", "choose-file", "drop-hint", "file-name", "file-note", "file-error", "output-heading",
    "output-folder", "choose-folder", "run-region", "start", "cancel", "start-problem", "progress-box", "progress",
    "progress-text", "message", "settings-view", "settings-heading", "language-legend", "language-choices",
    "formats-legend", "format-choices", "raw-files", "raw-files-label", "formats-error", "speakers-legend", "separation",
    "separation-label", "speaker-count", "speaker-count-label", "speaker-count-error", "names-hint",
    "speaker-names", "names-error", "summary", "summary-heading", "summary-list", "files-heading", "files-list",
    "open-folder", "another", "error", "version",
  ]) {
    el[id] = byId(doc, id);
  }

  // The repeated controls and the pictures are created once; render only sets their properties.
  el.logo.append(logoIcon(doc));
  el["open-settings"].append(gearIcon(doc));
  el.languageInputs = {};
  for (const code of LANGUAGES) {
    const made = flagChoice(doc, code, OPTIONS.languages[code]);
    el.languageInputs[code] = made.input;
    el["language-choices"].append(made.label);
  }
  el.formatInputs = {};
  for (const code of FORMATS) {
    const made = choice(doc, "format", code, OPTIONS.formats[code]);
    el.formatInputs[code] = made.input;
    el["format-choices"].append(made.label);
  }
  const writeFileName = cutLine(doc, el["file-name"], NAME_TAIL);
  const writeFolder = cutLine(doc, el["output-folder"], PATH_TAIL);
  el.nameInputs = [];
  for (let number = 1; number <= SPEAKER_MAX; number++) {
    const input = nameField(doc, number);
    el.nameInputs.push(input);
    el["speaker-names"].append(input);
  }

  // The static strings never change, so they are written once.
  doc.title = APP.title;
  setText(el["app-title"], APP.title);
  el["open-settings"].setAttribute("aria-label", APP.settings);
  el["open-settings"].title = APP.settings;
  setText(el["close-settings"], APP.settingsDone);
  el["inputs-legend"].textContent = APP.inputsLabel;
  el["run-region"].setAttribute("aria-label", APP.runLabel);
  setText(el["file-heading"], FILE.heading);
  setText(el["choose-file"], FILE.choose);
  setText(el["drop-hint"], FILE.dropHint);
  setText(el["output-heading"], OUTPUT.heading);
  setText(el["choose-folder"], OUTPUT.change);
  setText(el["settings-heading"], APP.settings);
  setText(el["language-legend"], OPTIONS.languageHeading);
  setText(el["formats-legend"], OPTIONS.formatsHeading);
  setText(el["raw-files-label"], OPTIONS.rawFiles);
  setText(el["speakers-legend"], OPTIONS.speakersHeading);
  setText(el["separation-label"], OPTIONS.separation);
  setText(el["speaker-count-label"], OPTIONS.speakerCount);
  el["speaker-count"].placeholder = OPTIONS.speakerCountPlaceholder;
  el["speaker-names"].setAttribute("aria-label", OPTIONS.namesGroup);
  setText(el["names-hint"], OPTIONS.namesHint);
  setText(el.start, RUN.start);
  setText(el["summary-heading"], SUMMARY.heading);
  setText(el["files-heading"], SUMMARY.files);
  setText(el["open-folder"], RUN.openFolder);
  setText(el.another, RUN.another);
  el.progress.setAttribute("aria-label", RUN.progressLabel);

  function renderSummary(outcome) {
    el["summary-list"].replaceChildren();
    const rows = [
      [SUMMARY.duration, formatDuration(outcome.durationSeconds)],
      [SUMMARY.processing, formatDuration(outcome.processingSeconds)],
      [SUMMARY.speed, formatSpeed(outcome.speedFactor)],
      [SUMMARY.speakers, String(outcome.speakers ?? 0)],
      [SUMMARY.words, String(outcome.words ?? 0)],
    ];
    for (const [term, value] of rows) {
      const dt = doc.createElement("dt");
      dt.textContent = term;
      const dd = doc.createElement("dd");
      dd.textContent = value;
      el["summary-list"].append(dt, dd);
    }
    // A file is listed by its name; the folder is one button away and the full path is the tooltip.
    el["files-list"].replaceChildren();
    const files = outcome.outputFiles ?? [];
    for (const path of files.length ? files : [SUMMARY.noFiles]) {
      const item = doc.createElement("li");
      item.className = "cut-line";
      cutLine(doc, item, NAME_TAIL)(files.length ? baseName(path) : path, path);
      el["files-list"].append(item);
    }
  }

  function renderSettings(state) {
    for (const code of LANGUAGES) {
      el.languageInputs[code].checked = state.language === code;
    }
    let formatCount = 0;
    for (const code of FORMATS) {
      el.formatInputs[code].checked = state.formats[code] === true;
      formatCount += state.formats[code] === true ? 1 : 0;
    }
    el["raw-files"].checked = state.rawFiles;
    setShown(el["formats-error"], formatCount === 0);
    setText(el["formats-error"], formatCount === 0 ? OPTIONS.formatsNeeded : "");

    el.separation.checked = state.speakerSeparation;
    // A text field is written only when it differs, so the caret does not jump while typing.
    if (el["speaker-count"].value !== state.speakerCountText) {
      el["speaker-count"].value = state.speakerCountText;
    }
    el["speaker-count"].disabled = !state.speakerSeparation;
    const countValid = parseSpeakerCount(state.speakerCountText).valid;
    el["speaker-count"].setAttribute("aria-invalid", countValid ? "false" : "true");
    setShown(el["speaker-count-error"], !countValid);
    setText(el["speaker-count-error"], countValid ? "" : OPTIONS.speakerCountInvalid);

    // One name field per speaker of the count; without a count there is nobody to name yet.
    const named = namedSpeakers(state);
    setShown(el["names-hint"], named === 0);
    setShown(el["speaker-names"], named > 0);
    el.nameInputs.forEach((input, index) => {
      setShown(input, index < named);
      if (input.value !== state.speakerNames[index]) {
        input.value = state.speakerNames[index];
      }
    });
    const namesValid = speakerNamesValid(state.speakerNames.slice(0, named));
    setShown(el["names-error"], !namesValid);
    setText(el["names-error"], namesValid ? "" : OPTIONS.namesInvalid);
  }

  function render(state) {
    const running = state.phase === "running";
    const done = state.phase === "done";
    const outcome = state.outcome;
    const completed = Boolean(done && outcome && outcome.kind === "completed");

    // Exactly one view shows: the settings while they are open, else the summary of a completed run,
    // else the main view.
    const showSettings = state.settingsOpen;
    const showSummary = !showSettings && completed;
    setShown(el["settings-view"], showSettings);
    setShown(el.summary, showSummary);
    setShown(el["main-view"], !showSettings && !showSummary);

    // The gear and Done share one place in the header; the settings are locked while a run is in flight.
    setShown(el["open-settings"], !showSettings);
    el["open-settings"].disabled = running;
    setShown(el["close-settings"], showSettings);

    setText(el.version, state.version ? versionLine(state.version) : "");
    setShown(el["models-message"], !state.models.ready);
    setText(el["models-message"], state.models.message ?? "");

    // Everything the user sets is locked while a run is in flight.
    el.inputs.disabled = running;
    el["drop-zone"].classList.toggle("is-dragging", state.dragging);
    el["file-name"].classList.toggle("is-empty", !state.file);
    writeFileName(state.file ? FILE.chosenPrefix + " " + state.file.name : FILE.none, state.file ? state.file.path : "");
    setShown(el["file-note"], Boolean(state.fileNote));
    setText(el["file-note"], state.fileNote ?? "");
    setShown(el["file-error"], Boolean(state.fileError));
    setText(el["file-error"], state.fileError ?? "");

    const folder = outputFolder(state);
    el["output-folder"].classList.toggle("is-empty", !folder);
    writeFolder(folder || OUTPUT.nothingYet, folder);

    renderSettings(state);

    // Start and Cancel swap places while running, and the progress takes the place of the sentence
    // that says which setting keeps Start off.
    const problem = startProblem(state);
    setShown(el.start, !running);
    el.start.disabled = !canStart(state);
    setShown(el.cancel, running);
    el.cancel.disabled = state.cancelling;
    setText(el.cancel, state.cancelling ? RUN.cancelling : RUN.cancel);
    setShown(el["start-problem"], !running);
    setCutText(el["start-problem"], problem ?? "");

    setShown(el["progress-box"], running);
    const percent = toPercent(state.progress.fraction);
    el.progress.value = percent;
    setCutText(el["progress-text"], running && state.progress.stage === "" && percent === 0 ? RUN.starting : stageLine(state.progress.stage, percent));

    // A run that did not complete is explained in a sentence; a completed one has its summary.
    const showMessage = done && outcome && outcome.kind !== "completed" && outcome.kind !== "failed";
    setShown(el.message, Boolean(showMessage));
    setText(el.message, showMessage ? outcome.message ?? "" : "");
    const errorText = state.error ?? (done && outcome && outcome.kind === "failed" ? outcome.message ?? "" : "");
    setShown(el.error, Boolean(errorText));
    setText(el.error, errorText);

    if (completed) {
      renderSummary(outcome);
    }
  }

  return { elements: el, render };
}
