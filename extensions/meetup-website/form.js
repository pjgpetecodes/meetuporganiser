// Injected into the active tab; no external helpers or page-internal APIs.
export async function fillMeetupForm(payload, consent) {
  const report = { filled: [], manual: [], errors: [] };
  const fail = message => { report.errors.push(message); return report; };
  if (!consent?.replace) return fail("Confirm replacement of the selected event's fields first.");
  const url = new URL(location.href);
  const parts = url.pathname.split("/").filter(Boolean);
  if (url.protocol !== "https:" || !["www.meetup.com", "meetup.com"].includes(url.hostname) ||
      url.port || url.search || url.hash || parts[0]?.toLowerCase() !== payload.groupSlug.toLowerCase()) {
    return fail("Target page does not match the prepared Meetup group. Nothing was filled.");
  }
  if (payload.operation === "update") {
    const target = new URL(payload.eventUrl).pathname.split("/").filter(Boolean);
    if (parts.length !== 4 || parts[1] !== "events" || parts[2] !== target[2] || parts[3] !== "edit")
      return fail("Open the exact event's edit form. Nothing was filled.");
  } else if (payload.operation !== "create" || !((parts.length === 3 && parts[1] === "events" && parts[2] === "create") ||
      (parts.length === 2 && parts[1] === "schedule"))) {
    return fail("Unsupported new-event route. Use manual copy/paste instead. Nothing was filled.");
  }
  const initialUrl = location.href;
  const visible = element => element && element.getClientRects().length > 0 &&
    getComputedStyle(element).visibility !== "hidden" && !element.closest("[hidden], [aria-hidden=true]");
  if (consent.photo) {
    const dialogs = [...document.querySelectorAll('[role="dialog"][data-slot="modal-content"]')].filter(visible)
      .filter(dialog => [...dialog.querySelectorAll("button")].some(button => button.textContent.trim() === "Choose photo"));
    if (dialogs.length !== 1) return fail(payload.operation === "create"
      ? "No supported photo chooser is open. On a new event, download the PNG and use the image Select button to choose it manually."
      : "Open the event image menu, choose Replace, and leave the Choose photo dialog open first.");
    const inputs = dialogs[0].querySelectorAll('input[type="file"][accept="image/*"]');
    if (inputs.length !== 1 || inputs[0].disabled || typeof DataTransfer !== "function")
      return fail("Supported photo chooser not found. Download the PNG and use Choose photo manually.");
    try {
      const binary = atob(payload.imageBase64);
      const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
      const transfer = new DataTransfer();
      transfer.items.add(new File([bytes], "meetup-social.png", { type: "image/png" }));
      inputs[0].files = transfer.files;
      inputs[0].dispatchEvent(new Event("input", { bubbles: true }));
      inputs[0].dispatchEvent(new Event("change", { bubbles: true }));
      report.filled.push("PNG supplied to the photo chooser (upload/crop not confirmed)");
      report.manual.push("Review the uploaded image and crop/resize dialog, apply the image yourself, and check it appears on the event before saving. Check for upload errors.");
    } catch (error) {
      report.errors.push(`Photo chooser could not receive the PNG: ${error.message}. Download it and use Choose photo manually.`);
    }
    return report;
  }
  const form = document.querySelector("main#event-schedule-main form");
  if (!form) return fail("Supported event form not found. Log in or complete any challenge manually. Nothing was filled.");
  const checks = [];
  function editorText(node) {
    if (node.nodeType === Node.TEXT_NODE) return node.textContent;
    if (node.nodeName === "BR") return node.classList.contains("ProseMirror-trailingBreak") ? "" : "\n";
    if (node.childNodes.length === 1 && node.firstChild.nodeName === "BR") return "";
    const lines = [];
    let inline = "", hasInline = false;
    for (const child of node.childNodes) {
      if (["DIV", "P", "UL", "OL", "LI"].includes(child.nodeName)) {
        if (hasInline) { lines.push(inline); inline = ""; hasInline = false; }
        lines.push(editorText(child));
      } else { inline += editorText(child); hasInline = true; }
    }
    if (hasInline) lines.push(inline);
    return lines.join("\n");
  }
  function richDescription(text) {
    const content = document.createElement("div");
    function appendInline(target, value, depth = 0) {
      if (depth >= 8) { target.append(document.createTextNode(value)); return; }
      const tokens = /\\(?<escaped>[\\`*_[\]<>#+.()-])|\*\*\*(?<both>.+?)\*\*\*|\*\*(?<bold>.+?)\*\*(?!\*)|__(?<boldAlt>.+?)__|\[(?<label>[^\]\n]+)\]\((?<url>https?:\/\/[^\s)]+)\)|\*(?<italic>[^*\n]+)\*|_(?<italicAlt>[^_\n]+)_/g;
      let offset = 0;
      for (const match of value.matchAll(tokens)) {
        if (match.index > offset) target.append(document.createTextNode(value.slice(offset, match.index)));
        const part = match.groups;
        if (part.escaped) {
          target.append(document.createTextNode(part.escaped));
          offset = match.index + match[0].length;
          continue;
        }
        const element = document.createElement(part.url ? "a" : part.italic || part.italicAlt ? "em" : "strong");
        if (part.url) {
          element.textContent = part.label;
          element.setAttribute("href", part.url);
        } else if (part.both) {
          const emphasis = document.createElement("em");
          appendInline(emphasis, part.both, depth + 1);
          element.append(emphasis);
        } else appendInline(element, part.bold ?? part.boldAlt ?? part.italic ?? part.italicAlt, depth + 1);
        target.append(element);
        offset = match.index + match[0].length;
      }
      if (offset < value.length) target.append(document.createTextNode(value.slice(offset)));
    }
    let list = null;
    for (const line of text.replace(/\r\n/g, "\n").split("\n")) {
      const listItem = /^\s*(?:[-+*]|\d+\.)\s+(.+)$/.exec(line);
      const listType = /^\s*\d+\./.test(line) ? "ol" : "ul";
      if (listItem) {
        if (!list || list.localName !== listType) {
          list = document.createElement(listType);
          content.append(list);
        }
      } else list = null;
      const paragraph = document.createElement(listItem ? "li" : "p");
      const heading = /^(?:#{1,6})\s+(.+)$/.exec(line);
      const label = /^([^:\n]{1,100}:)(?=\s+https?:\/\/|$)/.exec(line);
      const value = listItem ? listItem[1] : heading ? heading[1] : label ? line.slice(label[1].length) : line;
      let target = paragraph;
      if (heading || label) {
        const strong = document.createElement("strong");
        paragraph.append(strong);
        if (heading) target = strong;
        else strong.textContent = label[1];
      }
      // Construct an allowlisted fragment; description HTML is always treated as text.
      appendInline(target, value);
      if (!line) paragraph.append(document.createElement("br"));
      (list ?? content).append(paragraph);
    }
    return content;
  }
  function input(selector, value, label) {
    const elements = [...form.querySelectorAll(selector)].filter(visible);
    if (elements.length !== 1 || elements[0].disabled || elements[0].readOnly) {
      report.manual.push(`${label}: control missing, ambiguous or read-only. Enter it manually.`);
      return;
    }
    const element = elements[0];
    if (element.maxLength > 0 && value.length > element.maxLength) {
      report.errors.push(`${label}: prepared value exceeds this control's limit. Nothing was truncated.`);
      return;
    }
    const prototype = element.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value").set.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
    checks.push(() => {
      const current = [...form.querySelectorAll(selector)].filter(visible);
      if (current.length === 1 && current[0].value === value) report.filled.push(label);
      else report.errors.push(`${label}: Meetup did not retain the value. Enter it manually.`);
    });
  }
  try {
    input('input#title[data-testid="event-name-input"]', payload.title, "Title");
    if (consent.schedule)
      input('input[type="time"][aria-label="Edit start time"]', payload.startTime, "Start time");
    else report.manual.push(`Start time: set ${payload.startTime} ${payload.timeZoneLabel} after checking the date and Europe/London time zone.`);

    const editors = [...form.querySelectorAll(".ProseMirror[contenteditable=true]")].filter(visible);
    const activeEditors = editors.filter(editor => {
      const container = editor.closest(".toastui-editor-main");
      if (!container) return true;
      const richText = editor.classList.contains("toastui-editor-contents");
      return richText ? container.classList.contains("toastui-editor-ww-mode")
        : !container.classList.contains("toastui-editor-ww-mode");
    });
    if (activeEditors.length === 1 && typeof document.execCommand === "function") {
      const editor = activeEditors[0];
      const richText = editor.classList.contains("toastui-editor-contents");
      const formatted = richText ? richDescription(payload.description) : null;
      const expected = formatted ? editorText(formatted) : payload.description;
      const expectedBold = formatted ? [...formatted.querySelectorAll("strong")].map(node => node.textContent) : [];
      const expectedItalic = formatted ? [...formatted.querySelectorAll("em")].map(node => node.textContent) : [];
      const expectedLists = formatted ? [...formatted.querySelectorAll("ul, ol")].map(node => ({
        type: node.localName, text: editorText(node)
      })) : [];
      const expectedLinks = formatted ? [...formatted.querySelectorAll("a")].map(node => ({
        text: node.textContent, href: node.getAttribute("href")
      })) : [];
      editor.focus();
      const range = document.createRange();
      range.selectNodeContents(editor);
      const selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      if (!document.execCommand(richText ? "insertHTML" : "insertText", false, formatted ? formatted.innerHTML : payload.description)) {
        report.errors.push("Description: browser editor command failed. Paste the prepared description manually.");
      } else {
        checks.push(() => {
          const normalise = value => value.replace(/\r\n/g, "\n").replace(/\u00a0/g, " ").trim();
          const actualBold = [...editor.querySelectorAll("strong, b")].map(node => node.textContent);
          if (editor.isConnected && visible(editor) && normalise(editorText(editor)) === normalise(expected) &&
              expectedBold.every(text => actualBold.includes(text)) &&
              expectedItalic.every(text => [...editor.querySelectorAll("em, i")].some(node => node.textContent === text)) &&
              expectedLists.every(list => [...editor.querySelectorAll(list.type)].some(node => editorText(node) === list.text)) &&
              expectedLinks.every(link => [...editor.querySelectorAll("a")].some(node =>
                node.textContent === link.text && node.getAttribute("href") === link.href)))
            report.filled.push(richText ? "Description (formatted rich text)" : "Description (Markdown)");
          else report.errors.push("Description: editor did not retain the expected text or formatting. Review and correct it manually.");
        });
        if (richText)
          report.manual.push("Description: review bold headings, italic text, lists, links and paragraph spacing before saving. Advanced Markdown formatting is not converted.");
      }
    } else {
      report.manual.push("Description: no unique active editor was found. Paste the prepared description manually.");
    }
    if (payload.format !== "InPerson" && /^https?:\/\//i.test(payload.location ?? ""))
      input("input#onlineEventLink", payload.location, "Online meeting link");
    else report.manual.push("Online meeting link: check the correct event format and enter a valid link if applicable.");

    report.manual.push(`Start date: ${payload.startDate}; time zone: Europe/London (${payload.timeZoneLabel}). Check the date picker manually.`);
    report.manual.push(`End: ${payload.endDate} ${payload.endTime}; duration: ${payload.durationMinutes} minutes. Set the duration/end controls manually.`);
    report.manual.push(`Venue/format: ${payload.format}; ${payload.venueName ?? ""}; ${payload.location ?? ""}. Select and check the venue manually; arrival instructions are not the venue.`);
    report.manual.push(payload.operation === "create"
      ? "Event image: download the PNG and use the new-event image Select button to choose it manually. Review the crop and apply it yourself."
      : "Event image: open the image edit menu and choose Replace (not Remove), then use Send PNG to open photo chooser in the extension. Alternatively download the PNG and use Choose photo. Review the crop and apply it yourself.");
    await new Promise(resolve => setTimeout(resolve, 350));
    if (location.href !== initialUrl || !form.isConnected)
      return fail("Meetup navigated or replaced the form during filling. Review all fields; the result is not confirmed.");
    for (const check of checks) check();
  } catch (error) {
    report.errors.push(`Form filling stopped: ${error.message}. Some earlier fields may have changed; review them manually.`);
  }
  return report;
}
