// view.js: renders the store's state into the static markup of index.html. It keeps no state of its
// own: build() creates the repeated controls once, render(state) sets every property from the state.
import { APP, FILE, OPTIONS, OUTPUT, RUN, SUMMARY, stageLine, versionLine } from "./copy.js";
import { FORMATS, LANGUAGES, formatDuration, formatSpeed, parseSpeakerCount, toPercent } from "./model.js";
import { outputFolder, startProblem } from "./state.js";

function byId(doc, id) {
  const element = doc.getElementById(id);
  if (!element) {
    throw new Error("index.html has no element with id " + id + "; the page and its script are out of step");
  }
  return element;
}

// A labelled radio or checkbox in the page's choice style.
function choice(doc, type, name, value, text) {
  const label = doc.createElement("label");
  label.className = "choice";
  const input = doc.createElement("input");
  input.type = type;
  input.name = name;
  input.value = value;
  const span = doc.createElement("span");
  span.textContent = text;
  label.append(input, span);
  return { label, input };
}

function setText(element, text) {
  if (element.textContent !== text) {
    element.textContent = text;
  }
}

function setShown(element, shown) {
  element.hidden = !shown;
}

export function createView(doc) {
  const el = {};
  for (const id of [
    "app-title", "app-tagline", "models-message", "inputs", "inputs-legend", "file-heading", "drop-zone",
    "choose-file", "drop-hint", "file-name", "file-note", "file-error", "options-heading", "language-legend",
    "language-choices", "formats-legend", "format-choices", "formats-error", "speakers-legend", "separation",
    "separation-label", "speaker-count", "speaker-count-label", "speaker-count-help", "speaker-count-error",
    "raw-files", "raw-files-label", "output-heading", "output-folder", "choose-folder", "run-region", "start",
    "cancel", "start-problem", "progress-card", "progress", "progress-text", "message", "error", "summary",
    "summary-heading", "summary-list", "files-heading", "files-list", "open-folder", "another", "version",
  ]) {
    el[id] = byId(doc, id);
  }

  // The repeated controls are created once; render only sets their properties.
  el.languageInputs = {};
  for (const code of LANGUAGES) {
    const made = choice(doc, "radio", "language", code, OPTIONS.languages[code]);
    el.languageInputs[code] = made.input;
    el["language-choices"].append(made.label);
  }
  el.formatInputs = {};
  for (const code of FORMATS) {
    const made = choice(doc, "checkbox", "format", code, OPTIONS.formats[code]);
    el.formatInputs[code] = made.input;
    el["format-choices"].append(made.label);
  }

  // The static strings never change, so they are written once.
  doc.title = APP.title;
  setText(el["app-title"], APP.title);
  setText(el["app-tagline"], APP.tagline);
  el["inputs-legend"].textContent = APP.settingsLabel;
  el["options-heading"].textContent = APP.optionsLabel;
  el["run-region"].setAttribute("aria-label", APP.runLabel);
  setText(el["file-heading"], FILE.heading);
  setText(el["choose-file"], FILE.choose);
  setText(el["drop-hint"], FILE.dropHint);
  setText(el["language-legend"], OPTIONS.languageHeading);
  setText(el["formats-legend"], OPTIONS.formatsHeading);
  setText(el["speakers-legend"], OPTIONS.speakersHeading);
  setText(el["separation-label"], OPTIONS.separation);
  setText(el["speaker-count-label"], OPTIONS.speakerCount);
  setText(el["speaker-count-help"], OPTIONS.speakerCountHelp);
  el["speaker-count"].placeholder = OPTIONS.speakerCountPlaceholder;
  setText(el["raw-files-label"], OPTIONS.rawFiles);
  setText(el["output-heading"], OUTPUT.heading);
  setText(el["choose-folder"], OUTPUT.change);
  setText(el.cancel, RUN.cancel);
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
    el["files-list"].replaceChildren();
    const files = outcome.outputFiles ?? [];
    for (const path of files.length ? files : [SUMMARY.noFiles]) {
      const item = doc.createElement("li");
      item.textContent = path;
      el["files-list"].append(item);
    }
  }

  function render(state) {
    const running = state.phase === "running";
    const done = state.phase === "done";
    const outcome = state.outcome;

    setText(el.version, state.version ? versionLine(state.version) : "");

    setShown(el["models-message"], !state.models.ready);
    setText(el["models-message"], state.models.message ?? "");

    // Everything the user sets is locked while a run is in flight.
    el.inputs.disabled = running;
    el["drop-zone"].classList.toggle("is-dragging", state.dragging);
    setText(el["file-name"], state.file ? FILE.chosenPrefix + " " + state.file.name : FILE.none);
    setShown(el["file-note"], Boolean(state.fileNote));
    setText(el["file-note"], state.fileNote ?? "");
    setShown(el["file-error"], Boolean(state.fileError));
    setText(el["file-error"], state.fileError ?? "");

    for (const code of LANGUAGES) {
      el.languageInputs[code].checked = state.language === code;
    }
    let formatCount = 0;
    for (const code of FORMATS) {
      el.formatInputs[code].checked = state.formats[code] === true;
      formatCount += state.formats[code] === true ? 1 : 0;
    }
    setShown(el["formats-error"], formatCount === 0);
    setText(el["formats-error"], formatCount === 0 ? OPTIONS.formatsNeeded : "");

    el.separation.checked = state.speakerSeparation;
    if (el["speaker-count"].value !== state.speakerCountText) {
      el["speaker-count"].value = state.speakerCountText;
    }
    el["speaker-count"].disabled = running || !state.speakerSeparation;
    const countValid = parseSpeakerCount(state.speakerCountText).valid;
    el["speaker-count"].setAttribute("aria-invalid", countValid ? "false" : "true");
    setShown(el["speaker-count-error"], !countValid);
    setText(el["speaker-count-error"], countValid ? "" : OPTIONS.speakerCountInvalid);
    el["raw-files"].checked = state.rawFiles;

    const folder = outputFolder(state);
    setText(el["output-folder"], folder || OUTPUT.nothingYet);

    // Start and Cancel swap places while running.
    const problem = startProblem(state);
    setShown(el.start, !running);
    el.start.disabled = problem !== null;
    setText(el.start, RUN.start);
    setShown(el.cancel, running);
    el.cancel.disabled = state.cancelling;
    setText(el.cancel, state.cancelling ? RUN.cancelling : RUN.cancel);
    // The reason Start is off is shown, except for the plain "running" state.
    setText(el["start-problem"], problem && problem !== "running" ? problem : "");

    setShown(el["progress-card"], running);
    const percent = toPercent(state.progress.fraction);
    el.progress.value = percent;
    setText(el["progress-text"], running && state.progress.stage === "" && percent === 0 ? RUN.starting : stageLine(state.progress.stage, percent));

    // A run that did not complete is explained in a sentence; a completed one has its summary.
    const showMessage = done && outcome && outcome.kind !== "completed" && outcome.kind !== "failed";
    setShown(el.message, Boolean(showMessage));
    setText(el.message, showMessage ? outcome.message ?? "" : "");
    const errorText = state.error ?? (done && outcome && outcome.kind === "failed" ? outcome.message ?? "" : "");
    setShown(el.error, Boolean(errorText));
    setText(el.error, errorText);

    const showSummary = done && outcome && outcome.kind === "completed";
    setShown(el.summary, Boolean(showSummary));
    if (showSummary) {
      renderSummary(outcome);
    }
  }

  return { elements: el, render };
}
