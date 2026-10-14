<#
.SYNOPSIS
    Creates (or reuses) a deployment service principal for GitHub Actions and stores it as the
    AZURE_CREDENTIALS repository secret used by .github/workflows/deploy-azure.yml.

.DESCRIPTION
    Idempotent: re-running reuses the app registration and rotates its client secret.
    The principal gets Contributor plus Role Based Access Control Administrator (limited to
    assigning Storage Blob Data Contributor) on the resource group only, which is what the Bicep
    deployment (including storage-rbac.bicep) needs.

    Requires: az login with rights to create app registrations and role assignments, and gh auth login.

.PARAMETER ResourceGroupName
    Resource group the workflow deploys to.

.PARAMETER ServicePrincipalName
    Display name of the app registration.

.PARAMETER Repository
    GitHub repo (owner/name). Defaults to the current repo.

.PARAMETER SecretValidityYears
    Lifetime of the generated client secret.

.PARAMETER SkipGitHub
    Print nothing sensitive and skip 'gh secret set' (the JSON is written to -OutFile instead).

.PARAMETER OutFile
    With -SkipGitHub, path to write the credentials JSON to.
#>
[CmdletBinding()]
param(
    [string]$ResourceGroupName = "rg-meetuporganiser",
    [string]$ServicePrincipalName = "sp-meetuporganiser-deploy",
    [string]$Repository,
    [int]$SecretValidityYears = 1,
    [switch]$SkipGitHub,
    [string]$OutFile
)

$ErrorActionPreference = "Stop"

function Invoke-Az {
    $out = & az @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed: $out" }
    $out
}

$account = Invoke-Az account show -o json | ConvertFrom-Json
$subscriptionId = $account.id
$tenantId = $account.tenantId
$scope = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName"
Invoke-Az group show -n $ResourceGroupName -o none

Write-Host "Subscription: $($account.name) ($subscriptionId)"

# App registration
$appId = (Invoke-Az ad app list --display-name $ServicePrincipalName --query "[0].appId" -o tsv)
if (-not $appId) {
    Write-Host "Creating app registration '$ServicePrincipalName'..."
    $appId = (Invoke-Az ad app create --display-name $ServicePrincipalName --query appId -o tsv)
}
else { Write-Host "Reusing app registration $appId" }

# Service principal (retry while the new app replicates)
$spId = (Invoke-Az ad sp list --filter "appId eq '$appId'" --query "[0].id" -o tsv)
for ($i = 0; -not $spId -and $i -lt 6; $i++) {
    try { $spId = (Invoke-Az ad sp create --id $appId --query id -o tsv) }
    catch { Write-Host "Waiting for app replication..."; Start-Sleep -Seconds 10 }
}
if (-not $spId) { throw "Could not create service principal for $appId." }

# Role assignments on the resource group only
$rbacAdminCondition = "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {ba92f5b4-2d11-453d-a403-e96b0029c9fe})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {ba92f5b4-2d11-453d-a403-e96b0029c9fe}))"
$roles = @(
    @{ Name = "Contributor"; Condition = $null },
    @{ Name = "Role Based Access Control Administrator"; Condition = $rbacAdminCondition }
)
foreach ($role in $roles) {
    $existing = @(Invoke-Az role assignment list --assignee $spId --role $role.Name --scope $scope --query "[].id" -o tsv | Where-Object { $_ }).Count
    if ($existing -eq 0) {
        Write-Host "Assigning $($role.Name)..."
        $roleArgs = @("role", "assignment", "create", "--assignee-object-id", $spId, "--assignee-principal-type", "ServicePrincipal", "--role", $role.Name, "--scope", $scope, "-o", "none")
        if ($role.Condition) { $roleArgs += @("--condition", $role.Condition, "--condition-version", "2.0") }
        Invoke-Az @roleArgs
    }
    else { Write-Host "$($role.Name) already assigned." }
}

# New client secret
$endDate = (Get-Date).AddYears($SecretValidityYears).ToString("yyyy-MM-dd")
$secret = (Invoke-Az ad app credential reset --id $appId --end-date $endDate --query password -o tsv)

$credentials = [ordered]@{
    clientId       = $appId
    clientSecret   = $secret
    subscriptionId = $subscriptionId
    tenantId       = $tenantId
} | ConvertTo-Json

if ($SkipGitHub) {
    if (-not $OutFile) { throw "-OutFile is required with -SkipGitHub." }
    Set-Content -Path $OutFile -Value $credentials -Encoding ascii
    Write-Host "Credentials written to $OutFile (delete it after use)."
}
else {
    $ghArgs = @("secret", "set", "AZURE_CREDENTIALS")
    if ($Repository) { $ghArgs += @("--repo", $Repository) }
    $credentials | & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh secret set failed." }
    Write-Host "AZURE_CREDENTIALS secret set (client secret expires $endDate)."
}
