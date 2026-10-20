import { capturePreparedContent, meetupTarget, trustedOrigin, validatePayload } from "./handoff.js";
import { fillMeetupForm } from "./form.js";

const element = id => document.getElementById(id);
let pending;
let busy = false;
const status = (message, error = false) => {
  element("status").textContent = message;
  element("status").hidden = !message;
  element("status").dataset.kind = error ? "error" : "info";
};
async function activeTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !tab.url) throw new Error("Select the app or Meetup tab and open the extension again.");
  return tab;
}
function show() {
  element("handoff").hidden = !pending;
  element("fill-status").hidden = true;
  if (!pending) return;
  const payload = pending.payload;
  element("target").href = payload.eventUrl ?? `https://www.meetup.com/${payload.groupSlug}/`;
  element("image").href = `data:image/png;base64,${payload.imageBase64}`;
}
document.addEventListener("meetup-handoff-cleared", () => {
  pending = null;
  element("fill-status").hidden = true;
  element("image").removeAttribute("href");
  show();
});
function action(id, work) {
  element(id).addEventListener("click", async () => {
    if (busy) return;
    busy = true;
    element("results").replaceChildren();
    element("results").hidden = true;
    try { await work(); }
    catch (error) {
      status(error.message, true);
      if (id === "fill" || id === "photo") fillStatus(error.message, true);
    }
    finally { busy = false; }
  });
}
action("connect", async () => {
  const origin = trustedOrigin(element("origin").value);
  const { apiGrant, trustedOrigin: current } = await chrome.storage.local.get(["apiGrant", "trustedOrigin"]);
  if (apiGrant && current !== origin) throw new Error("Sign out before switching to a different app.");
  await chrome.storage.local.set({ trustedOrigin: origin });
  await chrome.storage.session.remove("pending");
  pending = null;
  show();
  status(`Trusted ${origin}. Prepare an event there and capture it using this popup.`);
});
action("capture", async () => {
  const tab = await activeTab();
  const { trustedOrigin: origin } = await chrome.storage.local.get("trustedOrigin");
  if (!origin || new URL(tab.url).origin !== origin) throw new Error("This tab does not match your trusted app origin.");
  const [{ result }] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: capturePreparedContent });
  if (result?.origin !== origin) throw new Error("The source tab changed during capture.");
  pending = { origin, payload: validatePayload(result.payload) };
  await chrome.storage.session.set({ pending });
  show();
  status("Captured. Open Meetup, navigate to the correct create/edit form, then open this extension again.");
});
function fillStatus(message, error = false) {
  element("fill-status").textContent = message;
  element("fill-status").hidden = false;
  element("fill-status").dataset.kind = error ? "error" : "info";
}
async function fill(photo = false) {
  if (!pending) throw new Error("Capture prepared content first.");
  pending.payload = validatePayload(pending.payload);
  const tab = await activeTab();
  meetupTarget(tab.url, pending.payload, true);
  const [{ result }] = await chrome.scripting.executeScript({
    target: { tabId: tab.id }, func: fillMeetupForm,
    args: [pending.payload, { replace: true, schedule: true, photo }]
  });
  if (!result || !Array.isArray(result.errors)) throw new Error("The tab did not return a valid result. Review its fields before retrying.");
  for (const [label, lines] of [["Filled", result.filled], ["Manual", result.manual], ["Error", result.errors]]) {
    for (const line of lines) {
      const li = document.createElement("li");
      li.textContent = `${label}: ${line}`;
      element("results").append(li);
    }
    element("results").hidden = false;
  }
  const message = result.errors.length
    ? `Filling incomplete. ${result.errors.join(" ")} Review the manual steps below. Nothing was saved or published.`
    : "Awaiting your review: complete all manual steps, then Save/Publish on Meetup yourself. Record the result back in the app.";
  status(message, result.errors.length > 0);
  fillStatus(message, result.errors.length > 0);
}
export async function fillSelectedEvent(saved) {
  pending = { origin: saved.origin, payload: validatePayload(saved.payload) };
  show();
  element("results").replaceChildren();
  element("results").hidden = true;
  try { await fill(); }
  catch (error) { fillStatus(error.message, true); throw error; }
}
action("fill", () => fill());
action("photo", () => fill(true));
action("clear", async () => {
  await chrome.storage.session.remove("pending");
  pending = null;
  element("image").removeAttribute("href");
  show();
  status("Prepared content cleared.");
});

try {
  const [{ trustedOrigin: origin }, { pending: saved }] = await Promise.all([
    chrome.storage.local.get("trustedOrigin"), chrome.storage.session.get("pending")
  ]);
  element("origin").value = origin ?? "";
  if (saved) {
    try {
      if (!origin || saved.origin !== origin) throw new Error("Prepared content no longer matches the trusted app origin.");
      pending = { origin, payload: validatePayload(saved.payload) };
    }
    catch (error) { await chrome.storage.session.remove("pending"); status(error.message); }
  }
  show();
} catch (error) { status(`Cannot load extension state: ${error.message}`); }
