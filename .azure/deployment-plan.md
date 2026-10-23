# Azure Deployment Plan

**Status:** Local implementation and testing; Azure deployment deferred.

## Implementation request

Implement the real local review flow behind disabled-by-default configuration,
without assuming permission to access a live account or provision Azure resources.
Use the existing authenticated web app as the owner of browser sessions. Local
workers bind to loopback and are reachable only through an organiser-authorised
proxy; browser credentials never enter an API request or log. Manual Save/Publish
remains unchanged. Azure preparation must not imply that local process launching is
a deployable Container Apps allocator. Subscription/region and live validation are
still unconfirmed.

## Goal and scope

Add on-demand, any-device Meetup browser preparation without changing the existing
App Service hosting. The approved session plan is the functional design. The first
deliverable is an offline-only interactive browser prototype using synthetic content.
Real-account testing is performed manually by the organiser in explicitly enabled
local live mode. No Azure resource creation or deployment is authorised by this file.

## Candidate architecture

- Existing App Service: authenticated organiser control plane and review UI.
- Container Apps Consumption: candidate for custom isolated worker allocation,
  not a selected or implemented hosting model. Microsoft's custom-container
  session pools use Dedicated compute, not Consumption; see
  `docs\HOSTED-BROWSER-AZURE.md` before choosing a cloud path.
- Existing SQL: ownership and lease metadata, not browser credentials.
- Private Blob Storage and Key Vault: optional encrypted authentication state after
  separate live restore/expiry/deletion feasibility checks. Remember-login defaults off.
- Modular Bicep: follow existing infrastructure layout after feasibility.

## Gates

1. Offline interaction, origin/authentication checks, exclusive control and cleanup.
2. Permitted use and authorised live-login/mobile review.
3. Consumption-compatible Chromium sandbox, worker allocation/routing and reconnect.
4. Subscription, region, quotas, budget, retention and network-isolation decisions.
5. Production implementation, validation, then separately approved deployment.

## Current constraints

Docker Desktop's Linux engine has been started successfully. Local Linux container
verification now passes with a non-root user and Chromium sandboxing enabled.
That does not validate Azure Consumption sandboxing or its session routing.
Subscription and region have not been selected. No resources are being provisioned;
quota checks and a complete resource inventory are deferred until that scope is approved.

## Feasibility implementation progress

Implemented `tools/hosted-browser-prototype`: a standalone, loopback-only Node.js
viewer and a fresh Chromium context showing intercepted synthetic form content.
The viewer exchanges a single-use fragment token for a local HttpOnly cookie, checks
host and origin, permits only one controller, streams in-memory frames and supports
manual input, scrolling and reconnect. It reuses the existing guarded form adapter.
Standalone mode remains offline. App-managed local mode now supports an explicitly
enabled real Meetup connection and owner-scoped encrypted cookies; no cloud
endpoint is implemented.

Six real-Chromium tests passed, including responsive viewer/mobile emulation,
authentication boundaries, isolation, expiry and manually simulated saving.
The existing extension's 75 tests passed. A headed-browser smoke check also confirmed
that the local viewer is responsive; all verification processes were closed.

This does not establish physical-device usability, Consumption sandboxing,
worker routing/leases or live Meetup login. Those gates remain blocked
on approved Azure/live-test context.
Do not mark this plan Ready for Validation or deploy the loopback prototype.

### Local app integration

The Development-only feature is disabled by default. `scripts\start-local-browser-review.ps1`
starts the existing app with an offline worker; `-LiveMeetup` explicitly enables
manual real-account login and filling. The authenticated app owns launch, HTTP and
WebSocket proxying, exact organiser ownership, origin/antiforgery checks, one
controller and explicit cleanup. It passes the current prepared event payload to a
separate local Node/Chrome process rather than regenerating saved event content.

The viewer supports manual login/review, guarded field filling, separate event and
speaker photo actions, reconnect and end. Save/Publish, venue result, time zone,
duration/end and crop/apply remain manual. External identity-provider login/popup
flows are not supported by this local version.

Remembered login is optional/default-off: save only Meetup cookies, encrypted with
owner-scoped ASP.NET Data Protection on the local computer, expiring after seven
days. It is not a cloud persistence implementation and does not guarantee live
Meetup login restoration. No live account, real upload or real publication was
performed during implementation. The organiser must run that final live workflow
before deciding whether to proceed to Azure.

### Linux-container follow-up

Added a test-only version-pinned Dockerfile, a narrowly allowlisted build context,
and a Linux headed-Chromium sandbox assertion. All seven Linux cases passed with
`--network none`, `--cap-drop ALL` and `no-new-privileges`, without custom seccomp
permissions, privileged mode or a disabled browser sandbox. Fixed viewer-test
teardown to model a separate device/browser and made prototype shutdown awaitable.
Local Windows cases still pass (the Linux-only assertion is skipped on Windows).
The earlier local Docker blocker is resolved; the remaining blockers are Azure
test/deployment context and cost approval, Consumption-specific behavior, and
permitted-use/authorised live Meetup review. No Azure or live-site actions were taken.
