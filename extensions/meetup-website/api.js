import { trustedOrigin } from "./handoff.js";

export function permissionPattern(origin) {
  const url = new URL(trustedOrigin(origin));
  return `${url.protocol}//${url.hostname}/*`;
}

export async function requestApi(origin, path, options = {}) {
  trustedOrigin(origin);
  if (!path.startsWith("/api/extension/")) throw new Error("Unsupported API path.");
  const response = await fetch(origin + path, {
    ...options, credentials: "omit", redirect: "error", cache: "no-store", referrerPolicy: "no-referrer"
  });
  if (response.status === 401) {
    const error = new Error("API access expired or was revoked. Connect again.");
    error.status = 401;
    throw error;
  }
  if (response.status === 429) throw new Error("Too many connection requests. Wait a minute before retrying.");
  if (!response.ok) {
    let message = `API request failed (${response.status}) at ${new URL(path, origin).pathname}.`;
    let hasDetails = false;
    try {
      const problem = await response.json();
      message = problem.error ?? problem.detail ?? problem.title ?? message;
      hasDetails = Boolean(problem.error ?? problem.detail ?? problem.title);
    } catch { /* Keep the explicit HTTP error for non-JSON responses. */ }
    if (response.status === 400 && !hasDetails) message += " Check the app is running the latest version and the app address/port is correct.";
    throw new Error(message);
  }
  if (response.status === 204) return null;
  return response.json();
}

export function encodeUrl(bytes) {
  return btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}
