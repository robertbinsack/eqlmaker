# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
#
# Minimal event source plugin (docs/plugins.md). Protocol:
#   standard input,  line 1: the capture options as JSON
#                    {"action":"start"|"reset","connection":...,"roles":[...],"databases":[...],
#                     "changes":true,"logFile":"","settings":{...}}   (settings = the section of
#                     eqlmaker.json that is named like the source)
#   standard output: one line {"info": {...}} as soon as the plugin is capturing (EQLMaker waits
#                    for it before it lets the application start), then one event per line
#                    (event format v0.1)
#   standard input,  later:   the line "stop" ends the capture; the plugin writes what is left
#                             and exits with code 0
# This example reads settings.file: a JSON Lines file with events that some other tool appends to.
# It delivers the lines that were added between start and stop.
$ErrorActionPreference = 'Stop'
# Input and output are UTF-8 whatever the code page of the console is. The reader also accepts a
# byte order mark in front of the first line.
$utf8 = New-Object Text.UTF8Encoding $false
$stdin = New-Object IO.StreamReader([Console]::OpenStandardInput(), $utf8)
$stdout = New-Object IO.StreamWriter([Console]::OpenStandardOutput(), $utf8)
$options = $stdin.ReadLine() | ConvertFrom-Json
if ($options.action -eq 'reset') { exit 0 }   # nothing is switched on anywhere, so nothing to undo

$file = $options.settings.file
if (-not $file) { [Console]::Error.WriteLine('jsonl-file: set "jsonl-file": { "file": "..." } in eqlmaker.json'); exit 1 }
$start = 0
if (Test-Path -LiteralPath $file) { $start = (Get-Item -LiteralPath $file).Length }

$stdout.WriteLine('{"info":{"fields":{"sql":true,"params":"as far as the file has them"},"warnings":[]}}')
$stdout.Flush()

# wait for "stop" (or the end of standard input)
while ($true) { $line = $stdin.ReadLine(); if ($null -eq $line -or $line.Trim() -eq 'stop') { break } }

if (Test-Path -LiteralPath $file) {
    $fs = [IO.File]::Open($file, 'Open', 'Read', 'ReadWrite')
    try {
        [void]$fs.Seek($start, 'Begin')
        $reader = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        while ($null -ne ($l = $reader.ReadLine())) { if ($l.Trim()) { $stdout.WriteLine($l) } }
    } finally { $fs.Close() }
}
$stdout.Flush()
exit 0
