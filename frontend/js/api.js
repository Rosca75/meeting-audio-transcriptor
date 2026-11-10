// api.js, the only module that calls the bridge: an async wrapper per host method that matches
// replies to calls by request id, and a subscription for host events. Every wrapper is async, so a
// missing bridge is a rejected promise with a readable sentence and never a synchronous throw.
import { transport as defaultTransport } from "./bridge.js";
import { ERRORS } from "./copy.js";

export function createApi(transport) {
  const pending = new Map();
  const subscribers = new Map();
  let nextId = 1;

  function handle(text) {
    let message;
    try {
      message = JSON.parse(text);
    } catch {
      return;
    }
    if (!message || typeof message !== "object") {
      return;
    }
    if ("id" in message) {
      const waiting = pending.get(message.id);
      if (!waiting) {
        return;
      }
      pending.delete(message.id);
      if (typeof message.error === "string") {
        waiting.reject(new Error(message.error));
      } else {
        waiting.resolve(message.result ?? null);
      }
      return;
    }
    if (typeof message.event === "string") {
      for (const callback of [...(subscribers.get(message.event) ?? [])]) {
        try {
          callback(message.payload ?? null);
        } catch (failure) {
          console.error("an event handler for " + message.event + " failed:", failure);
        }
      }
    }
  }
  transport.onMessage(handle);

  async function call(method, args) {
    if (!transport.available) {
      throw new Error(ERRORS.noBridge);
    }
    return new Promise((resolve, reject) => {
      const id = nextId++;
      pending.set(id, { resolve, reject });
      try {
        transport.send(JSON.stringify({ id, method, args }));
      } catch (failure) {
        pending.delete(id);
        reject(failure instanceof Error ? failure : new Error(String(failure)));
      }
    });
  }

  return {
    appInfo: () => call("app.info", []),
    pickFile: () => call("file.pick", []),
    pickFolder: (initialFolder) => call("folder.pick", [initialFolder ?? null]),
    openFolder: (path) => call("folder.open", [path]),
    startRun: (request) => call("run.start", [request]),
    cancelRun: () => call("run.cancel", []),
    // A drop: the names go over the bridge for the refusal wording, and on WebView2 the file objects
    // go alongside so the host can resolve their paths natively.
    async dropFiles(files) {
      const list = Array.from(files);
      // The file objects are handed over inside the drop event itself, before anything is awaited.
      transport.sendDrop(list);
      return call("files.dropped", [list.map((file) => file.name)]);
    },
    // Subscribes to one host event; returns the function that unsubscribes.
    on(name, callback) {
      if (!subscribers.has(name)) {
        subscribers.set(name, new Set());
      }
      subscribers.get(name).add(callback);
      return () => subscribers.get(name)?.delete(callback);
    },
  };
}

export const api = createApi(defaultTransport);
