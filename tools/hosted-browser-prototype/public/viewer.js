const element = id => document.getElementById(id);
const status = text => { element("status").textContent = text; };
const canvas = element("frame");
const screen = element("screen");
const base = new URL("./", location.href);
let socket;
let drawing = false;
let ended = false;
function send(message) {
  if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify(message));
}
function setConnected(connected) {
  for (const id of ["fill", "end", "send", "target", "photo", "speakerPhoto", "remember", "forget"]) element(id).disabled = !connected;
}
async function post(path) {
  try { return await fetch(new URL(path, base), { method: "POST" }); }
  catch { status("The browser service could not be reached. Check the local app and reconnect."); return null; }
}
async function connect() {
  if (socket && socket.readyState < WebSocket.CLOSING) return;
  if (ended) return;
  status("Connecting to the existing browser session...");
  const stream = new URL("stream", base);
  stream.protocol = location.protocol === "https:" ? "wss:" : "ws:";
  socket = new WebSocket(stream);
  socket.binaryType = "blob";
  socket.onopen = () => { setConnected(true); status("Connected. Log into Meetup if needed, open the prepared event, then fill and review."); };
  socket.onclose = () => { setConnected(false); if (!ended) status("Disconnected. Reconnect while the session is still alive; ending or expiry loses unsaved state."); };
  socket.onerror = () => status("Connection failed. Check that this session has not expired or has another controller.");
  socket.onmessage = async event => {
    if (typeof event.data === "string") {
      const message = JSON.parse(event.data);
      if (message.type === "navigation") {
        element("address").textContent = `${message.live ? "LIVE MEETUP" : "OFFLINE FIXTURE"} — ${message.url}`;
      }
      if (message.type === "report") element("report").textContent = JSON.stringify(message.report, null, 2);
      if (message.type === "busy") {
        element("fill").disabled = message.busy;
        element("send").disabled = message.busy;
        for (const id of ["target", "photo", "speakerPhoto"]) element(id).disabled = message.busy;
        status(message.busy ? "Preparing the event. Wait before interacting." : "Preparation finished. Review the report and remote form.");
      }
      if (message.type === "error") status(message.message);
      return;
    }
    if (drawing) return;
    drawing = true;
    try {
      const image = await createImageBitmap(event.data);
      canvas.width = image.width;
      canvas.height = image.height;
      canvas.getContext("2d").drawImage(image, 0, 0);
      image.close();
    } catch {
      status("A browser frame could not be decoded. Reconnect if the display remains stale.");
    } finally { drawing = false; }
  };
}
element("connect").onclick = () => connect();
element("fill").onclick = () => send({ type: "fill" });
element("fill").textContent = "Fill event fields (not Save/Publish)";
element("remember").onclick = async () => {
  const response = await post("remember");
  status(response?.ok ? "Meetup login cookies encrypted locally for up to seven days. Expiry/MFA may still require login."
    : "Login was not saved. Opt in when starting through the organiser app; standalone mode cannot remember login.");
};
element("forget").onclick = async () => {
  ended = true;
  const response = await post("forget");
  ended = response?.ok === true;
  if (ended) { socket?.close(); setConnected(false); }
  status(ended ? "Remembered login deleted and session ended." : "Could not forget login. Return to the organiser app to retry.");
};
for (const type of ["photo", "speakerPhoto"]) element(type).onclick = () => send({ type });
element("target").onclick = async () => {
  const response = await post("target");
  status(response?.ok ? "Returned to the prepared event. Review its identity before filling." : "Could not open the prepared event. Wait for browser interaction to finish and retry.");
};
element("end").onclick = async () => {
  ended = true;
  const response = await post("end");
  if (!response?.ok) { ended = false; status("Ending the session failed. Return to the organiser app and retry, or stop the local app."); return; }
  socket?.close();
  setConnected(false);
  status("Session ended. Browser data was discarded.");
};
element("send").onclick = () => {
  const text = element("text").value;
  if (text) send({ type: "text", text });
  element("text").value = "";
};
function point(event) {
  const bounds = canvas.getBoundingClientRect();
  return { x: Math.max(0, Math.min(canvas.width - 1, (event.clientX - bounds.left) * canvas.width / bounds.width)),
    y: Math.max(0, Math.min(canvas.height - 1, (event.clientY - bounds.top) * canvas.height / bounds.height)) };
}
let pointer;
let moveFrame;
let pendingMove;
screen.addEventListener("pointerdown", event => {
  if (event.button !== 0) return;
  screen.focus({ preventScroll: true });
  pointer = { ...point(event), id: event.pointerId };
  if (event.pointerType === "mouse") send({ type: "pointerDown", ...pointer });
  screen.setPointerCapture(event.pointerId);
});
screen.addEventListener("pointermove", event => {
  if (pointer?.id !== event.pointerId || event.pointerType !== "mouse") return;
  pendingMove = point(event);
  if (!moveFrame) moveFrame = requestAnimationFrame(() => {
    moveFrame = null;
    if (pointer) send({ type: "pointerMove", ...pendingMove });
  });
});
screen.addEventListener("pointerup", event => {
  if (!pointer || pointer.id !== event.pointerId) return;
  const end = point(event);
  const deltaY = pointer.y - end.y;
  if (event.pointerType === "mouse") send({ type: "pointerUp", ...end });
  else if (Math.abs(deltaY) > 12) send({ type: "wheel", deltaX: 0, deltaY });
  else send({ type: "click", ...end });
  pointer = null;
});
screen.addEventListener("pointercancel", event => {
  if (pointer && event.pointerType === "mouse") send({ type: "pointerUp", ...point(event) });
  pointer = null;
});
screen.addEventListener("wheel", event => {
  event.preventDefault();
  const multiplier = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? canvas.height : 1;
  send({ type: "wheel", deltaX: event.deltaX * multiplier, deltaY: event.deltaY * multiplier });
}, { passive: false });
screen.addEventListener("keydown", event => {
  if (event.ctrlKey || event.metaKey || event.altKey) return;
  if (event.key.length === 1) send({ type: "text", text: event.key });
  else if (["Backspace", "Delete", "Enter", "Tab", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(event.key))
    send({ type: "key", key: event.shiftKey && event.key === "Tab" ? "Shift+Tab" : event.key });
  else return;
  event.preventDefault();
});
async function start() {
  const token = location.hash.slice(1);
  history.replaceState({}, "", location.pathname);
  if (token) {
    const response = await fetch(new URL("authorize", base), { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token }) });
    if (!response.ok) { status("The one-time launch link is invalid or expired."); return; }
  }
  await connect();
}
start().catch(() => status("The browser review service could not be reached."));
