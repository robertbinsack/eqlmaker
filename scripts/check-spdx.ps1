# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Checks that every source file carries the licence header:
    SPDX-License-Identifier: AGPL-3.0-or-later
    Copyright (c) 2026 Robert Binsack   (not required in Markdown and HTML fragments, where only the identifier is asked for)
  Exit code 1 when a file has none.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$ext = '.cs', '.js', '.mjs', '.css', '.html', '.sql', '.ps1', '.cmd', '.yml', '.yaml'
$skip = '\\(node_modules|bin|work|out|\.git)\\'
$files = @(Get-ChildItem $root -Recurse -File -Force | Where-Object { $ext -contains $_.Extension.ToLowerInvariant() -and $_.FullName -notmatch $skip })
$bad = @()
foreach ($f in $files) {
    $head = (Get-Content -LiteralPath $f.FullName -TotalCount 12 -Encoding UTF8) -join "`n"
    if ($head -notmatch 'SPDX-License-Identifier: AGPL-3\.0-or-later') { $bad += "$($f.FullName.Substring($root.Length + 1)): no SPDX identifier in the first 12 lines"; continue }
    if ($f.Extension -notin '.html', '.yml', '.yaml' -and $head -notmatch 'Copyright \(c\) 2026 Robert Binsack') { $bad += "$($f.FullName.Substring($root.Length + 1)): no copyright line" }
}
Write-Host "licence headers: $($files.Count) source files checked, $($bad.Count) without header"
$bad | ForEach-Object { Write-Host "  $_" }
if ($bad.Count) { exit 1 }
