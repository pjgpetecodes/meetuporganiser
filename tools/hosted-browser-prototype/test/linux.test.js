import test from "node:test";
import assert from "node:assert/strict";
import { startPrototype } from "../server.js";

test("Linux headed Chromium runs sandboxed as a non-root user and serves the offline viewer", {
  skip: process.platform !== "linux"
}, async () => {
  assert.notEqual(process.getuid(), 0, "The prototype must not run as root.");
  assert.ok(process.env.DISPLAY, "A virtual X display is required for headed Chromium.");
  const prototype = await startPrototype({ channel: "chromium", headless: false });
  try {
    const response = await fetch(prototype.origin);
    assert.equal(response.status, 200);
    await prototype.page.goto("chrome://sandbox");
    const status = await prototype.page.locator("body").innerText();
    assert.match(status, /You are adequately sandboxed/);
  } finally {
    await prototype.close();
  }
});
