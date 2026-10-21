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

function speakerControls(window, enabled = true, labelled = true) {
  const section = window.document.createElement("section");
  section.innerHTML = `<div>${labelled ? '<button type="button" aria-label="Speakers">?</button>' : ""}
    <button type="button" role="switch" aria-checked="${enabled}">Speakers switch</button></div>
    <div id="speaker" ${enabled ? "" : "hidden"}>
    <input id="speaker.name" maxlength="80">
    <div id="speaker.bio"><div class="toastui-editor-main toastui-editor-ww-mode">
      <div class="ProseMirror" contenteditable="true">Hidden Markdown bio</div>
      <div class="ProseMirror toastui-editor-contents" contenteditable="true">Old bio</div>
    </div></div>
    ${["facebook", "instagram", "twitter", "linkedin", "other"].map(id =>
      `<input id="speaker.${id}" maxlength="120" value="Old ${id}">`).join("")}
    </div>`;
  window.document.querySelector("form").append(section);
  const toggle = section.querySelector('[role="switch"]');
  let clicks = 0;
  toggle.onclick = () => {
    clicks++;
    toggle.setAttribute("aria-checked", "true");
    section.querySelector("#speaker").hidden = false;
  };
  const rects = window.HTMLElement.prototype.getClientRects;
  window.HTMLElement.prototype.getClientRects = function () {
    return this.closest("[hidden]") ? [] : rects.call(this);
  };
  return { section, toggle, clicks: () => clicks };
}

function dateControls(window, { month = "2027-01", selected = "2027-01-22", disabledDay = false,
  stuck = false, revert = false, ambiguous = false, disabledNavigation = false } = {}) {
  const trigger = window.document.createElement("button");
  trigger.type = "button";
  trigger.setAttribute("aria-label", "Open date picker");
  trigger.setAttribute("aria-haspopup", "dialog");
  trigger.setAttribute("aria-controls", "calendar-test");
  trigger.setAttribute("aria-expanded", "false");
  trigger.textContent = "Date";
  window.document.querySelector("form").append(trigger);
  const dialog = window.document.createElement("div");
  dialog.id = "calendar-test";
  dialog.setAttribute("role", "dialog");
  dialog.hidden = true;
  window.document.body.append(dialog);
  let navigations = 0, selections = 0, opens = 0;
  let displayed = new Date(`${month}-01T00:00:00Z`);
  function render() {
    const heading = displayed.toLocaleDateString("en-US", { timeZone: "UTC", month: "long", year: "numeric" });
    const prefix = displayed.toISOString().slice(0, 7);
    const count = new Date(Date.UTC(displayed.getUTCFullYear(), displayed.getUTCMonth() + 1, 0)).getUTCDate();
    dialog.innerHTML = `<button type="button" aria-label="Go to the Previous Month" ${disabledNavigation ? "disabled" : ""}>Previous</button>
      <button type="button" aria-label="Go to the Next Month" ${disabledNavigation ? "disabled" : ""}>Next</button>
      <table role="grid" aria-label="${heading}"><tbody><tr>${Array.from({ length: count }, (_, index) => {
        const date = `${prefix}-${String(index + 1).padStart(2, "0")}`;
        return `<td role="gridcell" data-day="${date}" ${selected === date ? 'aria-selected="true"' : ""}>
          <button type="button" ${disabledDay ? 'disabled data-disabled="true"' : ""}>${index + 1}</button></td>`;
      }).join("")}</tr></tbody></table>`;
    for (const [index, button] of [...dialog.querySelectorAll(":scope > button")].entries()) {
      button.onclick = () => {
        navigations++;
        if (stuck) return;
        window.setTimeout(() => {
          displayed.setUTCMonth(displayed.getUTCMonth() + (index === 0 ? -1 : 1));
          render();
        }, 5);
      };
    }
    for (const button of dialog.querySelectorAll("td button"))
      button.onclick = () => {
        selections++;
        if (!revert) selected = button.parentElement.dataset.day;
        dialog.hidden = true;
        trigger.setAttribute("aria-expanded", "false");
      };
    if (ambiguous) dialog.append(dialog.querySelector("table").cloneNode(true));
  }
  trigger.onclick = () => {
    const opening = trigger.getAttribute("aria-expanded") !== "true";
    trigger.setAttribute("aria-expanded", String(opening));
    dialog.hidden = !opening;
    if (opening) { opens++; render(); }
  };
  return { trigger, dialog, selected: () => selected, navigations: () => navigations,
    selections: () => selections, opens: () => opens };
}

for (const month of ["2027-01", "2026-12", "2027-02"]) {
  test(`selects and reopens calendar to verify full date from ${month}, then sets native time without saving`, async () => {
    const { dom, window, run } = page();
    const calendar = dateControls(window, { month });
    let saves = 0;
    window.document.getElementById("save").onclick = () => saves++;
    try {
      const result = await run();
      assert.equal(result.errors.length, 0);
      assert.equal(calendar.selected(), "2027-01-25");
      assert.equal(calendar.navigations(), month === "2027-01" ? 0 : 1);
      assert.equal(calendar.selections(), 1);
      assert.equal(calendar.opens(), 2);
      assert.equal(calendar.trigger.getAttribute("aria-expanded"), "false");
      assert.ok(result.filled.includes("Start date: 2027-01-25"));
      assert.equal(window.document.querySelector('[type="time"]').value, "18:30");
      assert.equal(saves, 0);
    } finally { dom.window.close(); }
  });
}

test("already-selected day is verified without toggling it off, and absent schedule consent leaves calendar untouched", async () => {
  const { dom, window, run } = page();
  const calendar = dateControls(window, { selected: "2027-01-25" });
  try {
    await run(payload(), { replace: true, schedule: false });
    assert.equal(calendar.opens(), 0);
    assert.equal(window.document.querySelector('[type="time"]').value, "12:00");
    const result = await run();
    assert.equal(calendar.selections(), 0);
    assert.ok(result.filled.includes("Start date: 2027-01-25"));
  } finally { dom.window.close(); }
});

for (const options of [
  { disabledDay: true }, { revert: true }, { ambiguous: true },
  { month: "2026-12", stuck: true }, { month: "2026-12", disabledNavigation: true },
  { month: "2010-01" }
]) {
  test(`calendar failure ${JSON.stringify(options)} reports unconfirmed date and preserves time`, async () => {
    const { dom, window, run } = page();
    const calendar = dateControls(window, options);
    try {
      const result = await run();
      assert.ok(result.errors.some(line => line.startsWith("Start date:")));
      assert.ok(!result.filled.some(line => line.startsWith("Start date:")));
      assert.equal(calendar.selected(), "2027-01-22");
      assert.equal(window.document.querySelector('[type="time"]').value, "12:00");
    } finally { dom.window.close(); }
  });
}

test("calendar controls are untouched on the wrong event and date selection preserves an already-open picker", async () => {
  const wrong = page("/test-group/events/456/edit/");
  const wrongCalendar = dateControls(wrong.window);
  try {
    await wrong.run();
    assert.equal(wrongCalendar.opens(), 0);
  } finally { wrong.dom.window.close(); }
  const { dom, window, run } = page();
  const calendar = dateControls(window);
  calendar.trigger.click();
  try {
    const result = await run();
    assert.equal(result.errors.length, 0);
    assert.equal(calendar.trigger.getAttribute("aria-expanded"), "true");
    assert.equal(calendar.selected(), "2027-01-25");
  } finally { dom.window.close(); }
});

test("navigation while opening date picker stops further writes", async () => {
  const { dom, window, run } = page();
  const calendar = dateControls(window);
  calendar.trigger.onclick = () => window.history.replaceState({}, "", "/test-group/events/456/edit/");
  try {
    const result = await run();
    assert.ok(result.errors.some(line => line.includes("navigated")));
    assert.equal(window.document.querySelector('[type="time"]').value, "12:00");
    assert.equal(window.document.querySelector(".ProseMirror").textContent, "Old description");
  } finally { dom.window.close(); }
});

const preparedSpeaker = { name: "Alex Example", bio: "**Alex** builds apps.", twitter: "@alex",
  facebook: "https://facebook.com/alex", linkedIn: "https://linkedin.com/in/alex", website: "https://alex.example" };

function joiningControls(window, initial = "In person", { locked = false, revert = false, duplicate = false } = {}) {
  window.document.getElementById("onlineEventLink").remove();
  const card = window.document.createElement("section");
  const labels = ["In person", "Online", "Hybrid"];
  let clicks = 0;
  function render(selected) {
    card.innerHTML = `<div class="format-tabs">${labels.map(label => `<div><div><span>${label}</span></div>
      ${selected === label ? '<div class="bottom-0 bg-ds2-icon-fill-primary-enabled"></div>' : ""}</div>`).join("")}</div>
      ${locked ? "<h2>Hybrid events cannot be modified</h2>" : ""}
      ${selected !== "In person" ? '<input id="onlineEventLink" value="Old online link">' : ""}
      ${selected !== "Online" ? '<div id="venue"><input placeholder="Search or add location..." value="Old venue"></div>' : ""}`;
    for (const tab of card.querySelector(".format-tabs").children)
      tab.onclick = () => {
        clicks++;
        if (!revert) window.setTimeout(() => render(tab.textContent.trim()), 25);
      };
  }
  render(initial);
  window.document.querySelector("form").append(card);
  if (duplicate) window.document.querySelector("form").append(card.cloneNode(true));
  return { card, clicks: () => clicks };
}

for (const [format, label, initial] of [
  ["InPerson", "In person", "Online"], ["Online", "Online", "In person"], ["Hybrid", "Hybrid", "In person"]
]) {
  test(`selects ${format}, waits for rendered controls and fills only applicable joining details`, async () => {
    const { dom, window, run } = page();
    const controls = joiningControls(window, initial);
    let saved = false;
    window.document.getElementById("save").onclick = () => { saved = true; };
    try {
      const result = await run(payload({ format, onlineEventUrl: "https://zoom.us/saved", venueName: "Town Hall",
        location: "Physical address" }));
      assert.equal(result.errors.length, 0);
      assert.equal(controls.clicks(), 1);
      assert.ok(result.filled.includes(`Event format: ${label}`));
      if (format !== "InPerson")
        assert.equal(window.document.getElementById("onlineEventLink").value, "https://zoom.us/saved");
      else assert.equal(window.document.getElementById("onlineEventLink"), null);
      if (format !== "Online") {
        assert.equal(window.document.querySelector("#venue input").value, "Town Hall");
        assert.ok(result.manual.some(line => line.includes("does not select or save a venue")));
      } else assert.equal(window.document.querySelector("#venue"), null);
      assert.equal(window.document.getElementById("howToFindUs").value, "Do not replace arrival directions");
      assert.equal(saved, false);
    } finally { dom.window.close(); }
  });
}

test("already-selected hybrid can fill details without switching despite immutable hybrid notice", async () => {
  const { dom, window, run } = page();
  const controls = joiningControls(window, "Hybrid", { locked: true });
  try {
    const result = await run();
    assert.equal(result.errors.length, 0);
    assert.equal(controls.clicks(), 0);
    assert.equal(window.document.getElementById("onlineEventLink").value, payload().location);
    assert.equal(window.document.querySelector("#venue input").value, "Hall");
  } finally { dom.window.close(); }
});

for (const mode of ["locked", "revert", "duplicate"]) {
  test(`format ${mode} reports failure without writing mismatched joining details`, async () => {
    const { dom, window, run } = page();
    const controls = joiningControls(window, "Hybrid", { [mode]: true });
    try {
      const result = await run(payload({ format: "Online" }));
      assert.ok(result.errors.some(line => line.startsWith("Event format:")));
      assert.equal(window.document.getElementById("onlineEventLink").value, "Old online link");
      assert.equal(window.document.querySelector("#venue input").value, "Old venue");
      assert.equal(controls.clicks(), mode === "revert" ? 1 : 0);
    } finally { dom.window.close(); }
  });
}

test("unsafe or missing online links preserve existing data; blank venue does not erase search", async () => {
  const { dom, window, run } = page();
  joiningControls(window, "Hybrid");
  try {
    const result = await run(payload({ onlineEventUrl: "javascript:alert(1)", location: "", venueName: "" }));
    assert.equal(window.document.getElementById("onlineEventLink").value, "Old online link");
    assert.equal(window.document.querySelector("#venue input").value, "Old venue");
    assert.ok(result.manual.some(line => line.includes("no valid saved")));
    assert.ok(result.manual.some(line => line.includes("no saved venue")));
  } finally { dom.window.close(); }
});

test("enables evidenced Speakers switch and fills first speaker without mixing the description editors", async () => {
  const { dom, window, run } = page(undefined, true);
  const controls = speakerControls(window, false);
  let saves = 0;
  window.document.getElementById("save").onclick = () => saves++;
  try {
    const result = await run(payload({ speaker: preparedSpeaker, speakerCount: 2 }));
    assert.equal(result.errors.length, 0);
    assert.equal(controls.clicks(), 1);
    assert.ok(result.filled.includes("Speakers enabled"));
    assert.ok(result.filled.includes("Description (formatted rich text)"));
    assert.ok(result.filled.includes("Speaker bio (formatted rich text)"));
    assert.equal(window.document.getElementById("speaker.name").value, "Alex Example");
    assert.equal(window.document.getElementById("speaker.twitter").value, "https://x.com/alex");
    assert.equal(window.document.getElementById("speaker.linkedin").value, preparedSpeaker.linkedIn);
    assert.equal(window.document.getElementById("speaker.facebook").value, preparedSpeaker.facebook);
    assert.equal(window.document.getElementById("speaker.other").value, preparedSpeaker.website);
    assert.equal(window.document.getElementById("speaker.instagram").value, "Old instagram");
    assert.equal(controls.section.querySelector(".toastui-editor-contents strong").textContent, "Alex");
    assert.equal(controls.section.querySelector('.ProseMirror:not(.toastui-editor-contents)').textContent, "Hidden Markdown bio");
    assert.equal(window.document.querySelector("form > .ProseMirror strong").textContent, "New description");
    assert.ok(result.manual.some(line => line.includes("second/remaining")));
    assert.ok(result.manual.some(line => line.includes("Speaker photo")));
    assert.equal(saves, 0);
  } finally { dom.window.close(); }
});

test("fills already enabled speaker fields without toggling and refuses oversized or unsafe links", async () => {
  const { dom, window, run } = page();
  const controls = speakerControls(window, true, false);
  try {
    const result = await run(payload({ speaker: { ...preparedSpeaker, name: "A".repeat(81),
      facebook: "javascript:alert(1)", website: "https://example.test/" + "x".repeat(121) }, speakerCount: 1 }));
    assert.equal(controls.clicks(), 0);
    assert.ok(result.errors.some(line => line.startsWith("Speaker name:")));
    assert.ok(result.errors.some(line => line.startsWith("Speaker Other:")));
    assert.ok(result.manual.some(line => line.startsWith("Speaker Facebook:")));
    assert.equal(window.document.getElementById("speaker.facebook").value, "Old facebook");
    assert.ok(result.filled.includes("Description (Markdown)"));
    assert.ok(result.filled.includes("Speaker bio (formatted rich text)"));
  } finally { dom.window.close(); }
});

test("unidentified switch stays untouched; absent prepared speaker leaves existing profile alone", async () => {
  for (const data of [payload({ speaker: preparedSpeaker, speakerCount: 1 }), payload()]) {
    const { dom, window, run } = page();
    const controls = speakerControls(window, false, false);
    try {
      const result = await run(data);
      assert.equal(controls.clicks(), 0);
      assert.equal(window.document.getElementById("speaker.name").value, "");
      if (data.speaker) assert.ok(result.manual.some(line => line.includes("enable the Speakers switch manually")));
      assert.ok(result.filled.includes("Description (Markdown)"));
    } finally { dom.window.close(); }
  }
});

test("speaker values reverting after input are reported and missing bio does not erase existing content", async () => {
  const { dom, window, run } = page();
  const controls = speakerControls(window);
  const name = window.document.getElementById("speaker.name");
  name.addEventListener("input", () => { name.value = "Controlled old name"; });
  try {
    const result = await run(payload({ speaker: { ...preparedSpeaker, bio: "", linkedIn: null }, speakerCount: 1 }));
    assert.ok(result.errors.some(line => line.startsWith("Speaker name: Meetup did not retain")));
    assert.ok(!result.filled.includes("Speaker name"));
    assert.ok(result.manual.some(line => line.includes("no prepared bio")));
    assert.equal(controls.section.querySelector(".toastui-editor-contents").textContent, "Old bio");
    assert.equal(window.document.getElementById("speaker.linkedin").value, "Old linkedin");
    assert.ok(result.manual.some(line => line.startsWith("Speaker LinkedIn: no saved value")));
  } finally { dom.window.close(); }
});

test("Other social accepts a Bluesky profile and missing speaker payload explains reloading content", async () => {
  const { dom, window, run } = page();
  speakerControls(window);
  try {
    const result = await run(payload({ speaker: { ...preparedSpeaker, website: "https://bsky.app/profile/alex.example" }, speakerCount: 1 }));
    assert.equal(window.document.getElementById("speaker.other").value, "https://bsky.app/profile/alex.example");
    assert.ok(result.filled.includes("Speaker Other"));
    const old = await run(payload());
    assert.ok(old.manual.some(line => line.includes("load/capture fresh event content")));
  } finally { dom.window.close(); }
});

test("wrong target cannot toggle or write speaker fields", async () => {
  const { dom, window, run } = page("/another-group/events/123/edit/");
  const controls = speakerControls(window, false);
  try {
    const result = await run(payload({ speaker: preparedSpeaker, speakerCount: 1 }));
    assert.ok(result.errors.length);
    assert.equal(controls.clicks(), 0);
    assert.equal(window.document.getElementById("speaker.name").value, "");
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
test("speaker photo action supplies only speaker PNG to the open chooser and never applies it", async () => {
  const { dom, window, run } = page();
  speakerControls(window);
  window.document.body.insertAdjacentHTML("beforeend", `<div role="dialog" data-slot="modal-content">
    <div id="photo-uploader-drop-area"><button>Choose photo</button><input type="file" accept="image/*"></div>
    <button id="apply-speaker">Apply</button></div>`);
  const input = window.document.querySelector('input[type="file"]');
  Object.defineProperty(input, "files", { writable: true, value: [] });
  window.DataTransfer = class { files = []; items = { add: file => this.files.push(file) }; };
  let applied = false, changed = 0;
  window.document.getElementById("apply-speaker").onclick = () => { applied = true; };
  input.addEventListener("change", () => changed++);
  try {
    const photoBase64 = window.btoa("\x89PNG\r\n\x1a\nSPEAKER");
    const result = await run(payload({ speaker: { ...preparedSpeaker, photoBase64 }, speakerCount: 1 }),
      { replace: true, speakerPhoto: true });
    assert.equal(result.errors.length, 0);
    assert.equal(input.files[0].name, "speaker-photo.png");
    assert.equal(input.files[0].size, window.atob(photoBase64).length);
    assert.equal(input.files[0].type, "image/png");
    assert.equal(changed, 1);
    assert.equal(applied, false);
    assert.equal(window.document.getElementById("title").value, "Old title");
    assert.ok(result.manual.some(line => line.includes("not as the event image")));
  } finally { dom.window.close(); }
});
test("speaker photo action fails without a prepared photo or open chooser, never uses social image", async () => {
  const { dom, window, run } = page();
  speakerControls(window);
  try {
    const missing = await run(payload({ speaker: { ...preparedSpeaker, photoError: "Photo unavailable" }, speakerCount: 1 }),
      { replace: true, speakerPhoto: true });
    assert.match(missing.errors[0], /Photo unavailable/);
    const closed = await run(payload({ speaker: { ...preparedSpeaker, photoBase64: payload().imageBase64 }, speakerCount: 1 }),
      { replace: true, speakerPhoto: true });
    assert.match(closed.errors[0], /Upload Photo/);
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
