<#
.SYNOPSIS
    Stores Azure Communication Services email settings in dotnet user-secrets for local development.

.DESCRIPTION
    Looks up the Communication Service and its Azure-managed sender domain in the resource group
    created by setup-azure.ps1 and writes Email:ConnectionString / Email:SenderAddress to the
    Web project's user-secrets. Nothing is written to committed files.
#>
[CmdletBinding()]
param(
    [string]$ResourceGroupName = "rg-meetuporganiser",
    [string]$SenderAddress = ""
)

$ErrorActionPreference = "Stop"
$webProject = Join-Path $PSScriptRoot "..\src\MeetupOrganiser.Web"

az extension add --name communication --only-show-errors 2>$null | Out-Null

$svc = az communication list -g $ResourceGroupName --query "[0]" -o json | ConvertFrom-Json
if (-not $svc) { throw "No Communication Service found in '$ResourceGroupName'. Run setup-azure.ps1 first." }

$connectionString = az communication list-key -g $ResourceGroupName -n $svc.name --query primaryConnectionString -o tsv
$domainId = az resource list -g $ResourceGroupName --resource-type Microsoft.Communication/EmailServices/Domains --query "[0].id" -o tsv
$sender = az resource show --ids $domainId --query properties.mailFromSenderDomain -o tsv
if (-not $sender) { throw "Could not resolve the managed sender domain." }

if (-not $SenderAddress) { $SenderAddress = "DoNotReply@$sender" }
dotnet user-secrets init --project $webProject | Out-Null
dotnet user-secrets set "Email:ConnectionString" $connectionString --project $webProject | Out-Null
dotnet user-secrets set "Email:SenderAddress" $SenderAddress --project $webProject | Out-Null

# SMTP credentials for inline RSVP calendar invites (optional: skipped if the SMTP app doesn't exist)
$smtpApp = az ad app list --filter "displayName eq 'MeetupOrganiser-Smtp'" --output json 2>$null | ConvertFrom-Json
if ($smtpApp -and $smtpApp.Count -gt 0) {
    $tenantId = az account show --query tenantId -o tsv
    # --append keeps the deployed app's secret valid
    $smtpSecret = (az ad app credential reset --id $smtpApp[0].id --append --display-name "local-dev" --query password -o tsv).Trim()
    dotnet user-secrets set "Email:Smtp:Username" "$($svc.name).$($smtpApp[0].appId).$tenantId" --project $webProject | Out-Null
    dotnet user-secrets set "Email:Smtp:Password" $smtpSecret --project $webProject | Out-Null
    Write-Host "Local SMTP configured for inline calendar invites." -ForegroundColor Green
} else {
    Write-Warning "MeetupOrganiser-Smtp app not found; calendar invites will be sent as attachments only. Run setup-azure.ps1 first."
}

Write-Host "Local email configured. Sender: $SenderAddress" -ForegroundColor Green
