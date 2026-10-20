export async function savedGrant(storage, now = Date.now()) {
  // Upgrade v0.2's session-only credential without extending its original expiry.
  const [local, session] = await Promise.all([storage.local.get("apiGrant"), storage.session.get("apiGrant")]);
  const grant = local.apiGrant ?? session.apiGrant;
  if (!grant || !Number.isFinite(Date.parse(grant.expiresAt)) || Date.parse(grant.expiresAt) <= now) {
    await storage.local.remove("apiGrant");
    await storage.session.remove("apiGrant");
    return null;
  }
  if (!local.apiGrant) await storage.local.set({ apiGrant: grant });
  await storage.session.remove("apiGrant");
  return grant;
}

export async function clearConnection(storage) {
  await storage.local.remove("apiGrant");
  await storage.session.remove(["apiGrant", "apiPairing", "pending"]);
}
