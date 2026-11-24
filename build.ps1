# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Builds EQLMaker with the C# compiler that is part of the .NET Framework on every Windows
  (no SDK needed). Result in .\bin:
    eqlmaker.exe             command line and MCP server
    eqlmaker-timestamp.exe   time stamp overlay
    sample-desktop.exe       sample application (Windows)
    eqlmaker-tests.exe       tests
    share\                   viewer, templates, prompts
.PARAMETER Lint
  Also checks the JavaScript and PowerShell files for syntax errors (needs node for JavaScript).
#>
param([switch]$Lint)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler of the .NET Framework 4 not found' }
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Xml.dll'
function Compile([string]$out, [string]$target, [string[]]$sources) {
    $files = @($sources | ForEach-Object { Get-ChildItem (Join-Path $root $_) -File } | ForEach-Object FullName)
    # warnings are errors: the compiler is the linter of the C# sources
    $a = @('/nologo', '/preferreduilang:en-US', '/utf8output', '/codepage:65001', '/langversion:5', '/optimize+', '/warn:4', '/warnaserror+', "/target:$target", '/platform:anycpu', "/out:$(Join-Path $bin $out)")
    $a += $refs | ForEach-Object { "/r:$_" }
    $a += $files
    & $csc @a
    if ($LASTEXITCODE -ne 0) { throw "build of $out failed" }
    Write-Host "built $out ($($files.Count) files)"
}

Compile 'eqlmaker.exe' 'exe' @('src\core\*.cs', 'src\cli\*.cs')
Compile 'eqlmaker-timestamp.exe' 'winexe' @('src\timestamp\*.cs', 'src\core\Timecode.cs')
Compile 'sample-desktop.exe' 'winexe' @('examples\sample-app\desktop\*.cs', 'src\core\PgClient.cs', 'src\core\Json.cs')
Compile 'eqlmaker-tests.exe' 'exe' @('src\core\*.cs', 'tests\*.cs')

# resources next to the programs
$share = Join-Path $bin 'share'
if (Test-Path $share) { Remove-Item -Recurse -Force $share }
New-Item -ItemType Directory -Force $share | Out-Null
foreach ($d in 'viewer', 'templates', 'prompts', 'i18n') {
    Copy-Item (Join-Path $root "src\$d") (Join-Path $share $d) -Recurse -Exclude 'node_modules'
}
Set-Content (Join-Path $share 'VERSION') (Get-Content (Join-Path $root 'VERSION') -TotalCount 1) -Encoding ascii
Write-Host "resources copied to $share"

if ($Lint) {
    $bad = 0
    $node = Get-Command node -ErrorAction SilentlyContinue
    $js = @(Get-ChildItem $root -Recurse -File -Include *.js, *.mjs | Where-Object { $_.FullName -notmatch '\\(node_modules|bin|work|out)\\' })
    if ($node) {
        foreach ($f in $js) { & node --check $f.FullName; if ($LASTEXITCODE -ne 0) { $bad++ } }
        Write-Host "lint: $($js.Count) JavaScript files checked"
    } else { Write-Host 'lint: node not found, JavaScript files not checked' }
    $ps = @(Get-ChildItem $root -Recurse -File -Include *.ps1 | Where-Object { $_.FullName -notmatch '\\(node_modules|bin|work|out)\\' })
    foreach ($f in $ps) {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$errors)
        if ($errors.Count) { $bad++; Write-Host "$($f.FullName): $($errors[0].Message)" }
    }
    Write-Host "lint: $($ps.Count) PowerShell files checked"
    # (npm lock files use an empty key, which ConvertFrom-Json of Windows PowerShell cannot read)
    $json = @(Get-ChildItem $root -Recurse -File -Include *.json | Where-Object { $_.FullName -notmatch '\\(node_modules|bin|work|out)\\' -and $_.Name -ne 'package-lock.json' })
    foreach ($f in $json) { try { [void](Get-Content $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { $bad++; Write-Host "$($f.FullName): not valid JSON" } }
    Write-Host "lint: $($json.Count) JSON files checked"
    if ($bad) { throw "lint: $bad files with errors" }
}
