<#
.SYNOPSIS
  Builds everything a user needs into .\dist:
    PCGameCoverArt.scr                    - the screensaver (single self-contained file)
    PCGameCoverArtExporter_<version>.pext - the Playnite add-on package
  Used both locally and by GitHub Actions.

.EXAMPLE
  ./scripts/build-release.ps1                  # version 0.0.0-dev
  ./scripts/build-release.ps1 -Version 1.2.0
#>
param(
    [string]$Version = $(if ($env:GITHUB_REF_NAME -match '^v(\d+\.\d+\.\d+)') { $Matches[1] } else { '0.0.0' })
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$work = Join-Path $root 'obj\release'

Remove-Item $dist, $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $dist, $work -ItemType Directory | Out-Null

Write-Host "==> Running tests" -ForegroundColor Cyan
dotnet test "$root\tests\CoverArtSaver.Tests" -c Release
if ($LASTEXITCODE) { throw 'Tests failed' }

Write-Host "==> Publishing screensaver $Version" -ForegroundColor Cyan
dotnet publish "$root\src\CoverArtSaver" -c Release -r win-x64 -p:Version=$Version -o "$work\saver"
if ($LASTEXITCODE) { throw 'Screensaver publish failed' }
# A screensaver is just an .exe with a .scr extension.
Copy-Item "$work\saver\PCGameCoverArt.exe" "$dist\PCGameCoverArt.scr"

Write-Host "==> Building Playnite add-on $Version" -ForegroundColor Cyan
dotnet build "$root\src\CoverArtExporter" -c Release -p:Version=$Version -o "$work\exporter"
if ($LASTEXITCODE) { throw 'Add-on build failed' }

# Stamp the version into the manifest so Playnite shows the right number.
$manifest = "$work\exporter\extension.yaml"
(Get-Content $manifest) -replace '^Version:.*$', "Version: $Version" | Set-Content $manifest -Encoding utf8

# A .pext is a zip of the add-on folder. Only ship what Playnite needs.
$pextDir = "$work\pext"
New-Item $pextDir -ItemType Directory | Out-Null
Copy-Item "$work\exporter\CoverArtExporter.dll", $manifest, "$work\exporter\icon.png" $pextDir
Copy-Item "$root\LICENSE" "$pextDir\LICENSE.txt"   # MIT: the license must travel with every copy
Compress-Archive -Path "$pextDir\*" -DestinationPath "$work\CoverArtExporter.zip"
Move-Item "$work\CoverArtExporter.zip" "$dist\PCGameCoverArtExporter_$Version.pext"

Write-Host "==> Done. Files in ${dist}:" -ForegroundColor Green
Get-ChildItem $dist | Format-Table Name, Length
