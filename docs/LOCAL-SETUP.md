# Local setup

[Documentation home](../README.md) | [Configuration](CONFIGURATION.md) | [User guide](USER-GUIDE.md) | [Troubleshooting](OPERATIONS.md)

For copyable end-to-end recipes and a catalogue of script switches, see [Quickstarts and script reference](QUICKSTARTS.md).

## Prerequisites

The examples in this guide use PowerShell from the repository root.

| Dependency | Required for |
| --- | --- |
| Git | Cloning the repository |
| .NET **10 SDK** | Building and running the app; the runtime alone is insufficient |
| SQL Server Express, LocalDB, or another reachable SQL Server | Application data; SQLite is not a supported runtime substitute |
| Node.js/npm and Azurite | Local blob storage for photos, logos, and uploaded PPTX decks |
| A modern browser | Interactive server-side Blazor, downloads, clipboard, and file uploads |
| PowerShell 7, Azure CLI, and an Azure account | Optional local email setup or use of Azure storage |
| PowerPoint or another compatible PPTX editor | Optional template authoring and checking the finished deck; not needed to run the app |

Visual Studio with .NET 10 support or VS Code with C# tooling is optional. There is no npm frontend build, Node web server, or Office/COM installation required by the application.

```powershell
git clone https://github.com/pjgpetecodes/meetuporganiser.git
Set-Location .\meetuporganiser
dotnet --list-sdks
```

## 1. Configure SQL Server

### Option A: SQL Server Express

The checked-in **Development** configuration uses:

```text
Server=.\SQLEXPRESS;Database=MeetupOrganiserDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True
```

Install SQL Server Express Database Engine with the `SQLEXPRESS` instance name and Windows authentication. Ensure the instance is running and the account running `dotnet` has permission to connect, create the database on the first run, and apply schema changes. SQL Server Management Studio or Azure Data Studio is useful but not required.

The app creates its schema at startup; you do not need to create tables manually. If the database is pre-created, grant the application account appropriate schema-change permissions for startup migrations.

### Option B: SQL Server LocalDB on Windows

Install SQL Server Express LocalDB, then override the Express connection using user secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" 'Server=(localdb)\MSSQLLocalDB;Database=MeetupOrganiserDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True' --project .\src\MeetupOrganiser.Web
```

The Infrastructure project's code-level fallback mentions LocalDB, but `appsettings.Development.json` takes precedence. You **must** override the connection if you do not have `.\SQLEXPRESS`.

### Option C: Another SQL Server, including a container

Use an existing SQL Server instance or install SQL Server using the supported procedure for your operating system. For macOS/Linux development, use a reachable SQL Server or a supported SQL Server container/host; LocalDB and Windows integrated authentication are not portable alternatives.

For a local SQL-authenticated server:

```powershell
$sqlPassword = Read-Host "Local SQL password" -MaskInput
$connection = "Server=localhost,1433;Database=MeetupOrganiserDb;User ID=sa;Password=$sqlPassword;MultipleActiveResultSets=true;Encrypt=True;TrustServerCertificate=True"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" $connection --project .\src\MeetupOrganiser.Web
Remove-Variable sqlPassword,connection
```

Use the actual login/server for your installation. `-MaskInput` requires PowerShell 7. `TrustServerCertificate=True` is for a trusted local server with a development certificate; use a valid certificate and normal certificate validation for production.

User secrets keep values out of the repository, but are not an encrypted secrets vault. Do not paste real passwords into tracked JSON files or commit exported secrets.

## 2. Start local blob storage

Install Azurite once:

```powershell
npm install --global azurite
```

The VS Code **.NET Notts Web (Development)** debug configuration starts the Azurite blob service automatically before launching the app. It reuses a running Azurite instance and stores data in the repository root, preserving existing local uploads. Stop the `start-azurite` task explicitly when you no longer need it; stopping the debugger leaves it available for the next run.

For command-line development, run this in a separate terminal before `dotnet run`:

```powershell
.\scripts\start-azurite.ps1
```

The script requires PowerShell 7 and runs the blob service in the foreground unless it is already running. Keep that terminal open. The blob endpoint is `http://127.0.0.1:10000/devstoreaccount1`. Its data files in this repository are ignored by Git. Startup fails with a clear error if Azurite is missing or another service occupies port 10000.

The checked-in configuration already contains:

```json
{
  "Storage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

Containers are created on the first upload. They are `logos`, `speaker-photos`, and `templates`; the first two allow anonymous reads of individual blobs and templates are private. The infrastructure also reserves `generated-assets`, but current event downloads are generated on demand.

If you previously set `Storage:AccountUrl` in user secrets or the environment, it takes priority over the emulator connection. Remove that override to return to Azurite:

```powershell
dotnet user-secrets remove "Storage:AccountUrl" --project .\src\MeetupOrganiser.Web
```

Also remove any `Storage__AccountUrl` environment variable in the process launching the app, if present.

### Optional: use Azure Blob Storage locally

Sign into Azure CLI and configure the account endpoint:

```powershell
az login
dotnet user-secrets set "Storage:AccountUrl" "https://<storage-account>.blob.core.windows.net" --project .\src\MeetupOrganiser.Web
```

Replace `<storage-account>`. Your developer identity needs **Storage Blob Data Contributor** on that account. The client uses `DefaultAzureCredential`; another configured development credential may be selected before the Azure CLI identity. Azure resource Contributor alone does not grant blob data access, and the provisioned storage account disables shared-key access.

Use a separate development storage account/database where possible: uploads and photo imports are real writes.

## 3. Restore and run

```powershell
dotnet restore .\MeetupOrganiser.slnx
dotnet dev-certs https --trust
dotnet run --project .\src\MeetupOrganiser.Web --launch-profile https
```

| Launch profile | Addresses |
| --- | --- |
| `https` | `https://localhost:7225` and `http://localhost:5107` |
| `http` | `http://localhost:5107` |

For the HTTP profile:

```powershell
dotnet run --project .\src\MeetupOrganiser.Web --launch-profile http
```

Both profiles set `ASPNETCORE_ENVIRONMENT=Development`, which enables user secrets. The app also has HTTPS redirection middleware; use the HTTPS address for the normal browser workflow. On systems where automatic certificate trust is unavailable, follow the .NET HTTPS development certificate instructions for your browser/OS.

For change watching:

```powershell
dotnet watch --project .\src\MeetupOrganiser.Web run --launch-profile https
```

At startup the app attempts EF Core migrations, then seeds the default group, two example speakers, a sample event, and settings/templates. The sample event is historical, so it may not appear in **Upcoming Meetups**; use **Events** to find it.

**Check startup logs as well as the browser.** Database initialisation errors are logged and do not necessarily stop the web host. A responsive home page is not proof that SQL or blob storage works.

## 4. Sign in and create your first event

With the checked-in all-zero `AzureAd:ClientId`, sign-in uses a predefined developer cookie account. There is no local password registration/reset flow. This mode is for a trusted local machine only.

1. Open <https://localhost:7225> and choose sign-in.
2. Open **Groups**, edit the seeded group or create your own, and set the group name, location, organiser email, and Meetup slug.
3. Open **Speakers** and add a speaker, or use **Events > Create Event > Quick Invite**.
4. Save an event, open **Social & Slides**, and download a PNG and PPTX.
5. Upload a test logo/headshot to confirm Azurite is working.

Continue with the [full user guide](USER-GUIDE.md) and [template authoring guide](PPTX-TEMPLATES.md).

## 5. Optional real Entra sign-in locally

Use an Entra Web app registration, its tenant ID, client ID, and a client secret. Register the exact callback for the chosen local address, for example `https://localhost:7225/signin-oidc`.

```powershell
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>" --project .\src\MeetupOrganiser.Web
dotnet user-secrets set "AzureAd:ClientId" "<client-id>" --project .\src\MeetupOrganiser.Web
$clientSecret = Read-Host "Entra client secret" -MaskInput
dotnet user-secrets set "AzureAd:ClientSecret" $clientSecret --project .\src\MeetupOrganiser.Web
Remove-Variable clientSecret
```

Restart the app after changing host authentication configuration. A non-empty client ID not starting with `00000000` selects Entra authentication. See the [production access checklist](AZURE-SETUP.md#production-access-checklist): selecting Entra is not the same as removing the developer endpoint or restricting the organiser audience.

## 6. Optional email

Email is not needed to save events or speaker submissions. There is no local mail-catcher implementation: configured local email sends **real messages** through Azure Communication Services (ACS).

After provisioning ACS with the [Azure setup](AZURE-SETUP.md):

```powershell
az login
.\scripts\configure-local-email.ps1 -ResourceGroupName "rg-meetuporganiser"
```

The script retrieves the ACS connection string and managed-domain sender, storing `Email:ConnectionString` and `Email:SenderAddress` in the Web project's user secrets. If the `MeetupOrganiser-Smtp` Entra app exists, it appends a separate local SMTP credential without replacing the deployed credential.

For a verified custom sender, supply it explicitly:

```powershell
.\scripts\configure-local-email.ps1 -ResourceGroupName "rg-meetuporganiser" -SenderAddress "noreply@example.com"
```

In **Settings > General > Email Notifications**, set the default organiser recipient. A group's **Organiser Email** overrides it. Generate a link using a test speaker email you control; link generation can automatically send an invitation.

The [email configuration reference](CONFIGURATION.md#email) explains API/SMTP selection and calendar-invite fallbacks.

## 7. Optional platform integrations

All platforms can be configured independently; you do not need every integration to use the app.

Follow [API integrations](API-INTEGRATIONS.md). Save the Settings form before connecting Meetup or posting. For a local HTTPS session, register and save `https://localhost:7225/integrations/meetup/callback` rather than leaving the default HTTP callback.

## Builds and tests

From the repository root:

```powershell
dotnet build .\MeetupOrganiser.slnx --configuration Release
dotnet test .\tests\MeetupOrganiser.Tests\MeetupOrganiser.Tests.csproj --configuration Release --no-build
```

Optional coverage, matching the build workflow:

```powershell
dotnet test .\tests\MeetupOrganiser.Tests\MeetupOrganiser.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage"
```

There is no separate frontend dependency restore. The bundled decks and fonts are copied by MSBuild.

## Database migrations for contributors

Normal use relies on startup migrations. For explicit EF commands, install the .NET 10 EF tool if it is not already available:

```powershell
dotnet tool install --global dotnet-ef --version "10.*"
```

**Important:** the design-time context factory reads `ConnectionStrings__DefaultConnection` from the environment, otherwise it uses `.\SQLEXPRESS`. It does not read the Web project's user secrets. Set the correct target in the same PowerShell terminal before using EF:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=.\SQLEXPRESS;Database=MeetupOrganiserDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True'
dotnet ef database update --project .\src\MeetupOrganiser.Infrastructure --startup-project .\src\MeetupOrganiser.Web
Remove-Item Env:\ConnectionStrings__DefaultConnection
```

For a deliberate schema change, use the same environment override and project selectors with `dotnet ef migrations add <DescriptiveName>`. Review the migration before applying it. Never delete or recreate a database containing data you need just to bypass a migration error.

## Moving existing uploads

Older installations may have files under `src\MeetupOrganiser.Web\wwwroot\storage`. Use the [legacy upload migration procedure](OPERATIONS.md#migrating-legacy-local-uploads) to move them into Azurite or Azure and update database URLs. Copying the database alone does not copy files.
