import { createServer } from "node:http";
import { randomBytes, timingSafeEqual } from "node:crypto";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { WebSocket, WebSocketServer } from "ws";
import { fillMeetupForm } from "../../extensions/meetup-website/form.js";
import { meetupTarget } from "../../extensions/meetup-website/handoff.js";

const fixtureUrl = "https://www.meetup.com/offline-fixture/events/123/edit/";
const keys = new Set(["Backspace", "Delete", "Enter", "Tab", "Shift+Tab", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"]);
const equal = (left, right) => {
  const a = Buffer.from(left ?? "");
  const b = Buffer.from(right ?? "");
  return a.length === b.length && timingSafeEqual(a, b);
};

export async function startPrototype({ port = 0, channel = process.env.BROWSER_CHANNEL ?? "chrome", headless = false,
  now = Date.now, lifetimeMs = 15 * 60_000, idleMs = 5 * 60_000,
  payload = null, live = false, storageState = null } = {}) {
  if (!["chrome", "msedge", "chromium"].includes(channel)) throw new Error("Unsupported local browser channel.");
  if (live && !payload) throw new Error("Live mode requires an event prepared by the organiser app.");
  if (payload) {
    const { validatePayload } = await import("../../extensions/meetup-website/handoff.js");
    try { payload = validatePayload(payload); }
    catch { throw Object.assign(new Error("Prepared content is invalid or expired."), { startupCode: "invalid_payload" }); }
    lifetimeMs = Math.min(lifetimeMs, Date.parse(payload.expiresAtUtc) - Date.now());
  }
  const targetUrl = payload ? payload.operation === "update"
    ? `${payload.eventUrl.replace(/\/$/, "")}/edit/`
    : `https://www.meetup.com/${payload.groupSlug}/schedule/` : fixtureUrl;
  const assets = new Map(await Promise.all([
    ["/", "public/index.html", "text/html; charset=utf-8"],
    ["/viewer.js", "public/viewer.js", "text/javascript; charset=utf-8"],
    ["/viewer.css", "public/viewer.css", "text/css; charset=utf-8"]
  ].map(async ([route, path, type]) => [route, { body: await readFile(new URL(path, import.meta.url)), type }])));
  const fixture = await readFile(new URL("fixture.html", import.meta.url), "utf8");
  const browser = await chromium.launch({
    ...(channel === "chromium" ? {} : { channel }), headless,
    chromiumSandbox: process.platform === "linux"
  }).catch(() => { throw Object.assign(new Error("Could not launch the configured browser."), { startupCode: "browser_launch" }); });
  let server, sockets, timer, controller, sessionCookie, origin;
  let closing;
  let ended = false, filling = false, queued = 0;
  let queue = Promise.resolve();
  let launchToken = randomBytes(32).toString("hex");
  let lastActivity = now();
  const started = now();
  const expired = () => ended || now() - started >= lifetimeMs || now() - lastActivity >= idleMs;
  const authorised = request => {
    const cookie = request.headers.cookie?.split(";").map(value => value.trim()).find(value => value.startsWith("offline_session="))?.slice(16);
    return Boolean(sessionCookie && equal(cookie, sessionCookie) && !expired());
  };
  const send = message => {
    if (controller?.readyState === WebSocket.OPEN) controller.send(JSON.stringify(message));
  };
  function close() {
    if (closing) return closing;
    ended = true;
    launchToken = null;
    sessionCookie = null;
    clearInterval(timer);
    for (const socket of sockets?.clients ?? []) socket.terminate();
    sockets?.close();
    if (server?.listening) {
      server.close();
      server.closeAllConnections();
    }
    closing = browser.close();
    return closing;
  }
  function browserFailure() {
    if (ended) return;
    send({ type: "error", message: "The browser failed; this session will close and unsaved state will be discarded." });
    close().catch(() => { console.error("Failed to close the browser."); });
  }
  try {
    const context = await browser.newContext({
      viewport: { width: 1000, height: 800 }, acceptDownloads: false,
      serviceWorkers: "block", permissions: [], ...(storageState ? { storageState } : {})
    });
    if (!live) {
      await context.route("**/*", async route => {
        if (route.request().url() === targetUrl && route.request().isNavigationRequest())
          await route.fulfill({ status: 200, contentType: "text/html", body: fixture });
        else await route.abort("blockedbyclient");
      });
      await context.routeWebSocket(/.*/, socket => socket.close());
    } else {
      // The local test mode deliberately allows only Meetup documents and HTTPS resources.
      // A cloud worker additionally requires an infrastructure-level egress boundary.
      await context.route("**/*", async route => {
        const request = route.request();
        const url = new URL(request.url());
        const meetupHost = url.hostname === "meetup.com" || url.hostname.endsWith(".meetup.com");
        if (url.protocol !== "https:" || url.username || url.password ||
            (request.isNavigationRequest() && request.frame().parentFrame() === null && !meetupHost))
          await route.abort("blockedbyclient");
        else await route.continue();
      });
    }
    const page = await context.newPage();
    context.on("page", popup => { if (popup !== page) popup.close().catch(browserFailure); });
    page.on("close", browserFailure);
    page.on("crash", browserFailure);
    browser.on("disconnected", browserFailure);
    await page.goto(targetUrl, { waitUntil: "domcontentloaded", timeout: 30_000 })
      .catch(() => { throw Object.assign(new Error("Could not load the prepared Meetup page."), { startupCode: "navigation" }); });
    const cdp = await context.newCDPSession(page);
    const navigation = () => {
      const url = new URL(page.url());
      send({ type: "navigation", url: `${url.origin}${url.pathname}`, live });
    };
    page.on("framenavigated", frame => { if (frame === page.mainFrame()) navigation(); });

    async function readJson(request) {
      const chunks = [];
      let size = 0;
      for await (const chunk of request) {
        size += chunk.length;
        if (size > 1024) throw new Error("Request too large.");
        chunks.push(chunk);
      }
      return JSON.parse(Buffer.concat(chunks).toString("utf8"));
    }
    server = createServer((request, response) => {
      const run = async () => {
        const reply = (status, body = "", headers = {}) => {
          response.writeHead(status, { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff",
            "Referrer-Policy": "no-referrer",
            "Content-Security-Policy": "default-src 'self'; connect-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            ...headers });
          response.end(body);
        };
        if (request.headers.host !== new URL(origin).host) return reply(403, "Invalid host.");
        if (expired()) { reply(410, "Offline session ended or expired."); await close(); return; }
        if (request.method === "GET" && assets.has(request.url)) {
          const asset = assets.get(request.url);
          return reply(200, asset.body, { "Content-Type": asset.type });
        }
        if (request.method !== "POST") return reply(404);
        if (request.headers.origin !== origin) return reply(403, "Invalid origin.");
        if (request.url === "/authorize") {
          let data;
          try { data = await readJson(request); } catch { return reply(400, "Invalid request."); }
          if (!launchToken || typeof data.token !== "string" || !equal(data.token, launchToken))
            return reply(401, "Invalid or already-used launch link.");
          launchToken = null;
          sessionCookie = randomBytes(32).toString("hex");
          lastActivity = now();
          return reply(204, "", { "Set-Cookie": `offline_session=${sessionCookie}; HttpOnly; SameSite=Strict; Path=/` });
        }
        if (!authorised(request)) return reply(401, "Session authorisation required.");
        if (request.url === "/state") {
          if (filling || queued) return reply(409, "Wait for browser interaction to finish.");
          const state = await context.storageState();
          state.cookies = state.cookies.filter(cookie =>
            cookie.domain.replace(/^\./, "") === "meetup.com" || cookie.domain.endsWith(".meetup.com"));
          state.origins = [];
          return reply(200, JSON.stringify(state), { "Content-Type": "application/json" });
        }
        if (request.url === "/target") {
          if (filling || queued) return reply(409, "Wait for browser interaction to finish.");
          filling = true;
          send({ type: "busy", busy: true });
          try {
            await page.goto(targetUrl, { waitUntil: "domcontentloaded", timeout: 30_000 });
            lastActivity = now();
            return reply(204);
          } finally {
            filling = false;
            send({ type: "busy", busy: false });
          }
        }
        if (request.url === "/end") {
          reply(204, "", { "Set-Cookie": "offline_session=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0" });
          await close();
          return;
        }
        reply(404);
      };
      run().catch(() => {
        if (!response.headersSent) response.writeHead(500, { "Cache-Control": "no-store" });
        response.end("Offline prototype request failed.");
        browserFailure();
      });
    });
    sockets = new WebSocketServer({ noServer: true, maxPayload: 16 * 1024, perMessageDeflate: false });
    server.on("upgrade", (request, socket, head) => {
      let status;
      if (request.headers.host !== new URL(origin).host || request.headers.origin !== origin || request.url !== "/stream") status = 403;
      else if (!authorised(request)) status = 401;
      else if (controller && controller.readyState !== WebSocket.CLOSED) status = 409;
      if (status) {
        socket.end(`HTTP/1.1 ${status} Rejected\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);
        return;
      }
      sockets.handleUpgrade(request, socket, head, connection => {
        controller = connection;
        lastActivity = now();
        navigation();
        cdp.send("Page.captureScreenshot", { format: "jpeg", quality: 70 }).then(frame => {
          if (controller === connection && connection.readyState === WebSocket.OPEN)
            connection.send(Buffer.from(frame.data, "base64"));
        }).catch(browserFailure);
        let windowStart = now(), count = 0;
        connection.on("error", () => connection.terminate());
        connection.on("close", () => {
          if (controller === connection) {
            controller = null;
            queue = queue.then(() => page.mouse.up()).catch(browserFailure);
          }
        });
        connection.on("message", (raw, binary) => {
          if (expired()) { close().catch(browserFailure); return; }
          if (now() - windowStart >= 1000) { windowStart = now(); count = 0; }
          if (binary || ++count > 100 || queued >= 32) { connection.close(1008, "Input limit exceeded."); return; }
          let message;
          try { message = JSON.parse(raw.toString()); } catch { connection.close(1008, "Invalid input."); return; }
          if (!message || typeof message !== "object") { connection.close(1008, "Invalid input."); return; }
          if (filling) { send({ type: "error", message: "Wait for filling to finish before interacting." }); return; }
          lastActivity = now();
          queued++;
          queue = queue.then(async () => {
            if (controller !== connection || expired()) return;
            if (["fill", "photo", "speakerPhoto"].includes(message.type)) {
              filling = true;
              send({ type: "busy", busy: true });
              try {
                if (message.type !== "fill" && payload) {
                  meetupTarget(page.url(), payload, true);
                  const dialogs = page.locator('[role="dialog"][data-slot="modal-content"]:visible')
                    .filter({ has: page.getByRole("button", { name: "Choose photo", exact: true }) });
                  if (await dialogs.count() === 0) {
                    const form = page.locator("main#event-schedule-main form");
                    const trigger = message.type === "speakerPhoto"
                      ? form.getByRole("button", { name: "Upload Photo", exact: true }).filter({ visible: true })
                      : page.locator("main#event-schedule-main aside").getByRole("button", { name: "Select", exact: true }).filter({ visible: true });
                    if (await trigger.count() !== 1)
                      throw new Error("A unique photo upload button was not found.");
                    if (message.type === "speakerPhoto" && !payload.speaker?.photoBase64)
                      throw new Error("No speaker photo is prepared.");
                    await trigger.click({ timeout: 5000 });
                    await dialogs.waitFor({ state: "visible", timeout: 5000 });
                  }
                }
                const report = await page.evaluate(async ({ source, payload, action }) => {
                  const fill = (0, eval)(`(${source})`);
                  return fill(payload, { replace: true, schedule: true, photo: action === "photo", speakerPhoto: action === "speakerPhoto" });
                }, { source: fillMeetupForm.toString(), action: message.type, payload: payload ?? {
                  groupSlug: "offline-fixture", operation: "update", eventUrl: fixtureUrl,
                  title: "Offline prepared event", description: "Prepared description\n\nReview before the simulated save.",
                  startDate: "2027-01-25", startTime: "18:30", endDate: "2027-01-25", endTime: "20:00",
                  timeZoneLabel: "GMT", durationMinutes: 90, format: "Online", location: "https://example.invalid/meeting"
                } });
                send({ type: "report", report });
              } finally {
                filling = false;
                send({ type: "busy", busy: false });
              }
            } else if (message.type === "click" && Number.isFinite(message.x) && Number.isFinite(message.y) &&
                message.x >= 0 && message.x < 1000 && message.y >= 0 && message.y < 800) {
              await page.mouse.click(message.x, message.y);
            } else if (["pointerDown", "pointerMove", "pointerUp"].includes(message.type) &&
                Number.isFinite(message.x) && Number.isFinite(message.y) &&
                message.x >= 0 && message.x < 1000 && message.y >= 0 && message.y < 800) {
              await page.mouse.move(message.x, message.y);
              if (message.type === "pointerDown") await page.mouse.down();
              if (message.type === "pointerUp") await page.mouse.up();
            } else if (message.type === "text" && typeof message.text === "string" && message.text.length <= 4000) {
              await page.keyboard.insertText(message.text);
            } else if (message.type === "key" && keys.has(message.key)) {
              await page.keyboard.press(message.key);
            } else if (message.type === "wheel" && Number.isFinite(message.deltaX) && Number.isFinite(message.deltaY) &&
                Math.abs(message.deltaX) <= 4000 && Math.abs(message.deltaY) <= 4000) {
              await page.mouse.wheel(message.deltaX, message.deltaY);
            } else connection.close(1008, "Unsupported input.");
          }).catch(() => {
            send({ type: "error", message: "Browser interaction failed. For photos, check the correct upload control is visible or open its Choose photo dialog, then retry. Review the remote form before retrying." });
          }).finally(() => { queued--; });
        });
      });
    });
    cdp.on("Page.screencastFrame", event => {
      if (controller?.readyState === WebSocket.OPEN && controller.bufferedAmount < 1024 * 1024)
        controller.send(Buffer.from(event.data, "base64"));
      cdp.send("Page.screencastFrameAck", { sessionId: event.sessionId }).catch(browserFailure);
    });
    await cdp.send("Page.startScreencast", { format: "jpeg", quality: 70, maxWidth: 1000, maxHeight: 800, everyNthFrame: 1 });
    await new Promise((resolve, reject) => {
      server.once("error", reject);
      server.listen(port, "127.0.0.1", resolve);
    });
    origin = `http://127.0.0.1:${server.address().port}`;
    timer = setInterval(() => {
      if (expired()) close().catch(browserFailure);
    }, 1000);
    timer.unref();
    return { origin, url: `${origin}/#${launchToken}`, close, page, context, browser };
  } catch (error) {
    await close();
    throw error;
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  try {
    let options = {};
    if (process.argv.includes("--managed")) {
      const { createInterface } = await import("node:readline");
      const input = createInterface({ input: process.stdin, terminal: false });
      const line = await new Promise(resolve => input.once("line", resolve));
      input.close();
      options = JSON.parse(line);
    }
    const prototype = await startPrototype({ channel: process.env.BROWSER_CHANNEL ?? "chrome", ...options });
    console.log(process.argv.includes("--managed") ? JSON.stringify({ url: prototype.url }) :
      `Offline-only launch link (single use, local machine only):\n${prototype.url}`);
    const stop = () => prototype.close().catch(error => { console.error(error.message); process.exitCode = 1; });
    process.once("SIGINT", stop);
    process.once("SIGTERM", stop);
  } catch (error) {
    if (process.argv.includes("--managed"))
      console.log(JSON.stringify({ error: ["invalid_payload", "browser_launch", "navigation"].includes(error.startupCode)
        ? error.startupCode : "startup_failed" }));
    console.error(process.argv.includes("--managed") ? "The managed browser worker could not start." :
      `Offline prototype could not start: ${error.message}`);
    process.exitCode = 1;
  }
}
