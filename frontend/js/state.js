// state.js, the single holder of page state: one plain object, pure reducer-style transitions over it
// and a tiny store with subscribe. It has no DOM and no bridge access, so every flow is unit-testable.
import { SPEAKER_MAX, directoryOf, parseSpeakerCount, selectedFormats, speakerNamesToSend, speakerNamesValid } from "./model.js";
import { ERRORS, OPTIONS } from "./copy.js";

export function initialState() {
  return {
    // The host's answer to app.info.
    version: "",
    models: { checked: false, ready: true, message: null },
    // The chosen recording and the sentences about it.
    file: null,
    fileNote: null,
    fileError: null,
    dragging: false,
    // The form.
    language: "en",
    formats: { vtt: true, json: true, txt: true },
    speakerSeparation: true,
    speakerCountText: "",
    // One name per possible speaker, as typed; all ten are kept so a name survives a change of count.
    speakerNames: Array(SPEAKER_MAX).fill(""),
    rawFiles: false,
    chosenFolder: null,
    // True while the settings panel replaces the main view.
    settingsOpen: false,
    // The run: "idle", "running" or "done".
    phase: "idle",
    cancelling: false,
    progress: { stage: "", fraction: 0 },
    outcome: null,
    // A failure that is not a run outcome: a rejected call, a host error, a missing bridge.
    error: null,
  };
}

// The folder files will go to: the user's choice, else the recording's own folder, else nothing.
export function outputFolder(state) {
  if (state.chosenFolder) {
    return state.chosenFolder;
  }
  return state.file ? directoryOf(state.file.path) : "";
}

// How many speakers can be named: the count the user entered, or zero while separation is off or
// the count is automatic or invalid.
export function namedSpeakers(state) {
  return state.speakerSeparation ? parseSpeakerCount(state.speakerCountText).value ?? 0 : 0;
}

// The first sentence about a setting that keeps a run from starting, or null when the settings are
// fine. Only the names a run would carry are checked.
export function settingsProblem(state) {
  if (selectedFormats(state.formats).length === 0) {
    return OPTIONS.formatsNeeded;
  }
  if (!parseSpeakerCount(state.speakerCountText).valid) {
    return OPTIONS.speakerCountInvalid;
  }
  if (!speakerNamesValid(state.speakerNames.slice(0, namedSpeakers(state)))) {
    return OPTIONS.namesInvalid;
  }
  return null;
}

export function canStart(state) {
  return state.phase !== "running" && state.models.ready && state.file !== null && settingsProblem(state) === null;
}

// The sentence shown beside Start, or null. A run in flight, missing models (the banner says so) and
// a missing recording (the empty recording area says so) leave Start off without a sentence.
export function startProblem(state) {
  return state.phase === "running" ? null : settingsProblem(state);
}

// The run.start argument for the current form; call only when canStart is true.
export function buildRequest(state) {
  const count = parseSpeakerCount(state.speakerCountText);
  const request = {
    inputPath: state.file.path,
    language: state.language,
    formats: selectedFormats(state.formats),
    speakerSeparation: state.speakerSeparation,
    rawFiles: state.rawFiles,
  };
  if (state.speakerSeparation && count.value !== null) {
    request.speakerCount = count.value;
    const names = speakerNamesToSend(state.speakerNames, count.value);
    if (names.length > 0) {
      request.speakerNames = names;
    }
  }
  if (state.chosenFolder) {
    request.outputFolder = state.chosenFolder;
  }
  return request;
}

// Each action is a plain object with a type; reduce returns a new state and never mutates the old one.
export function reduce(state, action) {
  switch (action.type) {
    case "info": {
      const models = action.info?.models;
      return {
        ...state,
        version: String(action.info?.version ?? ""),
        models: {
          checked: true,
          ready: models ? models.ready === true : false,
          message: models?.message ?? null,
        },
      };
    }
    case "fileSelected": {
      if (state.phase === "running") {
        return state;
      }
      const selection = action.selection;
      return {
        ...state,
        file: { path: selection.path, name: selection.name },
        fileNote: selection.note ?? null,
        fileError: null,
        error: null,
        // A new recording starts a fresh round: the previous outcome no longer describes it.
        phase: "idle",
        outcome: null,
        progress: { stage: "", fraction: 0 },
        dragging: false,
      };
    }
    case "fileRefused":
      if (state.phase === "running") {
        return state;
      }
      return { ...state, fileError: action.reason, fileNote: null, dragging: false };
    case "dragging":
      return { ...state, dragging: state.phase === "running" ? false : action.value === true };
    case "languageChanged":
      return { ...state, language: action.language };
    case "formatToggled":
      return { ...state, formats: { ...state.formats, [action.format]: action.checked === true } };
    case "separationChanged":
      return { ...state, speakerSeparation: action.value === true };
    case "speakerCountChanged":
      return { ...state, speakerCountText: String(action.text ?? "") };
    case "speakerNameChanged": {
      if (!Number.isInteger(action.index) || action.index < 0 || action.index >= SPEAKER_MAX) {
        return state;
      }
      const speakerNames = [...state.speakerNames];
      speakerNames[action.index] = String(action.text ?? "");
      return { ...state, speakerNames };
    }
    case "settingsOpened":
      // The settings are locked while a run is in flight, so the panel stays shut.
      return state.phase === "running" || state.settingsOpen ? state : { ...state, settingsOpen: true };
    case "settingsClosed":
      return state.settingsOpen ? { ...state, settingsOpen: false } : state;
    case "rawFilesChanged":
      return { ...state, rawFiles: action.value === true };
    case "folderChosen":
      return { ...state, chosenFolder: action.path || state.chosenFolder };
    case "startRequested":
      return {
        ...state,
        phase: "running",
        settingsOpen: false,
        cancelling: false,
        progress: { stage: "", fraction: 0 },
        outcome: null,
        error: null,
        fileError: null,
      };
    case "startRejected":
      // Only a run the page believed it had started is undone; a stale rejection changes nothing.
      return state.phase === "running" && !state.outcome
        ? { ...state, phase: "idle", cancelling: false, error: action.message }
        : state;
    case "progress": {
      if (state.phase !== "running") {
        return state;
      }
      const fraction = Number(action.progress?.fraction);
      const safe = Number.isFinite(fraction) ? Math.min(1, Math.max(0, fraction)) : 0;
      return {
        ...state,
        progress: {
          stage: String(action.progress?.stage ?? state.progress.stage),
          // The bar never moves backwards, whatever order events arrive in.
          fraction: Math.max(state.progress.fraction, safe),
        },
      };
    }
    case "cancelRequested":
      return state.phase === "running" ? { ...state, cancelling: true } : state;
    case "finished": {
      const outcome = action.outcome;
      return {
        ...state,
        phase: "done",
        cancelling: false,
        outcome,
        progress: outcome.kind === "completed" ? { ...state.progress, fraction: 1 } : state.progress,
        error: null,
      };
    }
    case "failure":
      return { ...state, error: action.message };
    case "hostError":
      return { ...state, error: ERRORS.hostError };
    case "dismissError":
      return { ...state, error: null };
    case "another":
      return { ...state, phase: "idle", outcome: null, file: null, fileNote: null, fileError: null, progress: { stage: "", fraction: 0 } };
    default:
      return state;
  }
}

// The one store. Views subscribe and render; nothing else holds page state.
export function createStore(initial = initialState()) {
  let state = initial;
  const listeners = new Set();
  return {
    getState: () => state,
    dispatch(action) {
      const next = reduce(state, action);
      if (next === state) {
        return;
      }
      state = next;
      for (const listener of [...listeners]) {
        listener(state);
      }
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}
