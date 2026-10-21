import test from "node:test";
import assert from "node:assert/strict";
import { trustedOrigin, validatePayload, meetupTarget } from "../handoff.js";
import { payload } from "./fixture.js";

test("validates UK schedule including summer midnight rollover", () => {
  assert.equal(validatePayload(payload()).title, "New title");
  assert.doesNotThrow(() => validatePayload(payload({
    startUtc: "2027-06-01T23:30:00Z", startDate: "2027-06-02", startTime: "00:30",
    endDate: "2027-06-02", endTime: "02:00", timeZoneLabel: "BST"
  })));
});
test("rejects malformed, expired, oversized and inconsistent content", () => {
  for (const overrides of [
    { version: 2 }, { expiresAtUtc: "2020-01-01" }, { groupSlug: "../evil" },
    { title: "a".repeat(251) }, { description: "" }, { startTime: "25:30" },
    { timeZone: "UTC" }, { timeZoneLabel: "BST" }, { startDate: "2027-01-26" }, { endTime: "22:00" },
    { durationMinutes: -1 }, { imageBase64: "SGVsbG8=" }, { format: "Other" },
    { eventUrl: "https://evil.test/test-group/events/123/" },
    { eventUrl: "https://www.meetup.com/test-group/events/create/" }
  ]) assert.throws(() => validatePayload(payload(overrides)), JSON.stringify(overrides));
});
test("drops unexpected fields rather than storing private extras", () => {
  const validated = validatePayload(payload({ privateProfile: "not part of the public handoff" }));
  assert.equal(validated.privateProfile, undefined);
});
test("speaker payload is bounded, optional and strips private extras", () => {
  const speaker = { name: "Alex", bio: "**Bio**", twitter: "@alex", email: "private@example.test" };
  const validated = validatePayload(payload({ speaker, speakerCount: 2 }));
  assert.equal(validated.speaker.name, "Alex");
  assert.equal(validated.speakerCount, 2);
  assert.equal(validated.speaker.email, undefined);
  assert.equal(validatePayload(payload()).speaker, undefined);
  for (const changes of [
    { speaker: { ...speaker, name: "" }, speakerCount: 1 },
    { speaker: { ...speaker, bio: "a".repeat(60001) }, speakerCount: 1 },
    { speaker: { ...speaker, website: 123 }, speakerCount: 1 },
    { speaker, speakerCount: 0 }, { speaker: null, speakerCount: 2 }
  ]) assert.throws(() => validatePayload(payload(changes)));
});
test("accepts HTTPS app and loopback only, with no ambiguous origin paths", () => {
  assert.equal(trustedOrigin("https://organiser.example"), "https://organiser.example");
  assert.equal(trustedOrigin("http://localhost:5050"), "http://localhost:5050");
  for (const origin of ["http://public.example", "https://meetup.com", "https://app.example/path",
    "https://user@app.example", "https://app.example/?token=secret"]) assert.throws(() => trustedOrigin(origin));
});
test("speaker photo is bounded and allowlisted independently from event PNG", () => {
  const speaker = { name: "Alex", bio: "Bio", photoBase64: payload().imageBase64, photoError: null };
  assert.equal(validatePayload(payload({ speaker, speakerCount: 1 })).speaker.photoBase64, speaker.photoBase64);
  for (const invalid of ["SGVsbG8=", 123, "a".repeat(6990509)]) {
    assert.throws(() => validatePayload(payload({ speaker: { ...speaker, photoBase64: invalid }, speakerCount: 1 })));
  }
  const largePng = Buffer.concat([Buffer.from("\x89PNG\r\n\x1a\n", "latin1"), Buffer.alloc(3300000)]).toString("base64");
  assert.throws(() => validatePayload(payload({
    imageBase64: largePng, speaker: { ...speaker, photoBase64: largePng }, speakerCount: 1
  })), /too large for extension storage/);
});

test("separate online URL survives validation without replacing physical location", () => {
  const validated = validatePayload(payload({ onlineEventUrl: "https://zoom.us/saved", location: "Town centre" }));
  assert.equal(validated.onlineEventUrl, "https://zoom.us/saved");
  assert.equal(validated.location, "Town centre");
  assert.throws(() => validatePayload(payload({ onlineEventUrl: 123 })));
});
test("requires exact group and event identity before editing", () => {
  const data = payload();
  assert.doesNotThrow(() => meetupTarget("https://www.meetup.com/test-group/events/123/edit/", data, true));
  for (const url of [
    "https://www.meetup.com/test-group/events/456/edit/",
    "https://www.meetup.com/other-group/events/123/edit/",
    "https://www.meetup.com.evil.test/test-group/events/123/edit/",
    "https://www.meetup.com/test-group/events/123/",
    "https://www.meetup.com/test-group/events/123/edit/?next=evil"
  ]) assert.throws(() => meetupTarget(url, data, true));
});
test("accepts evidenced group schedule route only for new-event handoffs", () => {
  const data = payload({ operation: "create", eventUrl: null });
  for (const route of ["/test-group/schedule/", "/test-group/schedule/#", "/test-group/events/create/"])
    assert.doesNotThrow(() => meetupTarget(`https://www.meetup.com${route}`, data, true));
  for (const route of ["/other-group/schedule/", "/test-group/schedule/123/", "/test-group/schedule/?id=123",
    "/test-group/events/123/edit/"])
    assert.throws(() => meetupTarget(`https://www.meetup.com${route}`, data, true));
  assert.throws(() => meetupTarget("https://www.meetup.com/test-group/schedule/", payload(), true));
});
