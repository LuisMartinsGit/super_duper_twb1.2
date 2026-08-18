<#
.SYNOPSIS
    Publishes TWBLauncher.exe as a single self-contained file.

.DESCRIPTION
    Self-contained on purpose: testers must not be asked to install a .NET
    runtime before they can install the game. The result is one exe with no
    side files, which is also what makes the install-root layout work.
#>
[CmdletBinding()]
param(
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'

# Not defaulted in param(): $PSScriptRoot is not reliably bound during
# parameter default evaluation under Windows PowerShell 5.1.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutputDir) { $OutputDir = Join-Path $root 'publish' }

dotnet publish (Join-Path $root 'TWBLauncher.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $OutputDir

$exe = Join-Path $OutputDir 'TWBLauncher.exe'
if (-not (Test-Path $exe)) { throw "Publish did not produce $exe" }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Launcher published: $exe ($size MB)" -ForegroundColor Green
Write-Host "Copy this single file into the install root, beside the 'game' folder."
