param(
    [switch]$LiveMeetup,
    [ValidateSet('chrome', 'msedge')][string]$Browser = 'chrome',
    [string]$Urls = 'https://localhost:7225'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $null = Get-Command node -ErrorAction Stop
    $null = Get-Command dotnet -ErrorAction Stop
    if (-not (Test-Path '.\tools\hosted-browser-prototype\node_modules\playwright')) {
        Write-Error 'Install browser worker dependencies first: npm ci --prefix .\tools\hosted-browser-prototype'
    }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:HostedBrowser__Enabled = 'true'
    $env:HostedBrowser__AllowLiveMeetup = $LiveMeetup.IsPresent.ToString().ToLowerInvariant()
    $env:BROWSER_CHANNEL = $Browser
    Write-Host "Starting local browser review. Live Meetup: $($LiveMeetup.IsPresent). Save/Publish is manual."
    Write-Host "Open $Urls and sign into Meetup Organiser. Sessions last at most 30 minutes (10 minutes input-idle)."
    dotnet run --project .\src\MeetupOrganiser.Web\MeetupOrganiser.Web.csproj -c Release --no-launch-profile --urls $Urls
    if ($LASTEXITCODE -ne 0) { throw 'The local organiser app exited with an error.' }
}
finally {
    Pop-Location
}
