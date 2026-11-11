// copy.js: every string the page shows, in one place and in English, so wording is reviewed in one
// file and a test can check all of it. It imports nothing and uses no em dash (U+2014).

export const APP = {
  title: "Meeting transcriptor",
  inputsLabel: "Recording and output folder",
  settings: "Settings",
  settingsDone: "Done",
  runLabel: "Run",
};

export const FILE = {
  heading: "Recording",
  choose: "Choose file...",
  dropHint: "or drop a .m4a, .mp4 or .wav file here",
  none: "No file chosen yet.",
  dropActive: "Drop the file to select it",
  chosenPrefix: "Chosen file:",
  nothingDropped: "No file was received. Choose a file with the button, or drop one onto the page.",
};

export const OPTIONS = {
  languageHeading: "Recording language",
  languages: { en: "English", fr: "French", de: "German" },
  formatsHeading: "Output formats",
  formats: { vtt: "VTT (subtitles)", json: "JSON (full data)", txt: "TXT (plain text)" },
  formatsNeeded: "Tick at least one output format.",
  speakersHeading: "Speakers",
  separation: "Tell speakers apart",
  speakerCount: "Number of speakers",
  speakerCountPlaceholder: "Automatic",
  speakerCountInvalid: "Enter a whole number from 1 to 10, or leave the field empty.",
  namesGroup: "Speaker names",
  namesHint: "Enter the number of speakers to name them.",
  namesInvalid: "Names must be different and contain no < or >.",
  rawFiles: "Also write raw files",
};

export const OUTPUT = {
  heading: "Output folder",
  sameAsInput: "Same folder as the recording",
  change: "Change...",
  nothingYet: "Choose a recording first.",
};

export const RUN = {
  start: "Start",
  cancel: "Cancel",
  cancelling: "Cancelling...",
  starting: "Starting...",
  progressLabel: "Transcription progress",
  openFolder: "Open output folder",
  another: "Transcribe another file",
};

export const SUMMARY = {
  heading: "Transcript ready",
  duration: "Recording length",
  processing: "Processing time",
  speed: "Speed",
  speakers: "Speakers",
  words: "Words",
  files: "Files written",
  noFiles: "No files were listed.",
};

export const FOOTER = {
  version: "Version",
};

export const ERRORS = {
  noBridge:
    "The page cannot reach the application. Open it from the Meeting Audio Transcriptor window, not from a web browser.",
  hostError:
    "The application reported a problem it could not describe. Try again, and if it keeps failing restart the application.",
  startFailed: "The transcription could not start. Try again, and if it keeps failing restart the application.",
  modelsUnknown: "The models could not be checked. Restart the application.",
  folderFailed: "The folder could not be opened. Open it from your file manager instead.",
};

export const UNITS = {
  hours: "h",
  minutes: "min",
  seconds: "s",
  speedSuffix: "times real time",
};

// The sentence for refused file names; it is the same wording the host uses, so a refusal reads the
// same whichever side noticed it.
export function refusal(names) {
  const quoted = names.map((name) => '"' + name + '"').join(", ");
  return quoted + " cannot be used: only .m4a, .mp4 and .wav files are supported. Choose a recording in one of those formats.";
}

export function percent(value) {
  return value + "%";
}

export function stageLine(stage, value) {
  return stage ? stage + ", " + value + "%" : value + "%";
}

// The label a speaker has in the transcript until the user names it; the name field shows it.
export function speakerLabel(number) {
  return "Speaker " + number;
}

export function speakerNameLabel(number) {
  return "Name of speaker " + number;
}

export function versionLine(version) {
  return FOOTER.version + " " + version;
}
