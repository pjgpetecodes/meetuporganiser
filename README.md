# Meetup Organiser

Meetup Organiser is a meetup coordination app built with **ASP.NET Core Blazor Web App (.NET 10)**, SQL Server, and Azure Blob Storage. Organisers can manage multiple groups, collect speaker details through single-use links, import Sessionize and Meetup data, generate PowerPoint decks and social images, and publish announcements.

PowerPoint generation is headless: Microsoft Office is **not** required on the server. Decks use Open XML; image generation and approximate slide previews use SkiaSharp.

## Documentation

| Guide | Contents |
| --- | --- |
| [Quickstarts and script reference](docs/QUICKSTARTS.md) | Copyable local/Azure/CI/email/migration recipes and every setup script's parameters |
| [Local setup](docs/LOCAL-SETUP.md) | Prerequisites, SQL Server/LocalDB, Azurite, configuration, first run, email, builds, and database migrations |
| [Azure setup and deployment](docs/AZURE-SETUP.md) | Permissions, infrastructure, Entra sign-in, publishing the app, GitHub Actions, email domains, and production checks |
| [Application user guide](docs/USER-GUIDE.md) | Every main screen, groups, sponsors, speakers, events, invitations, imports, publishing, and downloads |
| [PPTX template authoring](docs/PPTX-TEMPLATES.md) | Creating and uploading a deck, all supported tokens, headshots, one/two-speaker layouts, next-event slides, and social cards |
| [Double-curly-brace parameters](docs/TEMPLATE-PARAMETERS.md) | Complete `{{...}}` lists for PPTX text/photos, Meetup descriptions, and social announcements |
| [API integrations](docs/API-INTEGRATIONS.md) | BlueSky, X, LinkedIn, Meetup OAuth, Zoom, and Sessionize credentials and fallbacks |
| [Configuration reference](docs/CONFIGURATION.md) | Configuration precedence, connection strings, authentication, storage, email, notification, and integration keys |
| [Operations and troubleshooting](docs/OPERATIONS.md) | Backups, upgrades, migrations, storage moves, logs, common problems, and current limitations |

**Start here:** choose a [quickstart](docs/QUICKSTARTS.md), or follow the detailed [local](docs/LOCAL-SETUP.md) / [Azure](docs/AZURE-SETUP.md) setup, then the [first-event walkthrough](docs/USER-GUIDE.md#first-event-walkthrough).

> **Before public production use:** read the [production access checklist](docs/AZURE-SETUP.md#production-access-checklist). The current implementation includes developer sign-in, broad authenticated-user access, and public asset download routes. Provisioning Azure resources is not, by itself, production hardening.

## Local quick start

Install the .NET 10 SDK, SQL Server Express with an instance named `SQLEXPRESS` (or configure another SQL Server), and Node.js/npm for Azurite. Run these PowerShell commands from the repository root:

```powershell
dotnet restore .\MeetupOrganiser.slnx
dotnet dev-certs https --trust
npm install --global azurite
```

Start the **.NET Notts Web (Development)** configuration in VS Code to automatically start Azurite and launch the app. For command-line development, run `.\scripts\start-azurite.ps1` in a separate PowerShell 7 terminal, then:

```powershell
dotnet run --project .\src\MeetupOrganiser.Web --launch-profile https
```

Open <https://localhost:7225>. With the checked-in placeholder Entra client ID, sign-in uses the local developer account. Startup applies database migrations and seeds demonstration data; the SQL login must be able to create/update the database. See [local setup](docs/LOCAL-SETUP.md) for connection overrides and failure diagnosis.

## What the app does

- Manage groups, sponsor records, logos, description templates, and per-group PPTX decks.
- Maintain a reusable speaker directory, including photos, social profiles, original bios, and editable third-person bios.
- Plan one- or two-speaker events; collect profiles and talks through expiring, single-use magic links.
- Read public Sessionize profiles/talks and import past Meetup events.
- Generate PPTX decks and social PNGs on demand, with custom-template support.
- Create Zoom meetings or reuse a fixed meeting link.
- Publish through configured APIs, or prepare copyable text and web-compose fallbacks.
- Email speaker links, organiser notifications, and speaker confirmations with calendar invites through Azure Communication Services.

Posting and meeting creation are explicit actions, not a background scheduler. A Bitly-style short URL is suggested from a naming stem; the app does not create the redirect in Bitly.

## Repository layout

| Location | Purpose |
| --- | --- |
| `src\MeetupOrganiser.Core` | Domain entities, interfaces, DTOs, calendar/text formatting |
| `src\MeetupOrganiser.Infrastructure` | EF Core, migrations, external clients, blob/email services, deck/image generation |
| `src\MeetupOrganiser.Web` | Blazor screens, authentication, HTTP endpoints, configuration |
| `tests\MeetupOrganiser.Tests` | xUnit tests |
| `pptx` | Bundled fallback decks, copied into build/publish output |
| `infra` | Modular Bicep infrastructure; deploy `main.bicep` |
| `scripts` | Azure provisioning, GitHub deployment credentials, local email, legacy upload migration |
| `docs` | Setup, user, template, and operations guides |

## Build and test

```powershell
dotnet build .\MeetupOrganiser.slnx --configuration Release
dotnet test .\tests\MeetupOrganiser.Tests\MeetupOrganiser.Tests.csproj --configuration Release --no-build
```

See [LICENSE](LICENSE) for the repository licence.
