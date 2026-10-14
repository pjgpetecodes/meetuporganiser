<#
.SYNOPSIS
    One-off migration of locally stored uploads (wwwroot/storage) to Blob Storage, rewriting the
    relative /storage/... URLs in the database.

.PARAMETER AccountName
    Target storage account (uses 'az login' credentials, i.e. Storage Blob Data Contributor).

.PARAMETER UseAzurite
    Target the local Azurite emulator instead of an Azure account.

.PARAMETER SqlConnectionString
    Connection string of the database whose URLs should be rewritten. Omit to only upload files.

.PARAMETER WhatIf
    Show what would be uploaded/updated without changing anything.

.EXAMPLE
    ./scripts/migrate-storage.ps1 -AccountName stmeetuporg123 -SqlConnectionString "Server=tcp:...;..."
.EXAMPLE
    ./scripts/migrate-storage.ps1 -UseAzurite -SqlConnectionString "Server=.\SQLEXPRESS;Database=MeetupOrganiserDb;Trusted_Connection=True;TrustServerCertificate=True"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$AccountName,
    [switch]$UseAzurite,
    [string]$SqlConnectionString,
    [string]$StoragePath = (Join-Path $PSScriptRoot "..\src\MeetupOrganiser.Web\wwwroot\storage")
)

$ErrorActionPreference = "Stop"

if (-not $UseAzurite -and -not $AccountName) { throw "Specify -AccountName or -UseAzurite." }
if (-not (Test-Path $StoragePath)) { throw "Storage path not found: $StoragePath" }

$azArgs = if ($UseAzurite) { @("--connection-string", "UseDevelopmentStorage=true") } else { @("--account-name", $AccountName, "--auth-mode", "login") }
$baseUrl = if ($UseAzurite) { "http://127.0.0.1:10000/devstoreaccount1" } else { "https://$AccountName.blob.core.windows.net" }

$publicAccess = @{ "logos" = "blob"; "speaker-photos" = "blob"; "templates" = "off" }
$contentTypes = @{
    ".png" = "image/png"; ".jpg" = "image/jpeg"; ".jpeg" = "image/jpeg"; ".webp" = "image/webp"
    ".pptx" = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
}

foreach ($dir in Get-ChildItem $StoragePath -Directory) {
    $container = $dir.Name
    $access = if ($publicAccess.ContainsKey($container)) { $publicAccess[$container] } else { "off" }

    if ($PSCmdlet.ShouldProcess($container, "Create container (public access: $access)")) {
        az storage container create --name $container --public-access $access @azArgs --only-show-errors | Out-Null
    }

    foreach ($file in Get-ChildItem $dir.FullName -File) {
        $type = $contentTypes[$file.Extension.ToLower()]
        if (-not $type) { $type = "application/octet-stream" }
        if ($PSCmdlet.ShouldProcess("$container/$($file.Name)", "Upload ($type)")) {
            az storage blob upload --container-name $container --name $file.Name --file $file.FullName `
                --content-type $type --overwrite true @azArgs --only-show-errors | Out-Null
        }
    }
}

if ($SqlConnectionString) {
    $prefix = "$baseUrl/"
    $updates = @(
        @{ Table = "MeetupGroups"; Column = "LogoBlobUrl" },
        @{ Table = "MeetupGroups"; Column = "PptxTemplateBlobUrl" },
        @{ Table = "Speakers";     Column = "PhotoBlobUrl" },
        @{ Table = "Sponsor";      Column = "LogoBlobUrl" }
    )

    $conn = New-Object System.Data.SqlClient.SqlConnection $SqlConnectionString
    $conn.Open()
    try {
        foreach ($u in $updates) {
            $sql = "UPDATE [$($u.Table)] SET [$($u.Column)] = @prefix + SUBSTRING([$($u.Column)], 10, 1000) WHERE [$($u.Column)] LIKE '/storage/%'"
            if ($PSCmdlet.ShouldProcess("$($u.Table).$($u.Column)", "Rewrite /storage/ URLs to $prefix")) {
                $cmd = $conn.CreateCommand()
                $cmd.CommandText = $sql
                [void]$cmd.Parameters.AddWithValue("@prefix", $prefix)
                Write-Host "$($u.Table).$($u.Column): $($cmd.ExecuteNonQuery()) row(s) updated"
            }
        }
    }
    finally { $conn.Close() }
}

Write-Host "Done."
