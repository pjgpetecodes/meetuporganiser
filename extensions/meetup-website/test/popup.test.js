import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { capturePreparedContent, meetupTarget, trustedOrigin, validatePayload } from "../handoff.js";
import { fillMeetupForm } from "../form.js";
import { payload } from "./fixture.js";

async function popup(saved = null) {
  const html = await readFile(new URL("../popup.html", import.meta.url), "utf8");
  const source = (await readFile(new URL("../popup.js", import.meta.url), "utf8")).replace(/^import .*;\r?\n/gm, "").replace("export async function", "async function");
  const dom = new JSDOM(html);
  const state = {
    local: { trustedOrigin: "https://organiser.example" },
    session: saved ? { pending: saved } : {},
    url: "https://organiser.example/events/123", injected: []
  };
  const area = key => ({
    get: async () => state[key],
    set: async value => Object.assign(state[key], value),
    remove: async keyToRemove => { delete state[key][keyToRemove]; }
  });
  const chrome = {
    storage: { local: area("local"), session: area("session") },
    tabs: { query: async () => [{ id: 1, url: state.url }] },
    scripting: { executeScript: async request => {
      state.injected.push(request);
      if (request.func === capturePreparedContent)
        return [{ result: { origin: new URL(state.url).origin, payload: payload() } }];
      return [{ result: { filled: ["Title"], manual: ["Review the image"], errors: [] } }];
    } }
  };
  const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
  const fillSelected = await new AsyncFunction("chrome", "document", "capturePreparedContent", "meetupTarget", "trustedOrigin",
    "validatePayload", "fillMeetupForm", source + "\nreturn fillSelectedEvent;")(chrome, dom.window.document, capturePreparedContent,
    meetupTarget, trustedOrigin, validatePayload, fillMeetupForm);
  const get = id => dom.window.document.getElementById(id);
  async function click(id) {
    get(id).click();
    // Drain the popup event handler's async storage/tab/injection calls.
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  return { dom, state, get, click, fillSelected };
}

test("popup captures only trusted app origin and keeps content in session storage", async () => {
  const { dom, state, get, click } = await popup();
  try {
    state.url = "https://untrusted.example/events/123";
    await click("capture");
    assert.equal(state.injected.length, 0);
    assert.match(get("status").textContent, /trusted app origin/);
    state.url = "https://organiser.example/events/123";
    await click("capture");
    assert.equal(state.session.pending.payload.title, "New title");
    assert.equal(state.local.pending, undefined);
    assert.equal(get("handoff").hidden, false);
  } finally { dom.window.close(); }
});
test("API selected event fills immediately and updates PNG without popup reload", async () => {
  const { dom, state, get, fillSelected } = await popup();
  try {
    state.url = "https://www.meetup.com/test-group/schedule/";
    await fillSelected({ origin: "https://organiser.example", payload: payload({ operation: "create", eventUrl: null }) });
    assert.equal(state.injected.length, 1);
    assert.equal(state.injected[0].args[0].title, "New title");
    assert.deepEqual(state.injected[0].args[1], { replace: true, schedule: true, photo: false });
    assert.match(get("image").href, /^data:image\/png;base64,/);
    assert.equal(get("handoff").hidden, false);
    assert.equal([...dom.window.document.querySelectorAll("button")].filter(button =>
      /fill meetup fields/i.test(button.textContent)).length, 1);
    assert.ok(get("fill").closest("details.fallback"));
  } finally { dom.window.close(); }
});
test("popup fills immediately without preview or checkboxes and retains PNG download", async () => {
  const { dom, state, get, click } = await popup({ origin: "https://organiser.example", payload: payload() });
  try {
    state.url = "https://www.meetup.com/test-group/events/123/edit/";
    await click("fill");
    assert.equal(state.injected.length, 1);
    assert.equal(state.injected[0].func, fillMeetupForm);
    assert.match(get("status").textContent, /Awaiting your review/);
    assert.equal(get("confirmed"), null);
    assert.equal(get("description"), null);
    assert.match(get("image").href, /^data:image\/png;base64,/);
    assert.equal(state.injected[0].args[1].schedule, true);
    await click("clear");
    assert.equal(state.session.pending, undefined);
    assert.equal(get("handoff").hidden, true);
    assert.equal(get("image").hasAttribute("href"), false);
  } finally { dom.window.close(); }
});
test("popup fills a new event on the evidenced schedule route and surfaces route errors beside the action", async () => {
  const { dom, state, get, click } = await popup({
    origin: "https://organiser.example", payload: payload({ operation: "create", eventUrl: null })
  });
  try {
    state.url = "https://www.meetup.com/other-group/schedule/";
    await click("fill");
    assert.equal(state.injected.length, 0);
    assert.match(get("fill-status").textContent, /group does not match/);
    assert.equal(get("fill-status").dataset.kind, "error");
    state.url = "https://www.meetup.com/test-group/schedule/#";
    await click("fill");
    assert.equal(state.injected.length, 1);
    assert.match(get("fill-status").textContent, /Awaiting your review/);
    assert.equal(get("fill-status").dataset.kind, "info");
  } finally { dom.window.close(); }
});
test("popup rejects expired state and supports separate explicit photo action", async () => {
  const expired = await popup({ origin: "https://organiser.example", payload: payload({ expiresAtUtc: "2000-01-01" }) });
  try {
    assert.equal(expired.state.session.pending, undefined);
    assert.equal(expired.get("handoff").hidden, true);
  } finally { expired.dom.window.close(); }
  const { dom, state, get, click } = await popup({ origin: "https://organiser.example", payload: payload() });
  try {
    state.url = "https://www.meetup.com/test-group/events/123/edit/";
    await click("photo");
    assert.equal(state.injected[0].args[1].photo, true);
    assert.equal(state.injected[0].args[1].schedule, true);
  } finally { dom.window.close(); }
});
