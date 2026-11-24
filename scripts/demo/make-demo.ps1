# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Makes the demonstration media of the repository from the sample application:
    docs\media\demo.gif   short animation for the README
    out\demo.mp4          the comparison video (original and browser variant side by side)
  The run operates the sample application with the real mouse on the visible desktop for about
  one minute: do not touch mouse or keyboard meanwhile. Nothing but the sample application and a
  browser window with the sample is shown; close other windows first.
.PARAMETER Project
  A project folder whose eqlmaker.json points to a PostgreSQL with wal_level = logical
  (the "project" folder of a set-up package works).
.PARAMETER Seconds
  Length of the GIF in seconds (default 40).
#>
param([Parameter(Mandatory = $true)][string]$Project, [int]$Seconds = 40)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $root 'bin\eqlmaker.exe'
if (-not (Test-Path $exe)) { throw 'build first: build.cmd' }
$ffmpeg = Join-Path $root 'tools\ffmpeg\bin\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { $ffmpeg = 'ffmpeg' }

Write-Host 'Starting in 5 seconds. Hands off mouse and keyboard until "done" appears.'
Start-Sleep 5
& $exe sample --project $Project --visible
if ($LASTEXITCODE) { throw 'the sample run failed (see the lines above)' }
& $exe video desktop web --project $Project --name demo
if ($LASTEXITCODE) { throw 'the comparison video could not be built' }

$pair = Join-Path $Project 'video\demo\pair.mp4'
New-Item -ItemType Directory -Force (Join-Path $root 'out'), (Join-Path $root 'docs\media') | Out-Null
Copy-Item $pair (Join-Path $root 'out\demo.mp4') -Force
# GIF with its own palette, no metadata
$palette = Join-Path $env:TEMP 'eqlmaker-demo-palette.png'
& $ffmpeg -hide_banner -loglevel error -y -t $Seconds -i $pair -vf 'fps=8,scale=960:-2:flags=lanczos,palettegen=max_colors=96' $palette
& $ffmpeg -hide_banner -loglevel error -y -t $Seconds -i $pair -i $palette -lavfi 'fps=8,scale=960:-2:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=4' -map_metadata -1 (Join-Path $root 'docs\media\demo.gif')
Remove-Item $palette -ErrorAction SilentlyContinue
$gif = Get-Item (Join-Path $root 'docs\media\demo.gif')
Write-Host ("done: {0} ({1:N1} MB), out\demo.mp4" -f $gif.FullName, ($gif.Length / 1MB))
if ($gif.Length -gt 8MB) { Write-Host 'The GIF is larger than 8 MB: run again with a smaller -Seconds.' }
