# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Builds the Windows release package of EQLMaker:
    out\eqlmaker-windows-x64.zip            the package (stable name)
    out\eqlmaker-windows-x64.zip.sha256     one line: SHA-256, two blanks, file name
    out\eqlmaker-version.json               name, version, asset, sha256, size, components
    out\eqlmaker-<version>-windows-x64.zip  the same file under a versioned name
.DESCRIPTION
  Runs build.ps1, copies the programs, the documentation, the examples and the setup files,
  builds the speech recognition (whisper.cpp) from the pinned source archive, fetches the pinned
  default model and the package archives of the browser sample, checks every fetched file
  against its checksum and writes MANIFEST.json (all files with size and SHA-256).
  Third-party files never enter the repository: they are fetched into -CacheDir.
  Needs: Windows PowerShell 5.1, curl.exe and tar.exe of Windows, the C++ build tools of Visual
  Studio with cmake (all present on the GitHub runner "windows-latest").
.PARAMETER CacheDir
  Folder for fetched archives and the built speech recognition (cache it in CI).
  Default: work\pack-cache in the repository.
.PARAMETER OutDir
  Default: out in the repository.
.PARAMETER AllowMissingDocs
  For local test builds: LICENSE, THIRD_PARTY_NOTICES.md, README.md, README.en.md and docs\ may be missing.
.PARAMETER SkipBuild
  Does not run build.ps1 and takes bin\ as it is.
.PARAMETER KeepStage
  Keeps the unpacked package tree in <OutDir>\stage.
.PARAMETER IncludeVcRuntime
  Adds the three Microsoft Visual C++ run-time libraries that the PostgreSQL programs need to the
  package (tools\vcruntime), for computers that do not have them. They are Microsoft
  redistributable files, not open source; off by default.
#>
param(
    [string]$CacheDir,
    [string]$OutDir,
    [switch]$AllowMissingDocs,
    [switch]$SkipBuild,
    [switch]$KeepStage,
    [switch]$IncludeVcRuntime
)
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$setupSrc = Join-Path $repo 'scripts\setup'
if (-not $CacheDir) { $CacheDir = Join-Path $repo 'work\pack-cache' }
if (-not $OutDir) { $OutDir = Join-Path $repo 'out' }
# relative paths count from the current location of the shell
$CacheDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CacheDir)
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
New-Item -ItemType Directory -Force $CacheDir, $OutDir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-Sha512Base64([string]$Path) {
    $sha = [System.Security.Cryptography.SHA512]::Create()
    $fs = [System.IO.File]::OpenRead($Path)
    try { return [Convert]::ToBase64String($sha.ComputeHash($fs)) } finally { $fs.Dispose(); $sha.Dispose() }
}
function Invoke-Tool([string]$Exe, [string[]]$Arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host "$_" } } finally { $ErrorActionPreference = $old }
}
# Fetches a file into the cache unless the verified file is there already. $Check is a script
# block that returns $true for a correct file.
function Get-Cached([string]$Url, [string]$Name, [scriptblock]$Check, [string]$What) {
    $path = Join-Path $CacheDir $Name
    if ((Test-Path -LiteralPath $path) -and (& $Check $path)) { Write-Host "cache: $Name verified"; return $path }
    if ($Url -notmatch '^https://') { throw "only HTTPS downloads are allowed: $Url" }
    $dir = Split-Path -Parent $path
    New-Item -ItemType Directory -Force $dir | Out-Null
    $part = "$path.part"
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    foreach ($try in 1, 2) {
        Write-Host "fetch: $Url"
        Invoke-Tool (Join-Path $env:WINDIR 'System32\curl.exe') @('--fail', '--location', '--silent', '--show-error', '--proto', '=https', '--proto-redir', '=https', '--retry', '3', '-C', '-', '-o', $part, $Url)
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $part) -and (& $Check $part)) {
            Move-Item -LiteralPath $part $path -Force
            return $path
        }
        if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }
    }
    throw "$What`: the fetched file does not have the pinned checksum (or the download failed): $Url"
}
function Copy-Text([string]$From, [string]$To) {
    # scripts of the package get Windows line ends, whatever the checkout produced
    $text = [System.IO.File]::ReadAllText($From) -replace "`r`n", "`n" -replace "`n", "`r`n"
    [System.IO.File]::WriteAllText($To, $text, $utf8)
}

$version = (Get-Content -LiteralPath (Join-Path $repo 'VERSION') -TotalCount 1).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$') { throw "VERSION does not hold a version number: '$version'" }
Write-Host "EQLMaker $version - building the release package"

# ---------------------------------------------------------------- required files
$missing = @()
foreach ($f in 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md', 'README.en.md') { if (-not (Test-Path -LiteralPath (Join-Path $repo $f))) { $missing += $f } }
if (-not (Test-Path -LiteralPath (Join-Path $repo 'docs')) -or -not @(Get-ChildItem -LiteralPath (Join-Path $repo 'docs') -File -Recurse).Count) { $missing += 'docs\ (empty)' }
if ($missing.Count) {
    if ($AllowMissingDocs) { Write-Host "WARNING: missing and left out (-AllowMissingDocs): $($missing -join ', ')" }
    else { throw "required files are missing: $($missing -join ', ') (use -AllowMissingDocs only for local test builds)" }
}
foreach ($f in 'eqlmaker-setup.cmd', 'scripts\setup\eqlmaker-setup.ps1', 'scripts\setup\components.json', 'scripts\setup\COMPONENTS.md', 'examples\sample-app\db\setup.ps1') {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $f))) { throw "required file is missing: $f" }
}

# ---------------------------------------------------------------- build
if (-not $SkipBuild) {
    Write-Host 'running build.ps1'
    & (Join-Path $repo 'build.ps1')
}
foreach ($f in 'eqlmaker.exe', 'eqlmaker-timestamp.exe', 'sample-desktop.exe', 'eqlmaker-tests.exe', 'share\VERSION') {
    if (-not (Test-Path -LiteralPath (Join-Path $repo "bin\$f"))) { throw "bin\$f is missing (build.ps1 did not produce it)" }
}

# ---------------------------------------------------------------- stage
$stage = Join-Path $OutDir 'stage'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

function Copy-Tree([string]$From, [string]$To, [string[]]$SkipDirs) {
    New-Item -ItemType Directory -Force $To | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $From -Force) {
        if ($item.PSIsContainer) {
            if ($SkipDirs -contains $item.Name) { continue }
            Copy-Tree $item.FullName (Join-Path $To $item.Name) $SkipDirs
        } else { Copy-Item -LiteralPath $item.FullName (Join-Path $To $item.Name) }
    }
}
Copy-Tree (Join-Path $repo 'bin') (Join-Path $stage 'bin') @('node_modules')
if (Test-Path -LiteralPath (Join-Path $repo 'docs')) { Copy-Tree (Join-Path $repo 'docs') (Join-Path $stage 'docs') @('node_modules') }
Copy-Tree (Join-Path $repo 'examples') (Join-Path $stage 'examples') @('node_modules')
# results of local test runs and debugger files do not belong into the package
Get-ChildItem -LiteralPath $stage -Recurse -File -Force |
    Where-Object { $_.Name -eq 'selftest-result.json' -or $_.Name -eq 'test-results.json' -or $_.Extension -eq '.user' -or $_.Extension -eq '.pdb' } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
# LICENSE and every Markdown file of the repository folder (README, ROADMAP, notices, policies, in all languages)
foreach ($f in @('LICENSE') + @(Get-ChildItem -LiteralPath $repo -File -Filter '*.md' | ForEach-Object Name | Sort-Object)) {
    if (Test-Path -LiteralPath (Join-Path $repo $f)) { Copy-Item -LiteralPath (Join-Path $repo $f) (Join-Path $stage $f) }
}
# setup files: the batch files in the root, everything else in setup\
Copy-Text (Join-Path $repo 'eqlmaker-setup.cmd') (Join-Path $stage 'eqlmaker-setup.cmd')
foreach ($f in Get-ChildItem -LiteralPath (Join-Path $setupSrc 'package') -File -Filter '*.cmd') { Copy-Text $f.FullName (Join-Path $stage $f.Name) }
$stageSetup = Join-Path $stage 'setup'
New-Item -ItemType Directory -Force $stageSetup | Out-Null
foreach ($f in 'eqlmaker-setup.ps1', 'make-test-audio.ps1') { Copy-Text (Join-Path $setupSrc $f) (Join-Path $stageSetup $f) }
foreach ($f in 'components.json', 'strings.en.json', 'strings.de.json', 'COMPONENTS.md') { Copy-Item -LiteralPath (Join-Path $setupSrc $f) (Join-Path $stageSetup $f) }

# ---------------------------------------------------------------- speech recognition
$components = Get-Content -LiteralPath (Join-Path $setupSrc 'components.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$whisper = $components.components | Where-Object { $_.id -eq 'whisper' }
$model = $components.components | Where-Object { $_.id -eq 'whisper-model' }
$sourceArchive = Get-Cached $whisper.url $whisper.file { param($p) (Get-Sha256 $p) -eq $whisper.sha256 } 'whisper.cpp sources'
# the built program is cached under the checksums of the sources and of the build script
$buildKey = (Get-Sha256 (Join-Path $setupSrc 'build-whisper.ps1')).Substring(0, 12)
$builtDir = Join-Path $CacheDir "whisper-build-$($whisper.version)-$buildKey"
if (-not (Test-Path -LiteralPath (Join-Path $builtDir 'whisper-cli.exe'))) {
    & (Join-Path $setupSrc 'build-whisper.ps1') -SourceArchive $sourceArchive -WorkDir (Join-Path $CacheDir 'whisper-work') -OutDir $builtDir
} else { Write-Host "cache: whisper-cli.exe $($whisper.version) (build $buildKey)" }
. (Join-Path $setupSrc 'pe-imports.ps1')
$imports = @(Get-PeImports (Join-Path $builtDir 'whisper-cli.exe'))
$foreign = @($imports | Where-Object { -not (Test-WindowsLibrary $_) })
if ($foreign.Count) { throw "whisper-cli.exe imports libraries that are not part of Windows: $($foreign -join ', ')" }
$stageWhisper = Join-Path $stage 'tools\whisper'
New-Item -ItemType Directory -Force (Join-Path $stageWhisper 'models') | Out-Null
Copy-Item -LiteralPath (Join-Path $builtDir 'whisper-cli.exe') (Join-Path $stageWhisper 'whisper-cli.exe')
Copy-Item -LiteralPath (Join-Path $builtDir 'LICENSE') (Join-Path $stageWhisper 'LICENSE')
$modelPath = Get-Cached $model.url $model.file { param($p) (Get-Sha256 $p) -eq $model.sha256 } 'speech model'
Copy-Item -LiteralPath $modelPath (Join-Path $stageWhisper "models\$($model.file)")
$licence = Get-Cached $model.licence_url 'whisper-model-LICENSE.txt' { param($p) (Get-Sha256 $p) -eq $model.licence_sha256 } 'licence of the speech model'
Copy-Item -LiteralPath $licence (Join-Path $stageWhisper 'models\LICENSE')
[System.IO.File]::WriteAllText((Join-Path $stageWhisper 'model.txt'), $model.file + "`r`n", (New-Object System.Text.ASCIIEncoding))

# ---------------------------------------------------------------- optional: Visual C++ run-time for PostgreSQL
$vcRuntime = $null
if ($IncludeVcRuntime) {
    # The PostgreSQL programs of EnterpriseDB need these three libraries. They are taken from the
    # redistributable folder of the Visual Studio build tools of this machine (app-local
    # deployment as described by Microsoft; licence: Microsoft Visual Studio redistributable terms).
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $crt = $null
    if (Test-Path -LiteralPath $vswhere) {
        $crt = & $vswhere -all -products * -find 'VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT\vcruntime140.dll' 2>$null | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Sort-Object | Select-Object -Last 1
    }
    if (-not $crt) { throw '-IncludeVcRuntime: the redistributable folder of the Visual C++ build tools was not found' }
    $crtDir = Split-Path -Parent $crt
    $stageCrt = Join-Path $stage 'tools\vcruntime'
    New-Item -ItemType Directory -Force $stageCrt | Out-Null
    foreach ($dll in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') { Copy-Item -LiteralPath (Join-Path $crtDir $dll) (Join-Path $stageCrt $dll) }
    $vcVersion = (Get-Item -LiteralPath (Join-Path $stageCrt 'vcruntime140.dll')).VersionInfo.FileVersion
    $note = "Microsoft Visual C++ run-time libraries $vcVersion (vcruntime140.dll, vcruntime140_1.dll, msvcp140.dll).`r`n" +
        "Copyright (c) Microsoft Corporation. Redistributable files of Microsoft Visual Studio; they are licensed`r`n" +
        "under the Microsoft Software License Terms of Visual Studio (section on distributable code),`r`n" +
        "https://visualstudio.microsoft.com/license-terms/ . They are used only when this computer does not`r`n" +
        "have them: the setup then copies them next to the PostgreSQL programs.`r`n"
    [System.IO.File]::WriteAllText((Join-Path $stageCrt 'LICENSE.txt'), $note, $utf8)
    $vcRuntime = [ordered]@{ name = 'Microsoft Visual C++ run-time libraries'; version = $vcVersion; licence = 'LicenseRef-Microsoft-Visual-Studio-Redistributable'; delivered = 'package' }
    Write-Host "vc run-time $vcVersion included (tools\vcruntime)"
}

# ---------------------------------------------------------------- package archives of the browser sample
$npmList = @()
# lock files of the browser sample and of the web templates: their package archives go into the
# package, so that 'npm ci --offline' works there
$lockFiles = @()
foreach ($candidate in @((Join-Path $stage 'examples\sample-app\web\package-lock.json')) + @(Get-ChildItem -LiteralPath (Join-Path $stage 'bin\share\templates') -Directory -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { Join-Path $_.FullName 'package-lock.json' })) {
    if (Test-Path -LiteralPath $candidate) { $lockFiles += $candidate }
}
if ($lockFiles.Count) {
    $stageNpm = Join-Path $stage 'downloads\npm'
    New-Item -ItemType Directory -Force $stageNpm | Out-Null
    $seen = @{}
    foreach ($lockFile in $lockFiles) {
        # a lock file has an empty key (the root package), which ConvertFrom-Json of Windows PowerShell rejects
        $lockText = [System.IO.File]::ReadAllText($lockFile, [System.Text.Encoding]::UTF8)
        if ($PSVersionTable.PSVersion.Major -ge 6) { $lock = $lockText | ConvertFrom-Json -AsHashtable }
        else {
            Add-Type -AssemblyName System.Web.Extensions
            $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
            $serializer.MaxJsonLength = [int]::MaxValue
            $lock = $serializer.DeserializeObject($lockText)
        }
        $keys = [string[]]@($lock['packages'].Keys)
        [Array]::Sort($keys, [System.StringComparer]::Ordinal)
        foreach ($key in $keys) {
            $v = $lock['packages'][$key]
            if (-not $v.ContainsKey('resolved')) { continue }
            if ("$($v['integrity'])" -notlike 'sha512-*') { throw "package-lock.json: $key has no SHA-512 integrity" }
            if ($v['resolved'] -notlike 'https://registry.npmjs.org/*') { throw "package-lock.json: $key does not come from the npm registry" }
            $file = ($v['resolved'] -split '/')[-1]
            $want = $v['integrity']
            if ($seen.ContainsKey($file)) {
                if ($seen[$file] -ne $want) { throw "package-lock.json files disagree about $file" }
                continue
            }
            $seen[$file] = $want
            $tgz = Get-Cached $v['resolved'] "npm\$file" { param($q) ('sha512-' + (Get-Sha512Base64 $q)) -eq $want } "npm package $file"
            Copy-Item -LiteralPath $tgz (Join-Path $stageNpm $file)
            $npmList += [ordered]@{ name = ($key -replace '^.*node_modules/', ''); version = $v['version']; licence = $v['license']; delivered = 'package'; file = "downloads/npm/$file" }
        }
    }
    Write-Host "npm: $($npmList.Count) package archives verified against $($lockFiles.Count) lock files"
}

# ---------------------------------------------------------------- manifest
$componentList = @()
foreach ($c in $components.components) {
    $componentList += [ordered]@{ name = $c.name; version = $c.version; licence = $c.licence; delivered = $c.delivery }
}
if ($vcRuntime) { $componentList += $vcRuntime }
foreach ($n in $npmList) { $componentList += [ordered]@{ name = "npm: $($n.name)"; version = $n.version; licence = $n.licence; delivered = 'package' } }

$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Force | ForEach-Object {
        [pscustomobject]@{ Rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/'); Full = $_.FullName; Size = $_.Length }
    })
# fixed order: ordinal by path, independent of the language settings of the machine
$relNames = [string[]]@($files | ForEach-Object Rel)
[Array]::Sort($relNames, [System.StringComparer]::Ordinal)
$byRel = @{}
foreach ($f in $files) { $byRel[$f.Rel] = $f }
$fileList = @()
foreach ($rel in $relNames) { $fileList += [ordered]@{ path = $rel; size = $byRel[$rel].Size; sha256 = (Get-Sha256 $byRel[$rel].Full) } }
$manifest = [ordered]@{
    name       = 'eqlmaker'
    version    = $version
    platform   = 'windows-x64'
    components = $componentList
    whisper    = [ordered]@{ version = $whisper.version; source_sha256 = $whisper.sha256; imports = $imports; default_model = $model.file }
    files      = $fileList
}
[System.IO.File]::WriteAllText((Join-Path $stage 'MANIFEST.json'), ($manifest | ConvertTo-Json -Depth 6), $utf8)
$relNames = [string[]](@($relNames) + 'MANIFEST.json')
[Array]::Sort($relNames, [System.StringComparer]::Ordinal)

# ---------------------------------------------------------------- zip
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$asset = 'eqlmaker-windows-x64.zip'
$zipPath = Join-Path $OutDir $asset
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
# one time stamp for all entries: SOURCE_DATE_EPOCH when set, otherwise now
$stamp = [DateTimeOffset]::Now
if ($env:SOURCE_DATE_EPOCH) { $stamp = [DateTimeOffset]::FromUnixTimeSeconds([long]$env:SOURCE_DATE_EPOCH).ToLocalTime() }
$zipStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($rel in $relNames) {
        # model files and package archives are compressed already
        $level = [System.IO.Compression.CompressionLevel]::Optimal
        if ($rel -like '*.bin' -or $rel -like '*.tgz') { $level = [System.IO.Compression.CompressionLevel]::NoCompression }
        $entry = $zip.CreateEntry($rel, $level)
        $entry.LastWriteTime = $stamp
        $in = [System.IO.File]::OpenRead((Join-Path $stage $rel.Replace('/', '\')))
        $out = $entry.Open()
        try { $in.CopyTo($out, 1048576) } finally { $out.Dispose(); $in.Dispose() }
    }
} finally { $zip.Dispose(); $zipStream.Dispose() }

$zipHash = Get-Sha256 $zipPath
$zipSize = (Get-Item -LiteralPath $zipPath).Length
[System.IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  $asset`n", (New-Object System.Text.ASCIIEncoding))
$versionInfo = [ordered]@{ name = 'eqlmaker'; version = $version; asset = $asset; sha256 = $zipHash; size = $zipSize; components = $componentList }
[System.IO.File]::WriteAllText((Join-Path $OutDir 'eqlmaker-version.json'), ($versionInfo | ConvertTo-Json -Depth 5), $utf8)
Copy-Item -LiteralPath $zipPath (Join-Path $OutDir "eqlmaker-$version-windows-x64.zip") -Force
if (-not $KeepStage) { Remove-Item -LiteralPath $stage -Recurse -Force }

Write-Host ''
Write-Host ("package: {0}" -f $zipPath)
Write-Host ("size:    {0} MB ({1} bytes), {2} files" -f ([Math]::Round($zipSize / 1048576.0, 1)).ToString([System.Globalization.CultureInfo]::InvariantCulture), $zipSize, $relNames.Count)
Write-Host ("sha256:  {0}" -f $zipHash)
