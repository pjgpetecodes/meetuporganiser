# meetuporganiser
## File storage

Uploaded logos, speaker photos and PPTX templates live in Azure Blob Storage (containers `logos` and `speaker-photos` public read; `templates` private).

- **Azure:** the App Service uses its managed identity (`Storage__AccountUrl`, role Storage Blob Data Contributor). Shared-key access is disabled on the account.
- **Local development:** run the Azurite emulator (`npm i -g azurite && azurite`). `Storage:ConnectionString` is `UseDevelopmentStorage=true`.
- **Migrating existing local uploads** (`wwwroot/storage`, ignored by git): `./scripts/migrate-storage.ps1 -AccountName <account> -SqlConnectionString "<conn>"` (or `-UseAzurite`). It uploads the files and rewrites the `/storage/...` URLs in the database. Use `-WhatIf` to preview.

## Speaker submission emails

When a speaker completes their magic-link form, the organiser gets an email (speaker, talk, event and an edit link) sent through Azure Communication Services Email.

- Set the recipient in **Settings → General → Email Notifications**, or per group under **Group Details → Organiser Email**.
- If the speaker entered a contact email, they also receive a confirmation (organiser on cc) with the meetup details and an `.ics` calendar invite attached.
- Beside the **Copy** button, the magic-link flows have an **Email link** option that emails the link to the speaker.
- **Azure:** `infra/modules/email.bicep` provisions the service with an Azure-managed sender domain, and the App Service managed identity is granted send access (`Email__Endpoint`, `Email__SenderAddress`).
- **Local development:** run `scripts/configure-local-email.ps1` (or `setup-azure.ps1 -ConfigureLocalEmail`) to store `Email:ConnectionString` and `Email:SenderAddress` in user secrets. If email isn't configured, a warning is logged and the speaker's submission still saves.
- **Custom sender domain (optional):** run `setup-azure.ps1 -EmailCustomDomain example.com [-EmailSenderUsername noreply]`. Add the DNS records shown for the domain in the Azure portal (Email Communication Service → Provision domains) and verify them, then re-run with `-EmailCustomDomainVerified` to link it and send from `noreply@example.com`. Until then the Azure-managed sender is used.

## API integrations

Setup instructions for BlueSky, X, LinkedIn, Meetup, Zoom, and Sessionize are in the [API integrations guide](docs/API-INTEGRATIONS.md).
