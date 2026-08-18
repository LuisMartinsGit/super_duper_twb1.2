<#
.SYNOPSIS
    Packages a Unity build and publishes it as a GitHub release the launcher
    can serve.

.DESCRIPTION
    Zips the build, hashes it, writes the manifest the update Worker reads, and
    uploads both as assets on a new release in the private releases repo.

    The token this needs is NOT the one the Worker holds. The Worker gets a
    read-only fine-grained PAT; this script needs Contents: Read and write.
    Keep it in .env as GH_RELEASE_TOKEN (.env is already gitignored).

.EXAMPLE
    .\tools\release.ps1 -BuildPath 'D:\Builds\TWB' -Version 0.0.9 -Notes 'Multiplayer desync fixes'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BuildPath,
    [string] $Version,
    [string] $Notes = '',
    [string] $Repo = 'Ahridan/TWB-Releases',
    [string] $StagingDir = (Join-Path $env:TEMP 'twb-release')
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---------------------------------------------------------------- version

# Read from Unity rather than typed by hand. A manifest that disagrees with
# bundleVersion ships a build that misreports itself in the lobby handshake and
# in every match log, which is a miserable thing to debug from a tester report.
$bundleVersion = $null
$projectSettings = Join-Path $root '..\ProjectSettings\ProjectSettings.asset'

if (Test-Path $projectSettings) {
    $match = Select-String -Path $projectSettings -Pattern '^\s*bundleVersion:\s*(\S+)\s*$' | Select-Object -First 1
    if ($match) { $bundleVersion = $match.Matches[0].Groups[1].Value.Trim() }
}

if (-not $Version) {
    if (-not $bundleVersion) {
        throw 'Could not read bundleVersion from ProjectSettings.asset. Pass -Version explicitly.'
    }
    $Version = $bundleVersion
    Write-Host "Version from Unity Player Settings: $Version" -ForegroundColor Cyan
}
elseif ($bundleVersion -and $Version -ne $bundleVersion) {
    throw ("-Version is $Version but Unity says $bundleVersion. " +
           'Bump bundleVersion in Player Settings and rebuild, or drop -Version to use Unity.')
}

# ---------------------------------------------------------------- token

$token = $env:GH_RELEASE_TOKEN

if (-not $token) {
    $envFile = Join-Path $root '..\.env'
    if (Test-Path $envFile) {
        $line = Select-String -Path $envFile -Pattern '^GH_RELEASE_TOKEN=' | Select-Object -First 1
        if ($line) { $token = ($line.Line -split '=', 2)[1].Trim() }
    }
}

if (-not $token) {
    throw 'No GH_RELEASE_TOKEN. Set the environment variable or add it to .env.'
}

if (-not (Test-Path $BuildPath)) { throw "Build folder not found: $BuildPath" }

# ---------------------------------------------------------------- package

if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

$zipName = "TheWaningBorder-$Version.zip"
$zipPath = Join-Path $StagingDir $zipName

Write-Host "Packaging $BuildPath ..." -ForegroundColor Cyan

# Zip the CONTENTS, not the wrapping folder. The launcher tolerates either,
# but this keeps the archive layout predictable.
Compress-Archive -Path (Join-Path $BuildPath '*') -DestinationPath $zipPath -CompressionLevel Optimal

$zipItem = Get-Item $zipPath
$sizeBytes = $zipItem.Length

# 2 GiB is the hard per-asset cap on GitHub releases. Catch it here rather
# than four minutes into a failed upload.
if ($sizeBytes -ge 2GB) {
    throw "Build is $([math]::Round($sizeBytes/1GB,2)) GB, over the 2 GiB per-asset limit."
}

$sha = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()

$manifestPath = Join-Path $StagingDir 'manifest.json'
[ordered]@{
    version   = $Version
    sha256    = $sha
    sizeBytes = $sizeBytes
    notes     = $Notes
} | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "  $zipName  $([math]::Round($sizeBytes/1MB,1)) MB"
Write-Host "  sha256    $sha"

# ---------------------------------------------------------------- release

$headers = @{
    Authorization          = "Bearer $token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'twb-release-script'
}

Write-Host "Creating release v$Version in $Repo ..." -ForegroundColor Cyan

$body = @{
    tag_name = "v$Version"
    name     = "v$Version"
    body     = $Notes
    draft    = $false
} | ConvertTo-Json

$release = Invoke-RestMethod -Method Post -Headers $headers `
    -Uri "https://api.github.com/repos/$Repo/releases" `
    -ContentType 'application/json' -Body $body

$uploadBase = ($release.upload_url -split '\{')[0]

# curl.exe rather than Invoke-RestMethod: it streams the upload instead of
# buffering the whole build in memory, which matters at these sizes.
foreach ($asset in @($zipPath, $manifestPath)) {
    $name = Split-Path $asset -Leaf
    $type = if ($name -like '*.json') { 'application/json' } else { 'application/zip' }

    Write-Host "Uploading $name ..." -ForegroundColor Cyan

    & curl.exe --fail --silent --show-error --location `
        -X POST "$uploadBase`?name=$name" `
        -H "Authorization: Bearer $token" `
        -H "Content-Type: $type" `
        -H 'X-GitHub-Api-Version: 2022-11-28' `
        --data-binary "@$asset" -o NUL

    if ($LASTEXITCODE -ne 0) { throw "Upload of $name failed (curl exit $LASTEXITCODE)." }
}

Write-Host ""
Write-Host "Released v$Version." -ForegroundColor Green
Write-Host "Launchers will pick it up on their next start."
