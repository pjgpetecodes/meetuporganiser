import test from "node:test";
import assert from "node:assert/strict";
import { once } from "node:events";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { WebSocket } from "ws";
import { chromium } from "playwright";
import { startPrototype } from "../server.js";

test("managed startup reports safe validation errors without echoing the payload", async () => {
  const child = spawn(process.execPath, [fileURLToPath(new URL("../server.js", import.meta.url)), "--managed"],
    { stdio: ["pipe", "pipe", "pipe"] });
  let output = "", diagnostics = "";
  child.stdout.on("data", value => { output += value; });
  child.stderr.on("data", value => { diagnostics += value; });
  child.stdin.end(JSON.stringify({ payload: { title: "sensitive-test-content" }, live: true }) + "\n");
  const [code] = await once(child, "close");
  assert.equal(code, 1);
  assert.deepEqual(JSON.parse(output), { error: "invalid_payload" });
  assert.equal(diagnostics.includes("sensitive-test-content"), false);
});

async function authorize(prototype) {
  const token = new URL(prototype.url).hash.slice(1);
  const response = await fetch(`${prototype.origin}/authorize`, {
    method: "POST", headers: { Origin: prototype.origin, "Content-Type": "application/json" },
    body: JSON.stringify({ token })
  });
  assert.equal(response.status, 204);
  return response.headers.get("set-cookie").split(";")[0];
}
function stream(prototype, cookie, origin = prototype.origin) {
  return new WebSocket(prototype.origin.replace("http:", "ws:") + "/stream", {
    headers: { Cookie: cookie, Origin: origin }
  });
}
async function rejected(prototype, cookie, origin, code) {
  const socket = stream(prototype, cookie, origin);
  await new Promise((resolve, reject) => {
    socket.on("unexpected-response", (_request, response) => {
      try { assert.equal(response.statusCode, code); response.resume(); socket.terminate(); resolve(); }
      catch (error) { reject(error); }
    });
    socket.on("error", () => {});
    socket.on("open", () => { socket.terminate(); reject(new Error("Unauthorised stream opened.")); });
  });
}
function message(socket, predicate) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { cleanup(); reject(new Error("Expected browser message did not arrive.")); }, 10_000);
    const receive = (data, binary) => {
      const value = binary ? data : JSON.parse(data.toString());
      if (predicate(value, binary)) { cleanup(); resolve(value); }
    };
    const cleanup = () => { clearTimeout(timeout); socket.off("message", receive); };
    socket.on("message", receive);
  });
}
async function retained(page, selector, expected) {
  await page.waitForFunction(({ selector, expected }) => document.querySelector(selector)?.value === expected,
    { selector, expected });
}

test("prepared content targets its group, restores only opted-in cookies and supplies separate photos without saving", async () => {
  const png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aE1cAAAAASUVORK5CYII=";
  const payload = {
    version: 1, sourceEventId: "12345678-1234-1234-1234-123456789012",
    groupSlug: "prepared-group", operation: "create", title: "Prepared in app", description: "Edited description",
    expiresAtUtc: new Date(Date.now() + 30 * 60_000).toISOString(),
    startUtc: "2027-01-25T18:30:00Z", startDate: "2027-01-25", startTime: "18:30",
    endDate: "2027-01-25", endTime: "20:00", durationMinutes: 90, timeZone: "Europe/London",
    timeZoneLabel: "GMT", format: "Online", imageBase64: png, speakerCount: 1,
    speaker: { name: "Synthetic speaker", bio: "Public bio", photoBase64: png }
  };
  const prototype = await startPrototype({ headless: true, payload, storageState: {
    cookies: [{ name: "synthetic", value: "restored", domain: ".meetup.com", path: "/",
      expires: -1, httpOnly: true, secure: true, sameSite: "Lax" }], origins: []
  } });
  try {
    assert.equal(prototype.page.url(), "https://www.meetup.com/prepared-group/schedule/");
    const cookie = await authorize(prototype);
    const socket = stream(prototype, cookie);
    await once(socket, "open");
    let report = message(socket, data => data.type === "report");
    socket.send(JSON.stringify({ type: "fill" }));
    assert.equal((await report).report.errors.length, 0);
    assert.equal(await prototype.page.locator("#title").inputValue(), payload.title);
    assert.equal(await prototype.page.locator("#outcome").textContent(), "Not saved");
    await prototype.page.evaluate(() => {
      const input = document.createElement("input");
      input.id = "speaker.name";
      document.querySelector("form").append(input);
      const speaker = document.createElement("button");
      speaker.type = "button";
      speaker.textContent = "Upload Photo";
      document.querySelector("form").append(speaker);
      const aside = document.createElement("aside");
      aside.innerHTML = '<button type="button">Select</button>';
      document.querySelector("main").append(aside);
      for (const button of [speaker, aside.querySelector("button")]) button.onclick = () => {
        const dialog = document.createElement("div");
        dialog.role = "dialog";
        dialog.dataset.slot = "modal-content";
        dialog.innerHTML = '<div id="photo-uploader-drop-area"><button>Choose photo</button><input type="file" accept="image/*" style="display:none"></div>';
        document.body.append(dialog);
      };
      localStorage.setItem("unsaved-form", "must-not-be-remembered");
    });
    for (const [action, name] of [["photo", "meetup-social.png"], ["speakerPhoto", "speaker-photo.png"]]) {
      report = message(socket, data => data.type === "report");
      socket.send(JSON.stringify({ type: action }));
      assert.deepEqual((await report).report.errors, []);
      assert.equal(await prototype.page.locator('input[type="file"]').evaluate(input => input.files[0].name), name);
      await prototype.page.locator('[role="dialog"]').evaluate(dialog => dialog.remove());
    }
    assert.equal(await prototype.page.locator("#outcome").textContent(), "Not saved");
    await prototype.page.evaluate(() => {
      const duplicate = document.createElement("button");
      duplicate.type = "button";
      duplicate.textContent = "Upload Photo";
      duplicate.onclick = () => { throw new Error("Ambiguous upload must not be clicked."); };
      document.querySelector("form").append(duplicate);
    });
    const refused = message(socket, data => data.type === "error");
    socket.send(JSON.stringify({ type: "speakerPhoto" }));
    assert.match((await refused).message, /photo/i);
    assert.equal(await prototype.page.locator('[role="dialog"]').count(), 0);
    await prototype.context.addCookies([{ name: "third-party", value: "omit", domain: "example.invalid", path: "/" }]);
    const state = await fetch(`${prototype.origin}/state`, { method: "POST", headers: { Origin: prototype.origin, Cookie: cookie } });
    assert.equal(state.status, 200);
    const saved = await state.json();
    assert.equal(saved.cookies.length, 1);
    assert.equal(saved.cookies[0].value, "restored");
    assert.deepEqual(saved.origins, []);
    const reopened = await fetch(`${prototype.origin}/target`, { method: "POST", headers: { Origin: prototype.origin, Cookie: cookie } });
    assert.equal(reopened.status, 204);
    assert.equal(await prototype.page.locator("#title").inputValue(), "Review me");
    socket.close();
  } finally { await prototype.close(); }
});

test("loopback remote browser authenticates, streams, fills, supports manual input and reconnects without saving", async () => {
  const prototype = await startPrototype({ headless: true });
  try {
    assert.equal(new URL(prototype.origin).hostname, "127.0.0.1");
    await rejected(prototype, "", prototype.origin, 401);
    const malicious = await fetch(`${prototype.origin}/authorize`, {
      method: "POST", headers: { Origin: "https://untrusted.invalid", "Content-Type": "application/json" },
      body: JSON.stringify({ token: new URL(prototype.url).hash.slice(1) })
    });
    assert.equal(malicious.status, 403);
    const cookie = await authorize(prototype);
    const replay = await fetch(`${prototype.origin}/authorize`, {
      method: "POST", headers: { Origin: prototype.origin, "Content-Type": "application/json" },
      body: JSON.stringify({ token: new URL(prototype.url).hash.slice(1) })
    });
    assert.equal(replay.status, 401);
    await rejected(prototype, cookie, "https://untrusted.invalid", 403);
    const socket = stream(prototype, cookie);
    const firstFrame = message(socket, (_data, binary) => binary);
    await once(socket, "open");
    const frame = await firstFrame;
    assert.equal(frame[0], 0xff);
    assert.equal(frame[1], 0xd8);
    await rejected(prototype, cookie, prototype.origin, 409);
    const filled = message(socket, data => data.type === "report");
    socket.send(JSON.stringify({ type: "fill" }));
    const report = (await filled).report;
    assert.equal(report.errors.length, 0);
    assert.ok(report.filled.includes("Title"));
    assert.equal(await prototype.page.locator("#title").inputValue(), "Offline prepared event");
    assert.equal(await prototype.page.locator("#outcome").textContent(), "Not saved");
    const title = await prototype.page.locator("#title").boundingBox();
    socket.send(JSON.stringify({ type: "click", x: title.x + title.width / 2, y: title.y + title.height / 2 }));
    socket.send(JSON.stringify({ type: "key", key: "End" }));
    socket.send(JSON.stringify({ type: "text", text: " reviewed" }));
    await retained(prototype.page, "#title", "Offline prepared event reviewed");
    socket.close();
    await once(socket, "close");
    const reconnected = stream(prototype, cookie);
    await once(reconnected, "open");
    assert.equal(await prototype.page.locator("#title").inputValue(), "Offline prepared event reviewed");
    assert.equal(await prototype.page.locator("#outcome").textContent(), "Not saved");
    reconnected.send(JSON.stringify({ type: "wheel", deltaX: 0, deltaY: 1000 }));
    await prototype.page.waitForFunction(() => window.scrollY > 0);
    const save = await prototype.page.locator("#save").boundingBox();
    reconnected.send(JSON.stringify({ type: "click", x: save.x + save.width / 2, y: save.y + save.height / 2 }));
    await prototype.page.waitForFunction(() => document.getElementById("outcome").textContent.startsWith("Simulated manual save"));
    reconnected.send(JSON.stringify({ type: "navigate", url: "https://www.meetup.com/" }));
    const [code] = await once(reconnected, "close");
    assert.equal(code, 1008);
    const deniedEnd = await fetch(`${prototype.origin}/end`, {
      method: "POST", headers: { Origin: "https://untrusted.invalid", Cookie: cookie }
    });
    assert.equal(deniedEnd.status, 403);
    const body = await fetch(`${prototype.origin}/authorize`, {
      method: "POST", headers: { Origin: prototype.origin }, body: "{"
    });
    assert.equal(body.status, 400);
    const ended = await fetch(`${prototype.origin}/end`, {
      method: "POST", headers: { Origin: prototype.origin, Cookie: cookie }
    });
    assert.equal(ended.status, 204);
  } finally { await prototype.close(); }
});

test("offline browser blocks external page fetches and does not share authentication between launches", async () => {
  const prototype = await startPrototype({ headless: true });
  try {
    const allowed = await prototype.page.evaluate(async () => {
      try { await fetch("https://example.com/must-not-contact"); return true; }
      catch { return false; }
    });

    assert.equal(allowed, false);
    const cookie = await authorize(prototype);
    const other = await startPrototype({ headless: true });
    try { await rejected(other, cookie, other.origin, 401); }
    finally { await other.close(); }
  } finally { await prototype.close(); }
});

test("invalid text and unsupported key combinations close the controller instead of injecting arbitrary commands", async () => {
  const prototype = await startPrototype({ headless: true });
  try {
    const cookie = await authorize(prototype);
    for (const input of [
      { type: "text", text: "x".repeat(4001) },
      { type: "key", key: "Control+L" },
      { type: "click", x: -1, y: 10 }
    ]) {
      const socket = stream(prototype, cookie);
      await once(socket, "open");
      socket.send(JSON.stringify(input));
      const [code] = await once(socket, "close");
      assert.equal(code, 1008);
    }
    assert.equal(prototype.page.url(), "https://www.meetup.com/offline-fixture/events/123/edit/");
    assert.equal(await prototype.page.locator("#title").inputValue(), "Review me");
  } finally { await prototype.close(); }
});

for (const reason of ["idle", "lifetime"]) {
  test(`expired ${reason} session cannot reconnect or reuse launch credentials`, async () => {
    let clock = Date.now();
    const prototype = await startPrototype({ headless: true, now: () => clock,
      idleMs: reason === "idle" ? 100 : 100_000, lifetimeMs: reason === "lifetime" ? 100 : 100_000 });
    try {
      const cookie = await authorize(prototype);
      clock += 101;
      await rejected(prototype, cookie, prototype.origin, 401);
      const response = await fetch(`${prototype.origin}/`, { headers: { Cookie: cookie } });
      assert.equal(response.status, 410);
    } finally { await prototype.close(); }
  });
}

test("responsive viewer removes launch token, draws live frames and forwards mobile text and manual clicks", async () => {
  const prototype = await startPrototype({ headless: true });
  const channel = process.env.BROWSER_CHANNEL ?? "chrome";
  const viewerBrowser = await chromium.launch({
    ...(channel === "chromium" ? {} : { channel }), headless: true,
    chromiumSandbox: process.platform === "linux"
  });
  const viewerContext = await viewerBrowser.newContext({
    viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true
  });
  try {
    const viewer = await viewerContext.newPage();
    await viewer.goto(prototype.url);
    await viewer.waitForFunction(() => document.getElementById("status").textContent.startsWith("Connected."));
    assert.equal(new URL(viewer.url()).hash, "");
    assert.ok((await viewerContext.cookies()).some(cookie => cookie.httpOnly && cookie.sameSite === "Strict"));
    await viewer.waitForFunction(() => document.querySelector("canvas").getContext("2d").getImageData(10, 10, 1, 1).data[3] > 0);
    assert.equal(await viewer.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await viewer.locator("#fill").tap();
    await viewer.waitForFunction(() => document.getElementById("report").textContent.includes("Title"));
    const title = await prototype.page.locator("#title").boundingBox();
    await viewer.locator("canvas").scrollIntoViewIfNeeded();
    const bounds = await viewer.locator("canvas").boundingBox();
    await viewer.touchscreen.tap(bounds.x + (title.x + 20) * bounds.width / 1000,
      bounds.y + (title.y + 15) * bounds.height / 800);
    await prototype.page.waitForFunction(() => document.activeElement?.id === "title");
    await viewer.locator("#text").fill(" mobile");
    await viewer.locator("#send").tap();
    await prototype.page.waitForFunction(() => document.getElementById("title").value.includes("mobile"));
    assert.equal(await prototype.page.locator("#outcome").textContent(), "Not saved");
    await viewer.locator("#end").tap();
  } finally {
    await viewerBrowser.close();
    await prototype.close();
  }
});
