export function trustedOrigin(value) {
  const url = new URL(value);
  if (url.username || url.password || url.pathname !== "/" || url.search || url.hash ||
      !(url.protocol === "https:" || (url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname))) ||
      ["meetup.com", "www.meetup.com"].includes(url.hostname)) {
    throw new Error("Use your app's HTTPS origin (or HTTP localhost), without a path.");
  }
  return url.origin;
}

export function meetupTarget(value, payload, editPage = false) {
  const url = new URL(value);
  if (url.protocol !== "https:" || !["www.meetup.com", "meetup.com"].includes(url.hostname) ||
      url.username || url.password || url.port || url.hash || url.search) {
    throw new Error("Select an HTTPS Meetup event page without query parameters or fragments.");
  }
  const parts = url.pathname.split("/").filter(Boolean);
  if (parts[0]?.toLowerCase() !== payload.groupSlug.toLowerCase()) throw new Error("The Meetup group does not match the prepared group.");
  if (payload.operation === "update") {
    const expected = new URL(payload.eventUrl).pathname.split("/").filter(Boolean);
    if (parts[1] !== "events" || !/^[0-9]+$/.test(parts[2] ?? "") || parts[2] !== expected[2] ||
        (editPage ? parts.length !== 4 || parts[3] !== "edit" : parts.length !== 3)) {
      throw new Error("Open the edit form for the exact prepared Meetup event.");
    }
  } else if (editPage && !((parts.length === 3 && parts[1] === "events" && parts[2] === "create") ||
      (parts.length === 2 && parts[1] === "schedule"))) {
    throw new Error("Open this group's new-event form. Other routes are not supported; use the manual copy fields instead.");
  }
  return url;
}

export function validatePayload(data, now = Date.now()) {
  if (!data || data.version !== 1 || !/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(data.sourceEventId ?? "") ||
      !/^[A-Za-z0-9][A-Za-z0-9_-]{0,199}$/.test(data.groupSlug ?? "") ||
      !["create", "update"].includes(data.operation) ||
      typeof data.title !== "string" || !data.title.trim() || data.title.length > 250 ||
      typeof data.description !== "string" || !data.description.trim() || data.description.length > 60000 ||
      data.timeZone !== "Europe/London" || !["GMT", "BST"].includes(data.timeZoneLabel) ||
      !Number.isInteger(data.durationMinutes) || data.durationMinutes < 1 || data.durationMinutes > 10080 ||
      !["Online", "InPerson", "Hybrid"].includes(data.format) ||
      !Number.isFinite(Date.parse(data.expiresAtUtc)) || Date.parse(data.expiresAtUtc) <= now ||
      Date.parse(data.expiresAtUtc) > now + 31 * 60 * 1000 ||
      typeof data.startUtc !== "string" || !Number.isFinite(Date.parse(data.startUtc)) || !data.startUtc.endsWith("Z")) {
    throw new Error("Prepared content is invalid, unsupported or expired. Prepare it again in your app.");
  }
  for (const key of ["startDate", "endDate"]) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(data[key] ?? "")) throw new Error("Invalid prepared date.");
  }
  for (const key of ["startTime", "endTime"]) {
    if (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(data[key] ?? "")) throw new Error("Invalid prepared time.");
  }
  for (const key of ["location", "venueName"]) {
    if (data[key] != null && (typeof data[key] !== "string" || data[key].length > 2000)) throw new Error("Invalid location.");
  }
  if (typeof data.imageBase64 !== "string" || data.imageBase64.length > 6990508 ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(data.imageBase64)) throw new Error("Invalid PNG image.");
  let bytes;
  try { bytes = atob(data.imageBase64); } catch { throw new Error("Invalid PNG encoding."); }
  if (bytes.length > 5242880 || !bytes.startsWith("\x89PNG\r\n\x1a\n")) throw new Error("Invalid PNG image.");
  const format = date => new Intl.DateTimeFormat("en-GB", {
    timeZone: "Europe/London", year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", hourCycle: "h23"
  }).formatToParts(date).reduce((parts, part) => ({ ...parts, [part.type]: part.value }), {});
  for (const [prefix, instant] of [
    ["start", new Date(data.startUtc)],
    ["end", new Date(Date.parse(data.startUtc) + data.durationMinutes * 60000)]
  ]) {
    const parts = format(instant);
    if (data[`${prefix}Date`] !== `${parts.year}-${parts.month}-${parts.day}` ||
        data[`${prefix}Time`] !== `${parts.hour}:${parts.minute}`) throw new Error("Prepared UK dates do not match the UTC schedule.");
    if (prefix === "start") {
      const offset = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute)) -
        Math.floor(instant.getTime() / 60000) * 60000;
      if (data.timeZoneLabel !== (offset === 3600000 ? "BST" : "GMT")) throw new Error("Prepared GMT/BST label does not match the event date.");
    }
  }
  if (data.operation === "update") meetupTarget(data.eventUrl, data);
  else if (data.eventUrl != null) throw new Error("A new-event handoff must not target an existing event.");
  const keys = ["version", "sourceEventId", "groupSlug", "operation", "eventUrl", "title", "description", "expiresAtUtc",
    "startUtc", "startDate", "startTime", "endDate", "endTime", "timeZone", "timeZoneLabel", "durationMinutes",
    "format", "venueName", "location", "imageBase64"];
  return Object.fromEntries(keys.map(key => [key, data[key]]));
}

// Runs in the tab's isolated world; keep this function self-contained.
export function capturePreparedContent() {
  const element = document.querySelector("textarea#meetup-organiser-handoff");
  if (!element || !element.value) throw new Error("Click Prepare for Meetup in the app first.");
  if (element.value.length > 7100000) throw new Error("Prepared content is too large.");
  return { origin: location.origin, payload: JSON.parse(element.value) };
}
