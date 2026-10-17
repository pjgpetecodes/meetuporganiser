# Quickstarts and script reference

[Documentation home](../README.md) | [Local setup](LOCAL-SETUP.md) | [Azure setup](AZURE-SETUP.md) | [Template parameters](TEMPLATE-PARAMETERS.md)

All commands below are **PowerShell**, run from the repository root. Replace angle-bracket placeholders with your values. Use PowerShell 7 for `Read-Host -MaskInput`.

There are four setup/maintenance scripts in `scripts`. There is **no all-in-one local bootstrap script**: SQL Server and Azurite must be installed/configured separately.

## Quickstart: run locally

Prerequisites: .NET 10 SDK, Node.js/npm, and a running SQL Server Express instance named `SQLEXPRESS`. For LocalDB/another SQL Server, first apply the connection override in [local setup](LOCAL-SETUP.md#1-configure-sql-server).

```powershell
dotnet restore .\MeetupOrganiser.slnx
dotnet dev-certs https --trust
npm install --global azurite
azurite
```

Leave Azurite running. In a **second terminal**, from the repository root:

```powershell
dotnet run --project .\src\MeetupOrganiser.Web --launch-profile https
```

Open <https://localhost:7225>, sign in with the developer fallback, then open Events. The app attempts migrations and seeds demonstration data automatically; check logs for failures.

## Quickstart: provision Azure and publish manually

Read the [Azure permissions](AZURE-SETUP.md#prerequisites-and-permissions) and [production access checklist](AZURE-SETUP.md#production-access-checklist) first. Provisioning is not application publishing or production hardening.

```powershell
az login
az account set --subscription "<subscription-id>"
$sqlPassword = Read-Host "Strong SQL administrator password; retain securely" -MaskInput

.\scripts\setup-azure.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -Location "northeurope" `
    -SqlAdminPassword $sqlPassword
```

Use the web app name printed by the script:

```powershell
$webApp = "<web-app-name-from-setup>"
dotnet publish .\src\MeetupOrganiser.Web\MeetupOrganiser.Web.csproj --configuration Release --output .\publish
Compress-Archive -Path .\publish\* -DestinationPath .\app.zip -Force
az webapp deploy --resource-group "rg-meetuporganiser" --name $webApp --src-path .\app.zip --type zip
az webapp config set --resource-group "rg-meetuporganiser" --name $webApp --web-sockets-enabled true --output none
Remove-Variable sqlPassword
```

Open the printed HTTPS URL, check migration/seed logs, and configure group/recipient settings. This path does not require GitHub Actions. The setup script creates the sign-in/SMTP apps, infrastructure, and role assignments; the publish commands deploy the code.

## Quickstart: Azure with GitHub Actions

For a **new environment**, authenticate both CLIs, then run:

```powershell
az login
az account set --subscription "<subscription-id>"
gh auth login
gh repo view --json nameWithOwner
$sqlPassword = Read-Host "Strong SQL administrator password; retain securely" -MaskInput

.\scripts\setup-azure.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -Location "northeurope" `
    -SqlAdminPassword $sqlPassword `
    -ConfigureGitHubSecrets

.\scripts\setup-github-credentials.ps1 -ResourceGroupName "rg-meetuporganiser"
gh workflow run deploy-azure.yml --ref main
gh run list --workflow deploy-azure.yml --limit 5
Remove-Variable sqlPassword
```

This configures the infrastructure/publish-profile secrets and the separate `AZURE_CREDENTIALS` deploy principal. The manually dispatched workflow uses code already on GitHub's `main`; it does not upload uncommitted local edits.

For an **existing environment**, use the retained SQL password and intended domain/SMTP settings when rerunning setup. Reruns can rotate Entra/SMTP credentials. Do not blindly rerun a fresh-environment recipe against production.

See the [workflow configuration inventory](AZURE-SETUP.md#workflow-configuration-inventory). Inspect login/Bicep steps even if the run is green: the current workflow tolerates errors in those steps. Enable WebSockets through App Service configuration if not already enabled.

## Quickstart: enable real local email

This uses existing Azure resources; Azurite does not emulate email. Messages are sent to real inboxes.

```powershell
az login
az account set --subscription "<subscription-id>"
.\scripts\configure-local-email.ps1 -ResourceGroupName "rg-meetuporganiser"
```

For a verified custom sender:

```powershell
.\scripts\configure-local-email.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -SenderAddress "noreply@example.com"
```

Restart the local app. Set the organiser recipient under **Settings > General > Email Notifications**, then generate a test speaker link for an address you control. The script stores ACS settings in user secrets and appends local SMTP credentials when the SMTP app exists.

You can combine initial Azure provisioning with local email setup by adding `-ConfigureLocalEmail` to `setup-azure.ps1`.

## Quickstart: custom email sender domain

Use your retained SQL password. This example also updates GitHub secrets; omit that switch if not using CI/CD.

```powershell
$sqlPassword = Read-Host "Existing SQL administrator password" -MaskInput
.\scripts\setup-azure.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -SqlAdminPassword $sqlPassword `
    -EmailCustomDomain "example.com" `
    -EmailSenderUsername "noreply" `
    -ConfigureGitHubSecrets
```

Add and verify the DNS records shown in **Azure Portal > Email Communication Service > Provision domains**. Only after actual verification, rerun:

```powershell
.\scripts\setup-azure.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -SqlAdminPassword $sqlPassword `
    -EmailCustomDomain "example.com" `
    -EmailSenderUsername "noreply" `
    -EmailCustomDomainVerified `
    -ConfigureGitHubSecrets
Remove-Variable sqlPassword
```

Until verification/linking, the Azure-managed sender remains active. Include any existing explicit Entra credentials/SMTP choices required by your environment; reruns can rotate automatically managed credentials.

## Quickstart: migrate old local uploads

Back up the database and source uploads. This only migrates legacy `wwwroot\storage` files and `/storage/...` database URLs.

```powershell
$connection = 'Server=.\SQLEXPRESS;Database=MeetupOrganiserDb;Trusted_Connection=True;TrustServerCertificate=True'
.\scripts\migrate-storage.ps1 -UseAzurite -SqlConnectionString $connection -WhatIf
```

Review the preview, then apply:

```powershell
.\scripts\migrate-storage.ps1 -UseAzurite -SqlConnectionString $connection
Remove-Variable connection
```

For Azure, replace `-UseAzurite` with `-AccountName "<storage-account>"` and authenticate `az login` with Blob Data Contributor access. Supply the database connection for the environment whose URLs should change.

See [migration safeguards](OPERATIONS.md#migrating-legacy-local-uploads); `-WhatIf` still opens SQL when a connection is supplied.

## Script catalogue

### `scripts\setup-azure.ps1`

**Purpose:** provision Azure resources, sign-in/SMTP registrations, redirect URI, and optional local/GitHub configuration. It does not publish the compiled application.

**Prerequisites:** authenticated Azure CLI, resource/provider/RBAC and Entra app-management permissions. Optional flags require `gh` or local .NET user-secrets support.

| Parameter | Default | Effect |
| --- | --- | --- |
| `-ResourceGroupName` | `rg-meetuporganiser` | Creates/targets the resource group |
| `-Location` | `northeurope` | Resource group location; Bicep uses the group's location |
| `-SqlAdminPassword` | Generated when omitted | SQL administrator password |
| `-EntraClientId` | Find/create `MeetupOrganiser` | Existing sign-in application client ID |
| `-EntraClientSecret` | Generated for automatically selected app when omitted | Existing web client secret; supply it if explicitly supplying an ID |
| `-EmailCustomDomain` | Empty | Optional customer-managed sender domain |
| `-EmailSenderUsername` | `noreply` | Custom-domain sender local part |
| `-EmailCustomDomainVerified` | Off | Link/use domain after DNS verification |
| `-SkipSmtpInvites` | Off | Do not create/configure SMTP application |
| `-ConfigureLocalEmail` | Off | Invoke local email configuration after provisioning |
| `-ConfigureGitHubSecrets` | Off | Set workflow secrets/variables, including publish profile |

**Side effects:** billable Azure resources, credential resets/rotations, resource configuration updates, role assignments, and optional secret writes. It has no `-WhatIf`/dry-run switch.

For infrastructure-only planning, use an Azure deployment what-if with all the same intended Bicep parameters under your normal deployment process; it does not preview directory/credential actions performed by the setup script.

### `scripts\setup-github-credentials.ps1`

**Purpose:** create/reuse a deployment principal, assign resource-group deployment/RBAC permissions, rotate its secret, and store `AZURE_CREDENTIALS`.

**Prerequisites:** resource group already exists; authenticated Azure CLI with directory/role-assignment permissions and GitHub CLI with secret-write access.

| Parameter | Default | Effect |
| --- | --- | --- |
| `-ResourceGroupName` | `rg-meetuporganiser` | Scope for deployment permissions |
| `-ServicePrincipalName` | `sp-meetuporganiser-deploy` | Registration display name |
| `-Repository` | Current GitHub repository | Explicit `owner/name` target for `AZURE_CREDENTIALS` |
| `-SecretValidityYears` | `1` | Requested deployment secret lifetime |
| `-SkipGitHub` | Off | Do not write the GitHub secret; write credentials to `-OutFile` instead |
| `-OutFile` | None | Required with `-SkipGitHub`; contains sensitive login JSON |

Example targeting a different repository explicitly:

```powershell
.\scripts\setup-github-credentials.ps1 `
    -ResourceGroupName "rg-meetuporganiser" `
    -Repository "<owner>/<repository>"
```

`-SkipGitHub` does **not** skip credential creation/rotation or role assignments; it only changes where the resulting JSON goes. If using it, keep the output outside the repository, restrict access, transfer through your approved secret channel, and delete that specific file after use.

This script configures only the deployment principal login secret, not all workflow variables or the publish profile.

### `scripts\configure-local-email.ps1`

**Purpose:** retrieve an existing Communication Service's credentials/sender and configure the local Web project's user secrets.

**Prerequisites:** Azure CLI/.NET SDK, permissions to read ACS keys and domain details, and permission to append a credential to the SMTP app if present. The script attempts to add the Azure CLI `communication` extension.

| Parameter | Default | Effect |
| --- | --- | --- |
| `-ResourceGroupName` | `rg-meetuporganiser` | Group containing the email resources |
| `-SenderAddress` | Resolved Azure-managed sender | Override with a verified/linked sender |

Writes `Email:ConnectionString`, `Email:SenderAddress`, and, when the SMTP app is found, `Email:Smtp:Username`/`Email:Smtp:Password`. It selects the first matching resources in the group; review the result when a group hosts multiple email services/domains. It appends a local SMTP credential rather than invalidating the deployed credential.

It does not set the organiser recipient or modify the app's SQL settings. Configure the recipient through the UI.

### `scripts\migrate-storage.ps1`

**Purpose:** upload legacy local files and optionally rewrite legacy URLs in SQL.

**Prerequisites:** Azure CLI; running Azurite or Azure Blob Data Contributor access; SQL access and `System.Data.SqlClient` when rewriting URLs.

| Parameter | Default | Effect |
| --- | --- | --- |
| `-AccountName` | None | Destination Azure storage account; required unless using Azurite |
| `-UseAzurite` | Off | Use the local emulator instead of Azure |
| `-SqlConnectionString` | None | Rewrite legacy URLs in that database; omit for upload only |
| `-StoragePath` | Web project's `wwwroot\storage` | Source directory with container-named subdirectories |
| `-WhatIf` | Off | Show intended changes without performing writes |
| `-Confirm` | PowerShell common behaviour | Request confirmation through `SupportsShouldProcess` |

The script preserves legacy filenames and overwrites matching destination blobs. Database changes and uploads are not transactional together. It is not a general blob-to-blob environment migration script.

## Quickstart: create a reusable template

There is no PPTX authoring script. Create a 16:9 `.pptx` in PowerPoint, add supported text tokens such as `{{SpeakerName}}`, `{{TalkTitle}}`, and `{{EventDateLine}}`, and mark picture alt text with `{{Speaker1Photo}}`. Upload through **Groups > Edit > Custom PPTX Template**, preview both speaker counts, and choose **Save Group**.

Use the [full authoring walkthrough](PPTX-TEMPLATES.md#creating-a-template-in-powerpoint) and [complete double-curly-brace parameter list](TEMPLATE-PARAMETERS.md) rather than guessing placeholder names.
