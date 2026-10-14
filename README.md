# meetuporganiser
## File storage

Uploaded logos, speaker photos and PPTX templates live in Azure Blob Storage (containers `logos` and `speaker-photos` public read; `templates` private).

- **Azure:** the App Service uses its managed identity (`Storage__AccountUrl`, role Storage Blob Data Contributor). Shared-key access is disabled on the account.
- **Local development:** run the Azurite emulator (`npm i -g azurite && azurite`). `Storage:ConnectionString` is `UseDevelopmentStorage=true`.
- **Migrating existing local uploads** (`wwwroot/storage`, ignored by git): `./scripts/migrate-storage.ps1 -AccountName <account> -SqlConnectionString "<conn>"` (or `-UseAzurite`). It uploads the files and rewrites the `/storage/...` URLs in the database. Use `-WhatIf` to preview.
