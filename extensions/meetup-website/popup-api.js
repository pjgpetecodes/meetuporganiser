import { trustedOrigin, validatePayload } from "./handoff.js";
import { encodeUrl, permissionPattern, requestApi } from "./api.js";
import { savedGrant, clearConnection } from "./connection.js";
import { fillSelectedEvent } from "./popup.js";

const get = id => document.getElementById(id);
let busy = false, page = 1, query = "";
function status(text, error = false) {
  get("status").textContent = text;
  get("status").hidden = !text;
  get("status").dataset.kind = error ? "error" : "info";
}
function clearContent() {
  document.dispatchEvent(new Event("meetup-handoff-cleared"));
  get("api-events").replaceChildren();
  get("api-load").disabled = true;
  get("api-next").disabled = true;
  get("api-prev").disabled = true;
}
function renderConnection(access, pairing = null) {
  const connected = Boolean(access);
  get("connection-summary").hidden = !connected;
  get("connection-setup").hidden = connected || Boolean(pairing);
  get("pairing-state").hidden = !pairing;
  get("event-picker").hidden = !connected;
  get("connection-state").textContent = connected ? "Connected" : pairing ? "Awaiting approval" : "Not connected";
  get("connection-state").className = `badge${connected ? " connected" : pairing ? " pending" : ""}`;
  get("connected-origin").textContent = access?.origin ?? "";
  get("connection-expiry").textContent = access ? `Signed in until ${new Date(access.expiresAt).toLocaleDateString()}` : "";
}
function action(id, work, event = "click") {
  get(id).addEventListener(event, async e => {
    e.preventDefault();
    if (busy) return;
    busy = true;
    get("status").setAttribute("aria-busy", "true");
    try { await work(); } catch (error) { status(error.message, true); }
    finally { busy = false; get("status").removeAttribute("aria-busy"); }
  });
}
async function grant() {
  const access = await savedGrant(chrome.storage);
  const { trustedOrigin: origin } = await chrome.storage.local.get("trustedOrigin");
  if (!access || access.origin !== origin) {
    await clearConnection(chrome.storage);
    clearContent();
    renderConnection(null);
    throw new Error("Your connection expired. Connect and approve access again.");
  }
  return access;
}
async function authenticated(path, options = {}) {
  const access = await grant();
  try {
    return await requestApi(access.origin, path, {
      ...options, headers: { ...options.headers, Authorization: `Bearer ${access.token}` }
    });
  } catch (error) {
    if (error.status === 401) {
      await clearConnection(chrome.storage);
      clearContent();
      renderConnection(null);
    }
    throw error;
  }
}
action("api-connect", async () => {
  const origin = trustedOrigin(get("origin").value);
  // Keep the permission request inside the direct browser user gesture.
  if (!await chrome.permissions.request({ origins: [permissionPattern(origin)] }))
    throw new Error("App-host permission was declined. Capture prepared content is still available.");
  if (await savedGrant(chrome.storage))
    throw new Error("Sign out of the current connection before connecting to another app.");
  const verifier = encodeUrl(crypto.getRandomValues(new Uint8Array(32)));
  const challenge = encodeUrl(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier))));
  const result = await requestApi(origin, "/api/extension/pairing/start", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ challenge })
  });
  if (!/^[A-Za-z0-9_-]{43}$/.test(result.requestId ?? "")) throw new Error("Unsupported pairing response.");
  await chrome.storage.local.set({ trustedOrigin: origin });
  await chrome.storage.session.remove("pending");
  clearContent();
  const pairing = { origin, verifier, requestId: result.requestId, expiresAt: new Date(Date.now() + 5 * 60000).toISOString() };
  await chrome.storage.session.set({ apiPairing: pairing });
  renderConnection(null, pairing);
  await chrome.tabs.create({ url: `${origin}/extension/authorize?request=${encodeURIComponent(result.requestId)}` });
  status("Approve access in the app tab, then reopen this extension. It will connect automatically.");
});
async function finishConnection(pairing) {
  const { trustedOrigin: origin } = await chrome.storage.local.get("trustedOrigin");
  if (!pairing || pairing.origin !== origin) throw new Error("Start a new connection first.");
  if (pairing.expiresAt && Date.parse(pairing.expiresAt) <= Date.now()) {
    await chrome.storage.session.remove("apiPairing");
    renderConnection(null);
    throw new Error("Approval request expired. Connect again.");
  }
  const result = await requestApi(origin, "/api/extension/pairing/redeem", {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ requestId: pairing.requestId, verifier: pairing.verifier })
  });
  if (result.status === "pending") {
    renderConnection(null, pairing);
    status("Still waiting for approval. Approve in the app, then reopen the extension.");
    return;
  }
  if (result.status !== "approved" || !/^[A-Za-z0-9_-]{43}$/.test(result.token ?? "") ||
      !Number.isFinite(Date.parse(result.expiresAt)) || Date.parse(result.expiresAt) <= Date.now()) {
    await chrome.storage.session.remove("apiPairing");
    renderConnection(null);
    throw new Error("Approval expired or was already used. Connect again.");
  }
  const access = { origin, token: result.token, expiresAt: result.expiresAt };
  await chrome.storage.local.set({ apiGrant: access });
  await chrome.storage.session.remove("apiPairing");
  renderConnection(access);
  await search();
}
action("api-finish", async () => {
  const { apiPairing } = await chrome.storage.session.get("apiPairing");
  await finishConnection(apiPairing);
});
action("api-cancel", async () => {
  await chrome.storage.session.remove("apiPairing");
  renderConnection(null);
  status("Connection cancelled.");
});
async function search() {
  get("api-events").replaceChildren();
  get("api-load").disabled = true;
  const data = await authenticated(`/api/extension/events/?search=${encodeURIComponent(query)}&page=${page}`);
  if (!Array.isArray(data.events) || data.events.length > 20) throw new Error("Unsupported event list response.");
  for (const event of data.events) {
    if (!/^[a-f0-9-]{36}$/i.test(event.id ?? "")) throw new Error("Invalid event response.");
    const option = document.createElement("option");
    option.value = event.id;
    const utc = event.eventDateTimeUtc + (event.eventDateTimeUtc.endsWith("Z") ? "" : "Z");
    const date = new Date(utc).toLocaleDateString("en-GB", { timeZone: "Europe/London", day: "numeric", month: "short" });
    option.textContent = `${date} \u00b7 ${event.group} \u2014 ${event.title}`;
    option.title = option.textContent;
    get("api-events").append(option);
  }
  if (data.events.length) get("api-events").selectedIndex = 0;
  get("api-load").disabled = !data.events.length;
  get("api-next").disabled = !data.hasMore;
  get("api-prev").disabled = page <= 1;
  get("api-page").textContent = `Page ${page}`;
  status(data.events.length ? "" : "No matching upcoming events. Try another search.");
}
action("event-search-form", async () => { page = 1; query = get("event-search").value; await search(); }, "submit");
action("api-next", async () => { page++; try { await search(); } catch (error) { page--; throw error; } });
action("api-prev", async () => { page = Math.max(1, page - 1); await search(); });
action("api-load", async () => {
  const id = get("api-events").value;
  if (!id) throw new Error("Search and select an event first.");
  status("Loading event and generating its image...");
  const access = await grant();
  const payload = validatePayload(await authenticated(`/api/extension/events/${encodeURIComponent(id)}`));
  await chrome.storage.session.set({ pending: { origin: access.origin, payload } });
  await fillSelectedEvent({ origin: access.origin, payload });
});
action("api-disconnect", async () => {
  const access = await savedGrant(chrome.storage);
  let warning = "";
  try {
    if (access) await requestApi(access.origin, "/api/extension/events/disconnect", {
      method: "POST", headers: { Authorization: `Bearer ${access.token}` }
    });
  } catch (error) {
    if (error.status !== 401)
      warning = ` Remote revocation could not be confirmed: ${error.message} Revoke from ${access.origin}/extension/connect when it is available.`;
  }
  await clearConnection(chrome.storage);
  clearContent();
  renderConnection(null);
  status(`Signed out of the extension. You can now enter a different app address.${warning}`, Boolean(warning));
});

busy = true;
get("status").setAttribute("aria-busy", "true");
try {
  await chrome.storage.local.setAccessLevel({ accessLevel: "TRUSTED_CONTEXTS" });
  const access = await savedGrant(chrome.storage);
  const { trustedOrigin: origin } = await chrome.storage.local.get("trustedOrigin");
  const { apiPairing } = await chrome.storage.session.get("apiPairing");
  if (access && access.origin === origin) {
    renderConnection(access);
    await search();
  } else if (apiPairing && apiPairing.origin === origin) {
    renderConnection(null, apiPairing);
    await finishConnection(apiPairing);
  } else {
    if (access) { await clearConnection(chrome.storage); clearContent(); }
    renderConnection(null);
  }
} catch (error) { status(error.message, true); }
finally { busy = false; get("status").removeAttribute("aria-busy"); }
