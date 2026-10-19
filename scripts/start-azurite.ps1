#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$endpoint = "http://127.0.0.1:10000"
Write-Host "Checking local Azurite blob service..."

try {
    $response = Invoke-WebRequest -Uri $endpoint -SkipHttpErrorCheck -TimeoutSec 3
}
catch [System.Net.Http.HttpRequestException] {
    if ($_.Exception.HttpRequestError -ne [System.Net.Http.HttpRequestError]::ConnectionError) {
        throw
    }
    $response = $null
}

if ($null -ne $response) {
    if ($response.Headers.Server -notmatch "Azurite-Blob") {
        throw "Port 10000 is in use by a service other than Azurite. Stop that service or start Azurite manually on the expected endpoint."
    }
    Write-Host "Azurite blob service ready (already running)."
    return
}

$azurite = Get-Command azurite-blob -ErrorAction SilentlyContinue
if ($null -eq $azurite) {
    throw "Azurite is not installed. Run 'npm install --global azurite', then start debugging again."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Write-Host "Starting Azurite blob service with data in $repoRoot"
& $azurite.Source --blobHost 127.0.0.1 --blobPort 10000 --location $repoRoot
if ($LASTEXITCODE -ne 0) {
    throw "Azurite exited with code $LASTEXITCODE."
}
