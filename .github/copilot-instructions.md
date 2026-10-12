# Meetup Organiser - GitHub Copilot Instructions

## Repository Overview
**Meetup Organiser** is a cloud-native meetup coordination hub built with **ASP.NET Core Blazor Web App (.NET 10)**, hosted on **Azure App Service** with an **Azure SQL Database (Serverless)** backend.
The platform enables community leaders to plan meetups, generate presentation slide decks (**PPTX**) and 1280x720 social cards (**PNG**), dispatch speaker magic links, and coordinate announcements across **Meetup.com**, **LinkedIn**, **X (Twitter)**, and **BlueSky**.

---

## Architectural Principles & Conventions

### 1. Solution Structure (.slnx)
- `src/MeetupOrganiser.Core`: Domain entities, value objects, interfaces, and integration DTOs. Zero third-party cloud SDK dependencies.
- `src/MeetupOrganiser.Infrastructure`: EF Core `AppDbContext`, OpenXML presentation generator, SkiaSharp image renderer, Blob storage client, and external HTTP clients.
- `src/MeetupOrganiser.Web`: Blazor Web App (.NET 10), interactive server components, Entra ID / Cookie auth pipelines, and minimal API endpoints.
- `tests/MeetupOrganiser.Tests`: xUnit unit and integration tests covering generators, services, and tokens.

### 2. .NET 10 & C# 13+ Standards
- Nullable reference types (`<Nullable>enable</Nullable>`) and implicit usings are enforced.
- File-scoped namespaces (`namespace MeetupOrganiser.Core.Entities;`).
- Expression-bodied members for short methods and properties.
- Asynchronous methods always accept `CancellationToken cancellationToken = default`.
- Modern collection initializers (`[]`) and record types for DTOs.

### 3. Slide & Image Generation (Headless & Cross-Platform)
- **No COM / Microsoft Office dependencies**: PowerPoint decks must be generated exclusively via `DocumentFormat.OpenXml` manipulating slide parts directly so they run headlessly in Azure Linux/Windows containers.
- **SkiaSharp for 2D Rendering**: Social images are rendered at exact 1280x720 resolution using `SkiaSharp`. Always use `SKFont` and `SKCanvas.DrawText(text, x, y, SKTextAlign, font, paint)` (SkiaSharp 4.x standard).

### 4. Speaker Magic Links
- Never store raw tokens in the database. Tokens are cryptographically generated (32 bytes / 256 bits) and stored as SHA-256 hashes.
- Magic link routes (`/speaker/submit/{Token}`) are unauthenticated and validate token freshness (`ExpiresAtUtc > UtcNow` and `UsedAtUtc == null`).
- Once submitted, tokens are marked as used and cannot be replayed.

### 5. Multi-Channel Social Integrations & Intent Fallbacks
- Every social network (LinkedIn, X/Twitter, BlueSky, Meetup) must support both automated API posting AND web intent / clipboard fallbacks.
- Web intent URLs:
  - Twitter: `https://twitter.com/intent/tweet?text={encodedText}`
  - LinkedIn: `https://www.linkedin.com/feed/?shareActive=true&text={encodedText}`
  - BlueSky: `https://bsky.app/intent/compose?text={encodedText}`

### 6. Infrastructure as Code (Bicep) & Azure
- All Azure services are declared in modular Bicep templates under `infra/` (`main.bicep`, `modules/appservice.bicep`, `modules/sql.bicep`, `modules/storage.bicep`, `modules/keyvault.bicep`, `modules/appinsights.bicep`).
- Azure SQL Database must use the Serverless tier (`GP_S_Gen5_1`) with auto-pause to minimize running costs.
- Secrets are stored in Azure Key Vault and referenced securely in App Service configuration.
- Setup CLI commands are documented and executable via `scripts/setup-azure.ps1`.
