<#
.SYNOPSIS
  Stages the LifeOS Home Assistant app (add-on) as a self-contained folder for a LOCAL install.

.DESCRIPTION
  The Supervisor builds a local app with the app folder as Docker build context, so the folder
  must contain the source. This copies config.yaml, the Dockerfile, Directory.Build.props and src/
  to artifacts/ha-app/lifeos. Copy that folder to the HA `addons` share (Samba/SSH) as /addons/lifeos,
  then: Settings > Apps (Add-ons) > Check for updates > Local apps > LifeOS > Install.
#>
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'artifacts/ha-app/lifeos'

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

# A local build must not use the published image, so drop the image: line.
(Get-Content (Join-Path $root 'lifeos/config.yaml')) | Where-Object { $_ -notmatch '^image:' } | Set-Content (Join-Path $out 'config.yaml') -Encoding utf8
Copy-Item (Join-Path $PSScriptRoot 'Dockerfile') $out
Copy-Item (Join-Path $root 'Directory.Build.props') $out

# Source without build output or local data.
$src = Join-Path $root 'src'
robocopy $src (Join-Path $out 'src') /E /XD bin obj data /XF *.db *.db-shm *.db-wal /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Write-Host "Staged HA app at: $out"
Write-Host "Copy the 'lifeos' folder to <HA>/addons/lifeos and install it from Local apps."
