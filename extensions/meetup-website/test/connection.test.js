import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { savedGrant, clearConnection } from "../connection.js";
import { trustedOrigin, validatePayload } from "../handoff.js";
import { encodeUrl, permissionPattern } from "../api.js";

function storage(data) {
  const area = key => ({
    get: async () => data[key],
    set: async values => Object.assign(data[key], values),
    remove: async names => {
      for (const name of Array.isArray(names) ? names : [names]) delete data[key][name];
    },
    setAccessLevel: async options => { data.accessLevel = options.accessLevel; }
  });
  return { local: area("local"), session: area("session") };
}
const access = () => ({ origin: "https://organiser.example", token: "b".repeat(43), expiresAt: new Date(Date.now() + 30 * 86400000).toISOString() });

test("grant persists through browser session loss, old grants migrate without extending expiry", async () => {
  const original = access();
  const data = { local: {}, session: { apiGrant: original } };
  const store = storage(data);
  assert.deepEqual(await savedGrant(store), original);
  assert.equal(data.session.apiGrant, undefined);
  assert.equal(data.local.apiGrant.expiresAt, original.expiresAt);
  data.session = {};
  assert.deepEqual(await savedGrant(store), original);
  assert.equal(await savedGrant(store, Date.parse(original.expiresAt)), null);
  assert.equal(data.local.apiGrant, undefined);
});
test("sign-out clears credential, pairing and prepared data from both stores", async () => {
  const data = { local: { apiGrant: access(), trustedOrigin: "https://organiser.example" },
    session: { apiGrant: access(), apiPairing: {}, pending: {} } };
  await clearConnection(storage(data));
  assert.equal(data.local.apiGrant, undefined);
  assert.deepEqual(data.session, {});
  assert.equal(data.local.trustedOrigin, "https://organiser.example");
});

async function popup(data, request) {
  const html = await readFile(new URL("../popup.html", import.meta.url), "utf8");
  const source = (await readFile(new URL("../popup-api.js", import.meta.url), "utf8")).replace(/^import .*;\r?\n/gm, "");
  const dom = new JSDOM(html);
  const chrome = {
    storage: storage(data),
    permissions: { request: async () => true },
    tabs: { create: async tab => { data.opened = tab.url; } }
  };
  const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
  await new AsyncFunction("chrome", "document", "location", "trustedOrigin", "validatePayload", "encodeUrl", "permissionPattern",
    "requestApi", "savedGrant", "clearConnection", "Event", source)(
    chrome, dom.window.document, { reload: () => {} }, trustedOrigin, validatePayload, encodeUrl, permissionPattern,
    request, savedGrant, clearConnection, dom.window.Event);
  return {
    dom, get: id => dom.window.document.getElementById(id),
    click: async id => {
      dom.window.document.getElementById(id).click();
      await new Promise(resolve => setTimeout(resolve, 50));
    }
  };
}
test("reopening popup automatically completes browser approval, no finish click needed", async () => {
  const data = {
    local: { trustedOrigin: "https://organiser.example" },
    session: { apiPairing: { origin: "https://organiser.example", requestId: "a".repeat(43), verifier: "c".repeat(43) } }
  };
  const calls = [];
  const ui = await popup(data, async (origin, path) => {
    calls.push(path);
    return path.endsWith("/redeem") ? { status: "approved", token: access().token, expiresAt: access().expiresAt }
      : { events: [], hasMore: false };
  });
  try {
    assert.ok(data.local.apiGrant);
    assert.equal(data.session.apiPairing, undefined);
    assert.equal(ui.get("connection-summary").hidden, false);
    assert.equal(ui.get("connection-setup").hidden, true);
    assert.equal(ui.get("event-picker").hidden, false);
    assert.equal(ui.get("api-finish").closest("#pairing-state").hidden, true);
    assert.ok(calls.includes("/api/extension/pairing/redeem"));
    assert.equal(data.accessLevel, "TRUSTED_CONTEXTS");
  } finally { ui.dom.window.close(); }
});
test("pending approval has one clear retry action, not a second mandatory step", async () => {
  const data = {
    local: { trustedOrigin: "https://organiser.example" },
    session: { apiPairing: { origin: "https://organiser.example", requestId: "a".repeat(43), verifier: "c".repeat(43) } }
  };
  const ui = await popup(data, async () => ({ status: "pending" }));
  try {
    assert.equal(ui.get("pairing-state").hidden, false);
    assert.equal(ui.get("connection-state").textContent, "Awaiting approval");
    assert.equal(ui.get("event-picker").hidden, true);
  } finally { ui.dom.window.close(); }
});
test("sign-out clears old environment even offline, then allows a new live connection", async () => {
  const old = access();
  const data = { local: { apiGrant: old, trustedOrigin: old.origin }, session: { pending: {} } };
  const calls = [];
  const ui = await popup(data, async (origin, path) => {
    calls.push({ origin, path });
    if (path.endsWith("/disconnect")) throw new Error("App is offline");
    if (path.endsWith("/start")) return { requestId: "a".repeat(43) };
    return { events: [], hasMore: false };
  });
  try {
    await ui.click("api-disconnect");
    assert.equal(data.local.apiGrant, undefined);
    assert.equal(data.session.pending, undefined);
    assert.equal(ui.get("connection-setup").hidden, false);
    assert.match(ui.get("status").textContent, /Remote revocation could not be confirmed/);
    ui.get("origin").value = "https://live.example";
    await ui.click("api-connect");
    assert.equal(data.local.trustedOrigin, "https://live.example");
    assert.match(data.opened, /^https:\/\/live.example\/extension\/authorize/);
    assert.equal(calls.find(call => call.path.endsWith("/disconnect")).origin, old.origin);
    assert.equal(calls.find(call => call.path.endsWith("/start")).origin, "https://live.example");
  } finally { ui.dom.window.close(); }
});
test("revoked grants reset the connection and do not show stale event choices", async () => {
  const old = access();
  const data = { local: { apiGrant: old, trustedOrigin: old.origin }, session: { pending: {} } };
  const ui = await popup(data, async () => { const error = new Error("Access revoked"); error.status = 401; throw error; });
  try {
    assert.equal(data.local.apiGrant, undefined);
    assert.equal(data.session.pending, undefined);
    assert.equal(ui.get("event-picker").hidden, true);
    assert.equal(ui.get("connection-setup").hidden, false);
    assert.equal(ui.get("status").dataset.kind, "error");
  } finally { ui.dom.window.close(); }
});
