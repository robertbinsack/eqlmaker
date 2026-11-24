# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Checks that the translations of the documentation have the same structure as the originals.
  Which language is the original and which files are the README and the ROADMAP of a language
  is read from docs\languages.json:
    { "canonical": "en", "languages": ["en", "de"],
      "readme":  { "de": "README.md", "en": "README.en.md" },
      "roadmap": { "en": "ROADMAP.md", "de": "ROADMAP.de.md" } }
  Pairs that are compared for every language other than the canonical one:
    - the README and the ROADMAP of the canonical language against those of the language
    - every other <name>.md of the repository folder against <name>.<lang>.md, when that exists
    - docs\*.md against docs\<lang>\*.md (every page must exist in both)
  Compared: the sequence of heading levels, the code blocks (they must be identical), the number
  of links and pictures, and the number of rows of every table.
  Writes a status table; exit code 1 on drift or missing files.

  A translation may differ from the original in the number of links on purpose. It then says so
  in a comment, with the difference to the original:   <!-- check-docs: links -1 -->
.PARAMETER Language
  Languages to check (default: every language of docs\languages.json but the canonical one).
.PARAMETER Table
  File to write the status table to as Markdown (optional).
#>
param([string[]]$Language = @(), [string]$Table = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$docs = Join-Path $root 'docs'
$utf8 = New-Object Text.UTF8Encoding($false)
$cfg = [IO.File]::ReadAllText((Join-Path $docs 'languages.json'), $utf8) | ConvertFrom-Json
$canonical = $cfg.canonical
if (-not $canonical) { throw 'docs\languages.json: "canonical" is missing' }
if (-not $Language.Count) { $Language = @($cfg.languages | Where-Object { $_ -ne $canonical }) }

function Get-Shape([string]$file) {
    $lines = [IO.File]::ReadAllLines($file, $utf8)
    $levels = @(); $blocks = @(); $links = 0; $inCode = $false; $code = New-Object Text.StringBuilder
    $tables = @(); $rows = 0; $allowed = 0
    foreach ($l in $lines) {
        if ($l -match '^\s*```') {
            if ($inCode) { $blocks += $code.ToString(); [void]$code.Clear() }
            $inCode = -not $inCode; continue
        }
        if ($inCode) { [void]$code.AppendLine($l.TrimEnd()); continue }
        if ($l -match '^(#{1,6})\s') { $levels += $Matches[1].Length }
        $links += ([regex]::Matches($l, '\]\(')).Count
        if ($l -match '<!--\s*check-docs:\s*links\s*([+-]\d+)\s*-->') { $allowed += [int]$Matches[1] }
        if ($l -match '^\s*\|') { $rows++ } elseif ($rows) { $tables += $rows; $rows = 0 }
    }
    if ($rows) { $tables += $rows }
    return @{ Levels = ($levels -join ''); Blocks = $blocks; Links = $links; Tables = $tables; Allowed = $allowed }
}

$rows = @(); $bad = 0
foreach ($lang in $Language) {
    $pairs = @()
    # README and ROADMAP: file names from languages.json
    $special = @()
    foreach ($kind in 'readme', 'roadmap') {
        $orig = $cfg.$kind.$canonical; $tr = $cfg.$kind.$lang
        if (-not $orig -or -not $tr) { $rows += [pscustomobject]@{ Language = $lang; File = $kind; Status = 'missing'; Detail = "docs\languages.json names no $kind for '$canonical' or '$lang'" }; $bad++; continue }
        $special += $orig; $special += $tr
        $pairs += @{ En = (Join-Path $root $orig); Tr = (Join-Path $root $tr); Name = "$orig / $tr" }
    }
    foreach ($l2 in @($cfg.languages)) { foreach ($kind in 'readme', 'roadmap') { if ($cfg.$kind.$l2) { $special += $cfg.$kind.$l2 } } }
    # other files of the repository folder: <name>.md with a translation <name>.<lang>.md
    foreach ($f in @(Get-ChildItem $root -File -Filter *.md | Where-Object { $special -notcontains $_.Name -and $_.BaseName -notmatch '\.[a-z]{2}$' })) {
        $tr = Join-Path $root "$($f.BaseName).$lang.md"
        if (Test-Path -LiteralPath $tr) { $pairs += @{ En = $f.FullName; Tr = $tr; Name = $f.Name } }
    }
    foreach ($f in @(Get-ChildItem $docs -File -Filter *.md)) { $pairs += @{ En = $f.FullName; Tr = (Join-Path (Join-Path $docs $lang) $f.Name); Name = "docs/$($f.Name)" } }
    foreach ($p in $pairs) {
        if (-not (Test-Path -LiteralPath $p.En)) { $rows += [pscustomobject]@{ Language = $lang; File = $p.Name; Status = 'missing'; Detail = 'the original does not exist' }; $bad++; continue }
        if (-not (Test-Path -LiteralPath $p.Tr)) { $rows += [pscustomobject]@{ Language = $lang; File = $p.Name; Status = 'missing'; Detail = '' }; $bad++; continue }
        $a = Get-Shape $p.En; $b = Get-Shape $p.Tr; $problems = @()
        if ($a.Levels -ne $b.Levels) { $problems += "headings differ ($($a.Levels.Length) / $($b.Levels.Length))" }
        if ($a.Blocks.Count -ne $b.Blocks.Count) { $problems += "code blocks: $($a.Blocks.Count) / $($b.Blocks.Count)" }
        else { for ($i = 0; $i -lt $a.Blocks.Count; $i++) { if ($a.Blocks[$i] -ne $b.Blocks[$i]) { $problems += "code block $($i + 1) differs"; break } } }
        if ($a.Links + $b.Allowed -ne $b.Links) { $problems += "links: $($a.Links) / $($b.Links)" }
        if ($a.Tables.Count -ne $b.Tables.Count) { $problems += "tables: $($a.Tables.Count) / $($b.Tables.Count)" }
        else { for ($i = 0; $i -lt $a.Tables.Count; $i++) { if ($a.Tables[$i] -ne $b.Tables[$i]) { $problems += "table $($i + 1): $($a.Tables[$i]) / $($b.Tables[$i]) rows"; break } } }
        if ($problems.Count) { $bad++ }
        $rows += [pscustomobject]@{ Language = $lang; File = $p.Name; Status = $(if ($problems.Count) { 'drift' } else { 'in step' }); Detail = ($problems -join '; ') }
    }
    foreach ($f in @(Get-ChildItem (Join-Path $docs $lang) -File -Filter *.md -ErrorAction SilentlyContinue)) {
        if (-not (Test-Path -LiteralPath (Join-Path $docs $f.Name))) { $rows += [pscustomobject]@{ Language = $lang; File = "docs/$lang/$($f.Name)"; Status = 'no original'; Detail = '' }; $bad++ }
    }
}
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
if ($Table) {
    $md = @('| Language | File | Status | Detail |', '|---|---|---|---|') + ($rows | ForEach-Object { "| $($_.Language) | $($_.File) | $($_.Status) | $($_.Detail) |" })
    Set-Content -LiteralPath $Table -Value $md -Encoding UTF8
}
Write-Host "translations: $($rows.Count) files checked, $bad not in step"
if ($bad) { exit 1 }
