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
    [string]$ResourceGroupName = "rg-meetuporganiser"
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

dotnet user-secrets init --project $webProject | Out-Null
dotnet user-secrets set "Email:ConnectionString" $connectionString --project $webProject | Out-Null
dotnet user-secrets set "Email:SenderAddress" "DoNotReply@$sender" --project $webProject | Out-Null

Write-Host "Local email configured. Sender: DoNotReply@$sender" -ForegroundColor Green
