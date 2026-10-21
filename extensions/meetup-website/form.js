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
  if (consent.photo || consent.speakerPhoto) {
    const speakerPhoto = Boolean(consent.speakerPhoto);
    const imageBase64 = speakerPhoto ? payload.speaker?.photoBase64 : payload.imageBase64;
    if (!imageBase64) return fail(speakerPhoto
      ? `Speaker photo: ${payload.speaker?.photoError ?? "No speaker photo is prepared. Update the app and load/capture fresh event content."}`
      : "No event PNG is prepared.");
    if (speakerPhoto && !document.querySelector('main#event-schedule-main form input[id="speaker.name"]'))
      return fail("Enable the Speakers section and open its Upload Photo dialog before sending the speaker photo.");
    const dialogs = [...document.querySelectorAll('[role="dialog"][data-slot="modal-content"]')].filter(visible)
      .filter(dialog => [...dialog.querySelectorAll("button")].some(button => button.textContent.trim() === "Choose photo"))
      .filter(dialog => !speakerPhoto || dialog.querySelector("#photo-uploader-drop-area"));
    if (dialogs.length !== 1) return fail(speakerPhoto
      ? "Click Upload Photo in the Speakers section and leave its Choose photo dialog open first."
      : payload.operation === "create"
      ? "No supported photo chooser is open. On a new event, download the PNG and use the image Select button to choose it manually."
      : "Open the event image menu, choose Replace, and leave the Choose photo dialog open first.");
    const inputs = dialogs[0].querySelectorAll('input[type="file"][accept="image/*"]');
    if (inputs.length !== 1 || inputs[0].disabled || typeof DataTransfer !== "function")
      return fail("Supported photo chooser not found. Download the PNG and use Choose photo manually.");
    try {
      const binary = atob(imageBase64);
      if (binary.length > 5242880 || !binary.startsWith("\x89PNG\r\n\x1a\n"))
        return fail("Prepared photo is not a supported PNG under 5 MB. Load fresh content.");
      const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
      const transfer = new DataTransfer();
      transfer.items.add(new File([bytes], speakerPhoto ? "speaker-photo.png" : "meetup-social.png", { type: "image/png" }));
      inputs[0].files = transfer.files;
      inputs[0].dispatchEvent(new Event("input", { bubbles: true }));
      inputs[0].dispatchEvent(new Event("change", { bubbles: true }));
      report.filled.push(speakerPhoto ? "Speaker photo supplied to the photo chooser (upload/crop not confirmed)"
        : "PNG supplied to the photo chooser (upload/crop not confirmed)");
      report.manual.push(speakerPhoto
        ? `Review the uploaded photo for ${payload.speaker.name}, crop and apply it yourself. Confirm it appears under Speakers, not as the event image. For multiple speakers, handle remaining photos manually.`
        : "Review the uploaded image and crop/resize dialog, apply the image yourself, and check it appears on the event before saving. Check for upload errors.");
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
  function fillEditor(root, text, label, excludeSpeaker = false) {
    const editors = [...root.querySelectorAll(".ProseMirror[contenteditable=true]")].filter(visible)
      .filter(editor => !excludeSpeaker || !editor.closest('[id="speaker.bio"], #speaker'));
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
      const formatted = richText ? richDescription(text) : null;
      const expected = formatted ? editorText(formatted) : text;
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
      if (!document.execCommand(richText ? "insertHTML" : "insertText", false, formatted ? formatted.innerHTML : text)) {
        report.errors.push(`${label}: browser editor command failed. Paste the prepared text manually.`);
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
            report.filled.push(`${label} (${richText ? "formatted rich text" : "Markdown"})`);
          else report.errors.push(`${label}: editor did not retain the expected text or formatting. Review and correct it manually.`);
        });
        if (richText)
          report.manual.push(`${label}: review bold headings, italic text, lists, links and paragraph spacing before saving. Advanced Markdown formatting is not converted.`);
      }
    } else {
      report.manual.push(`${label}: no unique active editor was found. Paste the prepared text manually.`);
    }
  }
  async function fillSpeaker() {
    if (!payload.speaker) {
      report.manual.push("Speaker profile/socials: no speaker details are present in this handoff. If the event has a speaker, update the app and load/capture fresh event content, then fill again.");
      return;
    }
    const speaker = payload.speaker;
    if (payload.speakerCount > 1)
      report.manual.push("Speakers: only the first speaker in session order is filled. Add the second/remaining speakers manually.");
    let name = form.querySelector('input[id="speaker.name"]');
    const candidates = [...form.querySelectorAll('button[role="switch"]')].filter(visible).filter(toggle => {
      let section = toggle.parentElement;
      while (section && section !== form) {
        if (section.querySelector('button[aria-label="Speakers"]'))
          return section.querySelectorAll('button[role="switch"]').length === 1;
        section = section.parentElement;
      }
      return false;
    });
    if (candidates.length === 1 && candidates[0].getAttribute("aria-checked") === "false" &&
        !candidates[0].disabled && candidates[0].getAttribute("aria-disabled") !== "true") {
      candidates[0].click();
      for (let attempt = 0; attempt < 20; attempt++) {
        if (location.href !== initialUrl || !form.isConnected) return;
        name = form.querySelector('input[id="speaker.name"]');
        if (visible(name) && candidates[0].getAttribute("aria-checked") === "true") break;
        await new Promise(resolve => setTimeout(resolve, 100));
      }
      const toggle = candidates[0];
      checks.push(() => {
        if (toggle.isConnected && toggle.getAttribute("aria-checked") === "true") report.filled.push("Speakers enabled");
        else report.errors.push("Speakers: Meetup did not retain the enabled switch. Enable it and check the profile manually.");
      });
    }
    name = form.querySelector('input[id="speaker.name"]');
    if (!visible(name)) {
      report.manual.push("Speakers: enable the Speakers switch manually, then click Fill Meetup Fields again. No speaker fields were changed.");
      return;
    }
    if ([...form.querySelectorAll('input[id="speaker.name"]')].filter(visible).length !== 1) {
      report.manual.push("Speakers: more than one speaker name control was found. Complete speaker profiles manually.");
      return;
    }
    input('input[id="speaker.name"]', speaker.name, "Speaker name");
    if (speaker.bio.trim()) {
      const bios = [...form.querySelectorAll('[id="speaker.bio"]')].filter(visible);
      if (bios.length === 1) fillEditor(bios[0], speaker.bio, "Speaker bio");
      else report.manual.push("Speaker bio: supported editor not found. Enter it manually.");
    } else report.manual.push("Speaker bio: no prepared bio is available. Complete the required bio manually.");
    for (const [key, id, label] of [
      ["facebook", "facebook", "Facebook"], ["twitter", "twitter", "X"],
      ["linkedIn", "linkedin", "LinkedIn"], ["website", "other", "Other"]
    ]) {
      const value = speaker[key];
      if (!value?.trim()) {
        report.manual.push(`Speaker ${label}: no saved value was supplied. Existing Meetup content was left unchanged.`);
        continue;
      }
      let link = value.trim();
      if (key === "twitter" && /^@?[A-Za-z0-9_]{1,15}$/.test(link)) link = `https://x.com/${link.replace(/^@/, "")}`;
      try {
        const parsed = new URL(link);
        if (!["https:", "http:"].includes(parsed.protocol) || parsed.username || parsed.password) throw new Error("Invalid URL");
      } catch {
        report.manual.push(`Speaker ${label}: no valid HTTP(S) link is available. Check it manually.`);
        continue;
      }
      input(`input[id="speaker.${id}"]`, link, `Speaker ${label}`);
    }
    report.manual.push(payload.speaker.photoBase64
      ? "Speaker photo: click Upload Photo under Speakers, then use Send speaker photo to open chooser in the extension. Review/crop/apply it yourself."
      : `Speaker photo: ${payload.speaker.photoError ?? "no prepared photo is available; upload it manually or load fresh content."}`);
    report.manual.push("Review all speaker fields, including existing links without prepared replacements (Instagram is not stored in the app).");
  }
  async function fillStartDate() {
    const selector = 'button[aria-label="Open date picker"][aria-haspopup="dialog"]';
    const triggers = [...form.querySelectorAll(selector)].filter(visible);
    if (triggers.length === 0) {
      report.manual.push(`Start date: set ${payload.startDate} manually; the supported date picker was not found.`);
      return true;
    }
    const problem = message => {
      report.errors.push(`Start date: ${message} Check ${payload.startDate} manually before setting the time.`);
      return false;
    };
    if (triggers.length !== 1 || triggers[0].disabled || triggers[0].getAttribute("aria-disabled") === "true")
      return problem("The date picker is ambiguous or disabled.");
    const trigger = () => {
      const current = [...form.querySelectorAll(selector)].filter(visible);
      return location.href === initialUrl && form.isConnected && current.length === 1 ? current[0] : null;
    };
    function calendar() {
      const control = trigger();
      const dialog = document.getElementById(control?.getAttribute("aria-controls"));
      if (!control || control.getAttribute("aria-expanded") !== "true" || !visible(dialog) ||
          dialog.getAttribute("role") !== "dialog") return null;
      const grids = [...dialog.querySelectorAll('table[role="grid"]')].filter(visible);
      if (grids.length !== 1) return null;
      const match = /^(January|February|March|April|May|June|July|August|September|October|November|December) (\d{4})$/
        .exec(grids[0].getAttribute("aria-label") ?? "");
      if (!match) return null;
      const month = ["January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"].indexOf(match[1]);
      return { dialog, grid: grids[0], month: Number(match[2]) * 12 + month };
    }
    async function waitFor(predicate) {
      for (let attempt = 0; attempt < 20; attempt++) {
        if (!trigger()) return null;
        const current = calendar();
        if (current && predicate(current)) return current;
        await new Promise(resolve => setTimeout(resolve, 50));
      }
      return null;
    }
    const originallyOpen = triggers[0].getAttribute("aria-expanded") === "true";
    try {
      if (!originallyOpen) triggers[0].click();
      let current = await waitFor(() => true);
      if (!current) return problem("The supported calendar did not open.");
      const [year, month] = payload.startDate.split("-").map(Number);
      const targetMonth = year * 12 + month - 1;
      if (Math.abs(targetMonth - current.month) > 120)
        return problem("The requested date is more than 120 months from the displayed month.");
      for (let step = 0; current.month !== targetMonth && step < 120; step++) {
        const direction = Math.sign(targetMonth - current.month);
        const buttons = [...current.dialog.querySelectorAll(
          `button[aria-label="Go to the ${direction > 0 ? "Next" : "Previous"} Month"]`)].filter(visible);
        if (buttons.length !== 1 || buttons[0].disabled || buttons[0].matches('[aria-disabled="true"], [data-disabled="true"]'))
          return problem("Month navigation is unavailable or disabled.");
        const expectedMonth = current.month + direction;
        buttons[0].click();
        current = await waitFor(value => value.month === expectedMonth);
        if (!current) return problem("Meetup did not confirm the requested calendar month.");
      }
      const cells = [...current.grid.querySelectorAll(`td[role="gridcell"][data-day="${payload.startDate}"]`)].filter(visible);
      const days = cells.length === 1 ? [...cells[0].querySelectorAll("button")].filter(visible) : [];
      if (days.length !== 1 || cells[0].matches('[data-disabled="true"], [data-outside="true"]') ||
          days[0].disabled || days[0].matches('[aria-disabled="true"], [data-disabled="true"]'))
        return problem("The requested day is unavailable or disabled.");
      if (cells[0].getAttribute("aria-selected") !== "true") days[0].click();
      await new Promise(resolve => setTimeout(resolve, 350));
      const control = trigger();
      if (!control) return problem("The page changed while selecting the date.");
      if (control.getAttribute("aria-expanded") !== "true") control.click();
      const verified = await waitFor(value => {
        const selected = value.grid.querySelectorAll('td[role="gridcell"][aria-selected="true"]');
        return selected.length === 1 && selected[0].getAttribute("data-day") === payload.startDate;
      });
      if (!verified) return problem("Meetup did not retain the full selected date.");
      report.filled.push(`Start date: ${payload.startDate}`);
      return true;
    } finally {
      const control = trigger();
      if (!originallyOpen && control?.getAttribute("aria-expanded") === "true") control.click();
    }
  }
  async function fillJoiningOptions() {
    const labels = { InPerson: "In person", Online: "Online", Hybrid: "Hybrid" };
    const target = labels[payload.format];
    if (!target) {
      report.errors.push("Event format: unsupported prepared format. Complete joining options manually.");
      return;
    }
    function tabGroups() {
      return [...form.querySelectorAll("div")].filter(visible).filter(node => {
        const children = [...node.children];
        return children.length === 3 && children.every((child, index) =>
          child.tagName === "DIV" && visible(child) && child.textContent.trim() === Object.values(labels)[index] &&
          child.querySelector("span"));
      });
    }
    function selected(group) {
      const active = [...group.children].filter(tab =>
        [...tab.children].some(child => child.classList.contains("bottom-0") &&
          child.classList.contains("bg-ds2-icon-fill-primary-enabled")));
      return active.length === 1 ? active[0].textContent.trim() : null;
    }
    const groups = tabGroups();
    if (groups.length > 1) {
      report.errors.push("Event format: multiple joining-option selectors found. Nothing in joining options was changed.");
      return;
    }
    if (groups.length === 1) {
      const group = groups[0];
      const current = selected(group);
      if (!current) {
        report.errors.push("Event format: current selection could not be verified. Set joining options manually.");
        return;
      }
      const tab = [...group.children].find(child => child.textContent.trim() === target);
      const lockedHybrid = current === "Hybrid" && [...form.querySelectorAll("h2")].some(
        heading => visible(heading) && heading.textContent.trim() === "Hybrid events cannot be modified");
      if (current !== target) {
        if (lockedHybrid || tab.matches('[aria-disabled="true"], [data-disabled="true"], [disabled]') ||
            tab.closest('[inert], [aria-disabled="true"]')) {
          report.errors.push(`Event format: Meetup has locked joining options; cannot change ${current} to ${target}. Review the event manually; no event was deleted or recreated.`);
          return;
        }
        tab.click();
      }
      let ready = false;
      for (let attempt = 0; attempt < 20; attempt++) {
        if (location.href !== initialUrl || !form.isConnected) return;
        const currentGroups = tabGroups();
        const venue = [...form.querySelectorAll('#venue input[placeholder="Search or add location..."]')].filter(visible);
        const online = [...form.querySelectorAll("input#onlineEventLink")].filter(visible);
        if (currentGroups.length === 1 && selected(currentGroups[0]) === target &&
            (payload.format === "Online" || venue.length === 1) &&
            (payload.format === "InPerson" || online.length === 1)) {
          ready = true;
          break;
        }
        await new Promise(resolve => setTimeout(resolve, 50));
      }
      if (!ready) {
        report.errors.push("Event format: Meetup did not confirm the requested selection and controls. Complete joining options manually.");
        return;
      }
      checks.push(() => {
        const currentGroups = tabGroups();
        if (currentGroups.length === 1 && selected(currentGroups[0]) === target)
          report.filled.push(`Event format: ${target}`);
        else report.errors.push("Event format: Meetup did not retain the selection. Check joining options manually.");
      });
    } else {
      report.manual.push(`Event format: select ${target} manually; the supported format selector was not found.`);
    }
    if (payload.format !== "InPerson") {
      const link = payload.onlineEventUrl?.trim() || payload.location?.trim();
      try {
        const parsed = new URL(link);
        if (!["https:", "http:"].includes(parsed.protocol) || parsed.username || parsed.password)
          throw new Error("Invalid meeting URL");
        input("input#onlineEventLink", link, "Online meeting link");
      } catch {
        report.manual.push("Online meeting link: no valid saved HTTP(S) meeting URL is available. Enter it manually; existing content was preserved.");
      }
    }
    if (payload.format !== "Online") {
      const venue = payload.venueName?.trim() ||
        (!/^https?:\/\//i.test(payload.location ?? "") ? payload.location?.trim() : "");
      if (venue) input('#venue input[placeholder="Search or add location..."]', venue, "Venue search text (not a selected venue)");
      else report.manual.push("Venue: no saved venue search text is available. Search and select the location manually.");
      report.manual.push("Venue: choose the correct search result or add the location manually, then verify its address. Entering search text does not select or save a venue; arrival instructions are unchanged.");
    }
  }
  try {
    input('input#title[data-testid="event-name-input"]', payload.title, "Title");
    if (consent.schedule) {
      const dateReady = await fillStartDate();
      if (location.href !== initialUrl || !form.isConnected)
        return fail("Meetup navigated or replaced the form during date selection. Review all fields; the result is not confirmed.");
      if (dateReady)
        input('input[type="time"][aria-label="Edit start time"]', payload.startTime, "Start time");
      else report.manual.push(`Start time: set ${payload.startTime} ${payload.timeZoneLabel} after correcting the date.`);
    }
    else report.manual.push(`Start date/time: set ${payload.startDate} ${payload.startTime} ${payload.timeZoneLabel} after checking the Europe/London time zone.`);
    fillEditor(form, payload.description, "Description", true);
    await fillSpeaker();
    if (location.href !== initialUrl || !form.isConnected)
      return fail("Meetup navigated or replaced the form during filling. Review all fields; the result is not confirmed.");
    await fillJoiningOptions();

    report.manual.push(`Time zone: confirm Europe/London (${payload.timeZoneLabel}) manually. Date and time values do not change Meetup's time zone.`);
    report.manual.push(`End: ${payload.endDate} ${payload.endTime}; duration: ${payload.durationMinutes} minutes. Set the duration/end controls manually.`);
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
