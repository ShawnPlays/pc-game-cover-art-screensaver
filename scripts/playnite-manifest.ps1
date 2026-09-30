<#
.SYNOPSIS
  Writes the "installer manifest" Playnite's add-on browser reads to find the add-on's latest version.
  It's attached to every GitHub Release, so this URL always serves the newest one:
    https://github.com/ShawnPlays/pc-game-cover-art-screensaver/releases/latest/download/installer.yaml
  The add-on's entry in Playnite's add-on database (docs/playnite-addon-database) points there.
  The changelog comes from the README's "#### Notes for version x.y.z" section, like the release notes.

.EXAMPLE
  ./scripts/playnite-manifest.ps1 -Version 2.0.0 -OutFile dist\installer.yaml
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not [IO.Path]::IsPathRooted($OutFile)) { $OutFile = Join-Path (Get-Location) $OutFile }
$repoUrl = if ($env:GITHUB_REPOSITORY) { "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)" }
           else { 'https://github.com/ShawnPlays/pc-game-cover-art-screensaver' }

$addonId = (Select-String -Path "$root\src\CoverArtExporter\extension.yaml" -Pattern '^Id:\s*(\S+)').Matches[0].Groups[1].Value
# The oldest Playnite that can run the add-on is the one whose SDK it was built against.
$sdk = (Select-String -Path "$root\src\CoverArtExporter\CoverArtExporter.csproj" -Pattern 'Include="PlayniteSDK" Version="([^"]+)"').Matches[0].Groups[1].Value

# Changelog: each top-level bullet of the version's README notes, as plain text on one line.
$lines = Get-Content "$root\README.md" -Encoding utf8
$start = [array]::IndexOf($lines, "#### Notes for version $Version")
$items = @()
if ($start -ge 0) {
    for ($i = $start + 1; $i -lt $lines.Count -and $lines[$i] -notmatch '^#{1,4} '; $i++) {
        if ($lines[$i] -match '^- (.*)') { $items += $Matches[1] }
        elseif ($lines[$i] -match '^\s+\S' -and $items.Count) { $items[-1] += ' ' + $lines[$i].Trim() }
    }
}
if (-not $items) { $items = @("See $repoUrl/releases/tag/v$Version") }
$changelog = $items | ForEach-Object {
    $text = $_ -replace '\*\*', '' -replace '`', '' -replace '\[([^\]]+)\]\([^)]+\)', '$1'
    "      - '" + $text.Replace("'", "''") + "'"   # YAML single-quoted string
}

$yaml = @"
AddonId: '$addonId'
Packages:
  - Version: $Version
    RequiredApiVersion: $sdk
    ReleaseDate: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'))
    PackageUrl: $repoUrl/releases/download/v$Version/PCGameCoverArtExporter_$Version.pext
    Changelog:
$($changelog -join "`n")
"@

New-Item (Split-Path $OutFile) -ItemType Directory -Force | Out-Null
[IO.File]::WriteAllText($OutFile, $yaml.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Playnite installer manifest for $Version written to $OutFile"
