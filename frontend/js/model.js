// model.js: pure helpers shared by the store, the controller and the view: file name checks, speaker
// count parsing, speaker names, path pieces and the formatting of a summary. It has no DOM and no
// bridge access.
import { FILE, UNITS, refusal } from "./copy.js";

export const SUPPORTED_EXTENSIONS = [".m4a", ".mp4", ".wav"];
export const LANGUAGES = ["en", "fr", "de"];
export const FORMATS = ["vtt", "json", "txt"];
export const SPEAKER_MIN = 1;
export const SPEAKER_MAX = 10;
export const SPEAKER_NAME_MAX = 40;

export function baseName(path) {
  const parts = String(path).split(/[\\/]/);
  return parts[parts.length - 1];
}

// The folder part of a path written with either separator; empty when there is none.
export function directoryOf(path) {
  const text = String(path);
  const cut = Math.max(text.lastIndexOf("/"), text.lastIndexOf("\\"));
  if (cut < 0) {
    return "";
  }
  // Keep the root of "/file.wav" and "C:\file.wav".
  return cut === 0 || text[cut - 1] === ":" ? text.slice(0, cut + 1) : text.slice(0, cut);
}

export function extensionOf(name) {
  const file = baseName(name);
  const dot = file.lastIndexOf(".");
  return dot <= 0 ? "" : file.slice(dot).toLowerCase();
}

// Splits a text for a line that is cut in its middle: the last `keep` characters always show (a
// file's extension, a path's last folder) and the part before them gives way with an ellipsis.
export function splitForCut(text, keep) {
  const letters = Array.from(String(text ?? ""));
  return letters.length > keep ? [letters.slice(0, -keep).join(""), letters.slice(-keep).join("")] : ["", letters.join("")];
}

export function isSupportedName(name) {
  return SUPPORTED_EXTENSIONS.includes(extensionOf(name));
}

// The first supported name among dropped names, or the refusal sentence when there is none.
export function checkDroppedNames(names) {
  if (names.length === 0) {
    return { ok: false, reason: FILE.nothingDropped };
  }
  if (names.some(isSupportedName)) {
    return { ok: true, reason: null };
  }
  return { ok: false, reason: refusal(names) };
}

// Reads the speaker count field: empty is automatic (valid, null); otherwise a whole number 1 to 10.
export function parseSpeakerCount(text) {
  const trimmed = String(text ?? "").trim();
  if (trimmed === "") {
    return { valid: true, value: null };
  }
  if (!/^\d+$/.test(trimmed)) {
    return { valid: false, value: null };
  }
  const value = Number(trimmed);
  return value >= SPEAKER_MIN && value <= SPEAKER_MAX ? { valid: true, value } : { valid: false, value: null };
}

// The names a run carries: index k names speaker k+1. The first count names, trimmed, with the
// trailing empty ones dropped; an empty entry in the middle keeps that speaker's default label.
export function speakerNamesToSend(names, count) {
  const list = names.slice(0, count).map((name) => String(name ?? "").trim());
  while (list.length > 0 && list[list.length - 1] === "") {
    list.pop();
  }
  return list;
}

// True when the host will accept the names: each at most 40 characters, with no control character
// and no angle bracket, and no two equal ignoring case. Empty names are always allowed.
export function speakerNamesValid(names) {
  const seen = new Set();
  for (const name of names.map((text) => String(text ?? "").trim()).filter((text) => text !== "")) {
    if (name.length > SPEAKER_NAME_MAX || /[\u0000-\u001f\u007f-\u009f<>]/.test(name)) {
      return false;
    }
    const folded = name.toUpperCase();
    if (seen.has(folded)) {
      return false;
    }
    seen.add(folded);
  }
  return true;
}

export function selectedFormats(formats) {
  return FORMATS.filter((code) => formats[code]);
}

// Whole percent from a 0 to 1 fraction, clamped.
export function toPercent(fraction) {
  if (!Number.isFinite(fraction)) {
    return 0;
  }
  return Math.round(Math.min(1, Math.max(0, fraction)) * 100);
}

export function formatDuration(seconds) {
  const total = Math.max(0, Math.round(Number(seconds) || 0));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = total % 60;
  if (hours > 0) {
    return `${hours} ${UNITS.hours} ${minutes} ${UNITS.minutes} ${rest} ${UNITS.seconds}`;
  }
  if (minutes > 0) {
    return `${minutes} ${UNITS.minutes} ${rest} ${UNITS.seconds}`;
  }
  return `${rest} ${UNITS.seconds}`;
}

export function formatSpeed(factor) {
  const value = Number(factor) || 0;
  return `${value.toFixed(1)} ${UNITS.speedSuffix}`;
}
