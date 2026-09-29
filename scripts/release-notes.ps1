<#
.SYNOPSIS
  Writes the text for a GitHub Release: what's new in this version, then how to install and upgrade.
  "What's new" comes from the README's "#### Notes for version x.y.z" section, so write that section
  before tagging a release. GitHub adds its generated list of changes below this text.

.EXAMPLE
  ./scripts/release-notes.ps1 -Version 1.3.2                      # writes obj\release-notes.md
  ./scripts/release-notes.ps1 -Version 1.3.2 -OutFile notes.md
#>
param(
    [string]$Version = $(if ($env:GITHUB_REF_NAME -match '^v(\d+\.\d+\.\d+)') { $Matches[1] } else { throw 'Pass -Version x.y.z' }),
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutFile) { $OutFile = Join-Path $root 'obj\release-notes.md' }

# Links in the README like (#music) point into the README; on the release page they must point at the repository.
$repoUrl = if ($env:GITHUB_REPOSITORY) { "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)" }
           else { 'https://github.com/ShawnPlays/pc-game-cover-art-screensaver' }

# The version's notes: everything after its heading, up to the next heading of the same or a higher level.
$lines = Get-Content (Join-Path $root 'README.md') -Encoding utf8
$start = [array]::IndexOf($lines, "#### Notes for version $Version")
$notes = @()
if ($start -ge 0) {
    for ($i = $start + 1; $i -lt $lines.Count -and $lines[$i] -notmatch '^#{1,4} '; $i++) { $notes += $lines[$i] }
}
$notes = ($notes -join "`n").Trim() -replace '\]\(#', "]($repoUrl#"

if (-not $notes) {
    # Shows as a warning on the Actions run; the release still goes out, with just the install steps.
    Write-Host "::warning::README.md has no '#### Notes for version $Version' section, so the release has no What's new."
}

$whatsNew = if ($notes) { "## What's new in $Version`n`n$notes`n`n" } else { '' }
$body = @"
$whatsNew## How to install

Download from **Assets** below:

- ``PCGameCoverArt.scr`` (everyone): right-click → **Properties** → tick **Unblock** → **OK**, then copy it into ``C:\Windows\System32``.
- ``PCGameCoverArtExporter_$Version.pext`` (Playnite users only): double-click it (or drag it onto Playnite), click **Yes**, then restart Playnite. Steam users don't need it.

Then open **Change screen saver** from the Start menu, choose **PCGameCoverArt**, click **Settings…**, and choose **Playnite** or **Steam** under **Get games from**.

## Upgrading from an earlier version

No need to uninstall; your settings are kept. Playnite users: install the new ``.pext`` first (Playnite offers to update the add-on), then restart Playnite. Close Screen Saver Settings, copy the new ``.scr`` into ``C:\Windows\System32`` and choose **Replace the file in the destination**.
Notes for every version: **$repoUrl#upgrade-from-an-earlier-version**

Full step-by-step directions, uninstalling and troubleshooting:
**$repoUrl#install**

Requires Windows 10/11 (64-bit), and Playnite 10 or Steam.
"@

New-Item (Split-Path $OutFile) -ItemType Directory -Force | Out-Null
[IO.File]::WriteAllText($OutFile, $body.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "Release notes for $Version written to $OutFile"
