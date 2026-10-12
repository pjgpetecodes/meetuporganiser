<#
.SYNOPSIS
    Provisions Azure Infrastructure for Meetup Organiser using Bicep.

.DESCRIPTION
    Automates resource group creation, Azure resource provider registration,
    Bicep deployment, and optional GitHub repository secrets configuration.

.PARAMETER ResourceGroupName
    Name of the Azure Resource Group to create or deploy to.

.PARAMETER Location
    Azure region (e.g. uksouth, westeurope, eastus).

.PARAMETER SqlAdminPassword
    Strong password for the Azure SQL Server administrator.

.PARAMETER ConfigureGitHubSecrets
    Switch to automatically configure GitHub Actions repository secrets and variables using `gh`.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName = "rg-meetuporganiser",

    [Parameter(Mandatory = $false)]
    [string]$Location = "northeurope",

    [Parameter(Mandatory = $false)]
    [string]$SqlAdminPassword = "",

    [Parameter(Mandatory = $false)]
    [string]$EntraClientId = "",

    [Parameter(Mandatory = $false)]
    [string]$EntraClientSecret = "",

    [Parameter(Mandatory = $false)]
    [switch]$ConfigureGitHubSecrets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     Meetup Organiser - Azure Infrastructure Setup       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Verify Azure CLI is installed and authenticated
Write-Host "`n[1/6] Checking Azure CLI authentication..." -ForegroundColor Yellow
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Error "Not logged into Azure CLI. Please run 'az login' first."
}
Write-Host "Connected to Subscription: $($account.name) ($($account.id))" -ForegroundColor Green

# 2. Generate strong SQL password if not provided
if ([string]::IsNullOrWhiteSpace($SqlAdminPassword)) {
    Write-Host "`n[2/7] Generating secure SQL Administrator password..." -ForegroundColor Yellow
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $SqlAdminPassword = "Mo!" + [Convert]::ToBase64String($bytes).Replace('+', 'X').Replace('/', 'Y').Replace('=', 'Z') + "2026#"
    Write-Host "Generated SQL Admin Password." -ForegroundColor Green
}

# 3. Register resource providers
Write-Host "`n[3/7] Ensuring required Azure Resource Providers are registered..." -ForegroundColor Yellow
$providers = @("Microsoft.Web", "Microsoft.Sql", "Microsoft.Storage", "Microsoft.KeyVault", "Microsoft.Insights", "Microsoft.OperationalInsights")
foreach ($provider in $providers) {
    Write-Host "  Registering $provider..." -ForegroundColor Gray
    az provider register --namespace $provider --wait --output none
}

# 4. Create or reuse Microsoft Entra ID App Registration
if ([string]::IsNullOrWhiteSpace($EntraClientId)) {
    Write-Host "`n[4/7] Ensuring Microsoft Entra ID App Registration exists..." -ForegroundColor Yellow
    $existingApp = az ad app list --filter "displayName eq 'MeetupOrganiser'" --output json 2>$null | ConvertFrom-Json
    if ($existingApp -and $existingApp.Count -gt 0) {
        $EntraClientId = $existingApp[0].appId
        $appObjectId = $existingApp[0].id
        Write-Host "Found existing App Registration: $EntraClientId" -ForegroundColor Green
    } else {
        $newApp = az ad app create --display-name "MeetupOrganiser" --output json | ConvertFrom-Json
        $EntraClientId = $newApp.appId
        $appObjectId = $newApp.id
        Write-Host "Created new App Registration: $EntraClientId" -ForegroundColor Green
    }

    if ([string]::IsNullOrWhiteSpace($EntraClientSecret)) {
        Start-Sleep -Seconds 2
        $EntraClientSecret = (az ad app credential reset --id $appObjectId --query password -o tsv).Trim()
        Write-Host "Generated Entra Client Secret." -ForegroundColor Green
    }
}

# 5. Create Resource Group
Write-Host "`n[5/7] Creating Resource Group '$ResourceGroupName' in '$Location'..." -ForegroundColor Yellow
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "Resource Group ready." -ForegroundColor Green

# 6. Deploy Bicep template
Write-Host "`n[6/7] Deploying Bicep infrastructure (main.bicep)..." -ForegroundColor Yellow
$bicepPath = Join-Path $PSScriptRoot "..\infra\main.bicep"

$deploymentParams = @(
    "sqlAdministratorLoginPassword=$SqlAdminPassword"
)
if (-not [string]::IsNullOrWhiteSpace($EntraClientId)) {
    $deploymentParams += "entraClientId=$EntraClientId"
}
if (-not [string]::IsNullOrWhiteSpace($EntraClientSecret)) {
    $deploymentParams += "entraClientSecret=$EntraClientSecret"
}

$deployJson = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $bicepPath `
    --parameters $deploymentParams `
    --output json

if ($LASTEXITCODE -ne 0) {
    Write-Error "Bicep deployment failed with exit code $LASTEXITCODE"
}

$deployOutput = $deployJson | ConvertFrom-Json
$outputs = $deployOutput.properties.outputs
$webAppName = $outputs.webAppName.value
$webAppUrl = $outputs.webAppUrl.value
$sqlServerFqdn = $outputs.sqlServerFqdn.value
$sqlDbName = $outputs.sqlDatabaseName.value
$storageAccountName = $outputs.storageAccountName.value
$keyVaultName = $outputs.keyVaultName.value

Write-Host "`n================ Deployment Completed ================" -ForegroundColor Green
Write-Host "Web App Name:         $webAppName"
Write-Host "Web App URL:          $webAppUrl"
Write-Host "Azure SQL Server:     $sqlServerFqdn"
Write-Host "Azure SQL Database:   $sqlDbName"
Write-Host "Storage Account:      $storageAccountName"
Write-Host "Key Vault:            $keyVaultName"
Write-Host "======================================================"

# 6. Configure GitHub Secrets if requested or `gh` is available
if ($ConfigureGitHubSecrets) {
    Write-Host "`n[6/6] Configuring GitHub Repository Secrets and Variables via GitHub CLI..." -ForegroundColor Yellow
    $ghAuth = gh auth status 2>&1
    if ($LASTEXITCODE -eq 0) {
        gh variable set AZURE_WEBAPP_NAME --body $webAppName
        gh variable set AZURE_RESOURCE_GROUP --body $ResourceGroupName
        gh variable set AZURE_SQL_SERVER --body $sqlServerFqdn
        gh variable set AZURE_SQL_DB --body $sqlDbName
        gh secret set AZURE_SQL_PASSWORD --body $SqlAdminPassword
        Write-Host "GitHub Variables & Secrets successfully configured!" -ForegroundColor Green
    } else {
        Write-Warning "GitHub CLI (gh) is not authenticated. Skipping automatic GitHub secret configuration."
    }
} else {
    Write-Host "`nTip: Run with -ConfigureGitHubSecrets to automatically populate GitHub Secrets & Variables." -ForegroundColor Cyan
}
