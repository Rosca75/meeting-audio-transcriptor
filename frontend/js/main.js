// A placeholder page module: it speaks both host transports, asks for app.info and shows drops.
const log = document.getElementById("log");
const webview = window.chrome && window.chrome.webview;
let nextId = 1;

function show(text) {
  log.textContent += text + "\n";
}

function send(message) {
  const text = JSON.stringify(message);
  if (webview) {
    webview.postMessage(text);
  } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.host) {
    window.webkit.messageHandlers.host.postMessage(text);
  } else {
    show("no host transport: open this page inside the application window");
  }
}

function receive(text) {
  show("host: " + text);
}

window.__hostReceive = receive;
if (webview) {
  webview.addEventListener("message", (event) => receive(event.data));
}

document.getElementById("info").addEventListener("click", () => send({ id: nextId++, method: "app.info" }));

const zone = document.getElementById("drop");
zone.addEventListener("dragover", (event) => {
  event.preventDefault();
  zone.classList.add("over");
});
zone.addEventListener("dragleave", () => zone.classList.remove("over"));
zone.addEventListener("drop", (event) => {
  event.preventDefault();
  zone.classList.remove("over");
  if (webview && webview.postMessageWithAdditionalObjects) {
    webview.postMessageWithAdditionalObjects(JSON.stringify({ drop: true }), event.dataTransfer.files);
  }
});

show("page ready");
