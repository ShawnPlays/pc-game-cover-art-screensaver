<#
.SYNOPSIS
  Writes the winget manifests for one released version, for the first submission to microsoft/winget-pkgs.
  After that, the release workflow submits each new version by itself (see .github/workflows/build.yml).

  The installer's SHA-256 is taken from the file on the GitHub Release, which is what winget will download.

.EXAMPLE
  ./scripts/winget-manifests.ps1 -Version 2.0.0          # writes obj\winget\2.0.0\*.yaml
  winget validate --manifest obj\winget\2.0.0
  wingetcreate submit --token <token> obj\winget\2.0.0
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$InstallerPath   # optional: hash a local copy instead of downloading the released one
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repoUrl = 'https://github.com/ShawnPlays/pc-game-cover-art-screensaver'
$id = 'ShawnPlays.PCGameCoverArt'
$url = "$repoUrl/releases/download/v$Version/PCGameCoverArtSetup_$Version.exe"
$out = Join-Path $root "obj\winget\$Version"
New-Item $out -ItemType Directory -Force | Out-Null

if (-not $InstallerPath) {
    # Not inside $out: "winget validate" reads every file in that folder as a manifest.
    $InstallerPath = Join-Path (Split-Path $out) "PCGameCoverArtSetup_$Version.exe"
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $InstallerPath -UseBasicParsing
}
$sha = (Get-FileHash $InstallerPath -Algorithm SHA256).Hash

# Inno Setup registers the app under its AppId plus "_is1"; winget uses that to spot an existing install.
$appId = (Select-String -Path "$root\installer\PCGameCoverArt.iss" -Pattern '^AppId=\{(\{[^}]+\})').Matches[0].Groups[1].Value
$schema = '1.12.0'

function Write-Manifest($name, $text) {
    [IO.File]::WriteAllText((Join-Path $out $name), $text.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}

Write-Manifest "$id.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema

"@

Write-Manifest "$id.installer.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
MinimumOSVersion: 10.0.0.0
InstallerType: inno
Scope: machine
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ElevationRequirement: elevatesSelf
ProductCode: '$($appId)_is1'
ReleaseDate: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'))
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema

"@

Write-Manifest "$id.locale.en-US.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: ShawnPlays
PublisherUrl: https://github.com/ShawnPlays
PublisherSupportUrl: $repoUrl/issues
PackageName: PC Game Cover Art Screensaver
PackageUrl: $repoUrl
License: MIT
LicenseUrl: $repoUrl/blob/main/LICENSE
Copyright: Copyright (c) 2026 ShawnPlays
ShortDescription: A Windows screensaver that shows the cover art from your Playnite or Steam library.
Description: |-
  Shows your game covers as a classic 3D coverflow or a wall of flipping tiles, like the old iTunes screensavers,
  optionally with your installed Steam soundtracks playing. Reads Steam's own files directly, or your Playnite
  library through a small Playnite add-on that the installer can set up.
Moniker: pcgamecoverart
Tags:
- coverflow
- games
- playnite
- screensaver
- steam
ReleaseNotesUrl: $repoUrl/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema

"@

Write-Host "winget manifests for $Version written to $out (installer SHA-256 $sha)"
