# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Writes the status blocks of the README and ROADMAP files from docs\status.json, the single
  source of what is available, in development and planned. Which files belong to which language
  is read from docs\languages.json; the texts of the blocks are under "labels" in status.json.

  A block is everything between the two marker lines
      <!-- status:begin KIND -->
      <!-- status:end -->
  KIND is one of:
      release          the status note (source published / release package available)
      features         table of the features that are available or in development   (default)
      available        table of the available features with version and tests
      in-development   table of the features in development
      planned          the planned items, grouped by their description
      summary          one sentence with the numbers

  The script fails (exit code 1) when a feature is "available" without a test, when a named test
  does not exist, or when a status is not one of: available, in development, planned.
  Tests of the test program are named Class.Method and are compared with the output of
  bin\eqlmaker-tests.exe --list when that program exists. Other tests are named
  "<file>: <name of the test>"; the file must exist and contain the name.
.PARAMETER Check
  Changes nothing; exit code 1 when a file is not up to date.
.PARAMETER Tests
  Requires bin\eqlmaker-tests.exe for the comparison of the test names (fails when it is missing).
#>
param([switch]$Check, [switch]$Tests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding($false)
function Read-Json([string]$file) { return [IO.File]::ReadAllText($file, $utf8) | ConvertFrom-Json }

$status = Read-Json (Join-Path $root 'docs\status.json')
$langs = Read-Json (Join-Path $root 'docs\languages.json')
$features = @($status.features)
$problems = @()

# ---- every status is known, every available feature has tests, every test exists
$known = $null
$exe = Join-Path $root 'bin\eqlmaker-tests.exe'
if (Test-Path -LiteralPath $exe) { $known = @(& $exe --list | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
elseif ($Tests) { $problems += 'bin\eqlmaker-tests.exe is missing (run build.cmd first)' }
foreach ($f in $features) {
    if ($f.status -notin 'available', 'in development', 'planned') { $problems += "$($f.id): unknown status '$($f.status)'"; continue }
    $t = @($f.tests | Where-Object { $_ })
    if ($f.status -ne 'available') { continue }
    if (-not $t.Count) { $problems += "$($f.id): available without a test" }
    if (-not $f.since) { $problems += "$($f.id): available without 'since'" }
    foreach ($name in $t) {
        if ($name -match '^[A-Za-z0-9_]+\.[A-Za-z0-9_]+$') {
            if ($null -ne $known -and $known -notcontains $name) { $problems += "$($f.id): test '$name' is not in the list of eqlmaker-tests.exe" }
        } elseif ($name -match '^(?<file>[^:]+): (?<test>.+)$') {
            $file = Join-Path $root ($Matches.file -replace '/', '\'); $test = $Matches.test
            if (-not (Test-Path -LiteralPath $file)) { $problems += "$($f.id): test file '$($Matches.file)' does not exist" }
            elseif (-not ([IO.File]::ReadAllText($file, $utf8)).Contains($test)) { $problems += "$($f.id): '$test' is not in $($Matches.file)" }
        } else { $problems += "$($f.id): test '$name' is neither Class.Method nor '<file>: <name>'" }
    }
}

# ---- the blocks
function Cell([string]$s) { return $s.Replace('|', '\|') }
function TestCell($f) { return (@($f.tests) | ForEach-Object { '`' + (Cell $_) + '`' }) -join '<br>' }
function Block([string]$kind, [string]$lang) {
    $l = $status.labels.$lang
    if (-not $l) { throw "docs\status.json has no labels for the language '$lang'" }
    $avail = @($features | Where-Object { $_.status -eq 'available' })
    $dev = @($features | Where-Object { $_.status -eq 'in development' })
    $plan = @($features | Where-Object { $_.status -eq 'planned' })
    $out = @()
    switch ($kind) {
        'release' {
            # {0} version, {1} date of the release, {2} the README of the language (the note also stands in the ROADMAP)
            if ($status.release) { $text = $l.release_done -f $status.release.version, $status.release.date, $langs.readme.$lang } else { $text = $l.release_none -f $status.version, '', $langs.readme.$lang }
            $out += '> [!NOTE]'; $out += "> $text"
        }
        'summary' { $out += ($l.summary -f $avail.Count, ($avail.Count + $dev.Count), ($status.version -replace '\.\d+$', ''), $dev.Count) }
        'features' {
            $out += "| $($l.feature) | $($l.description) | $($l.status) | $($l.tests) |"; $out += '|---|---|---|---|'
            foreach ($f in ($avail + $dev)) { $out += "| $(Cell $f.name.$lang) | $(Cell $f.description.$lang) | $($l.($f.status)) | $(TestCell $f) |" }
        }
        'available' {
            $out += "| $($l.feature) | $($l.description) | $($l.since) | $($l.tests) |"; $out += '|---|---|---|---|'
            foreach ($f in $avail) { $out += "| $(Cell $f.name.$lang) | $(Cell $f.description.$lang) | $($f.since) | $(TestCell $f) |" }
        }
        'in-development' {
            $out += "| $($l.feature) | $($l.description) |"; $out += '|---|---|'
            foreach ($f in $dev) { $out += "| $(Cell $f.name.$lang) | $(Cell $f.description.$lang) |" }
        }
        'planned' {
            $groups = @(); foreach ($f in $plan) { if ($groups -notcontains $f.description.$lang) { $groups += $f.description.$lang } }
            foreach ($g in $groups) {
                $out += "- **$g**"
                foreach ($f in ($plan | Where-Object { $_.description.$lang -eq $g })) { $out += "  - [ ] $($f.name.$lang)" }
            }
        }
        default { throw "unknown kind of status block: $kind" }
    }
    return $out
}

$stale = @(); $written = @(); $blocks = 0
foreach ($lang in @($langs.languages)) {
    foreach ($name in @($langs.readme.$lang, $langs.roadmap.$lang)) {
        if (-not $name) { $problems += "docs\languages.json names no README or ROADMAP for '$lang'"; continue }
        $file = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $file)) { $problems += "$name does not exist"; continue }
        $old = [IO.File]::ReadAllText($file, $utf8)
        $nl = if ($old.Contains("`r`n")) { "`r`n" } else { "`n" }
        $count = 0
        $new = [regex]::Replace($old, '(?s)(<!-- status:begin(?: ([a-z-]+))? -->)\r?\n.*?(<!-- status:end -->)', {
            param($m)
            $kind = if ($m.Groups[2].Success) { $m.Groups[2].Value } else { 'features' }
            $script:count++
            return $m.Groups[1].Value + $nl + ((Block $kind $lang) -join $nl) + $nl + $m.Groups[3].Value
        })
        $blocks += $count
        if (-not $count) { $problems += "$name has no status block (<!-- status:begin --> ... <!-- status:end -->)"; continue }
        if ($new -ne $old) {
            if ($Check) { $stale += $name } else { [IO.File]::WriteAllText($file, $new, $utf8); $written += $name }
        }
    }
}

$a = @($features | Where-Object { $_.status -eq 'available' }).Count
$d = @($features | Where-Object { $_.status -eq 'in development' }).Count
$p = @($features | Where-Object { $_.status -eq 'planned' }).Count
$checked = if ($null -ne $known) { "test names compared with $($known.Count) tests of eqlmaker-tests.exe" } else { 'test names of the test program not compared (bin\eqlmaker-tests.exe is not built)' }
Write-Host "status: $a available, $d in development, $p planned; $blocks blocks; $checked"
$written | ForEach-Object { Write-Host "  written: $_" }
$stale | ForEach-Object { Write-Host "  not up to date: $_ (run scripts\update-status.ps1)" }
$problems | ForEach-Object { Write-Host "  problem: $_" }
if ($problems.Count -or $stale.Count) { exit 1 }
