import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";

async function editor() {
  const dom = new JSDOM(`<div><div role="toolbar"><button data-rich-command="bold">Bold</button>
    <select data-rich-heading><option value="">Heading</option><option value="p">Normal text</option>
    ${[1,2,3,4,5,6].map(level => `<option value="h${level}">Heading ${level}</option>`).join("")}</select></div>
    <div contenteditable="true" tabindex="0"></div><p class="rich-save-error" hidden></p></div>`, { runScripts: "outside-only" });
  dom.window.eval(await readFile(new URL("../../../src/MeetupOrganiser.Web/wwwroot/site.js", import.meta.url), "utf8"));
  const area = dom.window.document.querySelector("[contenteditable]");
  const api = dom.window.meetupOrganiser;
  const changes = [];
  api.initRichEditor(area, { invokeMethodAsync: async (name, value) => { changes.push([name, value]); } });
  const drain = () => new Promise(resolve => setTimeout(resolve, 10));
  return { dom, area, api, changes, drain };
}

test("rich text serializes formatting, lists, links and placeholders without raw HTML", async () => {
  const { dom, area, changes, drain } = await editor();
  try {
    area.innerHTML = '<p><strong>{{EventTitle}}</strong> <em>Alex</em></p><ul><li>First</li><li>Second</li></ul><ol><li>One</li></ol><p><a href="https://example.com">Site</a></p>';
    area.dispatchEvent(new dom.window.Event("input"));
    await drain();
    assert.deepEqual(changes.at(-1), ["RichTextChanged",
      "**{{EventTitle}}** _Alex_\n\n- First\n- Second\n\n1. One\n\n[Site](https://example.com)"]);
    area.textContent = "*literal* #heading [not a link]";
    area.dispatchEvent(new dom.window.Event("input"));
    await drain();
    assert.equal(changes.at(-1)[1], "\\*literal\\* \\#heading \\[not a link\\]");
    area.innerHTML = "<p><strong>{{Welcome_Section}}</strong> literal_name</p>";
    area.dispatchEvent(new dom.window.Event("input"));
    await drain();
    assert.equal(changes.at(-1)[1], "**{{Welcome_Section}}** literal\\_name");
  } finally { dom.window.close(); }
});

test("heading selector restores selection, applies every level and saves matching Markdown", async () => {
  const { dom, area, changes, drain } = await editor();
  try {
    const select = dom.window.document.querySelector("[data-rich-heading]");
    for (const tag of ["h1", "h2", "h3", "h4", "h5", "h6", "p"]) {
      area.textContent = "Title";
      const range = dom.window.document.createRange();
      range.selectNodeContents(area);
      dom.window.getSelection().removeAllRanges();
      dom.window.getSelection().addRange(range);
      select.dispatchEvent(new dom.window.Event("pointerdown", { bubbles: true }));
      dom.window.getSelection().removeAllRanges();
      dom.window.document.execCommand = (command, unused, value) => {
        assert.equal(command, "formatBlock");
        assert.equal(value, tag);
        assert.equal(dom.window.getSelection().toString(), "Title");
        area.innerHTML = `<${value}>Title</${value}>`;
        return true;
      };
      select.value = tag;
      select.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      await drain();
      assert.equal(changes.at(-1)[1], tag === "p" ? "Title" : "#".repeat(Number(tag[1])) + " Title");
      assert.equal(select.value, "");
    }
  } finally { dom.window.close(); }
});

test("keyboard shortcuts apply formatting locally, preserve selection and suppress browser defaults", async () => {
  const { dom, area, changes, drain } = await editor();
  try {
    area.textContent = "Selected";
    const range = dom.window.document.createRange();
    range.selectNodeContents(area);
    dom.window.getSelection().addRange(range);
    const commands = [];
    dom.window.document.execCommand = (command, unused, value) => { commands.push([command, value]); return true; };
    for (const [key, options, command] of [
      ["b", { ctrlKey: true }, "bold"], ["i", { metaKey: true }, "italic"],
      ["7", { ctrlKey: true, shiftKey: true }, "insertOrderedList"],
      ["8", { ctrlKey: true, shiftKey: true }, "insertUnorderedList"]
    ]) {
      const event = new dom.window.KeyboardEvent("keydown", { key, ...options, bubbles: true, cancelable: true });
      area.dispatchEvent(event);
      assert.equal(event.defaultPrevented, true);
      assert.equal(commands.at(-1)[0], command);
    }
    dom.window.prompt = () => "https://example.com";
    area.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "k", ctrlKey: true, bubbles: true, cancelable: true }));
    assert.equal(commands.at(-1)[0], "createLink");
    assert.equal(commands.at(-1)[1], "https://example.com/");
    dom.window.prompt = () => "javascript:alert(1)";
    const count = commands.length;
    area.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "k", ctrlKey: true, bubbles: true, cancelable: true }));
    await drain();
    assert.equal(commands.length, count);
    assert.ok(changes.some(([name]) => name === "RichTextError"));
  } finally { dom.window.close(); }
});

test("toolbar retains selection, external reset updates editor and disposal removes handlers", async () => {
  const { dom, area, api, drain, changes } = await editor();
  try {
    api.setRichEditor(area, "<p>Reset {{EventTitle}}</p>");
    const range = dom.window.document.createRange();
    range.selectNodeContents(area.querySelector("p"));
    dom.window.getSelection().addRange(range);
    let selected;
    dom.window.document.execCommand = () => { selected = dom.window.getSelection().toString(); return true; };
    const button = dom.window.document.querySelector("button");
    button.dispatchEvent(new dom.window.Event("pointerdown", { bubbles: true, cancelable: true }));
    dom.window.getSelection().removeAllRanges();
    button.click();
    await drain();
    assert.equal(selected, "Reset {{EventTitle}}");
    api.disposeRichEditor(area);
    const count = changes.length;
    area.dispatchEvent(new dom.window.Event("input"));
    await drain();
    assert.equal(changes.length, count);
  } finally { dom.window.close(); }
});

test("paste uses text only, IME waits for composition and failed sync shows visible warning", async () => {
  const { dom, area, api, drain, changes } = await editor();
  try {
    let pasted;
    dom.window.document.execCommand = (command, unused, value) => { pasted = [command, value]; return true; };
    const paste = new dom.window.Event("paste", { cancelable: true });
    paste.clipboardData = { getData: type => type === "text/plain" ? "<script>text only</script>" : "<img src=x>" };
    area.dispatchEvent(paste);
    assert.deepEqual(pasted, ["insertText", "<script>text only</script>"]);
    await drain();
    const count = changes.length;
    area.dispatchEvent(new dom.window.Event("compositionstart"));
    area.textContent = "Composing";
    area.dispatchEvent(new dom.window.Event("input"));
    await drain();
    assert.equal(changes.length, count);
    area.dispatchEvent(new dom.window.Event("compositionend"));
    await drain();
    assert.equal(changes.at(-1)[1], "Composing");
    api.disposeRichEditor(area);
    dom.window.console.error = () => {};
    api.initRichEditor(area, { invokeMethodAsync: async () => { throw new Error("Disconnected"); } });
    area.dispatchEvent(new dom.window.Event("input"));
    await drain();
    assert.equal(area.getAttribute("aria-invalid"), "true");
    assert.equal(dom.window.document.querySelector(".rich-save-error").hidden, false);
  } finally { dom.window.close(); }
});

test("formatted clipboard provides both HTML and Markdown representations", async () => {
  const { dom, api } = await editor();
  try {
    Object.defineProperty(dom.window, "isSecureContext", { value: true });
    dom.window.ClipboardItem = class { constructor(data) { this.data = data; } };
    let items;
    Object.defineProperty(dom.window.navigator, "clipboard", { value: { write: async value => { items = value; } } });
    assert.equal(await api.copyMarkdown("**Bold**", "<p><strong>Bold</strong></p>"), "html");
    assert.deepEqual(Object.keys(items[0].data), ["text/html", "text/plain"]);
  } finally { dom.window.close(); }
});
