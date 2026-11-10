// bridge.js: the one transport abstraction over the two desktop hosts. WebView2 posts strings out with
// chrome.webview.postMessage and delivers messages through an event; WebKitGTK posts strings out
// through webkit.messageHandlers.host and delivers messages by calling window.__hostReceive. Only
// api.js imports this module, so no other file knows which host it runs in.

// Builds a transport over a window-like object. Tests pass a fake; the page passes window.
export function createTransport(win) {
  const listeners = new Set();
  const webview2 = win?.chrome?.webview;
  const gtk = win?.webkit?.messageHandlers?.host;

  function deliver(raw) {
    // The host may post a JSON string or, on WebView2, an already parsed object.
    const text = typeof raw === "string" ? raw : JSON.stringify(raw);
    for (const listener of [...listeners]) {
      try {
        listener(text);
      } catch (failure) {
        console.error("a bridge listener failed:", failure);
      }
    }
  }

  // The WebKitGTK host calls this; it is defined for both hosts so the contract has one entry point.
  if (win) {
    win.__hostReceive = deliver;
  }
  if (webview2 && typeof webview2.addEventListener === "function") {
    webview2.addEventListener("message", (event) => deliver(event.data));
  }

  const kind = webview2 ? "webview2" : gtk ? "gtk" : "none";

  return {
    kind,
    available: kind !== "none",
    send(text) {
      if (kind === "webview2") {
        webview2.postMessage(text);
      } else if (kind === "gtk") {
        gtk.postMessage(text);
      } else {
        throw new Error("no host transport is present");
      }
    },
    onMessage(callback) {
      listeners.add(callback);
      return () => listeners.delete(callback);
    },
    // Hands dropped File objects to the host. Only WebView2 can pass them natively; the GTK host
    // intercepts the drop itself, so there is nothing to send there.
    sendDrop(files) {
      if (kind === "webview2" && typeof webview2.postMessageWithAdditionalObjects === "function") {
        webview2.postMessageWithAdditionalObjects(JSON.stringify({ drop: true }), files);
      }
    },
  };
}

export const transport = createTransport(typeof window === "undefined" ? undefined : window);
