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

# ---------------------------------------------------------------- preflight

# Everything that can be checked cheaply is checked BEFORE packaging, because
# zipping a gigabyte and then failing on a two-second API call is a miserable
# way to find out the repo was not ready.
$preflightHeaders = @{
    Authorization          = "Bearer $token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'twb-release-script'
}

try {
    $repoInfo = Invoke-RestMethod -Headers $preflightHeaders -Uri "https://api.github.com/repos/$Repo"
}
catch {
    throw "Cannot reach $Repo with GH_RELEASE_TOKEN. Check the token and the repo name. $($_.Exception.Message)"
}

if (-not $repoInfo.permissions.push) {
    throw "GH_RELEASE_TOKEN cannot write to $Repo. It needs Contents: Read and write."
}

# A release tag needs a commit to point at; GitHub rejects the create with a
# bare "Repository is empty." otherwise.
if ($repoInfo.size -eq 0) {
    try {
        $null = Invoke-RestMethod -Headers $preflightHeaders -Uri "https://api.github.com/repos/$Repo/commits?per_page=1"
    }
    catch {
        throw "$Repo has no commits yet. Add a README to it on GitHub, then re-run."
    }
}

$existing = Invoke-RestMethod -Headers $preflightHeaders -Uri "https://api.github.com/repos/$Repo/releases?per_page=100"
if ($existing | Where-Object { $_.tag_name -eq "v$Version" }) {
    throw "v$Version is already released. Delete that release and its tag first, or bump bundleVersion."
}

# ---------------------------------------------------------------- package

if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

$zipName = "TheWaningBorder-$Version.zip"
$zipPath = Join-Path $StagingDir $zipName

Write-Host "Packaging $BuildPath ..." -ForegroundColor Cyan

# Never ship these. Unity puts the literal words DoNotShip in the Burst folder
# name; logs would hand every tester a copy of whoever built it match history,
# and the game recreates the folder on launch anyway.
$excludedTopLevel = @('logs')
$excludedPattern = '*_BurstDebugInformation_DoNotShip'

# ZipFile rather than Compress-Archive: the cmdlet is very slow on a build this
# size and is unreliable past 2 GB, and it cannot exclude a subtree. Zips the
# CONTENTS, not the wrapping folder, so the archive layout stays predictable.
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$root = (Resolve-Path $BuildPath).Path.TrimEnd('\')
$archive = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
$added = 0
$skipped = 0

try {
    foreach ($file in Get-ChildItem -Path $root -Recurse -File -Force) {
        $relative = $file.FullName.Substring($root.Length + 1)
        $top = $relative.Split('\')[0]

        if ($excludedTopLevel -contains $top -or $top -like $excludedPattern) {
            $skipped++
            continue
        }

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $relative, 'Optimal') | Out-Null
        $added++
    }
}
finally {
    $archive.Dispose()
}

Write-Host "  packed $added files, excluded $skipped"

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
