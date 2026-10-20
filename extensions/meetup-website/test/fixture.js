export function payload(overrides = {}) {
  return {
    version: 1, sourceEventId: "11111111-1111-1111-1111-111111111111", groupSlug: "test-group",
    operation: "update", eventUrl: "https://www.meetup.com/test-group/events/123/",
    title: "New title", description: "**New description**\n\nAnother paragraph",
    expiresAtUtc: new Date(Date.now() + 20 * 60000).toISOString(),
    startUtc: "2027-01-25T18:30:00.0000000Z",
    startDate: "2027-01-25", startTime: "18:30", endDate: "2027-01-25", endTime: "20:00",
    timeZone: "Europe/London", timeZoneLabel: "GMT", durationMinutes: 90, format: "Hybrid",
    venueName: "Hall", location: "https://zoom.us/example",
    imageBase64: Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 0]).toString("base64"), ...overrides
  };
}
