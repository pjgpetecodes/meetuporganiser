# Hosted browser on Azure: feasibility and preparation

**Status:** Preparation findings only; no Azure resources were deployed or
validated. **Last checked:** 2026-10-23.

This document covers only hosted-browser compute and its secure connection to the
existing Meetup Organiser web app. It does not change the existing App Service,
database, authentication, or local worker implementation. The local Node worker
launcher is not an Azure worker allocator: the current App Service definition is
Windows (`reserved: false`), and local child-process startup cannot create an
independently isolated, remotely reachable browser session in that hosting model.

## Decision summary

Azure Container Apps is a plausible hosting family, but **the documented
custom-container session-pool option is not an ACA Consumption workload**. Current
Microsoft billing documentation says custom-container session pools use the
Dedicated plan and dedicated E16 compute, billed according to pool/active-session
compute. That may conflict with the required low-idle-cost goal. The separate
Consumption plan remains an unproven candidate for a custom worker design, not an
isolated per-organiser allocator.

In addition, there is no evidence here that Consumption can run Chromium with
`chromiumSandbox: true`, or that the Container Apps session-management proxy will
carry the browser review WebSocket upgrade end to end.

The most promising ACA *isolation* candidate to test is a **custom-container
session pool**, not an autoscaled shared browser replica. However, it does not
meet the documented Consumption-plan assumption:

- The documented model creates isolated custom-container sessions, routes requests
  by a session identifier, supports session lifecycle/idle cleanup, and allows the
  pool to disable outbound network access by default.
- Keep the session-pool endpoint, session identifier, and Entra bearer token on the
  server. The authenticated .NET app should own allocation and proxy review traffic;
  the end user's browser should only connect to an authenticated app endpoint.
- This is a Dedicated-plan candidate, not a claim that it is compatible with
  Playwright, Chromium sandboxing, WebSocket review, or the low-idle-cost objective.
- A normal Consumption container app can scale to zero, but it provides app
  replicas, not an automatically isolated worker per organiser. Scale-to-zero and
  sticky sessions alone do not allocate, own, or recover a browser session safely.

No standalone Bicep module is added. A safe module would prematurely choose among
Container Apps apps, custom-container session pools, or the newer ACA Sandboxes
resource, while the image, networking, region, identity wiring, and interactive
proxy path are unresolved. A static module cannot make the existing local-process
launcher work on App Service.

## Proposed request and trust flow

```text
Organiser browser
  └─ HTTPS / authenticated WebSocket ─> existing .NET App Service
       ├─ authenticate organiser; check owner, active lease, origin and control lock
       ├─ use App Service managed identity + pool-scoped Session Executor role
       │    └─ allocate / call one ACA custom-container session
       └─ proxy only the review protocol to that session
            └─ non-root Playwright + headed Chromium, ephemeral profile
```

Implementation guardrails:

1. Generate a cryptographically random ACA session identifier per active browser
   session and keep its owner mapping server-side. Do not derive or expose it from
   an organiser ID. Apply ownership and exclusive-controller checks on every
   launch, reconnect, input, and end operation.
2. The App Service obtains its Entra token for the documented ACA session API
   audience and holds it server-side. Give its managed identity only the
   `Azure ContainerApps Session Executor` role scoped to the pool. Never send the
   token or the ACA management endpoint to the client.
3. Keep the browser worker's port private to the pool's session routing. Do not
   configure public Container App ingress for CDP, Playwright's remote debugging
   endpoint, VNC, or a raw worker endpoint. The only browser-facing route is the
   authenticated, origin-checked App Service proxy.
4. Do not enable a worker managed identity for code running in the browser session.
   Microsoft warns that code in a session could obtain tokens for its pool identity.
   Use pool identity for image pull only where needed.
5. Keep each browser profile and temporary auth state session-local and ephemeral.
   Do not put credentials, cookies, screenshots, typed text, DOM, or page contents in
   app settings, logs, telemetry, or shared storage. Remembered login remains a
   separate opt-in design gate and is not required for this Azure feasibility test.
6. Start with the synthetic fixture and no browser egress. For an authorised live
   review only, enable the minimum egress that is experimentally shown to be needed.
   Pool configuration documents egress enabled/disabled; it does not by itself
   establish a Meetup-only domain allowlist. Design and test outbound filtering
   before allowing internet access.
7. Bound active sessions, idle lifetime, hard lifetime, and concurrent sessions.
   Treat platform restart, session expiry, or WebSocket loss as loss of unsaved form
   state. Reconnect must re-check ownership; it must never replay Save/Publish.

The ACA session management API documents HTTP request routing to the session
container and a sensitive session-identifier query parameter. Standard app ingress
and session-pool management are separate paths; the reviewed session-pool
documentation does **not** establish that WebSocket upgrades survive the
session-pool API and App Service proxy chain. That exact path is a pre-production
test gate.

## Verified findings and blockers

| Area | Documentation-backed finding | Result for this workload |
| --- | --- | --- |
| Per-session isolation | ACA custom-container sessions are described as separate Hyper-V sandboxes; session-pool requests allocate or route by an application-supplied identifier. | Promising fit for one browser session per organiser, provided the app securely owns unpredictable IDs and ownership mapping. |
| Lifecycle | A session is destroyed after its idle cooldown; the pool API can also stop a session. | Use explicit lease/end cleanup and verify the chosen cooldown and hard-expiry policy. Do not rely on an active WebSocket to preserve a session without testing. |
| Egress | Pool network status can disable or enable session egress; the documented default is disabled. | Normal Meetup login needs external network access. Enabling general egress increases risk; per-domain enforcement is unresolved. |
| Chromium sandbox | ACA documents that privileged containers are disallowed. Playwright's Docker guidance recommends a non-root user and a seccomp profile permitting user-namespace syscalls to run sandboxed Chromium. ACA documentation reviewed here does not establish whether its runtime permits the exact namespace/syscall behavior Chromium needs. | **Hard go/no-go test.** The local Linux Docker pass with `chromiumSandbox: true` is not Azure validation. Do not disable the Chromium sandbox or use privileged mode to work around a failure. |
| WebSocket review | ACA ingress and custom session management are separate paths. Session-pool docs describe requests forwarded to a target container port, but do not explicitly verify WebSocket upgrade behavior through the session management endpoint. | **Hard go/no-go test.** Verify the authenticated App Service → ACA session → worker browser-view WebSocket, reconnects, and timeouts. Do not expose CDP publicly. |
| App Service boundary | Existing `infra/modules/appservice.bicep` configures a Windows App Service. The local worker launcher is not a substitute for remote worker allocation. | Keep App Service as control plane and authenticated proxy; use a separate ACA worker path. Validate App Service WebSocket configuration and networking for the chosen proxy. |
| Identity | ACA session requests require an Entra token and `Azure ContainerApps Session Executor` authorization on the pool. Microsoft explicitly says session tokens must not be exposed to end users. | Use the web app's managed identity server-side with pool-scoped role assignment; confirm App Service can reach the selected management endpoint. |
| Cost and capacity | Microsoft billing docs classify custom-container session pools as Dedicated-plan workloads running on dedicated E16 compute; allocation depends on active and ready sessions. A regular Consumption app is a separate option and can scale to zero, but it doesn't itself provide per-organiser session isolation. | Custom session pools may fail the low-idle-cost requirement. Compare actual rates and idle/warm behavior for the selected region; don't represent the candidate as Consumption or zero-cost. |
| Current alternative | ACA documents a separate first-class Sandboxes resource for lifecycle-managed interactive isolated compute, with data-plane APIs for sandboxes, ports, and networking policies. | Track as an alternative; its plan/billing, region/API, and ingress fit for this browser and WebSocket flow remain unverified here. |

### Chromium sandbox interpretation

Do not infer either success or impossibility from the local result. Playwright's
published Docker instructions explain that Chromium runs unsandboxed as root and
recommend a non-root user plus a seccomp profile with user-namespace permissions
for sandboxed browsing. This repository's local container test reportedly passes
as non-root without added capabilities or a custom seccomp override, but Docker's
local runtime is not the ACA managed runtime. ACA explicitly prohibits privileged
containers; the reviewed ACA docs do not say that Chromium sandboxing is supported
or unsupported. The only safe conclusion is that a real, isolated ACA test is
required before choosing this platform.

## Required Azure components if the tests pass

1. **Existing Windows App Service** — authenticated control plane, owner/lease
   checks, lifecycle API, and WebSocket proxy. No local browser process in this
   hosting target.
2. **Container Apps environment and custom-container session pool** — pinned,
   maintained Linux worker image containing Node, Playwright, and headed Chromium;
   non-root process; no public app ingress; minimum practical session resources.
   Set session/network lifecycle explicitly.
3. **Container registry** — private worker image storage and managed-identity image
   pulls. Use immutable image tags/digests and patch the browser image regularly.
4. **Managed identity and RBAC** — App Service identity with pool-scoped `Azure
   ContainerApps Session Executor`; separate least-privilege image-pull identity
   as needed. Browser sessions receive no Azure identity.
5. **Outbound network controls** — only the access required for the browser flow,
   with an explicit decision on filtering and logging. Do not store login data in
   environment variables.
6. **Existing SQL and monitoring** — existing application persistence may hold
   opaque owner/session/lease metadata only. Telemetry should record lifecycle,
   errors and resource metrics, never browser content or credentials. No SQL or
   authentication change is proposed by this document.

Do not create a dedicated public worker domain, public CDP/VNC endpoint, or
subscription-wide identity role. Whether an internal environment / VNet route is
required depends on how App Service reaches the session API and how ACA egress is
controlled; the current infrastructure does not define that network.

## Rollout and verification gates

No step below authorises deployment. Any Azure test requires an explicitly
selected/approved subscription, region, test resource group, budget, and permitted
use. Do not access accounts or run Azure mutations as part of this preparation.

1. **Choose the target** — Decide whether Dedicated-plan custom-container session
   pools are acceptable despite the low-idle-cost requirement, or evaluate a
   Consumption app-per-session design / ACA Sandboxes / another isolated runtime.
   Confirm supported region, subscription quota, cost ceiling, pool warm count,
   per-session resource size, and whether an internal network path is required.
2. **Prove browser sandboxing** — Build a pinned Linux worker image as non-root.
   In an approved test pool, launch headed Chromium with `chromiumSandbox: true`.
   Assert the browser's sandbox is active; record browser version, image digest,
   resource limits, logs, and exit status. Do not use `--no-sandbox`, privileged
   containers, or extra capabilities as a fallback. Stop and reassess the hosting
   choice if the assertion fails.
3. **Prove isolation and network posture** — Use synthetic content only. Verify
   separate session IDs cannot view/control each other's browser or temporary
   profile. Begin with egress disabled; then test approved, minimal egress and
   confirm the actual outbound policy. Verify no worker port is publicly reachable.
4. **Prove the review transport** — Route an authenticated WebSocket through the
   intended App Service proxy to one session. Test upgrade headers, origin and
   owner checks, frame/input limits, idle/long-duration behavior, mobile reconnect,
   simultaneous-controller rejection, and reconnect after session expiry. Verify
   CDP/VNC/debug ports are unreachable from the user device and internet.
5. **Prove lifecycle and loss behavior** — Allocate/end/expire sessions, kill a
   worker, restart the app, and drop/reconnect the WebSocket. Confirm lease cleanup,
   token revocation/expiry, profile deletion, explicit re-preparation after loss,
   and no automatic form submission.
6. **Measure cost and capacity** — Measure cold startup and image pull, browser
   CPU/RAM, stream data, concurrency, and actual billing meters for the selected
   region. Exercise configured maximum concurrent sessions and expiry cleanup.
7. **Only then prepare production IaC** — Add modular Bicep and CI/CD integration
   after selecting the service/API version, region, image publishing path, identity
   assignments, networking, observability, quotas, retention, budget, and the
   accepted transport design. Validate the template offline; deployment remains a
   separate approval.
8. **Only after separate permitted-use approval** — Consider an authorised manual
   Meetup login/review. Keep Save/Publish manual. Evaluate remembered-login storage
   separately, default off, after restore/expiry/deletion behavior is approved and
   tested.

## Explicit remaining decisions

- Is Dedicated-plan custom-container session-pool compute acceptable, or is
  Consumption-level idle billing mandatory? If mandatory, which alternative can
  provide per-organiser isolation and stable session routing?
- Does the selected ACA runtime run Chromium sandboxed with the required
  `chromiumSandbox: true` settings as non-root?
- Can the exact session-pool HTTP proxy carry WebSocket upgrade frames reliably
  for the required review duration, and can the authenticated App Service relay it?
- Which region/subscription, quotas, budget, concurrency, resources, session
  cooldown/hard expiry, image registry, and networking topology are approved?
- How will session-pool egress be restricted enough for an authenticated browser
  visiting a third-party website?
- Are permitted use and authorised live Meetup login/review confirmed? Is
  remembered login in scope at all, and what retention/deletion policy would apply?
- What recovery behavior and user messaging are required after an ACA session,
  App Service, network, or browser crash?

Until the first four technical questions have test evidence, this is a feasibility
candidate only. No subscription/region check, quota lookup, Azure deployment,
WebSocket cloud test, or live Meetup interaction was performed for this document.

## Authoritative references

- [Containers in Azure Container Apps — limitations and managed identities](https://learn.microsoft.com/en-us/azure/container-apps/containers)
- [Custom container sessions in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/sessions-custom-container)
- [Use session pools in Azure Container Apps — lifecycle and management endpoint](https://learn.microsoft.com/en-us/azure/container-apps/session-pool)
- [Use dynamic sessions in Azure Container Apps — request routing, security, identifiers, Entra auth](https://learn.microsoft.com/en-us/azure/container-apps/sessions-usage)
- [Ingress in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview)
- [Session affinity in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/sticky-sessions)
- [Scaling in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/scale-app)
- [Billing in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/billing)
- [Azure Container Apps plan types](https://learn.microsoft.com/en-us/azure/container-apps/plans)
- [Quotas for Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/quotas)
- [Azure Container Apps Sandboxes overview](https://learn.microsoft.com/en-us/azure/container-apps/sandboxes-overview)
- [Playwright Docker guidance — Chromium sandboxing](https://playwright.dev/docs/docker)
