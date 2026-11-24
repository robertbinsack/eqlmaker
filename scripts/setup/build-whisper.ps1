# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Builds whisper-cli.exe of whisper.cpp from the pinned source archive as one program without
  further libraries: CPU only, C/C++ run-time library linked statically, no OpenMP. The result
  runs on a Windows x64 without the Visual C++ redistributable.
.DESCRIPTION
  Needs the C++ build tools of Visual Studio (MSVC) and cmake, as they are on the GitHub runner
  "windows-latest". cmake is taken from PATH or from the Visual Studio installation.
  The instruction set is fixed to AVX2 with FMA, F16C and BMI2 (Intel from 2013, AMD from 2015),
  not to the processor of the build machine.
.PARAMETER SourceArchive
  The verified source archive (tar.gz) of the pinned tag.
.PARAMETER WorkDir
  Folder for the unpacked sources and the build tree.
.PARAMETER OutDir
  Folder that receives whisper-cli.exe, LICENSE and imports.txt (the list of imported libraries).
#>
param(
    [Parameter(Mandatory = $true)][string]$SourceArchive,
    [Parameter(Mandatory = $true)][string]$WorkDir,
    [Parameter(Mandatory = $true)][string]$OutDir
)
$ErrorActionPreference = 'Stop'

function Find-VsTool([string]$Pattern) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { return $null }
    $found = & $vswhere -all -products * -find $Pattern 2>$null | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if ($found -and (Test-Path -LiteralPath $found)) { return $found }
    return $null
}

# runs a program; its error output is shown as plain text and never stops the script by itself
function Invoke-Tool([string]$Exe, [string[]]$Arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host "$_" } } finally { $ErrorActionPreference = $old }
}

$cmake = (Get-Command cmake.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $cmake) { $cmake = Find-VsTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' }
if (-not $cmake) { throw 'cmake not found (neither on PATH nor in a Visual Studio installation)' }
$tar = Join-Path $env:WINDIR 'System32\tar.exe'
if (-not (Test-Path -LiteralPath $tar)) { throw 'tar.exe of Windows not found' }

New-Item -ItemType Directory -Force $WorkDir, $OutDir | Out-Null
$srcRoot = Join-Path $WorkDir 'src'
if (Test-Path -LiteralPath $srcRoot) { Remove-Item -LiteralPath $srcRoot -Recurse -Force }
New-Item -ItemType Directory -Force $srcRoot | Out-Null
Invoke-Tool $tar @('-xzf', $SourceArchive, '-C', $srcRoot)
if ($LASTEXITCODE -ne 0) { throw 'unpacking the whisper.cpp sources failed' }
$src = @(Get-ChildItem -LiteralPath $srcRoot -Directory)[0].FullName
$build = Join-Path $WorkDir 'build'
if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force }

$options = @(
    '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW',
    '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',
    '-DBUILD_SHARED_LIBS=OFF',
    '-DGGML_NATIVE=OFF', '-DGGML_SSE42=ON', '-DGGML_AVX=ON', '-DGGML_AVX2=ON', '-DGGML_FMA=ON', '-DGGML_F16C=ON', '-DGGML_BMI2=ON',
    '-DGGML_OPENMP=OFF', '-DGGML_CCACHE=OFF',
    '-DWHISPER_BUILD_TESTS=OFF', '-DWHISPER_BUILD_SERVER=OFF', '-DWHISPER_BUILD_EXAMPLES=ON',
    '-DWHISPER_SDL2=OFF', '-DWHISPER_CURL=OFF'
)
Invoke-Tool $cmake (@('-S', $src, '-B', $build, '-A', 'x64') + $options)
if ($LASTEXITCODE -ne 0) { throw 'cmake configuration of whisper.cpp failed' }
Invoke-Tool $cmake @('--build', $build, '--config', 'Release', '--target', 'whisper-cli', '--parallel')
if ($LASTEXITCODE -ne 0) { throw 'build of whisper-cli failed' }

$exe = Get-ChildItem -LiteralPath $build -Recurse -Filter 'whisper-cli.exe' | Select-Object -First 1
if (-not $exe) { throw 'whisper-cli.exe was not built' }
Copy-Item -LiteralPath $exe.FullName (Join-Path $OutDir 'whisper-cli.exe') -Force
Copy-Item -LiteralPath (Join-Path $src 'LICENSE') (Join-Path $OutDir 'LICENSE') -Force

# proof of the imports: only libraries that are part of Windows may be listed
. (Join-Path $PSScriptRoot 'pe-imports.ps1')
$imports = @(Get-PeImports (Join-Path $OutDir 'whisper-cli.exe'))
Set-Content -LiteralPath (Join-Path $OutDir 'imports.txt') -Value $imports -Encoding ascii
$foreign = @($imports | Where-Object { -not (Test-WindowsLibrary $_) })
if ($foreign.Count) { throw "whisper-cli.exe imports libraries that are not part of Windows: $($foreign -join ', ')" }
Write-Host "whisper-cli.exe built; imports: $($imports -join ', ')"
