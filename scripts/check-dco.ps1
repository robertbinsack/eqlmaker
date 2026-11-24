# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Checks that every commit of a range is signed off (Developer Certificate of Origin, DCO.md):
  the commit message must contain "Signed-off-by: Name <email>" with the email of the author.
.PARAMETER Range
  Git revision range, e.g. origin/main..HEAD
#>
param([Parameter(Mandatory = $true)][string]$Range)
$ErrorActionPreference = 'Stop'
$commits = @(git rev-list --no-merges $Range)
if ($LASTEXITCODE -ne 0) { throw "git rev-list failed for $Range" }
$bad = 0
foreach ($c in $commits) {
    $email = (git show -s --format='%ae' $c).Trim()
    $body = (git show -s --format='%B' $c) -join "`n"
    $signed = [regex]::Matches($body, '(?im)^Signed-off-by:\s*.+<([^>]+)>\s*$') | ForEach-Object { $_.Groups[1].Value.Trim().ToLowerInvariant() }
    if ($signed -notcontains $email.ToLowerInvariant()) {
        $bad++
        Write-Host "not signed off by its author: $($c.Substring(0, 10)) $((git show -s --format='%s' $c))"
    }
}
Write-Host "sign-off: $($commits.Count) commits checked, $bad without Signed-off-by of the author"
if ($bad) {
    Write-Host 'Add the line with: git commit -s   (existing commits: git rebase --signoff <base>). See DCO.md.'
    exit 1
}
