# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
#
# Sets up an unpacked EQLMaker package inside its own folder: downloads the pinned portable
# components (FFmpeg, Node.js, optionally PostgreSQL), checks every file against its SHA-256,
# unpacks them, creates the portable database cluster with the sample database and runs the
# self test. Nothing is written outside the package folder, no admin rights are used.
# Started by eqlmaker-setup.cmd; the options are described in docs\setup.md.
#
# Exit codes: 0 ready, 1 failed, 2 self test failed, 3 offline and files are missing.

$ErrorActionPreference = 'Stop'
$script:ExitCode = 0
if ($env:EQLMAKER_SETUP_MARK) {
    try { Set-Content -LiteralPath $env:EQLMAKER_SETUP_MARK -Value 'started' -Encoding ascii } catch { }
}

$SetupDir = $PSScriptRoot
$Root = [System.IO.Path]::GetFullPath((Join-Path $SetupDir '..')).TrimEnd('\')
$Downloads = Join-Path $Root 'downloads'
$TempDir = Join-Path $Downloads 'tmp'
$LogFile = Join-Path $Downloads 'setup.log'

# ------------------------------------------------------------------ options
$opt = @{ Offline = $false; Lang = ''; Model = ''; NoPostgres = $false; NoSelftest = $false; Unattended = $false; Help = $false }
$badOption = $null
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = "$($args[$i])"
    switch -Regex ($a.ToLowerInvariant()) {
        '^[-/]+offline$' { $opt.Offline = $true; break }
        '^[-/]+no-postgres$' { $opt.NoPostgres = $true; break }
        '^[-/]+no-selftest$' { $opt.NoSelftest = $true; break }
        '^[-/]+unattended$' { $opt.Unattended = $true; break }
        '^[-/]+(help|h|\?)$' { $opt.Help = $true; break }
        '^[-/]+lang$' { $i++; if ($i -lt $args.Count) { $opt.Lang = "$($args[$i])".ToLowerInvariant() } else { $badOption = $a }; break }
        '^[-/]+model$' { $i++; if ($i -lt $args.Count) { $opt.Model = "$($args[$i])" } else { $badOption = $a }; break }
        default { $badOption = $a }
    }
}

# ------------------------------------------------------------------ texts and output
if ($opt.Lang -ne 'en' -and $opt.Lang -ne 'de') {
    if ($opt.Lang) { $badOption = "-lang $($opt.Lang)" }
    $opt.Lang = 'en'
    try { if ((Get-UICulture).TwoLetterISOLanguageName -eq 'de') { $opt.Lang = 'de' } } catch { }
}
function Read-Texts([string]$Lang) {
    $file = Join-Path $SetupDir "strings.$Lang.json"
    $map = @{}
    if (Test-Path -LiteralPath $file) {
        $obj = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        foreach ($p in $obj.PSObject.Properties) { $map[$p.Name] = [string]$p.Value }
    }
    return $map
}
$TextsEn = Read-Texts 'en'
$Texts = if ($opt.Lang -eq 'en') { $TextsEn } else { Read-Texts $opt.Lang }

# Umlauts are written as they are only where that is safe: on a real console window or when the
# output code page is UTF-8. Redirected output in another code page gets ASCII replacements.
$script:AsciiOnly = $false
try { if ([Console]::IsOutputRedirected -and [Console]::OutputEncoding.CodePage -ne 65001) { $script:AsciiOnly = $true } } catch { $script:AsciiOnly = $true }
function ConvertTo-Safe([string]$s) {
    if (-not $script:AsciiOnly) { return $s }
    $pairs = @(([char]0xE4), 'ae', ([char]0xF6), 'oe', ([char]0xFC), 'ue', ([char]0xC4), 'Ae', ([char]0xD6), 'Oe', ([char]0xDC), 'Ue', ([char]0xDF), 'ss')
    for ($k = 0; $k -lt $pairs.Count; $k += 2) { $s = $s.Replace([string]$pairs[$k], [string]$pairs[$k + 1]) }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) { if ([int]$ch -lt 128) { [void]$sb.Append($ch) } else { [void]$sb.Append('?') } }
    return $sb.ToString()
}
function T([string]$Key) {
    $s = $Texts[$Key]
    if (-not $s) { $s = $TextsEn[$Key] }
    if (-not $s) { $s = $Key }
    $rest = @($args)
    if ($rest.Count) { $s = [string]::Format($s, [object[]]$rest) }
    return $s
}
function Write-Log([string]$s) {
    try { Add-Content -LiteralPath $LogFile -Value ("{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $s) -Encoding UTF8 } catch { }
}
function Say([string]$s) {
    Write-Host (ConvertTo-Safe $s)
    Write-Log $s
}
function Step([string]$s) { Say ''; Say "== $s" }

class SetupStop : System.Exception {
    [int]$Code
    SetupStop([string]$message, [int]$code) : base($message) { $this.Code = $code }
}
function Stop-Setup([string]$Message, [int]$Code = 1) { throw [SetupStop]::new($Message, $Code) }

# ------------------------------------------------------------------ helpers
function Get-Sha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = [System.IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($fs)) -replace '-', '').ToLowerInvariant() } finally { $fs.Dispose(); $sha.Dispose() }
}
function Get-Sha512Base64([string]$Path) {
    $sha = [System.Security.Cryptography.SHA512]::Create()
    $fs = [System.IO.File]::OpenRead($Path)
    try { return [Convert]::ToBase64String($sha.ComputeHash($fs)) } finally { $fs.Dispose(); $sha.Dispose() }
}
function Format-Size([long]$Bytes) { return ([Math]::Round($Bytes / 1048576.0, 1)).ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture) + ' MB' }

# Runs a program and returns its output lines; the exit code is in $LASTEXITCODE.
function Invoke-Tool([string]$Exe, [string[]]$Arguments, [string]$WorkDir) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($WorkDir) { Push-Location -LiteralPath $WorkDir }
    # the comma keeps a single line an array
    try { return , @(& $Exe @Arguments 2>&1 | ForEach-Object { "$_" }) }
    finally { if ($WorkDir) { Pop-Location }; $ErrorActionPreference = $old }
}

# Runs a program without a window and without handing over the output handles (needed for the
# database server: it must neither keep a pipe of this script open nor die with this console).
function Start-Hidden([string]$Exe, [string]$ArgumentLine, [string]$WorkDir, [int]$TimeoutSeconds) {
    $p = Start-Process -FilePath $Exe -ArgumentList $ArgumentLine -WorkingDirectory $WorkDir -WindowStyle Hidden -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { return -1 }
    return $p.ExitCode
}

function Get-ProxyFor([string]$Url) {
    # 1. environment (the same names curl knows), 2. proxy configured in Windows
    foreach ($name in 'HTTPS_PROXY', 'https_proxy', 'ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy') {
        $v = [Environment]::GetEnvironmentVariable($name)
        if ($v) { return [pscustomobject]@{ Address = $v; Source = 'environment' } }
    }
    try {
        $uri = [Uri]$Url
        $sys = [System.Net.WebRequest]::GetSystemWebProxy()
        if (-not $sys.IsBypassed($uri)) {
            $p = $sys.GetProxy($uri)
            if ($p -and $p.AbsoluteUri -ne $uri.AbsoluteUri) { return [pscustomobject]@{ Address = $p.AbsoluteUri.TrimEnd('/'); Source = 'system' } }
        }
    } catch { }
    return $null
}

function Invoke-Download([string]$Url, [string]$Target) {
    # resumes a partial file at $Target; returns $true when the transfer ended without error
    if ($Url -notmatch '^https://') { Stop-Setup (T 'err.nothttps' $Url) }
    $proxy = Get-ProxyFor $Url
    $curl = Join-Path $env:WINDIR 'System32\curl.exe'
    if (Test-Path -LiteralPath $curl) {
        $a = @('--fail', '--location', '--proto', '=https', '--proto-redir', '=https', '--retry', '3', '--retry-delay', '2', '--connect-timeout', '30', '-C', '-', '-o', $Target)
        if ($proxy -and $proxy.Source -eq 'system') { $a += @('--proxy', $proxy.Address) }
        $quiet = $true
        try { $quiet = [Console]::IsOutputRedirected -or [Console]::IsErrorRedirected } catch { }
        if ($quiet) { $a += @('--silent', '--show-error') } else { $a += '--progress-bar' }
        $a += $Url
        $old = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            if ($quiet) { & $curl @a 2>&1 | ForEach-Object { Say "  curl: $_" } } else { & $curl @a }
        } finally { $ErrorActionPreference = $old }
        Write-Log "curl exit code $LASTEXITCODE for $Url"
        return ($LASTEXITCODE -eq 0)
    }
    # fallback without curl: .NET, with resume through a Range request
    try {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
        $req = [System.Net.HttpWebRequest]::Create($Url)
        $req.UserAgent = 'eqlmaker-setup'
        if ($proxy) { $req.Proxy = New-Object System.Net.WebProxy($proxy.Address) }
        $have = 0
        if (Test-Path -LiteralPath $Target) { $have = (Get-Item -LiteralPath $Target).Length }
        if ($have -gt 0) { $req.AddRange([long]$have) }
        $resp = $req.GetResponse()
        $mode = [System.IO.FileMode]::Create
        if ($have -gt 0 -and [int]$resp.StatusCode -eq 206) { $mode = [System.IO.FileMode]::Append }
        $in = $resp.GetResponseStream()
        $out = New-Object System.IO.FileStream($Target, $mode)
        try { $in.CopyTo($out, 1048576) } finally { $out.Dispose(); $in.Dispose(); $resp.Dispose() }
        return $true
    } catch {
        Say "  $($_.Exception.Message)"
        return $false
    }
}

$script:Missing = New-Object System.Collections.Generic.List[object]

# Makes sure that $Path is the file described by $Item (url, sha256, size). Returns $true when the
# verified file is there. In offline mode a missing file is noted and $false is returned.
function Get-VerifiedFile($Item, [string]$Path, [string]$Label) {
    $part = "$Path.part"
    if (Test-Path -LiteralPath $Path) {
        if ((Get-Sha256 $Path) -eq $Item.sha256) { Say (T 'dl.cached' $Label); return $true }
        $len = (Get-Item -LiteralPath $Path).Length
        if ($opt.Offline) {
            # offline nothing is touched: the damaged file is only reported
            Say (T 'dl.badsum.cached' $Label)
            $script:Missing.Add([pscustomobject]@{ Label = $Label; File = $Path; Url = $Item.url; Sha256 = $Item.sha256 })
            return $false
        }
        if ($len -lt [long]$Item.size) {
            Say (T 'dl.incomplete' $Label (Format-Size $len) (Format-Size $Item.size))
            if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }
            Move-Item -LiteralPath $Path $part
        } else {
            Say (T 'dl.badsum.cached' $Label)
            if ($opt.Offline) {
                $script:Missing.Add([pscustomobject]@{ Label = $Label; File = $Path; Url = $Item.url; Sha256 = $Item.sha256 })
                return $false
            }
            Remove-Item -LiteralPath $Path -Force
        }
    }
    if ($opt.Offline) {
        $script:Missing.Add([pscustomobject]@{ Label = $Label; File = $Path; Url = $Item.url; Sha256 = $Item.sha256 })
        return $false
    }
    for ($try = 1; $try -le 2; $try++) {
        $have = 0
        if (Test-Path -LiteralPath $part) { $have = (Get-Item -LiteralPath $part).Length }
        if ($have -gt [long]$Item.size) { Remove-Item -LiteralPath $part -Force; $have = 0 }
        if ($have -lt [long]$Item.size) {
            if ($have -gt 0) { Say (T 'dl.resume' $Label (Format-Size $have) (Format-Size $Item.size)) }
            else { Say (T 'dl.start' $Label (Format-Size $Item.size)); Say "  $($Item.url)" }
            if (-not (Invoke-Download $Item.url $part)) {
                if ($try -eq 2) { Stop-Setup (T 'err.download' $Label $Item.url) }
                continue
            }
        }
        if ((Test-Path -LiteralPath $part) -and (Get-Sha256 $part) -eq $Item.sha256) {
            Move-Item -LiteralPath $part $Path -Force
            Say (T 'dl.verified' $Label)
            return $true
        }
        if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force }
        if ($try -eq 1) { Say (T 'dl.badsum.retry' $Label) }
    }
    Stop-Setup (T 'err.checksum' $Label $Item.sha256)
}

function Test-Pattern([string]$Rel, $Patterns) {
    foreach ($p in @($Patterns)) {
        if (-not $p) { continue }
        if ($p -eq '*') { return $true }
        if ($Rel -like $p) { return $true }
    }
    return $false
}

# Unpacks a zip archive with the rules of a component. All entries are checked first; an entry
# that would land outside the target stops the setup before anything is written.
function Expand-Component($Comp, [string]$Archive, [string]$Target) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $strip = 0
    if ($Comp.PSObject.Properties['strip']) { $strip = [int]$Comp.strip }
    $include = @('*'); if ($Comp.PSObject.Properties['include'] -and @($Comp.include).Count) { $include = @($Comp.include) }
    $exclude = @(); if ($Comp.PSObject.Properties['exclude']) { $exclude = @($Comp.exclude) }
    $new = "$Target.new"
    if (Test-Path -LiteralPath $new) { Remove-Item -LiteralPath $new -Recurse -Force }
    $newFull = [System.IO.Path]::GetFullPath($new).TrimEnd('\')
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $plan = New-Object System.Collections.Generic.List[object]
        foreach ($e in $zip.Entries) {
            $name = $e.FullName.Replace('\', '/')
            if ($name.StartsWith('/') -or $name -match '^[A-Za-z]:' -or $name -match '(^|/)\.\.(/|$)' -or $name.IndexOf([char]0) -ge 0) {
                Stop-Setup (T 'err.traversal' $Comp.name $e.FullName)
            }
            if ($name.EndsWith('/')) { continue }
            $parts = $name.Split('/')
            if ($parts.Count -le $strip) { continue }
            $rel = ($parts[$strip..($parts.Count - 1)]) -join '/'
            if (-not (Test-Pattern $rel $include)) { continue }
            if (Test-Pattern $rel $exclude) { continue }
            $dest = [System.IO.Path]::GetFullPath((Join-Path $newFull $rel.Replace('/', '\')))
            if (-not $dest.StartsWith($newFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-Setup (T 'err.traversal' $Comp.name $e.FullName)
            }
            if ($dest.Length -gt 259) { Stop-Setup (T 'err.longpath' $Root.Length (Get-PathLimit)) }
            $plan.Add([pscustomobject]@{ Entry = $e; Dest = $dest })
        }
        if (-not $plan.Count) { Stop-Setup (T 'err.emptyarchive' $Comp.name) }
        $made = @{}
        foreach ($item in $plan) {
            $dir = [System.IO.Path]::GetDirectoryName($item.Dest)
            if (-not $made.ContainsKey($dir)) { [void][System.IO.Directory]::CreateDirectory($dir); $made[$dir] = 1 }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($item.Entry, $item.Dest, $true)
        }
        $count = $plan.Count
    } finally { $zip.Dispose() }
    if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Recurse -Force }
    Move-Item -LiteralPath $new $Target
    return $count
}

# longest path below the package folder that the setup itself creates (tools\node\node_modules\...)
function Get-PathLimit { return 140 }

function Get-Marker([string]$Target) {
    $f = Join-Path $Target '.eqlmaker-component.json'
    if (Test-Path -LiteralPath $f) { try { return (Get-Content -LiteralPath $f -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { } }
    return $null
}
function Set-Marker([string]$Target, $Comp, [int]$Files) {
    $o = [ordered]@{ id = $Comp.id; version = $Comp.version; sha256 = $Comp.sha256; files = $Files }
    Set-Content -LiteralPath (Join-Path $Target '.eqlmaker-component.json') -Value ($o | ConvertTo-Json) -Encoding UTF8
}

function Test-ComponentInstalled($Comp) {
    $target = Join-Path $Root ($Comp.target.Replace('/', '\'))
    $check = Join-Path $Root ($Comp.check.Replace('/', '\'))
    $marker = Get-Marker $target
    return [bool]($marker -and $marker.sha256 -eq $Comp.sha256 -and (Test-Path -LiteralPath $check))
}

# Step 1 of 2: the verified archive is in downloads\ (or the component is installed already).
function Get-ComponentArchive($Comp) {
    $archive = Join-Path $Downloads $Comp.file
    if (Test-ComponentInstalled $Comp) {
        # installed: the archive is not needed again, it is only verified when it is still there
        if (Test-Path -LiteralPath $archive) {
            if ((Get-Sha256 $archive) -ne $Comp.sha256) { Say (T 'dl.badsum.cached' $Comp.file) } else { Say (T 'dl.cached' $Comp.file) }
        }
        return $true
    }
    return (Get-VerifiedFile $Comp $archive $Comp.file)
}

# Step 2 of 2: unpack. Only called when all archives of this run are there and verified.
function Install-Component($Comp) {
    $target = Join-Path $Root ($Comp.target.Replace('/', '\'))
    if (Test-ComponentInstalled $Comp) {
        Say (T 'comp.present' $Comp.name $Comp.version $Comp.target.Replace('/', '\'))
        return
    }
    $archive = Join-Path $Downloads $Comp.file
    # checked again directly before unpacking
    if ((Get-Sha256 $archive) -ne $Comp.sha256) { Stop-Setup (T 'err.checksum' $Comp.file $Comp.sha256) }
    Say (T 'comp.unpack' $Comp.name $Comp.target.Replace('/', '\'))
    $n = Expand-Component $Comp $archive $target
    $check = Join-Path $Root ($Comp.check.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $check)) { Stop-Setup (T 'err.unpack' $Comp.name $Comp.check) }
    Set-Marker $target $Comp $n
    Say (T 'comp.installed' $Comp.name $Comp.version $n)
}

function Test-PortFree([int]$Port) {
    $used = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | ForEach-Object { $_.Port }
    return -not ($used -contains $Port)
}
function New-Password([int]$Length) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 1
    $sb = New-Object System.Text.StringBuilder
    try {
        while ($sb.Length -lt $Length) {
            $rng.GetBytes($bytes)
            # 228 = 4 * 57: values above are thrown away so that every character is equally likely
            if ($bytes[0] -lt 228) { [void]$sb.Append($chars[$bytes[0] % $chars.Length]) }
        }
    } finally { $rng.Dispose() }
    return $sb.ToString()
}
function Protect-File([string]$Path) {
    # readable by the current user only; works without admin rights for own files
    try {
        $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $null = Invoke-Tool (Join-Path $env:WINDIR 'System32\icacls.exe') @($Path, '/inheritance:r', '/grant:r', "*${sid}:(F)")
        return ($LASTEXITCODE -eq 0)
    } catch { return $false }
}
function Read-PgPass([string]$File, [string]$User) {
    if (-not (Test-Path -LiteralPath $File)) { return $null }
    foreach ($line in [System.IO.File]::ReadAllLines($File)) {
        $f = $line.Split(':')
        if ($f.Count -ge 5 -and $f[3] -eq $User) { return ($f[4..($f.Count - 1)] -join ':') }
    }
    return $null
}

# ------------------------------------------------------------------ main
$summary = New-Object System.Collections.Generic.List[string]
$versions = [ordered]@{}
try {
    if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
        Write-Host (ConvertTo-Safe (T 'err.languagemode' $ExecutionContext.SessionState.LanguageMode))
        exit 1
    }
    if ($opt.Help -or $badOption) {
        if ($badOption) { Write-Host (ConvertTo-Safe (T 'err.option' $badOption)) }
        Write-Host (ConvertTo-Safe (T 'usage'))
        if ($badOption) { exit 1 } else { exit 0 }
    }
    New-Item -ItemType Directory -Force $Downloads, $TempDir | Out-Null
    # temporary files of this script and of the programs it starts stay inside the package folder
    $env:TEMP = $TempDir
    $env:TMP = $TempDir
    $env:LC_MESSAGES = 'C'
    Write-Log "---- setup started: $($args -join ' ')"

    $packageVersion = ''
    $versionFile = Join-Path $Root 'bin\share\VERSION'
    if (Test-Path -LiteralPath $versionFile) { $packageVersion = (Get-Content -LiteralPath $versionFile -TotalCount 1).Trim() }
    Say (T 'title' $packageVersion)
    Say (T 'folder' $Root)
    if ($opt.Offline) { Say (T 'mode.offline') }

    if ($Root.Length -gt (Get-PathLimit)) { Stop-Setup (T 'err.longpath' $Root.Length (Get-PathLimit)) }
    foreach ($need in 'bin\eqlmaker.exe', 'setup\components.json') {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $need))) { Stop-Setup (T 'err.notpackage' $need) }
    }
    $components = Get-Content -LiteralPath (Join-Path $SetupDir 'components.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $byId = @{}
    foreach ($c in $components.components) { $byId[$c.id] = $c }

    # ---------------------------------------------------------- 1. shipped speech recognition
    Step (T 'step.whisper')
    $whisperDir = Join-Path $Root 'tools\whisper'
    $whisperExe = Join-Path $whisperDir 'whisper-cli.exe'
    $modelTxt = Join-Path $whisperDir 'model.txt'
    if (-not (Test-Path -LiteralPath $whisperExe) -or -not (Test-Path -LiteralPath $modelTxt)) { Stop-Setup (T 'err.notpackage' 'tools\whisper') }
    $manifestFile = Join-Path $Root 'MANIFEST.json'
    $manifest = $null
    if (Test-Path -LiteralPath $manifestFile) { $manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    if ($manifest) {
        $bad = 0; $n = 0
        foreach ($f in $manifest.files) {
            if ($f.path -notlike 'tools/whisper/*') { continue }
            $p = Join-Path $Root $f.path.Replace('/', '\')
            if ($f.path -eq 'tools/whisper/model.txt') { continue }
            $n++
            if (-not (Test-Path -LiteralPath $p) -or (Get-Sha256 $p) -ne $f.sha256) { $bad++; Say (T 'whisper.badfile' $f.path) }
        }
        if ($bad) { Stop-Setup (T 'err.whisper.damaged') }
        Say (T 'whisper.verified' $n)
    }
    if ($opt.Model) {
        $m = $components.models | Where-Object { $_.name -eq $opt.Model } | Select-Object -First 1
        if (-not $m) { Stop-Setup (T 'err.model.unknown' $opt.Model (($components.models | ForEach-Object { $_.name }) -join ', ')) }
        $modelPath = Join-Path $whisperDir "models\$($m.file)"
        if (Get-VerifiedFile $m $modelPath $m.file) {
            Set-Content -LiteralPath $modelTxt -Value $m.file -Encoding ascii
            Say (T 'whisper.model.set' $m.file)
        }
    }
    $modelName = (Get-Content -LiteralPath $modelTxt -TotalCount 1).Trim()
    $modelFile = Join-Path $whisperDir "models\$modelName"
    if (-not (Test-Path -LiteralPath $modelFile)) { Stop-Setup (T 'err.model.missing' $modelName) }
    $extra = $components.models | Where-Object { $_.file -eq $modelName } | Select-Object -First 1
    if ($extra -and (Get-Sha256 $modelFile) -ne $extra.sha256) { Stop-Setup (T 'err.model.damaged' $modelName) }
    Say (T 'whisper.model' $modelName (Format-Size (Get-Item -LiteralPath $modelFile).Length))

    # ---------------------------------------------------------- 2. decide about PostgreSQL
    $projectDir = Join-Path $Root 'project'
    $projectFile = Join-Path $projectDir 'eqlmaker.json'
    $dataDir = Join-Path $Root 'data'
    $cluster = Join-Path $dataDir 'pg'
    $portFile = Join-Path $dataDir 'pg-port.txt'
    $passFile = Join-Path $dataDir 'pgpass.conf'
    $hasCluster = Test-Path -LiteralPath (Join-Path $cluster 'PG_VERSION')
    $usePg = $true
    if ($opt.NoPostgres) { $usePg = $false }
    elseif (-not $hasCluster) {
        if (Test-Path -LiteralPath $projectFile) {
            # a connection is configured already and it is not the portable one
            $usePg = $false
            Say (T 'pg.ownconfig')
        } elseif (-not $opt.Unattended) {
            $interactive = $true
            try { if ([Console]::IsInputRedirected) { $interactive = $false } } catch { $interactive = $false }
            if ($interactive) {
                Say ''
                $answer = Read-Host (ConvertTo-Safe (T 'pg.ask' (Format-Size $byId['postgresql'].size)))
                if ($answer -match '^\s*(n|no|nein)\s*$') { $usePg = $false }
            }
        }
    }
    $runtimeToCopy = @()
    $PgRoot = $Root
    if ($usePg) {
        # PostgreSQL on Windows cannot work below a path with characters outside ASCII (the path
        # reaches the server as text in the wrong encoding). Windows usually has a short name
        # (8.3) for such a folder; the PostgreSQL programs are then run through that name.
        if ($Root -match '[^\x20-\x7E]') {
            try { $PgRoot = (New-Object -ComObject Scripting.FileSystemObject).GetFolder($Root).ShortPath.TrimEnd('\') } catch { }
            if ($PgRoot -match '[^\x20-\x7E]') { Stop-Setup (T 'err.pg.path') }
        }
        # The PostgreSQL programs need the Microsoft Visual C++ run-time libraries. They are on
        # most computers; where they are not, copies shipped in tools\vcruntime are put next to
        # the PostgreSQL programs. Checked before anything is downloaded.
        $system = Join-Path $env:WINDIR 'System32'
        if (-not [Environment]::Is64BitProcess) { $system = Join-Path $env:WINDIR 'Sysnative' }
        foreach ($dll in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') {
            $inSystem = (Test-Path -LiteralPath (Join-Path $system $dll)) -and -not $env:EQLMAKER_TEST_NO_SYSTEM_VCRUNTIME
            if ($inSystem -or (Test-Path -LiteralPath (Join-Path $Root "tools\pgsql\bin\$dll"))) { continue }
            if (-not (Test-Path -LiteralPath (Join-Path $Root "tools\vcruntime\$dll"))) { Stop-Setup (T 'err.pg.vcruntime' $dll) }
            $runtimeToCopy += $dll
        }
    }

    # ---------------------------------------------------------- 3. downloads and unpacking
    Step (T 'step.components')
    $wanted = @('ffmpeg', 'node')
    if ($usePg) { $wanted += 'postgresql' }
    $allThere = $true
    foreach ($id in $wanted) { if (-not (Get-ComponentArchive $byId[$id])) { $allThere = $false } }

    # dependencies of the sample's browser variant: tarballs shipped in downloads\npm, each one
    # is checked against the SHA-512 of package-lock.json before it goes into the local cache
    $webDir = Join-Path $Root 'examples\sample-app\web'
    $lockFile = Join-Path $webDir 'package-lock.json'
    $npmCache = Join-Path $Downloads 'npm-cache'
    $npmTarballs = Join-Path $Downloads 'npm'
    $npmNeeded = (Test-Path -LiteralPath $lockFile) -and -not (Test-Path -LiteralPath (Join-Path $webDir 'node_modules\.package-lock.json'))
    $lockEntries = @()
    if (Test-Path -LiteralPath $lockFile) {
        # the lock file has an empty key (the root package), which ConvertFrom-Json of Windows PowerShell rejects
        Add-Type -AssemblyName System.Web.Extensions
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        $serializer.MaxJsonLength = [int]::MaxValue
        $lock = $serializer.DeserializeObject([System.IO.File]::ReadAllText($lockFile, [System.Text.Encoding]::UTF8))
        foreach ($key in @($lock['packages'].Keys)) {
            $v = $lock['packages'][$key]
            if ($v.ContainsKey('resolved') -and $v.ContainsKey('integrity')) {
                $lockEntries += [pscustomobject]@{ File = ($v['resolved'] -split '/')[-1]; Url = $v['resolved']; Integrity = $v['integrity'] }
            }
        }
    }
    $npmOffline = $true
    foreach ($e in $lockEntries) {
        $tgz = Join-Path $npmTarballs $e.File
        if (-not (Test-Path -LiteralPath $tgz)) { $npmOffline = $false; continue }
        if ($e.Integrity -notlike 'sha512-*' -or ('sha512-' + (Get-Sha512Base64 $tgz)) -ne $e.Integrity) { Stop-Setup (T 'err.npm.integrity' $e.File) }
    }
    if ($lockEntries.Count -and $npmOffline) { Say (T 'npm.verified' $lockEntries.Count) }
    if ($npmNeeded -and -not $npmOffline -and $opt.Offline) {
        foreach ($e in $lockEntries) {
            if (-not (Test-Path -LiteralPath (Join-Path $npmTarballs $e.File))) {
                $script:Missing.Add([pscustomobject]@{ Label = $e.File; File = (Join-Path $npmTarballs $e.File); Url = $e.Url; Sha256 = $e.Integrity })
            }
        }
        $allThere = $false
    }

    if (-not $allThere) {
        Say ''
        Say (T 'offline.missing')
        foreach ($m in $script:Missing) {
            Say (T 'offline.missing.item' $m.Label)
            Say "    $($m.Url)"
            Say "    -> $($m.File)"
            Say "    $($m.Sha256)"
        }
        Say (T 'offline.missing.hint')
        Stop-Setup (T 'err.offline') 3
    }

    # all archives are there and verified: unpack what is not installed yet
    # (the running portable server is stopped before its programs are replaced by another version)
    if ($usePg -and $hasCluster -and -not (Test-ComponentInstalled $byId['postgresql']) -and (Test-Path -LiteralPath (Join-Path $Root 'tools\pgsql\bin\pg_ctl.exe'))) {
        $null = Invoke-Tool (Join-Path $PgRoot 'tools\pgsql\bin\pg_ctl.exe') @('-D', 'data\pg', '-m', 'fast', '-w', 'stop') $PgRoot
    }
    foreach ($id in $wanted) { Install-Component $byId[$id] }
    foreach ($dll in $runtimeToCopy) {
        Copy-Item -LiteralPath (Join-Path $Root "tools\vcruntime\$dll") (Join-Path $Root "tools\pgsql\bin\$dll") -Force
        Say (T 'pg.vcruntime.copied' $dll)
    }

    # ---------------------------------------------------------- 4. the tools answer
    Step (T 'step.tools')
    $ffmpeg = Join-Path $Root 'tools\ffmpeg\bin\ffmpeg.exe'
    $out = Invoke-Tool $ffmpeg @('-hide_banner', '-version')
    if ($LASTEXITCODE -ne 0) { Stop-Setup (T 'err.tool' 'ffmpeg') }
    $config = "$($out | Where-Object { $_ -like 'configuration:*' } | Select-Object -First 1)"
    if ($config -match '--enable-gpl|--enable-nonfree') { Stop-Setup (T 'err.ffmpeg.licence') }
    $versions['FFmpeg'] = ("$($out[0])" -replace '^ffmpeg version (\S+).*$', '$1')
    Say (T 'tool.ok' 'FFmpeg' $versions['FFmpeg'] 'tools\ffmpeg\bin\ffmpeg.exe')
    $node = Join-Path $Root 'tools\node\node.exe'
    $out = Invoke-Tool $node @('--version')
    if ($LASTEXITCODE -ne 0) { Stop-Setup (T 'err.tool' 'node') }
    $versions['Node.js'] = "$($out[0])".Trim()
    Say (T 'tool.ok' 'Node.js' $versions['Node.js'] 'tools\node\node.exe')
    $out = Invoke-Tool $whisperExe @('--version')
    if ($LASTEXITCODE -ne 0) {
        if ($LASTEXITCODE -eq -1073741795) { Stop-Setup (T 'err.cpu') }
        Stop-Setup (T 'err.tool' 'whisper-cli')
    }
    $versions['whisper.cpp'] = $byId['whisper'].version
    Say (T 'tool.ok' 'whisper.cpp' $versions['whisper.cpp'] 'tools\whisper\whisper-cli.exe')

    # ---------------------------------------------------------- 5. packages of the browser sample
    if ($npmNeeded) {
        Step (T 'step.npm')
        $npm = Join-Path $Root 'tools\node\npm.cmd'
        $oldPath = $env:PATH
        $env:PATH = (Join-Path $Root 'tools\node') + ';' + $env:PATH
        $env:npm_config_cache = $npmCache
        $env:npm_config_update_notifier = 'false'
        $env:npm_config_fund = 'false'
        $env:npm_config_audit = 'false'
        try {
            $ciArgs = @('ci', '--no-audit', '--no-fund')
            if ($npmOffline) {
                foreach ($e in $lockEntries) {
                    $r = Invoke-Tool $npm @('cache', 'add', (Join-Path $npmTarballs $e.File), '--offline')
                    if ($LASTEXITCODE -ne 0) { $r | ForEach-Object { Say "  $_" }; Stop-Setup (T 'err.npm') }
                }
                $ciArgs += '--offline'
            }
            $r = Invoke-Tool $npm $ciArgs $webDir
            $r | ForEach-Object { if ($_.Trim()) { Say "  $_" } }
            if ($LASTEXITCODE -ne 0) { Stop-Setup (T 'err.npm') }
        } finally { $env:PATH = $oldPath }
        Say (T 'npm.done' 'examples\sample-app\web')
    } elseif (Test-Path -LiteralPath $lockFile) {
        Step (T 'step.npm')
        Say (T 'npm.present' 'examples\sample-app\web')
    }

    # ---------------------------------------------------------- 6. portable PostgreSQL
    $port = 0
    $pgRunning = $false
    if ($usePg) {
        Step (T 'step.postgres')
        $pgBin = Join-Path $PgRoot 'tools\pgsql\bin'
        if ($PgRoot -ne $Root) { Say (T 'pg.shortpath' $PgRoot) }
        $out = Invoke-Tool (Join-Path $pgBin 'postgres.exe') @('-V')
        if ($LASTEXITCODE -ne 0) { Stop-Setup (T 'err.tool' 'postgres') }
        $versions['PostgreSQL'] = ("$($out[0])" -replace '^.*\s(\d+\.\d+).*$', '$1')
        Say (T 'tool.ok' 'PostgreSQL' $versions['PostgreSQL'] 'tools\pgsql\bin')
        $env:PGPASSFILE = $passFile
        $env:PGCLIENTENCODING = 'UTF8'
        $env:PGCONNECT_TIMEOUT = '10'
        foreach ($v in 'PGPASSWORD', 'PGHOST', 'PGPORT', 'PGUSER', 'PGDATABASE', 'PGSERVICE', 'PGSERVICEFILE', 'PGDATA', 'PGOPTIONS', 'PGSSLMODE') { [Environment]::SetEnvironmentVariable($v, $null) }
        if (-not $hasCluster) {
            if (Test-Path -LiteralPath $cluster) {
                if (@(Get-ChildItem -LiteralPath $cluster -Force).Count) { Stop-Setup (T 'err.pg.halfcluster' 'data\pg') }
            }
            New-Item -ItemType Directory -Force $dataDir | Out-Null
            $port = 0
            foreach ($candidate in 55432..55531) { if (Test-PortFree $candidate) { $port = $candidate; break } }
            if (-not $port) { Stop-Setup (T 'err.pg.noport') }
            $adminPw = New-Password 32
            $appPw = New-Password 32
            $pwFile = Join-Path $TempDir 'pw.txt'
            [System.IO.File]::WriteAllText($pwFile, $adminPw, (New-Object System.Text.ASCIIEncoding))
            $null = Protect-File $pwFile
            Say (T 'pg.initdb' 'data\pg')
            try {
                $r = Invoke-Tool (Join-Path $pgBin 'initdb.exe') @('-D', 'data\pg', '-U', 'postgres', '-A', 'scram-sha-256', '--pwfile=downloads\tmp\pw.txt', '-E', 'UTF8', '--locale-provider=builtin', '--builtin-locale=C.UTF-8', '--locale=C', '--no-instructions') $PgRoot
            } finally { Remove-Item -LiteralPath $pwFile -Force -ErrorAction SilentlyContinue }
            if ($LASTEXITCODE -ne 0) { $r | ForEach-Object { Say "  $_" }; Stop-Setup (T 'err.pg.initdb') }
            $conf = @(
                '',
                '# --- settings of the portable EQLMaker database (written by eqlmaker-setup)',
                "listen_addresses = '127.0.0.1'",
                "port = $port",
                'wal_level = logical',
                'logging_collector = on',
                "log_directory = 'log'",
                "log_filename = 'postgresql-%a.log'",
                'log_truncate_on_rotation = on',
                'log_rotation_age = 1d',
                "password_encryption = 'scram-sha-256'",
                "lc_messages = 'C'"
            )
            [System.IO.File]::AppendAllLines((Join-Path $cluster 'postgresql.conf'), [string[]]$conf, (New-Object System.Text.UTF8Encoding($false)))
            [System.IO.File]::WriteAllText($portFile, "$port`r`n", (New-Object System.Text.ASCIIEncoding))
            $lines = @("127.0.0.1:${port}:*:postgres:$adminPw", "localhost:${port}:*:postgres:$adminPw", "127.0.0.1:${port}:*:sample_app:$appPw", "localhost:${port}:*:sample_app:$appPw")
            [System.IO.File]::WriteAllLines($passFile, [string[]]$lines, (New-Object System.Text.ASCIIEncoding))
            if (Protect-File $passFile) { Say (T 'pg.passfile' 'data\pgpass.conf') } else { Say (T 'pg.passfile.acl' 'data\pgpass.conf') }
            $adminPw = $null
            Say (T 'pg.created' $port)
        } else {
            if (-not (Test-Path -LiteralPath $portFile)) { Stop-Setup (T 'err.pg.noportfile' 'data\pg-port.txt') }
            $port = [int]((Get-Content -LiteralPath $portFile -TotalCount 1).Trim())
            Say (T 'pg.exists' 'data\pg' $port)
        }
        $pgCtl = Join-Path $pgBin 'pg_ctl.exe'
        $null = Invoke-Tool $pgCtl @('-D', 'data\pg', 'status') $PgRoot
        if ($LASTEXITCODE -eq 0) { Say (T 'pg.running' $port) }
        else {
            if (-not (Test-PortFree $port)) { Stop-Setup (T 'err.pg.portbusy' $port 'data\pg-port.txt') }
            $code = Start-Hidden $pgCtl '-D data\pg -l data\pg-start.log -w -t 90 start' $PgRoot 120
            if ($code -ne 0) {
                $startLog = Join-Path $dataDir 'pg-start.log'
                if (Test-Path -LiteralPath $startLog) { Get-Content -LiteralPath $startLog -Tail 15 | ForEach-Object { Say "  $_" } }
                Stop-Setup (T 'err.pg.start' 'data\pg\log')
            }
            Say (T 'pg.started' $port)
        }
        $pgRunning = $true
        $psql = Join-Path $pgBin 'psql.exe'
        $r = Invoke-Tool $psql @('-X', '-At', '-h', '127.0.0.1', '-p', "$port", '-U', 'postgres', '-d', 'postgres', '-w', '-c', 'select current_setting(''server_version'')')
        if ($LASTEXITCODE -ne 0) { $r | ForEach-Object { Say "  $_" }; Stop-Setup (T 'err.pg.connect') }
        Say (T 'pg.connected' "$($r[0])".Trim())

        # sample database: role sample_app, database eqlmaker_sample (script of the sample application)
        $sampleScript = Join-Path $Root 'examples\sample-app\db\setup.ps1'
        if (Test-Path -LiteralPath $sampleScript) {
            $appPw = Read-PgPass $passFile 'sample_app'
            if (-not $appPw) { Stop-Setup (T 'err.pg.nosamplepw' 'data\pgpass.conf') }
            $env:SAMPLE_APP_PASSWORD = $appPw
            try {
                $ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
                $r = Invoke-Tool $ps @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $sampleScript, '-Psql', $psql, '-AdminHost', '127.0.0.1', '-AdminPort', "$port", '-AdminUser', 'postgres')
                $code = $LASTEXITCODE
            } finally { $env:SAMPLE_APP_PASSWORD = $null; $appPw = $null }
            $r | ForEach-Object { if ($_.Trim()) { Say "  $_" } }
            if ($code -ne 0) { Stop-Setup (T 'err.pg.sample') }
            Say (T 'pg.sample' 'eqlmaker_sample')
        } else { Say (T 'pg.nosample') }
    }

    # ---------------------------------------------------------- 7. project configuration
    Step (T 'step.project')
    $projectIsTemplate = $false
    if (Test-Path -LiteralPath $projectFile) { Say (T 'project.exists' 'project\eqlmaker.json') }
    else {
        New-Item -ItemType Directory -Force $projectDir | Out-Null
        if ($usePg) {
            $json = '{"source":"postgres","connection":"host=127.0.0.1 port=' + $port + ' user=postgres dbname=postgres","passfile":"..\\data\\pgpass.conf","capture":{"roles":[],"databases":["eqlmaker_sample"],"changes":true},"input":"background","tools":{}}'
            Say (T 'project.written' 'project\eqlmaker.json')
        } else {
            $json = '{"source":"postgres","connection":"host=127.0.0.1 port=5432 user=postgres dbname=postgres","capture":{"roles":[],"databases":["eqlmaker_sample"],"changes":true},"input":"background","tools":{}}'
            $projectIsTemplate = $true
            Say (T 'project.template' 'project\eqlmaker.json')
        }
        [System.IO.File]::WriteAllText($projectFile, $json + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
    }

    # ---------------------------------------------------------- 8. self test
    $selfTest = 'skipped'
    if ($opt.NoSelftest) { Step (T 'step.selftest'); Say (T 'selftest.skipped') }
    elseif ($projectIsTemplate) { Step (T 'step.selftest'); Say (T 'selftest.noconnection' 'project\eqlmaker.json') }
    else {
        Step (T 'step.selftest')
        # speech recognition on a sentence spoken by a Windows voice (made here, not shipped)
        $speech = 'skipped'
        foreach ($lang in 'en', 'de') {
            $wav = Join-Path $TempDir "selftest-$lang.wav"
            $sentence = 'The invoice was saved and the customer list is open.'
            $expect = @('invoice', 'customer')
            if ($lang -eq 'de') { $sentence = 'Die Rechnung wurde gespeichert und die Kundenliste ist offen.'; $expect = @('Rechnung', 'Kundenliste') }
            $ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
            $null = Invoke-Tool $ps @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $SetupDir 'make-test-audio.ps1'), '-Text', $sentence, '-Language', $lang, '-OutFile', $wav)
            if ($LASTEXITCODE -ne 0) { Say (T 'selftest.novoice' $lang); continue }
            $base = Join-Path $TempDir "selftest-$lang"
            # the model is given relative to the working directory: whisper-cli cannot open a model
            # below a path with characters outside ASCII when the path is given in full
            $null = Invoke-Tool $whisperExe @('-m', "models\$modelName", '-f', $wav, '-l', $lang, '-oj', '-of', $base, '-np') $whisperDir
            $code = $LASTEXITCODE
            $text = ''
            if ($code -eq 0 -and (Test-Path -LiteralPath "$base.json")) {
                $j = [System.IO.File]::ReadAllText("$base.json", [System.Text.Encoding]::UTF8) | ConvertFrom-Json
                $text = (@($j.transcription | ForEach-Object { $_.text }) -join ' ').Trim()
            }
            Remove-Item -LiteralPath $wav, "$base.json" -Force -ErrorAction SilentlyContinue
            $hit = @($expect | Where-Object { $text -match [regex]::Escape($_) }).Count
            if ($code -eq -1073741795) { Stop-Setup (T 'err.cpu') }
            if ($hit -eq $expect.Count) { Say (T 'selftest.speech.ok' $lang $text); if ($speech -ne 'failed') { $speech = 'passed' } }
            else { Say (T 'selftest.speech.fail' $lang $text); $speech = 'failed' }
        }
        # the self test builds the sample database again; with the password of the password file
        # in the environment the role sample_app keeps the password that is written there
        $samplePw = Read-PgPass $passFile 'sample_app'
        if ($usePg -and $samplePw) { $env:SAMPLE_APP_PASSWORD = $samplePw }
        try {
            $r = Invoke-Tool (Join-Path $Root 'bin\eqlmaker.exe') @('selftest', '--project', $projectDir)
            $code = $LASTEXITCODE
        } finally { $env:SAMPLE_APP_PASSWORD = $null; $samplePw = $null }
        $r | ForEach-Object { if ($_.Trim()) { Say "  $_" } }
        if ($code -eq 0 -and $speech -ne 'failed') { $selfTest = 'passed'; Say (T 'selftest.passed') }
        else { $selfTest = 'failed'; Say (T 'selftest.failed' $code); $script:ExitCode = 2 }
    }

    # ---------------------------------------------------------- 9. summary
    Step (T 'step.summary')
    Say (T 'sum.folder' $Root)
    foreach ($k in $versions.Keys) { Say ("  {0,-12} {1}" -f $k, $versions[$k]) }
    Say (T 'sum.model' $modelName)
    if ($usePg) {
        Say (T 'sum.pg' $port)
        Say (T 'sum.pg.files')
        Say (T 'sum.pg.startstop')
    } else { Say (T 'sum.nopg') }
    Say (T 'sum.selftest' (T "selftest.state.$selfTest"))
    Say (T 'sum.start')
    Say (T 'sum.npm')
    Say (T 'sum.remove')
    Say (T 'sum.log' 'downloads\setup.log')
    if ($script:ExitCode -eq 0) { Say ''; Say (T 'done') }
} catch [SetupStop] {
    $e = $_.Exception
    Say ''
    Say ((T 'failed') + ' ' + $e.Message)
    $script:ExitCode = $e.Code
} catch {
    Say ''
    Say ((T 'failed') + ' ' + $_.Exception.Message)
    Write-Log $_.ScriptStackTrace
    $script:ExitCode = 1
} finally {
    if (Test-Path -LiteralPath $TempDir) { Get-ChildItem -LiteralPath $TempDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
}
Write-Log "---- setup ended with exit code $($script:ExitCode)"
exit $script:ExitCode
