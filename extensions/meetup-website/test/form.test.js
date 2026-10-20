import test from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import { fillMeetupForm } from "../form.js";
import { capturePreparedContent } from "../handoff.js";
import { payload } from "./fixture.js";

// Synthetic controls based only on evidenced form structure, not copied page content.
function page(path = "/test-group/events/123/edit/", richText = false) {
  const dom = new JSDOM(`<!doctype html><main id="event-schedule-main"><form>
    <input id="title" data-testid="event-name-input" value="Old title">
    <input type="time" aria-label="Edit start time" value="12:00">
    <div class="ProseMirror${richText ? " toastui-editor-contents" : ""}" contenteditable="true">Old description</div>
    <input id="onlineEventLink" value="https://zoom.us/old">
    <textarea id="howToFindUs">Do not replace arrival directions</textarea>
    <button type="button" id="save">Save changes</button></form></main>`,
  { url: `https://www.meetup.com${path}`, runScripts: "outside-only" });
  const { window } = dom;
  window.HTMLElement.prototype.getClientRects = function () {
    return this.hidden || this.style.display === "none" ? [] : [{}];
  };
  Object.defineProperty(window.HTMLElement.prototype, "innerText", {
    get() { return this.textContent; }
  });
  window.document.execCommand = (command, unused, text) => {
    assert.ok(["insertText", "insertHTML"].includes(command));
    const editor = window.document.activeElement;
    if (command === "insertHTML") editor.innerHTML = text;
    else editor.textContent = text;
    editor.dispatchEvent(new window.Event("input", { bubbles: true }));
    return true;
  };
  const run = (data = payload(), consent = { replace: true, schedule: true }) =>
    window.eval(`(${fillMeetupForm.toString()})`)(data, consent);
  return { dom, window, run };
}

test("fills evidenced fields only; never saves or replaces venue directions", async () => {
  const { dom, window, run } = page();
  let saves = 0;
  window.document.getElementById("save").onclick = () => saves++;
  try {
    const result = await run();
    assert.deepEqual(Array.from(result.errors), []);
    assert.deepEqual(Array.from(result.filled), ["Title", "Start time", "Description (Markdown)", "Online meeting link"]);
    assert.equal(window.document.getElementById("title").value, "New title");
    assert.equal(window.document.getElementById("howToFindUs").value, "Do not replace arrival directions");
    assert.equal(saves, 0);
    assert.ok(result.manual.some(line => line.includes("duration")));
    assert.ok(result.manual.some(line => line.includes("image")));
  } finally { dom.window.close(); }
});
test("requires explicit replacement consent and correct target before modifying anything", async () => {
  for (const [path, consent] of [
    ["/test-group/events/456/edit/", { replace: true }],
    ["/another-group/events/123/edit/", { replace: true }],
    ["/test-group/events/123/edit/", { replace: false }]
  ]) {
    const { dom, window, run } = page(path);
    try {
      const result = await run(payload(), consent);
      assert.ok(result.errors.length);
      assert.equal(window.document.getElementById("title").value, "Old title");
    } finally { dom.window.close(); }
  }
});
test("formats rich-text description without changing unconfirmed time", async () => {
  const { dom, window, run } = page(undefined, true);
  try {
    const result = await run(payload(), { replace: true, schedule: false });
    assert.equal(window.document.querySelector(".ProseMirror strong").textContent, "New description");
    assert.ok(result.filled.includes("Description (formatted rich text)"));
    assert.equal(window.document.querySelector('[type="time"]').value, "12:00");
  } finally { dom.window.close(); }
});
test("selects the active rich-text editor when both Toast UI surfaces have layout boxes", async () => {
  const { dom, window, run } = page(undefined, true);
  const rich = window.document.querySelector(".ProseMirror");
  const container = window.document.createElement("div");
  container.className = "toastui-editor-main toastui-editor-ww-mode";
  rich.replaceWith(container);
  const markdown = window.document.createElement("div");
  markdown.className = "ProseMirror";
  markdown.setAttribute("contenteditable", "true");
  markdown.textContent = "Hidden Markdown content";
  container.append(markdown, rich);
  try {
    const result = await run();
    assert.equal(result.errors.length, 0);
    assert.equal(markdown.textContent, "Hidden Markdown content");
    assert.equal(rich.querySelector("strong").textContent, "New description");
    assert.ok(result.filled.includes("Description (formatted rich text)"));
  } finally { dom.window.close(); }
});
test("formats event headings, speaker labels and contact links without inserting untrusted HTML", async () => {
  const { dom, window, run } = page(undefined, true);
  const description = "# A talk about C#\n\nOur Speaker:\n\nAlex Example:\n\nA bio with **bold** and __more bold__.\n\nHow to find Alex:\n\nBluesky: https://bsky.app/profile/example.test\n[Website](https://example.test/?a=1&b=2)\n\nHow to find us:\n\n<img src=x onerror=alert(1)> & [unsafe](javascript:alert(1))";
  try {
    const result = await run(payload({ description }));
    assert.equal(result.errors.length, 0);
    const editor = window.document.querySelector(".ProseMirror");
    assert.deepEqual([...editor.querySelectorAll("strong")].map(node => node.textContent),
      ["A talk about C#", "Our Speaker:", "Alex Example:", "bold", "more bold", "How to find Alex:", "Bluesky:", "How to find us:"]);
    assert.equal(editor.querySelector("a").getAttribute("href"), "https://example.test/?a=1&b=2");
    assert.equal(editor.querySelectorAll("img, script, [onerror]").length, 0);
    assert.ok(editor.textContent.includes("<img src=x onerror=alert(1)>"));
    assert.ok(!editor.textContent.includes("# A talk"));
    assert.ok(editor.textContent.includes("[unsafe](javascript:alert(1))"));
  } finally { dom.window.close(); }
});
test("reports rich-text formatting stripped by the editor rather than claiming success", async () => {
  const { dom, window, run } = page(undefined, true);
  window.document.execCommand = (command, unused, html) => {
    const temporary = window.document.createElement("div");
    temporary.innerHTML = html;
    for (const node of temporary.querySelectorAll("strong")) node.replaceWith(node.textContent);
    window.document.activeElement.innerHTML = temporary.innerHTML;
    return true;
  };
  try {
    const result = await run();
    assert.ok(result.errors.some(line => line.startsWith("Description:")));
    assert.ok(!result.filled.includes("Description (formatted rich text)"));
  } finally { dom.window.close(); }
});
test("preserves toolbar italic text and numbered/bullet lists in rich-text mode", async () => {
  const { dom, window, run } = page(undefined, true);
  try {
    const result = await run(payload({ description: "# Talk\n\n*Italic*\n\n- First\n- **Second**\n\n1. One\n2. Two\n\n***Both*** and **bold with *emphasis***" }));
    assert.equal(result.errors.length, 0);
    const editor = window.document.querySelector(".ProseMirror");
    assert.equal(editor.querySelector("em").textContent, "Italic");
    assert.deepEqual([...editor.querySelectorAll("ul li")].map(node => node.textContent), ["First", "Second"]);
    assert.deepEqual([...editor.querySelectorAll("ol li")].map(node => node.textContent), ["One", "Two"]);
    assert.ok([...editor.querySelectorAll("strong em")].some(node => node.textContent === "Both"));
    assert.ok([...editor.querySelectorAll("strong em")].some(node => node.textContent === "emphasis"));
    assert.ok(result.filled.includes("Description (formatted rich text)"));
  } finally { dom.window.close(); }
});
test("preserves literal Markdown characters escaped by the app rich-text editor", async () => {
  const { dom, window, run } = page(undefined, true);
  try {
    const result = await run(payload({ description: "\\# Literal heading\n\n\\*literal stars\\* and \\[brackets\\] with **_bold italic_**" }));
    assert.equal(result.errors.length, 0);
    const editor = window.document.querySelector(".ProseMirror");
    assert.ok(editor.textContent.includes("# Literal heading"));
    assert.ok(editor.textContent.includes("*literal stars* and [brackets]"));
    assert.equal(editor.querySelector("strong em").textContent, "bold italic");
  } finally { dom.window.close(); }
});
test("detects controlled input reverting changes", async () => {
  const { dom, window, run } = page();
  const title = window.document.getElementById("title");
  title.addEventListener("input", () => { title.value = "Old title"; });
  try {
    const result = await run();
    assert.ok(result.errors.some(line => line.startsWith("Title:")));
    assert.ok(!result.filled.includes("Title"));
  } finally { dom.window.close(); }
});
test("verifies native block markup without treating browser innerText layout spacing as extra Markdown", async () => {
  const { dom, window, run } = page();
  window.document.execCommand = () => {
    window.document.querySelector(".ProseMirror").innerHTML = "**New description**<div><br></div><div>Another paragraph</div>";
    return true;
  };
  try {
    const result = await run();
    assert.ok(result.filled.includes("Description (Markdown)"));
    assert.equal(result.errors.length, 0);
  } finally { dom.window.close(); }
});
test("unsupported create form reports limitation without writing fields", async () => {
  const { dom, window, run } = page("/test-group/events/create/");
  window.document.querySelector("main").id = "unknown-form";
  try {
    const result = await run(payload({ operation: "create", eventUrl: null }));
    assert.ok(result.errors.length);
    assert.equal(window.document.getElementById("title").value, "Old title");
  } finally { dom.window.close(); }
});
test("fills evidenced new-event schedule form without saving or touching unrelated controls", async () => {
  const { dom, window, run } = page("/test-group/schedule/#");
  window.document.querySelector("form").insertAdjacentHTML("afterbegin",
    '<input aria-label="Search"><textarea id="event-chat-welcome-message">Welcome</textarea><button id="publish" type="button">Publish</button>');
  let published = false;
  window.document.getElementById("publish").onclick = () => { published = true; };
  try {
    const result = await run(payload({ operation: "create", eventUrl: null }));
    assert.equal(result.errors.length, 0);
    assert.equal(window.document.getElementById("title").value, "New title");
    assert.ok(result.filled.includes("Description (Markdown)"));
    assert.equal(window.document.getElementById("event-chat-welcome-message").value, "Welcome");
    assert.equal(published, false);
    assert.ok(result.manual.some(line => line.includes("Select button")));
  } finally { dom.window.close(); }
});
test("injected adapter rejects schedule route for updates and wrong groups", async () => {
  for (const [route, data] of [
    ["/test-group/schedule/", payload()],
    ["/another-group/schedule/", payload({ operation: "create", eventUrl: null })],
    ["/test-group/schedule/123/", payload({ operation: "create", eventUrl: null })]
  ]) {
    const { dom, window, run } = page(route);
    try {
      assert.ok((await run(data)).errors.length);
      assert.equal(window.document.getElementById("title").value, "Old title");
    } finally { dom.window.close(); }
  }
});
test("capture reads prepared data and actual source origin, not page scripts", () => {
  const dom = new JSDOM("<textarea id='meetup-organiser-handoff' hidden></textarea>",
    { url: "https://organiser.example/events/123", runScripts: "outside-only" });
  try {
    dom.window.document.querySelector("textarea").value = JSON.stringify(payload());
    const result = dom.window.eval(`(${capturePreparedContent.toString()})`)();
    assert.equal(result.origin, "https://organiser.example");
    assert.equal(result.payload.title, "New title");
  } finally { dom.window.close(); }
});

test("supplies PNG only to the evidenced open photo chooser and leaves final actions alone", async () => {
  const { dom, window, run } = page();
  window.document.body.insertAdjacentHTML("beforeend", `<div role="dialog" data-slot="modal-content">
    <button>Choose photo</button><input type="file" accept="image/*"><button id="apply-image">Apply</button></div>`);
  const input = window.document.querySelector('input[type="file"]');
  Object.defineProperty(input, "files", { writable: true, value: [] });
  window.DataTransfer = class {
    files = [];
    items = { add: file => this.files.push(file) };
  };
  let applied = false, changes = 0;
  window.document.getElementById("apply-image").onclick = () => { applied = true; };
  input.addEventListener("change", () => changes++);
  try {
    const result = await run(payload(), { replace: true, photo: true });
    assert.equal(input.files[0].name, "meetup-social.png");
    assert.equal(input.files[0].type, "image/png");
    assert.equal(changes, 1);
    assert.equal(applied, false);
    assert.equal(window.document.getElementById("title").value, "Old title");
    assert.equal(result.errors.length, 0);
    assert.ok(result.filled[0].includes("not confirmed"));
  } finally { dom.window.close(); }
});
test("photo action fails explicitly when chooser is closed without editing anything", async () => {
  const { dom, window, run } = page();
  try {
    const result = await run(payload(), { replace: true, photo: true });
    assert.ok(result.errors[0].includes("Replace"));
    assert.equal(window.document.getElementById("title").value, "Old title");
  } finally { dom.window.close(); }
});
test("new-event Select image button remains a manual action without an evidenced file input", async () => {
  const { dom, window, run } = page("/test-group/events/create/");
  window.document.body.insertAdjacentHTML("beforeend", '<aside><button id="select-image">Select</button></aside>');
  let clicked = false;
  window.document.getElementById("select-image").onclick = () => { clicked = true; };
  try {
    const result = await run(payload({ operation: "create", eventUrl: null }), { replace: true, photo: true });
    assert.ok(result.errors[0].includes("Select button"));
    assert.equal(clicked, false);
  } finally { dom.window.close(); }
});
