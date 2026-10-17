# Configuration reference

[Documentation home](../README.md) | [Local setup](LOCAL-SETUP.md) | [Azure setup](AZURE-SETUP.md)

## Sources and precedence

Host configuration uses ASP.NET Core's normal providers: `appsettings.json`, environment-specific JSON, development user secrets, environment variables, and command-line arguments. Later/higher-priority providers override earlier ones. User secrets are loaded in **Development**, not automatically in Production.

Integration/general settings accessed through `SettingsService` have an additional rule: **a non-blank database value wins over host configuration**. Clearing a field and saving allows the service to fall back to configuration; it does not necessarily disable an integration if credentials remain in the environment.

For example, a saved BlueSky app password overrides `Integrations__BlueSky__AppPassword`. To rotate a configuration-backed credential, clear the saved override too, or update the saved value.

Group-specific values can override global settings for organiser email, Meetup group slug, bio preference, and Meetup description template. Group social text and PPTX templates are configured on the group, not through host configuration.

Meetup has a subtle credential rule: a group legacy-token override is selected before global legacy credentials, but an available connected OAuth token takes precedence over that selected token. There is currently one shared OAuth connection, not a separate connection per group.

### Naming and secret handling

Use `:` in .NET configuration/user-secret keys and `__` in environment/App Service setting names:

```powershell
dotnet user-secrets set "Integrations:Zoom:DefaultMeetingUrl" "https://zoom.us/j/<meeting-id>" --project .\src\MeetupOrganiser.Web
```

The corresponding environment variable is `Integrations__Zoom__DefaultMeetingUrl`.

Settings entered in the app are stored in SQL. Password masking and `AppSetting.IsSecret` are **not application-level encryption** of those settings. The connected Meetup OAuth token set is a separate exception: it is protected with ASP.NET Core Data Protection.

Restrict organiser access, protect database backups, and prefer secret references for production. Do not commit credentials, connection strings containing passwords, publish profiles, or exported user secrets.

## Database

| Key | Purpose/default |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server application connection |
| `ConnectionStrings__DefaultConnection` | Environment equivalent; also used directly by the EF design-time factory |

Development JSON uses `.\SQLEXPRESS`, database `MeetupOrganiserDb`, Windows authentication, and trusted local certificates. Without a configured connection, service registration has a LocalDB fallback; do not rely on this when Development JSON is present.

Azure Bicep sets the application connection to the provisioned SQL database using SQL authentication. SQL access is **not** managed-identity based in the current templates.

## Authentication and hosting

| Key | Purpose/default |
| --- | --- |
| `AzureAd:Instance` | Entra authority base URL, normally `https://login.microsoftonline.com/` |
| `AzureAd:TenantId` | Entra tenant ID; checked-in default is `common`, Azure deployment uses the subscription tenant |
| `AzureAd:ClientId` | Real app registration selects Entra; blank/all-zero-prefix selects developer cookie fallback |
| `AzureAd:ClientSecret` | Web app registration client secret |
| `AzureAd:Domain` | Tenant domain metadata in the checked-in configuration |
| `AzureAd:CallbackPath` | `/signin-oidc` |
| `AzureAd:SignedOutCallbackPath` | `/signout-callback-oidc` |
| `ASPNETCORE_ENVIRONMENT` | `Development` in local launch profiles; `Production` in Bicep |
| `AllowedHosts` | Checked-in value is `*`; narrow for your hosting policy if appropriate |

Register the full callback URL for each hostname/scheme you use. Entra and Meetup OAuth have **different** callback paths.

The current organiser policy only requires an authenticated user; it does not check a named organiser role or per-group ownership. See [production access](AZURE-SETUP.md#production-access-checklist).

## Storage

| Key | Behaviour |
| --- | --- |
| `Storage:AccountUrl` | Blob service URL; when non-blank, uses `DefaultAzureCredential` and wins over connection strings |
| `ConnectionStrings:Storage` | Optional blob connection string; wins over `Storage:ConnectionString` if supplied |
| `Storage:ConnectionString` | Local default: `UseDevelopmentStorage=true` |

The current callers use fixed container names:

| Container | Contents | Access |
| --- | --- | --- |
| `logos` | Group/sponsor logos | Individual blobs publicly readable |
| `speaker-photos` | Uploaded/localised headshots | Individual blobs publicly readable |
| `templates` | Group PPTX uploads | Private; the app downloads with its storage credentials |
| `generated-assets` | Reserved infrastructure container | Public blob access in the current Bicep; current asset downloads do not persist here |

Names such as `Storage:ContainerSpeakerPhotos`, `Storage:ContainerTemplates`, and `Storage:ContainerAssets` appear in checked-in JSON, while Bicep also sets `Storage:ContainerName`, `Storage:PhotosContainer`, and `Storage:AssetsContainer`. **These do not rename the containers used by the current upload/template call sites.** Use the fixed names above unless the implementation is changed consistently.

Azure storage disables shared-key access and grants the App Service identity **Storage Blob Data Contributor**. Local Azurite uses its development credentials instead.

## Email

These settings come from host configuration, not the Settings page:

| Key | Purpose |
| --- | --- |
| `Email:Endpoint` | ACS endpoint for `DefaultAzureCredential` authentication in Azure |
| `Email:ConnectionString` | ACS connection string, normally for local development; takes precedence over endpoint authentication when present |
| `Email:SenderAddress` | Verified/linked sender address; required for sending |
| `Email:Smtp:Username` | ACS SMTP username, normally `<acs-name>.<smtp-client-id>.<tenant-id>` |
| `Email:Smtp:Password` | SMTP Entra app client secret |
| `Email:Smtp:Host` | Optional SMTP hostname; default `smtp.azurecomm.net`, port 587 with STARTTLS |

ACS API email requires a sender plus either an endpoint or connection string. A missing configuration logs a warning and returns "not sent"; speaker submission still saves.

Invitations and organiser notifications use the ACS Email API. Speaker confirmations have an `.ics` attachment. When an organiser email is known and SMTP is configured, confirmations use inline `text/calendar; method=REQUEST` to enable RSVP controls in compatible email clients. An SMTP exception falls back to the ACS API and attachment. Confirmations also include Google/Outlook add-to-calendar links.

The app sends requests/invitations; it does not ingest RSVP replies or track calendar acceptance.

### Notification recipient and text settings

These can be saved through the app:

| Key | UI/use |
| --- | --- |
| `Notifications:OrganiserEmail` | Settings > General > Email Notifications; group email wins when supplied |
| `Integrations:Bio:Preference` | Settings > General > Speaker Bios; `ThirdPerson` (default) or `Original`, with optional group override |
| `Integrations:Meetup:DescriptionTemplate` | Global Meetup description template, with optional group override |

The setting keys describe integration content; they do not configure the email transport.

## Platform integrations

See [API integrations](API-INTEGRATIONS.md) for account creation, permissions, callback URLs, and full per-platform keys.

| Prefix | Required credentials or alternative |
| --- | --- |
| `Integrations:BlueSky` | `Handle`, `AppPassword` |
| `Integrations:Twitter` | `ApiKey`, `ApiSecret`, `AccessToken`, `AccessTokenSecret` |
| `Integrations:LinkedIn` | `AccessToken`, `AuthorUrn`; optional recorded `ClientId`, `ClientSecret` |
| `Integrations:Meetup` | OAuth `ClientId`, `ClientSecret`, `RedirectUri`, default `Urlname`; optional legacy `ApiKey` |
| `Integrations:Zoom` | `AccountId`, `ClientId`, `ClientSecret`, or `DefaultMeetingUrl` and optional `DefaultPasscode` |
| `Integrations:Sessionize` | `DefaultEventId` for public API imports |

`Integrations:Meetup:OAuthTokenSet` is app-managed protected state, not a credential to populate manually. The default Meetup callback is `http://localhost:5107/integrations/meetup/callback`; change it for HTTPS or Azure.

Status badges generally indicate credentials are present, not a completed live credential/permission check.

## Logging and telemetry

| Key | Purpose |
| --- | --- |
| `Logging:LogLevel:Default` | Default application log level (`Information`) |
| `Logging:LogLevel:Microsoft.AspNetCore` | Framework log level (`Warning`) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Application Insights connection; supplied by Bicep |
| `ApplicationInsights:ConnectionString` | Alternative connection key recognised by startup |

Application Insights registration is conditional on a non-blank connection string. Without one, local development still writes application logs to the console.

Treat logs as sensitive operational data: they may contain API error responses and submission URLs/tokens from failure paths. Redact them before sharing and restrict retention/access.
