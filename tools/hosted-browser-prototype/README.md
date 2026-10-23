# Local Meetup browser review

Prepare an event in Meetup Organiser, log into Meetup in a separate controlled
browser, fill the supported fields, then review and manually Save/Publish. No
extension or Meetup API access is required. **This implementation is local
development only, disabled by default, and is not an Azure worker allocator.**

## Test the complete local workflow

Prerequisites: the existing app's normal local database/storage/authentication
configuration, .NET 10, Node.js with npm, and installed Google Chrome (or Edge).
Stop any existing instance on the same app port first. HTTPS uses the normal .NET
development certificate (`dotnet dev-certs https --trust` if not already trusted).

```powershell
npm ci --prefix .\tools\hosted-browser-prototype
.\scripts\start-local-browser-review.ps1 -LiveMeetup
```

1. Open `https://localhost:7225`, sign into Meetup Organiser normally, and open the
   event's **Socials & Slides** page. Select the event, review/edit its Meetup
   title and description, and prepare its social image.
2. Click **Prepare new event for Meetup** (or **Prepare update for Meetup**).
   Then click **Open local browser review (no extension)**. A viewer opens in a
   new tab. The app launches a separate Chrome process; it never imports your
   normal browser profile or cookies.
   App-launched Chrome runs without a separate desktop window; interact through
   the streamed review page. For worker debugging only, set
   `HostedBrowser__ShowBrowserWindow=true` before starting the app to show both
   views of the same session.
3. Confirm **LIVE MEETUP** and the displayed group/event URL. Sign into Meetup
   manually in the remote browser. This local mode supports direct Meetup
   sign-in, not Google/Apple/external-provider pages or popups. Complete normal
   MFA/challenges yourself; stop if they cannot be completed.
4. Click **Return to event form** after login if necessary. Check the signed-in
   account in Meetup and the exact event/group. **Fill event fields** replaces
   supported values but does not click Save/Publish. Read the field report.
5. Click **Upload event PNG** or **Upload speaker photo**. The worker clicks the
   new-event image **Select** button or speaker **Upload Photo** button and supplies
   the prepared image to its dialog. For an existing event image, open the image
   menu and choose **Replace** first; that menu opener is not yet supported.
   Complete cropping/applying manually. Use the remote view for remaining edits.
6. Review everything and click Meetup's Save/Publish yourself inside the remote
   view. Filling can trigger normal website autosave/network behavior; it is not a
   guarantee of zero external writes. Back in the app, explicitly record the
   actual Meetup URL and saved draft/published outcome if desired.
7. Use **End session** when finished. Closing only the viewer tab allows reconnect
   while the worker survives; it does not delete remembered login.

The helper accepts `-Browser msedge` and `-Urls <your configured local HTTPS URL>`.
Keep the URL consistent with any Entra redirect URI. It does not rewrite
appsettings or user secrets. To use an existing IDE launch instead:

```powershell
$env:HostedBrowser__Enabled = 'true'
$env:HostedBrowser__AllowLiveMeetup = 'true'
# Start the existing app in Development, then follow the same EventAssets flow.
```

Without `-LiveMeetup`, the helper uses an intercepted **OFFLINE FIXTURE**, not the
real Meetup site. This lets you exercise the app launch/proxy/filling workflow
without an account. It models only a subset of the real controls.

### Supported fields and manual steps

Uses the existing guarded extension adapter for title, formatted description,
UK-local date/start time, In person/Online/Hybrid, online URL, venue search text,
first-speaker name/bio/socials and explicit photo-upload actions. Unsupported or
unavailable controls produce a report rather than a claimed success.

Venue-result selection, time zone, duration/end, remaining speakers and photo
crop/apply are manual. Immutable Meetup Hybrid settings are not forced.
The remote view supports clicks, mouse dragging, typing, selected keyboard keys,
wheel scrolling and touch scrolling. The text control supports mobile-keyboard
entry; direct paste/clipboard sharing and arbitrary address-bar navigation are
not provided.

### Optional remembered login

Default off. Check **Allow remembered Meetup login** before opening the viewer,
then click **Remember Meetup login** after signing in. This stores only Meetup
domain cookies, not passwords, local storage, IndexedDB, browser history or the
unsaved form. Cookies can grant account access: treat the local OS account and
ASP.NET Data Protection keys as sensitive.

State is owner-scoped and encrypted with ASP.NET Data Protection in
`%LOCALAPPDATA%\MeetupOrganiser\BrowserLogin` (override
`HostedBrowser:LoginDirectory` if needed). It expires after seven days and expired
files are removed on next load, not by a background retention job. Opt in again
on the next launch to restore it. Cookie-only restore may not satisfy Meetup;
expiry, MFA or revocation can require manual login. No live restore guarantee has
been established. **Forget login and end session** deletes the owner's saved file
and closes the active browser. A launch with the checkbox off ignores any saved file.
If saved state cannot be decrypted after local key changes, launch with the
checkbox off, then use **Forget login and end session** before signing in again.

### Local boundaries and troubleshooting

The app authenticates each viewer request and binds it to its organiser. Only the
app knows the loopback worker cookie; it proxies the HTTP/WebSocket stream. Each
organiser has one session, one active controller and an isolated Chrome process;
up to three local sessions are allowed. A managed session ends at 30 minutes,
prepared-content expiry, or ten minutes without input, whichever comes first.
Worker/app loss discards unsaved browser state. Start again rather than assuming
a save succeeded; do not blindly repeat publication.

For a startup failure, check Node, installed Chrome/Edge, the worker dependencies,
session capacity and prepared-content expiry. An existing session must be ended
before starting another. `HostedBrowser:WorkerScript` and
`HostedBrowser:NodeExecutable` can override local paths. If a viewer connection
fails, close the other controller tab and reconnect. Prepare fresh content if expired.

Do **not** expose the development app or worker through a tunnel/public ingress.
Live mode is not a production egress sandbox: it allows HTTPS page resources, and
restricts top-level navigation to Meetup. Browser frames remain in memory; no
routine recordings are written. The viewer shows origin/path only, never URL
query/fragment credentials. No public CDP/VNC endpoint is provided.

## Standalone offline fixture

```powershell
npm start --prefix .\tools\hosted-browser-prototype
```

Open the one-time loopback launch link printed in the terminal. Treat it as a
credential. Standalone mode is always synthetic and cannot remember login.
Its limits are fifteen minutes total/five minutes input-idle. Use
`BROWSER_CHANNEL=msedge` for Edge or `chromium` for an already-installed
Playwright browser. No browser is downloaded automatically.

## Verification

```powershell
npm test --prefix .\tools\hosted-browser-prototype
$env:MEETUP_HOSTED_BROWSER_TESTS = '1'
dotnet test .\tests\MeetupOrganiser.Tests\MeetupOrganiser.Tests.csproj -c Release --filter 'FullyQualifiedName~HostedBrowserTests'
```

The Node tests use real Chrome against intercepted fixture content. The opt-in .NET
integration test exercises authenticated HTTP launch, antiforgery, ownership,
WebSocket proxy/filling, encrypted-cookie restore/save and forgetting/teardown.
Other .NET tests cover encryption, corruption and expiry without Chrome.
No automated test logs into a real Meetup account or saves/publishes there.

## Linux container verification

The test-only Dockerfile uses the version-matched Microsoft Playwright image, a
non-root user and Chromium sandboxing. A virtual X display exercises headed
Chromium. The build context allowlist includes only this prototype and the existing
form adapter; it excludes other repository content and local dependencies.

From the repository root, with Docker Desktop's Linux engine running:

```powershell
docker build -f .\tools\hosted-browser-prototype\Dockerfile -t meetup-hosted-browser-offline:feasibility .
docker run --rm --init --network none --cap-drop ALL --security-opt no-new-privileges --shm-size=256m --memory=2g --cpus=2 meetup-hosted-browser-offline:feasibility
```

This command publishes no ports, mounts no host data, uses no cloud identity and
disables container networking. It runs the suite and exits; it does not start a
public service. All eight cases passed in Linux, including a headed-browser check
that Chromium reports adequate sandboxing. No `--no-sandbox`, `SYS_ADMIN`,
privileged mode or custom seccomp exemption was used.

The Playwright base image is intended for testing/development; this is not a
production browsing image. Passing under local Docker does not prove the same
sandbox will work on Azure Container Apps Consumption.

## Remaining feasibility gates

First-party streaming through Azure ingress, Consumption sandbox compatibility,
isolated per-organiser worker allocation, active leases, scale-to-zero,
networking/costs and cleanup still require an explicitly approved Azure test
environment. Do not deploy this test image as a production worker.

Permitted use and authorised manual Meetup login/MFA/mobile review must be established
before live tests. Do not bypass challenges. Public production access must remain
disabled until the control-plane authentication, tenant isolation and least-privilege
network design exist.

Local encrypted remembered cookies are implemented, not cloud persistence.
Azure still requires a production allocator/leases, private ingress/egress design,
private Blob/Key Vault persistence, retention/revocation policy, operating budget
and the cloud sandbox/streaming proof. See `docs\HOSTED-BROWSER-AZURE.md`.
Deployment is deferred until local real-account testing and iteration are complete.
