<#
.SYNOPSIS
  Builds dist\PCGameCoverArtSetup_<version>.exe from the .scr and .pext already in .\dist
  (run ./scripts/build-release.ps1 first; it calls this unless given -NoInstaller).
  Needs Inno Setup 6: winget install JRSoftware.InnoSetup

.EXAMPLE
  ./scripts/build-installer.ps1 -Version 2.0.0
#>
param(
    [string]$Version = $(if ($env:GITHUB_REF_NAME -match '^v(\d+\.\d+\.\d+)') { $Matches[1] } else { '0.0.0' })
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",   # winget --scope user
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",       # the usual install, and GitHub's Windows runners
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup 6 is needed to build the installer. Install it with: winget install JRSoftware.InnoSetup' }

Write-Host "==> Building installer $Version" -ForegroundColor Cyan
& $iscc /Q "/DAppVersion=$Version" "/DDistDir=$dist" "$root\installer\PCGameCoverArt.iss"
if ($LASTEXITCODE) { throw 'Installer build failed' }
