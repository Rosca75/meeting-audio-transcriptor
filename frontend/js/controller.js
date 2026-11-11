// controller.js: turns user gestures and host events into store actions and bridge calls. It holds no
// state of its own and touches no DOM, so every flow runs in a Node test with a fake api and a store.
import { ERRORS } from "./copy.js";
import { checkDroppedNames } from "./model.js";
import { buildRequest, canStart, outputFolder } from "./state.js";

function messageOf(failure) {
  return failure instanceof Error && failure.message ? failure.message : ERRORS.hostError;
}

export function createController({ api, store }) {
  const dispatch = (action) => store.dispatch(action);

  // Runs a bridge call and turns a rejection into the page's error line.
  async function guarded(work) {
    try {
      return await work();
    } catch (failure) {
      dispatch({ type: "failure", message: messageOf(failure) });
      return null;
    }
  }

  return {
    // Subscribes to host events and reads the application info. Returns when the info arrived.
    async init() {
      api.on("file:selected", (payload) => dispatch({ type: "fileSelected", selection: payload }));
      api.on("file:refused", (payload) => dispatch({ type: "fileRefused", reason: payload.reason }));
      api.on("run:progress", (payload) => dispatch({ type: "progress", progress: payload }));
      api.on("run:finished", (payload) => dispatch({ type: "finished", outcome: payload }));
      api.on("host:error", () => dispatch({ type: "hostError" }));
      try {
        dispatch({ type: "info", info: await api.appInfo() });
      } catch (failure) {
        // Without the host's answer nothing can run, so Start stays disabled and says why.
        dispatch({ type: "info", info: { version: "", models: { ready: false, message: messageOf(failure) } } });
      }
    },

    async pick() {
      if (store.getState().phase === "running") {
        return;
      }
      const selection = await guarded(() => api.pickFile());
      if (selection) {
        dispatch({ type: "fileSelected", selection });
      }
    },

    // The files were dropped on the page. A wholly unsupported drop is refused at once; the host is
    // told either way, because it resolves the paths and double-checks.
    async drop(files) {
      if (store.getState().phase === "running") {
        return;
      }
      const names = Array.from(files).map((file) => file.name);
      const check = checkDroppedNames(names);
      if (!check.ok) {
        dispatch({ type: "fileRefused", reason: check.reason });
      }
      await guarded(() => api.dropFiles(files));
    },

    async chooseFolder() {
      const state = store.getState();
      if (state.phase === "running") {
        return;
      }
      const path = await guarded(() => api.pickFolder(outputFolder(state) || null));
      if (path) {
        dispatch({ type: "folderChosen", path });
      }
    },

    async start() {
      const state = store.getState();
      if (!canStart(state)) {
        return;
      }
      dispatch({ type: "startRequested" });
      try {
        await api.startRun(buildRequest(state));
      } catch (failure) {
        dispatch({ type: "startRejected", message: messageOf(failure) });
      }
    },

    async cancel() {
      if (store.getState().phase !== "running") {
        return;
      }
      dispatch({ type: "cancelRequested" });
      await guarded(() => api.cancelRun());
    },

    async openOutputFolder() {
      const state = store.getState();
      const path = state.outcome?.outputFolder || outputFolder(state);
      if (!path) {
        return;
      }
      await guarded(() => api.openFolder(path));
    },

    another() {
      dispatch({ type: "another" });
    },

    // Shows the settings panel. Returns true when it opened, so the caller can move the focus.
    openSettings() {
      const wasOpen = store.getState().settingsOpen;
      dispatch({ type: "settingsOpened" });
      return !wasOpen && store.getState().settingsOpen;
    },

    // Returns to the main view. Returns true when the panel was open.
    closeSettings() {
      const wasOpen = store.getState().settingsOpen;
      dispatch({ type: "settingsClosed" });
      return wasOpen;
    },
  };
}
