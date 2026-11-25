# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
#
# Minimal reader plugin (docs/plugins.md). Protocol:
#   standard input:  one JSON line  {"action":"read","paths":["<file or folder>", ...],"out":"<folder>"}
#   the plugin writes out\reader.json in the format "eqlmaker-static" (docs/plugins.md)
#   standard output: one JSON line  {"result":{...numbers for people...}}
# This example turns every .sql file into one object of the kind "script" with its statements
# (split at semicolons) and the tables after FROM, JOIN, INTO and UPDATE.
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding $false
[Console]::OutputEncoding = $utf8
$stdin = New-Object IO.StreamReader([Console]::OpenStandardInput(), $utf8)
$r = $stdin.ReadLine().TrimStart([char]0xFEFF) | ConvertFrom-Json

$files = @()
foreach ($p in $r.paths) {
    if (Test-Path $p -PathType Container) { $files += Get-ChildItem $p -Filter *.sql -File | Sort-Object Name }
    else { $files += Get-Item $p }
}
$objects = @(); $sqlCount = 0
foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f.FullName, $utf8)
    $stmts = @($text -split ';' | ForEach-Object { ($_ -replace '\s+', ' ').Trim() } | Where-Object { $_ })
    $tables = @()
    foreach ($s in $stmts) {
        foreach ($m in [regex]::Matches($s, '(?i)\b(?:FROM|JOIN|INTO|UPDATE)\s+([A-Za-z_][A-Za-z0-9_.]*)')) { if ($tables -notcontains $m.Groups[1].Value) { $tables += $m.Groups[1].Value } }
    }
    $sqlCount += $stmts.Count
    $objects += [ordered]@{ name = $f.BaseName; kind = 'script'; library = $f.Name; sql = @($stmts); tables = @($tables); refs = @(); texts = @(); labels = [ordered]@{} }
}
$doc = [ordered]@{ format = 'eqlmaker-static'; version = '0.1'; reader = 'sql-scripts'; libraries = @(); menus = @(); counts = [ordered]@{ objects = $objects.Count; sql = $sqlCount }; objects = $objects }
New-Item -ItemType Directory -Force $r.out | Out-Null
[IO.File]::WriteAllText((Join-Path $r.out 'reader.json'), ($doc | ConvertTo-Json -Depth 8), $utf8)
[Console]::Out.WriteLine((@{ result = [ordered]@{ objects = $objects.Count; sql = $sqlCount } } | ConvertTo-Json -Compress -Depth 4))
