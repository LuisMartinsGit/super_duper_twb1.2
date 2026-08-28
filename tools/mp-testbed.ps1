<#
.SYNOPSIS
    Two game instances side by side on this PC, so multiplayer can be tested
    solo - with AI factions filling the rest of the lobby.

.DESCRIPTION
    Multiplayer needs two processes, and a desync needs BOTH sides' logs to
    diagnose. This mirrors the install into a second folder and launches both
    windowed, so each instance keeps its own logs\ tree and its own
    settings.json - the two Lockstep.log files land in separate folders ready
    to diff, instead of interleaving in one.

    Why a copy rather than two runs of the same folder: LogPaths claims a
    per-process instance slot and suffixes filenames (Console-2.log), so one
    folder does work - but the two sides' match folders then land in one tree,
    settings.json is shared so both lobby rows read the same name, and telling
    host from client afterwards is guesswork.

    Both instances MUST be the same build: the host refuses a join whose
    BuildFingerprint differs (MultiplayerPanel, TWB_JOIN). Re-run with
    -Refresh after every new build to re-mirror the peer copy.

.EXAMPLE
    .\tools\mp-testbed.ps1
    Mirror the peer copy if missing, launch both windowed side by side.

.EXAMPLE
    .\tools\mp-testbed.ps1 -Refresh
    Re-mirror the peer copy from the install first. Use after a new build.

.EXAMPLE
    .\tools\mp-testbed.ps1 -Install 'D:\TWB\game' -Width 1600 -Height 900
#>
[CmdletBinding()]
param(
    [string] $Install,
    [string] $Peer,
    [int]    $Width,
    [int]    $Height,
    [switch] $Refresh,
    [switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'
$ExeName = 'The Waning Border.exe'

# ------------------------------------------------------------------ install

if (-not $Install) {
    $candidates = @(
        (Join-Path $env:USERPROFILE 'Documents\TWB_TesterPackage_v2\TheWaningBorder_Launcher\The Waning Border\game'),
        (Join-Path $env:USERPROFILE 'Documents\TWB_TesterPackage_v2\The Waning Border\game')
    ) | Where-Object { Test-Path (Join-Path $_ $ExeName) }

    if ($candidates.Count -eq 0) {
        throw "No install found. Pass -Install <folder containing '$ExeName'>."
    }
    # Newest build wins - the data folder, not the exe, is what a patch rewrites.
    $Install = $candidates |
        Sort-Object { (Get-Item (Join-Path $_ 'The Waning Border_Data')).LastWriteTime } -Descending |
        Select-Object -First 1
}

$Install = (Resolve-Path $Install).Path
$installExe = Join-Path $Install $ExeName
if (-not (Test-Path $installExe)) { throw "No '$ExeName' in $Install" }

if (-not $Peer) { $Peer = "$Install-peer2" }

Write-Host "install : $Install"
Write-Host "peer    : $Peer"

# --------------------------------------------------------------- peer mirror

$needMirror = $Refresh -or (-not (Test-Path (Join-Path $Peer $ExeName)))

if ($needMirror) {
    Write-Host 'mirroring peer copy (this takes a moment)...'
    # /MIR so a -Refresh after a patch drops files the new build removed.
    # logs, the instance locks and settings.json stay each install's own.
    robocopy $Install $Peer /MIR /NFL /NDL /NJH /NJS /NP /XD 'logs' /XF '.instance*.lock' 'settings.json' | Out-Null
    # robocopy exit codes 0-7 are success; 8 and up is a real failure. Clear it
    # afterwards either way: a bare 1 ("files were copied") is robocopy's normal
    # success, and leaving it in $LASTEXITCODE makes the whole script look failed
    # to anything that checks.
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
    Write-Host 'mirror done.'
} else {
    Write-Host 'peer copy present - pass -Refresh to re-mirror after a new build.'
}

# ------------------------------------------------------------ window sizing

if ((-not $Width) -or (-not $Height)) {
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    if (-not $Width)  { $Width  = [int]([math]::Floor($bounds.Width / 2)) - 20 }
    if (-not $Height) { $Height = $bounds.Height - 80 }
}
Write-Host "window  : ${Width}x${Height} each"

# ------------------------------------------------------------ settings.json
#
# The window size has to go in settings.json, NOT on the command line:
# OptionsMenuUI.LoadAndApplySettings calls Screen.SetResolution from the file
# at boot, which lands after Unity has honoured -screen-width/-screen-height
# and overrides it. NameConfirmed keeps the first-run name prompt away.

function Set-InstanceSettings {
    param([string] $Folder, [string] $Name)

    $path = Join-Path $Folder 'settings.json'
    if (Test-Path $path) {
        $cfg = Get-Content $path -Raw | ConvertFrom-Json
    } else {
        $cfg = [pscustomobject]@{
            PlayerName = ''; Language = ''; GraphicsQuality = 0
            ResolutionWidth = 0; ResolutionHeight = 0; Fullscreen = 1
            MasterVolume = 100.0; MusicVolume = 0.0; NameConfirmed = $false
        }
    }

    $cfg.PlayerName       = $Name
    $cfg.NameConfirmed    = $true
    $cfg.Fullscreen       = 0
    $cfg.ResolutionWidth  = $Width
    $cfg.ResolutionHeight = $Height

    $cfg | ConvertTo-Json | Set-Content -Path $path -Encoding utf8
    Write-Host "settings: $path"
}

Set-InstanceSettings -Folder $Install -Name 'HOST'
Set-InstanceSettings -Folder $Peer    -Name 'CLIENT'

if ($NoLaunch) {
    Write-Host ''
    Write-Host 'Prepared. -NoLaunch given, so nothing was started.'
    return
}

# ----------------------------------------------------------------- launch

function Start-Instance {
    param([string] $Folder, [string] $Label)

    $exe = Join-Path $Folder $ExeName
    # The exe direct, never TWBLauncher.exe - two updaters racing across an
    # install pair is not something to debug on top of a desync.
    $p = Start-Process -FilePath $exe -WorkingDirectory $Folder -PassThru
    Write-Host "launched: $Label pid $($p.Id)"
    return $p
}

$hostProc   = Start-Instance -Folder $Install -Label 'HOST  '
$clientProc = Start-Instance -Folder $Peer    -Label 'CLIENT'

# Nudge the two windows apart. Cosmetic - if it fails the windows are still
# there to drag, so nothing here is allowed to take the script down.
try {
    $sig = '[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);'
    Add-Type -Namespace Win32 -Name Win -MemberDefinition $sig
    $deadline = (Get-Date).AddSeconds(45)
    foreach ($pair in @(, @($hostProc, 0)) + @(, @($clientProc, ($Width + 10)))) {
        $proc = $pair[0]
        $x    = $pair[1]
        while (($proc.MainWindowHandle -eq 0) -and ((Get-Date) -lt $deadline)) {
            Start-Sleep -Milliseconds 500
            $proc.Refresh()
        }
        if ($proc.MainWindowHandle -ne 0) {
            [void][Win32.Win]::MoveWindow($proc.MainWindowHandle, $x, 0, $Width, $Height, $true)
        }
    }
} catch {
    Write-Host "(window placement skipped: $($_.Exception.Message))"
}

Write-Host ''
Write-Host 'In the two windows:'
Write-Host '  HOST    Multiplayer > HOST. Pick the map, then hit the OPEN'
Write-Host '          button on an empty row to turn that slot into an AI;'
Write-Host '          the row then carries a difficulty button and a strategy'
Write-Host '          dropdown. Only the host can do this.'
Write-Host '  CLIENT  Multiplayer > JOIN. The host should appear in the browse'
Write-Host '          list; if it does not, direct connect to 127.0.0.1:7979.'
Write-Host '  HOST    Start once the client row shows up in the lobby.'
Write-Host ''
Write-Host 'Logs afterwards:'
Write-Host "  host   $Install\logs\<timestamp>_<map>_host"
Write-Host "  client $Peer\logs\<timestamp>_<map>_client0"
Write-Host 'A desync writes Desync_tick<N>_p<i>.log plus a _trace.log on BOTH'
Write-Host 'sides - the pair is what names the forked entity and field.'
