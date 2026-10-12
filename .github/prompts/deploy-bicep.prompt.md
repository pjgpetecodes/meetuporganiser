---
description: Prompt to validate and deploy Azure infrastructure using Bicep and PowerShell.
---

Deploy or validate the Meetup Organiser Azure Infrastructure:

1. **Parameters**:
   - Resource Group: `{{resourceGroup}}` (default: `rg-meetuporganiser-prod`)
   - Region: `{{location}}` (default: `uksouth`)
   - SQL Password: (auto-generated or secure prompt)
2. **Steps**:
   - Run `az bicep build --file infra/main.bicep` to ensure zero compilation warnings.
   - Run `az deployment group create` or execute `powershell -File scripts/setup-azure.ps1`.
   - Configure GitHub Secrets using `gh secret set` and `gh variable set`.
3. Provide deployment status, Web App URL, and SQL Server FQDN outputs.
