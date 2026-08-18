<#
.SYNOPSIS
    Lists and downloads the match logs testers have uploaded.

.DESCRIPTION
    Logs live in R2 and are reachable only through the update Worker, behind
    the admin key. A tester key can post logs but can never read anyone else's,
    including their own - so this needs the admin key, which is in
    testers.local.md (gitignored) or TWB_ADMIN_KEY in .env.

    Downloads unzip into logs-inbox\<tester>\<match>\ beside the repo, and
    anything already there is skipped, so re-running only pulls what is new.

.EXAMPLE
    .\tools\fetch-logs.ps1
    List everything, download nothing.

.EXAMPLE
    .\tools\fetch-logs.ps1 -Download
    Pull every log not already in the inbox.

.EXAMPLE
    .\tools\fetch-logs.ps1 -Tester Hugo -Download
    Only Hugo, and only what is missing.
#>
[CmdletBinding()]
param(
    [string] $Tester,
    [switch] $Download,
    [string] $ApiBase = 'https://twb-updates.luis-resmart.workers.dev',
    [string] $Inbox
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Inbox) { $Inbox = Join-Path $root '..\logs-inbox' }

# ---------------------------------------------------------------- admin key

$adminKey = $env:TWB_ADMIN_KEY

if (-not $adminKey) {
    $envFile = Join-Path $root '..\.env'
    if (Test-Path $envFile) {
        $line = Select-String -Path $envFile -Pattern '^TWB_ADMIN_KEY=' | Select-Object -First 1
        if ($line) { $adminKey = ($line.Line -split '=', 2)[1].Trim() }
    }
}

if (-not $adminKey) {
    # -Encoding UTF8: the file carries tester names with accents, and 5.1
    # reads as system ANSI by default.
    $notes = Join-Path $root '..\testers.local.md'
    if (Test-Path $notes) {
        $inBlock = $false
        foreach ($line in (Get-Content $notes -Encoding UTF8)) {
            if ($line -match '^##\s*Admin key') { $inBlock = $true; continue }
            if ($inBlock -and $line -match '^\s*([A-Za-z0-9_\-]{20,})\s*$') { $adminKey = $Matches[1]; break }
        }
    }
}

if (-not $adminKey) {
    throw 'No admin key. Add TWB_ADMIN_KEY to .env, or keep it in testers.local.md.'
}

$headers = @{ 'X-TWB-Admin' = $adminKey; 'User-Agent' = 'twb-fetch-logs' }

# ---------------------------------------------------------------- list

$uri = "$ApiBase/logs"
if ($Tester) { $uri += "?tester=$([uri]::EscapeDataString($Tester))" }

$index = Invoke-RestMethod -Headers $headers -Uri $uri

if ($index.count -eq 0) {
    Write-Host 'No logs uploaded yet.' -ForegroundColor Yellow
    return
}

Write-Host "$($index.count) log$(if ($index.count -ne 1) { 's' }) in the bucket." -ForegroundColor Cyan
if ($index.truncated) { Write-Warning 'More logs exist than were listed.' }

# Worst first: a match that threw is the one worth opening.
$sorted = $index.objects | Sort-Object `
    @{ Expression = { [int]$_.exceptions }; Descending = $true },
    @{ Expression = { [int]$_.errors };     Descending = $true },
    @{ Expression = { $_.uploaded };        Descending = $true }

foreach ($o in $sorted) {
    $exc = [int]$o.exceptions
    $err = [int]$o.errors
    $colour = if ($exc -gt 0) { 'Red' } elseif ($err -gt 0) { 'Yellow' } else { 'Green' }

    Write-Host ''
    Write-Host ("  {0}  [{1}]" -f $o.tester, $o.match) -ForegroundColor $colour
    Write-Host ("    {0} {1} | {2} | {3} | {4}" -f $o.version, $o.fingerprint, $o.map, $o.mode, $o.outcome)
    Write-Host ("    exceptions {0} | errors {1} | warnings {2} | {3} | {4:N0} KB" -f `
        $exc, $err, $o.warnings, $o.duration, ($o.size / 1KB))
}

if (-not $Download) {
    Write-Host ''
    Write-Host 'Re-run with -Download to pull them.' -ForegroundColor DarkGray
    return
}

# ---------------------------------------------------------------- download

Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
$got = 0
$skipped = 0

foreach ($o in $sorted) {
    # The object key already sanitises the tester name, so it is safe as a path.
    $folder = Join-Path $Inbox ($o.key -replace '^logs/', '' -replace '\.zip$', '')

    if (Test-Path $folder) { $skipped++; continue }

    $tmp = Join-Path $env:TEMP ("twblog-" + [System.IO.Path]::GetRandomFileName() + ".zip")

    try {
        Invoke-WebRequest -Headers $headers -Uri "$ApiBase/$($o.key)" -OutFile $tmp
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($tmp, $folder)
        Write-Host ("  pulled {0}" -f $o.key) -ForegroundColor Green
        $got++
    }
    finally {
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
    }
}

Write-Host ''
Write-Host "$got downloaded, $skipped already present." -ForegroundColor Cyan
Write-Host "Inbox: $((Resolve-Path $Inbox).Path)"
