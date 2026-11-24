# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
#
# Minimal clone renderer plugin (docs/plugins.md). Protocol:
#   standard input:  one JSON line
#     {"mask":{"id":..,"title":..},"reference":"../reference/<id>.png","width":..,"height":..,
#      "controls":[{"text":..,"class":..,"x":..,"y":..,"w":..,"h":..,"enabled":..,"writes":..}],
#      "queries":[..],"tables_read":[..],"out":"<folder for the page>"}
#   the plugin writes its page (and any files it needs) into "out"
#   standard output: one JSON line {"page":"<file name>"}
# This example outlines every control and prints its text, which makes the recorded structure of
# a mask visible; controls that changed data get the class "archive-off".
$ErrorActionPreference = 'Stop'
# Input and output are UTF-8 whatever the code page of the console is. The reader also accepts a
# byte order mark in front of the first line.
$utf8 = New-Object Text.UTF8Encoding $false
[Console]::OutputEncoding = $utf8
$stdin = New-Object IO.StreamReader([Console]::OpenStandardInput(), $utf8)
$r = $stdin.ReadLine() | ConvertFrom-Json
function Esc([string]$s) { return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;') }

$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine('<!doctype html>')
[void]$sb.AppendLine('<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->')
[void]$sb.AppendLine('<html lang="en"><head><meta charset="utf-8"><title>' + (Esc $r.mask.title) + ' (outline)</title>')
[void]$sb.AppendLine('<style>body{margin:0;background:#6b7280}.mask{position:relative;background-repeat:no-repeat}.ctl{position:absolute;box-sizing:border-box;border:1px solid #d1242f;font:10px/1.1 sans-serif;color:#d1242f;overflow:hidden}.ctl.archive-off{background:rgba(255,255,255,.6);border-style:dashed}</style></head><body>')
[void]$sb.AppendLine('<div class="mask" id="' + (Esc $r.mask.id) + '" style="width:' + $r.width + 'px;height:' + $r.height + 'px;background-image:url(''' + $r.reference + ''')">')
foreach ($c in @($r.controls)) {
    $cls = 'ctl'; if ($c.writes) { $cls += ' archive-off' }
    [void]$sb.AppendLine('  <div class="' + $cls + '" style="left:' + $c.x + 'px;top:' + $c.y + 'px;width:' + $c.w + 'px;height:' + $c.h + 'px">' + (Esc "$($c.text)") + '</div>')
}
[void]$sb.AppendLine('</div></body></html>')
$name = "$($r.mask.id).outline.html"
[IO.File]::WriteAllText((Join-Path $r.out $name), $sb.ToString(), (New-Object Text.UTF8Encoding $false))
$stdout = New-Object IO.StreamWriter([Console]::OpenStandardOutput(), $utf8)
$stdout.WriteLine((@{ page = $name } | ConvertTo-Json -Compress)); $stdout.Flush()
exit 0
