<#
.SYNOPSIS
    Publishes TWBLauncher.exe as a single self-contained file.

.DESCRIPTION
    Self-contained on purpose: testers must not be asked to install a .NET
    runtime before they can install the game. The result is one exe with no
    side files, which is also what makes the install-root layout work.

    -Edition picks which build the launcher installs (A = demo, B, C — see
    Edition.cs). Each edition publishes to publish\<Edition>\, which is
    where AlphaBuildPostProcess picks up the launcher a build of that same
    edition carries. A demo build must never carry a developer launcher.
#>
[CmdletBinding()]
param(
    [ValidateSet('A', 'B', 'C')]
    [string] $Edition = 'A',
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

# Not defaulted in param(): $PSScriptRoot is not reliably bound during
# parameter default evaluation under Windows PowerShell 5.1.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutputDir) { $OutputDir = Join-Path (Join-Path $root 'publish') $Edition }

dotnet publish (Join-Path $root 'TWBLauncher.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    "-p:TwbEdition=$Edition" `
    -o $OutputDir

$exe = Join-Path $OutputDir 'TWBLauncher.exe'
if (-not (Test-Path $exe)) { throw "Publish did not produce $exe" }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Launcher (edition $Edition) published: $exe ($size MB)" -ForegroundColor Green
$installName = if ($Edition -eq 'A') { 'The Waning Border' } else { "The Waning Border $Edition" }
Write-Host "Testers run it from anywhere: it installs into %LOCALAPPDATA%\Programs\Shardroot Entertainment\$installName."
Write-Host "Builds carry it (AlphaBuildPostProcess), so the next release also updates every tester's launcher."
