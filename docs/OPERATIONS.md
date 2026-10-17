# Operations and troubleshooting

[Documentation home](../README.md) | [Local setup](LOCAL-SETUP.md) | [Azure setup](AZURE-SETUP.md) | [Configuration](CONFIGURATION.md)

## Data to protect

| Data | Location | Recovery consideration |
| --- | --- | --- |
| Groups, speakers, events, sessions, settings, token hashes | SQL database | Use database backups/point-in-time recovery; saved API credentials can be in this data |
| Photos and logos | Public-readable blobs | Back up files and keep database URL references consistent |
| Uploaded PPTX templates | Private `templates` blobs | Retain original editable masters and recover the matching blobs |
| Connected Meetup OAuth tokens | Protected value in SQL | Requires the associated ASP.NET Core Data Protection keys |
| Authentication cookies/protected state | Data Protection | Plan key persistence, access control, and recovery for your host |
| Infrastructure/deployment credentials | App Service settings, Key Vault, GitHub secrets, Entra apps | Rotate securely and keep environment/workflow values consistent |
| Downloaded PPTX/PNG and external posts | Browser/user storage and destination platforms | Downloads are on-demand; the app is not an archive of previously published artefacts |

Back up SQL and blobs as a coordinated application dataset. Restoring SQL alone may leave URLs pointing to missing or different-environment blobs. Store credentials and Data Protection keys securely; user secrets are not a production vault.

Bicep enables seven-day blob delete retention and Key Vault soft-delete retention. These are useful protections, not a complete backup policy. Confirm Azure SQL backup retention/recovery settings for your environment and rehearse a restore rather than assuming a deployable template can recover user data.

There is no built-in organiser export/import wizard, recycle bin, or automatic orphaned-blob cleanup. Removing a logo reference, replacing a template, or deleting an event does not necessarily delete uploaded files.

## Routine upgrades

1. Back up the target database and required blobs; review pending EF migrations and release changes.
2. Build/test the intended code and publish the **complete** Web output, including bundled PPTX files/fonts/static assets.
3. Apply infrastructure changes deliberately; preserve the same SQL password, Entra/SMTP credentials, and email-domain parameters used by the environment.
4. Inspect startup migration/seed logs and perform the [deployment acceptance checks](AZURE-SETUP.md#deployment-acceptance-checks).
5. Confirm a representative deck/image and integration connection after the update.

Startup uses `MigrateAsync`, then seeding. If migrations fail, it attempts `EnsureCreatedAsync` and seeding as a fallback. **That is not a supported repair procedure for a partially migrated existing database.** Investigate the original error; do not assume the fallback upgraded the schema.

Rolling back application files does not roll back the database or externally published content. For a schema rollback, use a reviewed migration/recovery plan.

At larger scale, coordinate migrations before starting multiple app instances rather than treating concurrent startup seeding as a deployment orchestrator.

## Logs and diagnosis

### Local

Use the terminal running `dotnet run`/`dotnet watch`. Look for migration/seed errors, blob endpoint selection, Sessionize/API errors, and email warnings. Start Azurite separately and inspect its output too.

The app can continue hosting after database initialisation failure, so always exercise a database-backed screen.

### Azure

Use **App Service > Log stream**, deployment logs, and Application Insights failures/traces. With the application/resource names from setup:

```powershell
$resourceGroup = "rg-meetuporganiser"
$webApp = "<web-app-name>"
az webapp show --resource-group $resourceGroup --name $webApp --query "{state:state,host:defaultHostName}" --output table
az webapp config show --resource-group $resourceGroup --name $webApp --query "{runtime:netFrameworkVersion,webSockets:webSocketsEnabled,alwaysOn:alwaysOn}" --output table
az monitor activity-log list --resource-group $resourceGroup --status Failed --max-events 10 --output table
```

If App Service filesystem application logging is enabled, stream it with:

```powershell
az webapp log tail --resource-group $resourceGroup --name $webApp
```

Do not print entire application settings, Key Vault values, publish profiles, or connection strings into shared logs. API responses and some failure paths can include sensitive data; redact before sharing.

For GitHub deployments:

```powershell
gh run list --workflow deploy-azure.yml --limit 5
gh run view "<run-id>" --log-failed
```

The login/Bicep workflow steps currently tolerate failure; inspect their individual logs even when the overall run appears successful.

## Common problems

| Symptom | Likely cause and action |
| --- | --- |
| Home loads but Events/Settings fail | SQL startup error; verify the selected connection, SQL service, permissions, network/firewall, and migration logs |
| `.\SQLEXPRESS` cannot be reached | Only LocalDB/default SQL instance is installed; set the correct user-secret connection |
| EF command targets the wrong database | Design-time factory uses an environment variable or Express, not Web user secrets; see [EF setup](LOCAL-SETUP.md#database-migrations-for-contributors) |
| Upload reports connection refused | Azurite is stopped, blob port 10000 unavailable, or the app has an unexpected `Storage:AccountUrl` override |
| Azure blob upload/download returns 403 | Check the credential identity and Storage Blob Data Contributor; resource Contributor is insufficient; allow role propagation time |
| Browser cannot display photo/logo | Check its saved URL and blob-public access; template containers intentionally remain private |
| Template URL gives 403 directly in browser | Expected for private templates; inspect through the signed-in group preview instead |
| App never switches to Entra | Client ID blank/all-zero-prefix or wrong configuration source; restart after correcting host settings |
| Entra redirect mismatch | Register the exact host/scheme `/signin-oidc`; custom domain and local HTTPS callbacks differ |
| Too many people can access admin screens | Current authorisation requires authentication only; apply the [production access checklist](AZURE-SETUP.md#production-access-checklist) |
| Blazor reconnects repeatedly | Check server errors, WebSockets, network/proxy timeout, session affinity, App Service restarts and scale settings |
| Sessionize returns no data | Verify public API view, event ID/endpoint shape, profile visibility, and logs; use an event ID when fetching both speakers and sessions |
| Zoom generation fails | Supply all API credentials and permissions, or configure a fixed URL; no automatic fake meeting is created |
| Event/Zoom time differs from expectation | Server-local editor conversion and UK output formatting; verify UTC and external meeting time, not only the local label |
| API still uses old credentials | Non-blank SQL settings override environment values; update/clear the saved override |
| Meetup OAuth state error | Start Connect again in the same browser/hostname; state cookie expires after ten minutes; verify cookie/HTTPS behaviour |
| Meetup token cannot be read after move | Restore/preserve Data Protection keys, or reconnect Meetup deliberately |
| "Published" result links to `/events/create` | No actual API event was created; complete the manual Meetup form |
| Repeated Meetup publications create duplicates | Current publisher creates, not updates; check destination and save the real URL locally |
| Copy/Web Intent misses revised text | Some fallback links use initial generated text; copy the edited textarea manually |
| Clipboard access denied | Use HTTPS/localhost, permit clipboard access, or select/copy text manually |
| Magic link invalid | Expired after 14 days, already used, revoked by a newer link, or wrong URL; issue a replacement |
| Remote speaker cannot open a link | URL contains localhost or an inaccessible host; generate from the public deployment |
| Submission saved but no email | Check recipient, sender, transport, domain link/verification, identity/RBAC, and ACS delivery diagnostics |
| Confirmation lacks Accept/Decline buttons | Check SMTP credentials and organiser email; fallback API sends `.ics`; client support also varies |
| Wrong or missing template content | Follow the [PPTX troubleshooting checklist](PPTX-TEMPLATES.md#troubleshooting-checklist) |
| First SQL request is slow | Serverless database may be resuming; inspect retries, connection timeout, and service health before repeated writes |

For platform-specific credentials/permissions, see [API integrations](API-INTEGRATIONS.md).

## Email delivery and credential rotation

An ACS API send call starting successfully is not proof of final inbox delivery. Inspect ACS delivery status/logs, sender-domain verification, recipient address, spam filtering, and service limits.

The default SMTP host is `smtp.azurecomm.net:587` with STARTTLS. Confirmations with a known organiser use SMTP when configured; SMTP failures are logged and fall back to the ACS API. Other messages use the API directly.

The setup scripts have credential-changing side effects:

- `setup-azure.ps1` resets the automatically managed web and SMTP app secrets. Update GitHub secrets and local consumers when rerunning it.
- `configure-local-email.ps1` appends a local SMTP secret, preserving the currently deployed credential.
- `setup-github-credentials.ps1` rotates the deploy principal secret and updates `AZURE_CREDENTIALS`; its default requested secret lifetime is one year.

Track actual expiry dates in Entra for web, SMTP, local, and deployment credentials. Rotating one does not rotate all the others. Rerunning setup can also change the SQL password if you do not provide the existing one, so keep deployment settings consistent.

## Migrating legacy local uploads

This script is specifically for old `/storage/...` URLs and files in `src\MeetupOrganiser.Web\wwwroot\storage`. It is **not** a general Azure-to-Azure/Azurite blob migration or database migration tool.

Back up SQL and source files first. Install/authenticate Azure CLI; for Azure, use an identity with **Storage Blob Data Contributor** on the destination. Start Azurite if targeting the emulator.

Preview a migration to Azurite:

```powershell
$connection = 'Server=.\SQLEXPRESS;Database=MeetupOrganiserDb;Trusted_Connection=True;TrustServerCertificate=True'
.\scripts\migrate-storage.ps1 -UseAzurite -SqlConnectionString $connection -WhatIf
```

Preview a migration to Azure:

```powershell
az login
.\scripts\migrate-storage.ps1 -AccountName "<storage-account>" -SqlConnectionString $connection -WhatIf
```

Replace `$connection` with the connection for the database being migrated; do not accidentally rewrite your local database to production blobs. For SQL-authenticated targets, read the connection securely rather than putting credentials in history.

After reviewing the preview, rerun the chosen command **without `-WhatIf`**. The script creates containers, uploads files preserving their existing filenames, and rewrites legacy URLs in group logos/templates, speaker photos, and sponsor logos.

- Omit `-SqlConnectionString` to upload files without rewriting database values.
- Use `-StoragePath "<absolute-directory>"` if the source is elsewhere.
- `-WhatIf` suppresses writes but, with a connection supplied, still opens the database connection.
- The script uses `System.Data.SqlClient`; run it in a PowerShell environment where that provider is available.
- Inspect command output for upload/SQL errors and verify files/URLs afterwards; upload plus SQL rewriting is not one atomic transaction.

Keep the original files until you have confirmed private template loading, public images, and event downloads. Do not recursively remove the repository's storage folders as a "fix".

### Moving already-blob-backed environments

Use an appropriate authenticated blob-copy/export process and a reviewed database URL-update procedure. Also preserve Data Protection keys or plan to reconnect Meetup. Changing `Storage:AccountUrl` alone does not rewrite old URLs in SQL or copy their data.

For a production SQL connection from a developer machine, an administrator may need to add a narrow client-IP firewall rule. Do not open SQL to all IPs just to perform a migration.

## Current boundaries to plan around

- Organiser access is shared authenticated access, not group-scoped tenancy; developer sign-in still needs gating before public production.
- Event PPTX/social-image HTTP downloads currently have no authentication requirement.
- Event editing uses server-local time conversion; promo output is UK-specific.
- Main authoring/generation flows target one or two speakers, not arbitrary conference programmes.
- Custom PNG rendering is approximate; custom slides must be 16:9 for 1280x720.
- A per-group deck controls visual branding; stored colours/logos/sponsors do not automatically redraw every asset.
- Downloads are generated on demand. There is no generated-asset history browser or scheduled publish job.
- The short-URL stem formats a suggestion; it is not a Bitly API integration.
- Publishing does not continuously synchronise local events, external event state, meetings, social posts, or calendar RSVPs.
- Meetup publication uses saved data, can return a manual-create fallback, and does not persist the external result to the event.
- Entra/SMTP/deployment credentials and Data Protection require an operational lifecycle beyond resource provisioning.

These limitations describe the current source behaviour. Do not rely on a button label, stored status, hidden slide, or GUID URL as stronger evidence than the underlying service result/access policy.
