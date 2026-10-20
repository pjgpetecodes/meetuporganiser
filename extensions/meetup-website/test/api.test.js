import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { trustedOrigin, validatePayload } from "../handoff.js";
import { encodeUrl, permissionPattern, requestApi } from "../api.js";
import { payload } from "./fixture.js";
import { savedGrant, clearConnection } from "../connection.js";

test("API fetch sends no cookies, rejects redirects and reports authorization failures", async () => {
  const original = globalThis.fetch;
  try {
    globalThis.fetch = async (url, options) => {
      assert.equal(url, "https://organiser.example/api/extension/events/");
      assert.equal(options.credentials, "omit");
      assert.equal(options.redirect, "error");
      assert.equal(options.cache, "no-store");
      assert.equal(options.referrerPolicy, "no-referrer");
      assert.equal(options.headers.Authorization, "Bearer session-token");
      return new Response("{}", { status: 401 });
    };
    await assert.rejects(requestApi("https://organiser.example", "/api/extension/events/", {
      headers: { Authorization: "Bearer session-token" }
    }), /expired or was revoked/);
    await assert.rejects(requestApi("https://organiser.example", "/some-other-endpoint"), /Unsupported/);
  } finally { globalThis.fetch = original; }
  assert.equal(permissionPattern("https://organiser.example:5000"), "https://organiser.example/*");
});

test("popup pairs with proof, searches, loads saved content and disconnects", async () => {
  const html = await readFile(new URL("../popup.html", import.meta.url), "utf8");
  const source = (await readFile(new URL("../popup-api.js", import.meta.url), "utf8")).replace(/^import .*;\r?\n/gm, "");
  const dom = new JSDOM(html);
  const state = { local: {}, session: {}, urls: [], permissions: [], requests: [], reloaded: false };
  const area = key => ({
    get: async () => state[key],
    set: async value => Object.assign(state[key], value),
    remove: async keys => { for (const item of Array.isArray(keys) ? keys : [keys]) delete state[key][item]; },
    setAccessLevel: async () => {}
  });
  const chrome = {
    storage: { local: area("local"), session: area("session") },
    permissions: { request: async value => { state.permissions.push(value); return true; } },
    tabs: { create: async value => state.urls.push(value.url) }
  };
  const request = async (origin, path, options) => {
    assert.equal(origin, "https://organiser.example");
    state.requests.push({ path, options });
    if (path.endsWith("/start")) {
      const { challenge } = JSON.parse(options.body);
      assert.match(challenge, /^[A-Za-z0-9_-]{43}$/);
      return { requestId: "a".repeat(43) };
    }
    if (path.endsWith("/redeem")) {
      const { verifier } = JSON.parse(options.body);
      assert.match(verifier, /^[A-Za-z0-9_-]{43}$/);
      const expected = encodeUrl(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier))));
      const started = state.requests.find(request => request.path.endsWith("/start"));
      assert.equal(JSON.parse(started.options.body).challenge, expected);
      return { status: "approved", token: "b".repeat(43), expiresAt: new Date(Date.now() + 30 * 86400000).toISOString() };
    }
    assert.equal(options.headers.Authorization, `Bearer ${"b".repeat(43)}`);
    if (path.includes("?search=")) return { events: [{ id: payload().sourceEventId, title: "Saved event", group: "Community", eventDateTimeUtc: "2027-01-25T18:30:00" }], hasMore: false };
    if (path.endsWith("/disconnect")) return null;
    return payload();
  };
  const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
  await new AsyncFunction("chrome", "document", "location", "trustedOrigin", "validatePayload", "encodeUrl", "permissionPattern", "requestApi", "savedGrant", "clearConnection", "Event", "fillSelectedEvent", source)(
    chrome, dom.window.document, { reload: () => { state.reloaded = true; } },
    trustedOrigin, validatePayload, encodeUrl, permissionPattern, request, savedGrant, clearConnection, dom.window.Event,
    async saved => { state.filled = saved; });
  const get = id => dom.window.document.getElementById(id);
  async function click(id) {
    get(id).click();
    for (let i = 0; i < 200 && get("status").getAttribute("aria-busy") === "true"; i++)
      await new Promise(resolve => setTimeout(resolve, 10));
  }
  try {
    get("origin").value = "https://organiser.example";
    await click("api-connect");
    assert.deepEqual(state.permissions, [{ origins: ["https://organiser.example/*"] }]);
    assert.match(state.urls[0] ?? get("status").textContent, /^https:\/\/organiser.example\/extension\/authorize\?request=/);
    assert.equal(state.local.apiGrant, undefined);
    await click("api-finish");
    assert.ok(state.local.apiGrant);
    assert.equal(state.session.apiGrant, undefined);
    assert.equal(state.session.apiPairing, undefined);
    await click("event-search-button");
    assert.equal(get("api-events").options[0].textContent.includes("Saved event"), true);
    await click("api-load");
    assert.equal(state.session.pending.payload.title, "New title");
    assert.equal(state.reloaded, false);
    assert.equal(state.filled.payload.title, "New title");
    await click("api-disconnect");
    assert.equal(state.local.apiGrant, undefined);
    assert.equal(state.session.apiGrant, undefined);
    assert.equal(state.session.pending, undefined);
    assert.ok(state.requests.some(request => request.path.endsWith("/disconnect")));
  } finally { dom.window.close(); }
});
