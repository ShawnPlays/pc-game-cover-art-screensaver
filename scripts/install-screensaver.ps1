<#
.SYNOPSIS
  Installs PCGameCoverArt.scr so it appears in Windows' Screen Saver Settings list,
  then opens that dialog so you can select it and set the wait time.
  Also upgrades: it replaces an older copy, and your settings are kept.
  Run from an elevated (Administrator) 64-bit PowerShell.

.EXAMPLE
  ./scripts/install-screensaver.ps1 -Path .\dist\PCGameCoverArt.scr
#>
param(
    [string]$Path = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\PCGameCoverArt.scr')
)

$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Please run this from an Administrator PowerShell (copying into System32 needs it).' }
if (-not [Environment]::Is64BitProcess) { throw 'Please use 64-bit PowerShell; 32-bit PowerShell would copy into SysWOW64 instead.' }
if (-not (Test-Path $Path)) { throw "Can't find $Path. Build it first with ./scripts/build-release.ps1" }

$target = Join-Path $env:SystemRoot 'System32\PCGameCoverArt.scr'
# Upgrading: Windows can't replace the file while the old version runs (Screen Saver Settings' preview runs it).
if (Get-Process -Name 'PCGameCoverArt' -ErrorAction SilentlyContinue) {
    throw 'The screensaver is running. Close Screen Saver Settings (and stop any preview), then run this again.'
}
Copy-Item $Path $target -Force
Unblock-File $target   # remove the "downloaded from the internet" mark
Write-Host "Installed to $target" -ForegroundColor Green

# Open Screen Saver Settings. Pick "PCGameCoverArt", then Settings..., Preview, Apply.
Start-Process 'control.exe' 'desk.cpl,,@screensaver'
