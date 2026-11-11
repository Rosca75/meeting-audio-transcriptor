// main.js, the page's entry point: creates the store, the controller and the view, wires DOM events
// to the controller, and asks the host for the application info. It holds no logic of its own.
import { api } from "./api.js";
import { createController } from "./controller.js";
import { createStore } from "./state.js";
import { createView } from "./view.js";

const store = createStore();
const view = createView(document);
const controller = createController({ api, store });
const el = view.elements;

store.subscribe((state) => view.render(state));
view.render(store.getState());

el["choose-file"].addEventListener("click", () => controller.pick());
el["choose-folder"].addEventListener("click", () => controller.chooseFolder());
el.start.addEventListener("click", () => controller.start());
el.cancel.addEventListener("click", () => controller.cancel());
el["open-folder"].addEventListener("click", () => controller.openOutputFolder());
el.another.addEventListener("click", () => controller.another());

// The gear and Done share one place in the header, so the keyboard focus follows the swap; Escape
// leaves the settings as Done does.
el["open-settings"].addEventListener("click", () => controller.openSettings() && el["close-settings"].focus());
el["close-settings"].addEventListener("click", () => controller.closeSettings() && el["open-settings"].focus());
document.addEventListener("keydown", (event) => event.key === "Escape" && controller.closeSettings() && el["open-settings"].focus());

for (const [code, input] of Object.entries(el.languageInputs)) {
  input.addEventListener("change", () => store.dispatch({ type: "languageChanged", language: code }));
}
for (const [code, input] of Object.entries(el.formatInputs)) {
  input.addEventListener("change", () => store.dispatch({ type: "formatToggled", format: code, checked: input.checked }));
}
el.separation.addEventListener("change", () => store.dispatch({ type: "separationChanged", value: el.separation.checked }));
el["speaker-count"].addEventListener("input", () => store.dispatch({ type: "speakerCountChanged", text: el["speaker-count"].value }));
el.nameInputs.forEach((input, index) => {
  input.addEventListener("input", () => store.dispatch({ type: "speakerNameChanged", index, text: input.value }));
});
el["raw-files"].addEventListener("change", () => store.dispatch({ type: "rawFilesChanged", value: el["raw-files"].checked }));

// Dropping a file anywhere on the page selects it; the default action (the browser opening the file)
// is always cancelled, so a miss never navigates away from the page.
let dragDepth = 0;
document.addEventListener("dragenter", (event) => {
  event.preventDefault();
  dragDepth += 1;
  store.dispatch({ type: "dragging", value: true });
});
document.addEventListener("dragover", (event) => {
  event.preventDefault();
});
document.addEventListener("dragleave", () => {
  dragDepth = Math.max(0, dragDepth - 1);
  if (dragDepth === 0) {
    store.dispatch({ type: "dragging", value: false });
  }
});
document.addEventListener("drop", (event) => {
  event.preventDefault();
  dragDepth = 0;
  store.dispatch({ type: "dragging", value: false });
  const files = event.dataTransfer ? event.dataTransfer.files : [];
  if (files.length > 0) {
    controller.drop(files);
  }
});

controller.init();
