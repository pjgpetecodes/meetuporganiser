# Azure setup and deployment

[Documentation home](../README.md) | [Configuration](CONFIGURATION.md) | [Operations](OPERATIONS.md)

For compact script-driven recipes and a full parameter catalogue, see [Quickstarts and script reference](QUICKSTARTS.md).

This guide separates **provisioning infrastructure** from **publishing application code**. `setup-azure.ps1` does the first; it does not deploy the compiled web app.

The current Bicep targets **Windows App Service**, not a Linux container. Deck generation remains headless and does not require Office.

## Prerequisites and permissions

- An Azure subscription, Azure CLI, PowerShell 7, and the .NET 10 SDK for manual publishing.
- A region with the required App Service and Azure SQL capacity, and subscription quota for those services.
- Permission to create a resource group, register resource providers, deploy resources, and create role assignments. Subscription **Owner**, or appropriately combined delegated roles, can cover resource-side setup; Contributor alone cannot grant RBAC.
- Entra permissions to create/reuse app registrations and service principals and manage their credentials/redirect URIs. Azure subscription roles do not automatically grant directory permissions.
- Optional GitHub CLI (`gh auth login`) and repository admin/secrets permissions for CI/CD.
- Optional control of DNS for a custom email domain or custom application hostname.

Review the [production access checklist](#production-access-checklist) before making the site publicly available.

```powershell
az login
az account list --output table
az account set --subscription "<subscription-id>"
az account show --query "{name:name,id:id,tenantId:tenantId}" --output table
```

Run the remaining commands from the repository root.

## What gets created

Deploy `infra\main.bicep`; the checked-in `main.json` is not the source used by the setup script/workflow.

| Resource | Current defaults/purpose |
| --- | --- |
| Resource group | `rg-meetuporganiser`, setup-script default location `northeurope` |
| Windows App Service plan | Basic **B1**, 64-bit worker, Always On |
| Web app | .NET 10, HTTPS only, TLS 1.2 minimum, system-assigned identity |
| SQL logical server/database | SQL authentication; `sqldb-meetuporganiser`, `GP_S_Gen5_1`, 1 vCore maximum, 0.5 minimum, 60-minute auto-pause, 32 GB maximum |
| Storage | Standard LRS, Hot, shared keys disabled, OAuth/managed identity; private `templates` and blob-public `logos`, `speaker-photos`, `generated-assets` |
| Key Vault | RBAC-enabled; receives SQL/App Insights secrets and optional SMTP password |
| Application Insights and Log Analytics | Telemetry and logs |
| Email Communication Service and ACS | Azure-managed sender domain, optional customer-managed sender domain; data location defaults to Europe |
| RBAC | Blob data access and email access for the web identity; optional SMTP principal email role |
| Entra app registrations | `MeetupOrganiser` for web sign-in; `MeetupOrganiser-Smtp` for SMTP calendar requests unless skipped |

Resource names use prefix `meetuporg` plus a suffix derived from the resource group ID. The plan name also includes environment `prod`.

**Important implementation details:**

- SQL uses a public endpoint and an "allow Azure services" firewall rule; there are no private endpoints in the supplied topology.
- The Key Vault is populated, but App Service currently receives SQL, Entra, and SMTP values directly in its application settings. Key Vault references and a web-identity Key Vault read-role assignment are **not automatically wired**.
- The SQL application connection uses the SQL administrator credentials. Managed identity is used for Blob Storage and ACS API email, not SQL.
- SCM basic publishing authentication is enabled for the publish-profile deployment path.
- SQL auto-pause does not stop App Service billing, storage costs, or other resource charges. Ongoing activity can prevent SQL from pausing.

## 1. Provision the environment

Choose and securely retain the SQL password, especially if CI/CD will redeploy the database configuration. Avoid literal passwords in commands/history:

```powershell
$sqlPassword = Read-Host "Strong SQL administrator password" -MaskInput
.\scripts\setup-azure.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -Location "northeurope" `
    -SqlAdminPassword $sqlPassword
```

If no password is supplied, the script generates one. It is not printed; retain it through your approved secret-handling process or configure GitHub secrets during that run. Do not assume a later generated password will match a previously saved deployment secret.

The script:

1. Checks the active Azure subscription.
2. Registers Web, SQL, Storage, Key Vault, Insights, Operational Insights, and Communication resource providers.
3. Finds or creates the web sign-in app and, by default, the SMTP app/service principal.
4. Creates the resource group and deploys Bicep.
5. Prints resource names, URL, and email sender.
6. Adds `https://<web-app-host>/signin-oidc` to the Entra app, preserving existing redirect URIs, and enables ID tokens.
7. Optionally configures local email and GitHub variables/secrets.

If you cannot create directory objects, have an administrator prepare them. To use an existing sign-in registration, supply **both** its client ID and a valid client secret:

```powershell
$entraSecret = Read-Host "Existing Entra web-app client secret" -MaskInput
.\scripts\setup-azure.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -SqlAdminPassword $sqlPassword `
    -EntraClientId "<client-id>" `
    -EntraClientSecret $entraSecret `
    -SkipSmtpInvites
Remove-Variable entraSecret
```

The script does not generate a web secret when you explicitly supply a client ID. It still attempts to update that app's redirect URIs; arrange this manually if you lack permission.

### Setup script switches

| Parameter | Meaning |
| --- | --- |
| `-ResourceGroupName` | Target resource group; default `rg-meetuporganiser` |
| `-Location` | Location used when creating the resource group; Bicep defaults to that group's location |
| `-SqlAdminPassword` | SQL password; generated if omitted |
| `-EntraClientId`, `-EntraClientSecret` | Existing sign-in registration credentials |
| `-SkipSmtpInvites` | Skip SMTP app creation/configuration; ACS API/attachment email remains available |
| `-EmailCustomDomain` | Optional customer-managed sender domain to create |
| `-EmailSenderUsername` | Custom sender local part; default `noreply` |
| `-EmailCustomDomainVerified` | Link/use the custom domain only after actual DNS verification |
| `-ConfigureLocalEmail` | Also write local ACS credentials to the Web project's user secrets |
| `-ConfigureGitHubSecrets` | Set workflow variables/secrets and retrieve/store the publish profile using `gh` |

**Reruns have side effects.** The automatically managed web app credential and SMTP credential are reset without `--append`. This can invalidate credentials used by other environments/local secrets. The GitHub credential script also rotates its deployment principal's secret. Reuse the intended SQL password and update any affected deployment secrets; rerun with `-ConfigureGitHubSecrets` if CI/CD must receive the new values.

Using the same resource group reuses deterministic resource names; it is not a read-only setup check. The scripts find directory apps by display name, so plan separate app registrations deliberately for multiple environments.

## 2. Publish the application

Use either manual publishing or GitHub Actions.

### Option A: manual publish

Use the actual web app name printed by the setup script:

```powershell
$resourceGroup = "rg-meetuporganiser"
$webApp = "<web-app-name-from-setup>"

dotnet publish .\src\MeetupOrganiser.Web\MeetupOrganiser.Web.csproj --configuration Release --output .\publish
Compress-Archive -Path .\publish\* -DestinationPath .\app.zip -Force
az webapp deploy --resource-group $resourceGroup --name $webApp --src-path .\app.zip --type zip
```

The ZIP must contain published files at its root, not an extra `publish` directory. MSBuild includes `pptx\1 Speaker.pptx`, `pptx\2 Speakers.pptx`, static content, and bundled fonts. Do not deploy only the web DLL.

For interactive server Blazor, enable WebSockets in App Service configuration (the supplied Bicep does not explicitly enable it):

```powershell
az webapp config set --resource-group $resourceGroup --name $webApp --web-sockets-enabled true --output none
```

Manage this setting consistently if you customise/redeploy infrastructure. Keep session affinity when running server-side Blazor; scaling across instances also needs an intentional Data Protection/circuit strategy.

On first application start, EF Core attempts migrations and then seed data. Confirm success in logs and by opening the Events page; do not infer database readiness from the landing page alone.

### Option B: GitHub Actions

The repository has:

- **Build and Test**: runs on main pushes and PRs targeting main.
- **Deploy to Azure**: runs on main pushes and manual dispatch. Infrastructure/web deploy jobs require `refs/heads/main`; dispatching another branch builds but does not deploy.

Authenticate `gh` against the intended repository, then provision/configure the deployment settings. Prefer doing this during the original setup run:

```powershell
gh auth login
gh repo view --json nameWithOwner

.\scripts\setup-azure.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -SqlAdminPassword $sqlPassword `
    -ConfigureGitHubSecrets

.\scripts\setup-github-credentials.ps1 -ResourceGroupName "rg-meetuporganiser"
```

If already provisioned, be aware that the setup rerun above rotates app credentials. Include the same custom-domain/SMTP choices you intend to keep.

`setup-github-credentials.ps1` grants the deployment principal resource-group-scoped Contributor and conditional Role Based Access Control Administrator, restricted to the role IDs needed by the supplied Bicep. It writes the login JSON directly to `AZURE_CREDENTIALS`; do not commit it.

Although the workflow requests `id-token: write`, the current Azure login uses a **client-secret JSON credential**, not federated OIDC.

#### Workflow configuration inventory

| GitHub item | Type | Purpose |
| --- | --- | --- |
| `AZURE_CREDENTIALS` | Secret | Deployment principal login JSON, created by `setup-github-credentials.ps1` |
| `AZURE_SQL_PASSWORD` | Secret | SQL administrator password |
| `AZURE_ENTRA_CLIENT_SECRET` | Secret | Web sign-in app secret |
| `AZURE_SMTP_CLIENT_SECRET` | Secret | SMTP app secret when enabled |
| `AZURE_WEBAPP_PUBLISH_PROFILE` | Secret | Application-package deployment |
| `AZURE_WEBAPP_NAME` | Variable | App Service target |
| `AZURE_RESOURCE_GROUP` | Variable | Infrastructure deployment target |
| `AZURE_ENTRA_CLIENT_ID` | Variable | Web sign-in app ID |
| `AZURE_SMTP_CLIENT_ID` | Variable | SMTP app ID when enabled |
| `AZURE_SMTP_SERVICE_PRINCIPAL_ID` | Variable | SMTP service principal **object** ID |
| `AZURE_EMAIL_CUSTOM_DOMAIN` | Variable | Optional custom sender domain |
| `AZURE_EMAIL_SENDER_USERNAME` | Variable | Default `noreply` |
| `AZURE_EMAIL_CUSTOM_DOMAIN_VERIFIED` | Variable | `true` only after verification; otherwise `false` |
| `AZURE_SQL_SERVER`, `AZURE_SQL_DB` | Variables | Set by setup for reference; not currently consumed by the workflow |

The workflow redeploys infrastructure with the stored Entra/SMTP/domain values, then publishes the built artifact. Keep those values consistent when rotating secrets or switching domains.

**Do not treat a green deployment as proof that infrastructure succeeded:** the current Azure login and Bicep steps use `continue-on-error: true`, and infrastructure deployment is skipped when `AZURE_RESOURCE_GROUP` is empty. Inspect those steps explicitly. The publish-profile step can deploy code even when the resource update failed.

To run and inspect the workflow:

```powershell
gh workflow run deploy-azure.yml --ref main
gh run list --workflow deploy-azure.yml --limit 5
```

Do not publish or print the publish profile to a shared terminal/log. When changing deployment credentials, rotate the GitHub secret before relying on the next workflow run.

## 3. Configure email

Provisioning already sets `Email__Endpoint` and `Email__SenderAddress` and grants the web identity access. Configure recipients in the app:

1. Open **Settings > General > Email Notifications** and save the default organiser email.
2. Optionally set each group's **Group Details > Organiser Email**.
3. Create a test speaker invitation addressed to an inbox you control.
4. Submit that speaker's form and confirm organiser notification and speaker confirmation delivery.

The Azure-managed sender works without your own DNS. By default, the setup script also creates `MeetupOrganiser-Smtp`, sets the ACS SMTP username/password, and grants that principal **Communication and Email Service Owner** on ACS. SMTP is used for inline calendar requests; the API sends other messages and provides attachment fallback.

The role assignment can take time to propagate. Missing organiser email prevents organiser notifications and inline calendar requests, but a valid speaker email can still receive an API confirmation attachment.

### Optional custom email domain

1. Run setup with your domain but without the verified switch:

   ```powershell
   .\scripts\setup-azure.ps1 `
       -ResourceGroupName "rg-meetuporganiser" `
       -SqlAdminPassword $sqlPassword `
       -EmailCustomDomain "example.com" `
       -EmailSenderUsername "noreply" `
       -ConfigureGitHubSecrets
   ```

2. In Azure Portal, open the **Email Communication Service > Provision domains**, select the domain, and add the exact ownership/SPF/DKIM records Azure displays to your DNS.
3. Verify all required records/statuses in Azure. The flag is not a substitute for this verification.
4. Rerun with the same domain/username/password and `-EmailCustomDomainVerified` (and `-ConfigureGitHubSecrets` when using CI/CD).

Until verification is complete, the app continues using the Azure-managed sender. Afterwards the sender is `noreply@example.com`, or your chosen username. A sender domain and an App Service custom hostname are separate configurations.

For local development against this service, follow [local email setup](LOCAL-SETUP.md#6-optional-email).

## 4. Configure the app and integrations

Use the [user guide](USER-GUIDE.md) to create your groups, speakers, events, sponsors, and PPTX templates. Seeded data is illustrative and includes group-specific defaults; review it before sending real messages or creating events.

Configure each required API in **Settings > API Integrations** using [API integrations](API-INTEGRATIONS.md). In Azure, set Meetup's exact callback to:

```text
https://<your-app-host>/integrations/meetup/callback
```

Save before choosing **Connect Meetup**. Use the same public hostname for the browser and registered callbacks.

For configuration-backed secrets, use App Service settings or explicitly configure Key Vault references. The current setup does not automatically create secret entries for each external integration.

### Optional Key Vault references

To stop passing a secret directly in App Service settings, an administrator must:

1. Store the value in Key Vault.
2. Grant the App Service identity **Key Vault Secrets User** (or the equivalent least-privilege access) on the appropriate vault.
3. Replace the matching application setting with a Key Vault reference.

Example syntax for the SQL connection setting:

```text
ConnectionStrings__DefaultConnection=@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/SqlConnectionString/)
```

Make the corresponding change in IaC: a later stock Bicep deployment otherwise writes the direct value again. Database-saved integration values still override configuration-backed references.

### Optional custom application hostname

Configure the hostname and TLS binding in App Service using Azure's DNS verification requirements. Add `https://<custom-host>/signin-oidc` to the Entra registration, update/register Meetup's callback separately, and use that hostname when generating speaker links. Existing links contain the hostname from the browser session that created them.

## Production access checklist

The supplied app is not a complete multi-tenant or role-isolated administration system. Before exposing a real environment:

- Configure a real tenant-specific Entra client/secret and verify sign-in/sign-out.
- **Remove or environment-gate developer sign-in before public use.** The current `Program.cs` still maps `/login` to a predefined developer identity without an environment guard, even when Entra is configured. Merely setting `ASPNETCORE_ENVIRONMENT=Production` is not sufficient.
- Restrict the Entra Enterprise Application to approved organisers, for example through required user assignment, and verify the resulting access. Current `[Authorize]`/`OrganizerOnly` checks require authentication, not an organiser role or group-specific permission.
- Decide whether public event asset URLs are acceptable. Current `/api/events/{id}/pptx` and `/api/events/{id}/social-image` routes do not require authentication; a GUID is not an access-control boundary.
- Treat public headshots/logos and the contents of decks as publishable data. Keep template blobs private.
- Protect SQL/API credentials, backups, publish profiles, Data Protection keys, and logs; define rotation and recovery procedures.
- Review the broad Azure-services SQL firewall rule and public service endpoints against your network policy.
- Persist/protect Data Protection keys appropriately for your hosting/scaling/recovery needs, especially for connected Meetup OAuth state.
- Review demo defaults, date/time behaviour, external API permissions, and real email recipients before live posting.

These are current implementation boundaries, not actions performed by the setup script. See [operations and limitations](OPERATIONS.md) for additional behaviour to account for.

## Deployment acceptance checks

Before using a newly published environment:

1. Verify infrastructure deployment logs, runtime startup, EF migration/seed success, and no unresolved App Service configuration references.
2. Sign in as an approved organiser and inspect Events/Groups/Speakers/Settings.
3. Upload a logo, photo, and template; open one- and two-speaker previews.
4. Download and open an event PPTX and social PNG.
5. Send a test invitation and complete it once; confirm a reused link is rejected.
6. Verify Meetup OAuth and a deliberate test API action only on accounts/events where a real write is acceptable.
7. Confirm application logs/telemetry and backups are available to the intended operators.

Remove sensitive variables from your terminal when finished:

```powershell
Remove-Variable sqlPassword
```

Do not delete the resource group to troubleshoot a failed deployment; use the [operations guide](OPERATIONS.md) to inspect failures and preserve data.
