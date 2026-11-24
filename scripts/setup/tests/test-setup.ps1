# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Tests of the release package and of eqlmaker-setup.cmd. Every setup run happens as an ordinary,
  non-elevated user (see run-restricted.ps1). Results: <WorkDir>\results.json and one evidence
  file per run in <WorkDir>\evidence.
.PARAMETER Zip
  The release zip built by scripts\pack-release.ps1.
.PARAMETER WorkDir
  Empty folder for the test packages. Use a short path; it must not be inside the repository.
.PARAMETER SeedDir
  Folder with already downloaded archives (names as in components.json). Tests take from here
  what the first test did not download itself (the EnterpriseDB archive, for example).
.PARAMETER SeedFirstTest
  Also the first test takes its archives from -SeedDir. Without this switch the first test
  downloads everything it needs from the internet, as a user would.
.PARAMETER Psql
  A psql.exe (version 15 or later) from anywhere, used by the tests to ask the server questions.
  The default package has no psql; without this parameter those checks are reported as SKIP.
.PARAMETER ReleaseDir
  Folder with eqlmaker-windows-x64.zip, its .sha256 file and eqlmaker-version.json (the out folder
  of pack-release.ps1). With it test T18 uses the three files the way a downloader would.
.PARAMETER Only
  Run only these tests (ids such as T01, T05). Tests after T01 need the package of T01.
.PARAMETER KeepFolders
  Do not delete the test packages at the end.
.NOTES
  The tests read the registry, the service list and the firewall rules, and they change nothing
  outside -WorkDir. The proxy variables are set for the test processes only.
#>
param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [Parameter(Mandatory = $true)][string]$WorkDir,
    [string]$SeedDir,
    [switch]$SeedFirstTest,
    [string]$Psql,
    [string]$ReleaseDir,
    [string[]]$Only,
    [switch]$KeepFolders
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'run-restricted.ps1')
. (Join-Path $PSScriptRoot 'snapshot-system.ps1')
. (Join-Path $PSScriptRoot '..\pe-imports.ps1')

$WorkDir = [System.IO.Path]::GetFullPath($WorkDir).TrimEnd('\')
$Evidence = Join-Path $WorkDir 'evidence'
New-Item -ItemType Directory -Force $WorkDir, $Evidence | Out-Null
$sys = Join-Path $env:WINDIR 'System32'
$cmdExe = Join-Path $sys 'cmd.exe'
$results = New-Object System.Collections.Generic.List[object]
$umlaut = [string][char]0xFC
$mainName = "EQL Maker Pr${umlaut}fung"
$Main = Join-Path $WorkDir $mainName
$Offline = Join-Path $WorkDir 'offline ok'
$Partial = Join-Path $WorkDir 'partial'
$TarCheck = Join-Path $WorkDir 'tarcheck'
$Rt = Join-Path $WorkDir 'runtime'
$Edb = Join-Path $WorkDir 'edb build'
$Cpu = Join-Path $WorkDir 'no avx2'
$Dl = Join-Path $WorkDir 'download'
$allFolders = @($Main, $Offline, $Partial, $TarCheck, $Rt, $Edb, $Cpu, (Join-Path $Dl 'eqlmaker'))

function Add-Result([string]$Id, [string]$Test, [bool]$Ok, [string]$EvidenceText, [string]$State) {
    if (-not $State) { $State = if ($Ok) { 'PASS' } else { 'FAIL' } }
    $results.Add([pscustomobject]@{ id = $Id; test = $Test; result = $State; evidence = $EvidenceText })
    Write-Host ("{0} {1,-5} {2} - {3}" -f $Id, $State, $Test, $EvidenceText)
    ConvertTo-Json -InputObject $results.ToArray() -Depth 4 | Set-Content -LiteralPath (Join-Path $WorkDir 'results.json') -Encoding UTF8
}
function Wants([string]$Id) { return (-not $Only -or $Only -contains $Id) }

# runs a command line through cmd as a restricted user; output goes to an evidence file
function Run-Cmd([string]$Name, [string]$Line, [string]$Dir, [int]$Timeout = 2400) {
    $log = Join-Path $Evidence "$Name.log"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-Restricted "`"$cmdExe`" /d /s /c `"$Line > `"$log`" 2>&1`"" $Dir $Timeout
    $text = ''
    if (Test-Path -LiteralPath $log) { $text = [System.IO.File]::ReadAllText($log) }
    return [pscustomobject]@{ Code = $code; Seconds = [int]$sw.Elapsed.TotalSeconds; Log = $log; Text = $text }
}
function Run-Setup([string]$Name, [string]$Package, [string]$Options) {
    return (Run-Cmd $Name "`"$Package\eqlmaker-setup.cmd`" $Options" $Package)
}
function Describe-Exit($Run) { return "exit code $($Run.Code), $($Run.Seconds) s" }
# runs a PowerShell script text as the restricted user (files it creates belong to that user)
function Invoke-AsUser([string]$Script) {
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes("`$ErrorActionPreference = 'Stop'; " + $Script))
    $code = Invoke-Restricted "`"$sys\WindowsPowerShell\v1.0\powershell.exe`" -NoProfile -EncodedCommand $encoded" $WorkDir 900
    if ($code -ne 0) { throw "step as restricted user failed ($code): $Script" }
}
function Copy-AsUser([string]$From, [string]$To) {
    Invoke-AsUser "New-Item -ItemType Directory -Force -Path '$(Split-Path -Parent $To)' | Out-Null; Copy-Item -LiteralPath '$From' -Destination '$To' -Force"
}
function New-Package([string]$Folder) {
    if (Test-Path -LiteralPath $Folder) { Remove-Item -LiteralPath $Folder -Recurse -Force }
    Invoke-AsUser "New-Item -ItemType Directory -Force -Path '$Folder' | Out-Null"
    # unpacked by the restricted user with the tar of Windows
    $code = Invoke-Restricted "`"$sys\tar.exe`" -xf `"$Zip`" -C `"$Folder`"" $Folder 600
    if ($code -ne 0) { throw "unpacking the release zip into $Folder failed ($code)" }
}
function Get-Components([string]$Package) {
    return (Get-Content -LiteralPath (Join-Path $Package 'setup\components.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
}
function Get-Component([string]$Package, [string]$Id) { return ((Get-Components $Package).components | Where-Object { $_.id -eq $Id }) }
# file names of the archives of a group: ffmpeg, node, pg (the default build with its unpacking
# tool), psql (the two extra packages of -with-psql), edb (the EnterpriseDB archive)
function Get-ArchiveNames([string]$Package, [string[]]$Groups) {
    $names = @()
    foreach ($g in $Groups) {
        switch ($g) {
            'pg' {
                $z = Get-Component $Package 'zstd'
                $names += $z.file
                foreach ($x in $z.extras) { $names += $x.file }
                foreach ($p in (Get-Component $Package 'postgresql-mingw').packages) { if (-not $p.PSObject.Properties['only_with']) { $names += $p.file } }
                foreach ($x in @((Get-Component $Package 'postgresql-mingw').extras)) { if ($x) { $names += $x.file } }
            }
            'psql' { foreach ($p in (Get-Component $Package 'postgresql-mingw').packages) { if ($p.PSObject.Properties['only_with']) { $names += $p.file } } }
            'edb' { $names += (Get-Component $Package 'postgresql').file }
            default { $names += (Get-Component $Package $g).file }
        }
    }
    return $names
}
# fills downloads\ of a package from a folder with archives; what is not there is left out
function Copy-Cache([string]$From, [string]$Package, [string[]]$Groups) {
    if (-not $From) { return }
    foreach ($name in (Get-ArchiveNames $Package $Groups)) {
        $src = Join-Path $From $name
        if (Test-Path -LiteralPath $src) { Copy-AsUser $src (Join-Path $Package "downloads\$name") }
    }
}
function Get-Port([string]$Package) { return [int]((Get-Content -LiteralPath (Join-Path $Package 'data\pg-port.txt') -TotalCount 1).Trim()) }
function Get-Listeners([int]$Port) {
    return , @([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -eq $Port } | ForEach-Object { $_.Address.ToString() })
}
function Get-PackageProcesses([string]$Package) {
    return @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Package, [System.StringComparison]::OrdinalIgnoreCase) })
}
# asks the server of a package with the psql given by -Psql (or with another psql when named)
function Invoke-Psql([string]$Package, [string]$User, [string]$Database, [string]$Sql, [string]$Password, [string]$Program) {
    if (-not $Program) { $Program = $Psql }
    $old = @{ PGPASSFILE = $env:PGPASSFILE; PGPASSWORD = $env:PGPASSWORD }
    $env:PGPASSFILE = Join-Path $Package 'data\pgpass.conf'
    if ($Password) { $env:PGPASSWORD = $Password } else { $env:PGPASSWORD = $null }
    $ea = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        $out = & $Program -X -At -w -h 127.0.0.1 -p (Get-Port $Package) -U $User -d $Database -c $Sql 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ Code = $LASTEXITCODE; Out = ($out -join "`n") }
    } finally { $ErrorActionPreference = $ea; $env:PGPASSFILE = $old.PGPASSFILE; $env:PGPASSWORD = $old.PGPASSWORD }
}
function Stop-Db([string]$Package) {
    if (Test-Path -LiteralPath (Join-Path $Package 'data\pg\PG_VERSION')) { $null = Run-Cmd ("stop-" + [System.IO.Path]::GetFileName($Package).Replace(' ', '_').Replace($umlaut, 'ue')) "`"$Package\eqlmaker-db-stop.cmd`"" $Package 180 }
}
function Get-TreeState([string]$Package) {
    $lines = @()
    foreach ($sub in 'tools', 'bin', 'setup') {
        $dir = Join-Path $Package $sub
        if (Test-Path -LiteralPath $dir) { $lines += Get-ChildItem -LiteralPath $dir -Recurse -File -Force | ForEach-Object { "$($_.FullName.Substring($Package.Length))|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" } }
    }
    foreach ($f in 'data\pgpass.conf', 'data\pg-port.txt', 'project\eqlmaker.json', 'tools\whisper\model.txt') { $p = Join-Path $Package $f; if (Test-Path -LiteralPath $p) { $lines += "$f|$((Get-FileHash -LiteralPath $p).Hash)" } }
    return @($lines | Sort-Object)
}
# imports of all programs below a folder that are neither part of Windows nor in that folder
function Get-ForeignImports([string]$Folder) {
    $files = @(Get-ChildItem -LiteralPath $Folder -Recurse -File | Where-Object { $_.Extension -eq '.exe' -or $_.Extension -eq '.dll' })
    $have = @{}
    foreach ($f in $files) { $have[$f.Name.ToLowerInvariant()] = 1 }
    $missing = @{}
    foreach ($f in $files) {
        try { $imports = Get-PeImports $f.FullName } catch { continue }
        foreach ($i in $imports) { $k = $i.ToLowerInvariant(); if (-not (Test-WindowsLibrary $k) -and -not $have.ContainsKey($k)) { $missing[$k] = $f.Name } }
    }
    return @($missing.Keys | Sort-Object | ForEach-Object { "$_ (needed by $($missing[$_]))" })
}
# a tar archive with one harmless file and one entry that points outside the target
function New-EvilTar([string]$Path) {
    $out = New-Object System.IO.MemoryStream
    foreach ($name in 'ucrt64/bin/postgres.exe', 'ucrt64/../../../evil-outside-tar.txt') {
        $data = [System.Text.Encoding]::ASCII.GetBytes('test')
        $h = New-Object byte[] 512
        $put = { param($text, $at) $b = [System.Text.Encoding]::ASCII.GetBytes($text); [Array]::Copy($b, 0, $h, $at, $b.Length) }
        & $put $name 0
        & $put '0000644' 100; & $put '0000000' 108; & $put '0000000' 116
        & $put ([Convert]::ToString($data.Length, 8).PadLeft(11, '0')) 124
        & $put '00000000000' 136
        & $put '        ' 148
        $h[156] = [byte][char]'0'
        & $put 'ustar' 257; & $put '00' 263
        $sum = 0; foreach ($byte in $h) { $sum += $byte }
        & $put ([Convert]::ToString($sum, 8).PadLeft(6, '0')) 148; $h[154] = 0; $h[155] = 32
        $out.Write($h, 0, 512)
        $block = New-Object byte[] 512
        [Array]::Copy($data, $block, $data.Length)
        $out.Write($block, 0, 512)
    }
    $end = New-Object byte[] 1024
    $out.Write($end, 0, 1024)
    [System.IO.File]::WriteAllBytes($Path, $out.ToArray())
}

$started = Get-Date
$before = Get-SystemSnapshot
$oldProxy = @{ HTTPS_PROXY = $env:HTTPS_PROXY; HTTP_PROXY = $env:HTTP_PROXY; ALL_PROXY = $env:ALL_PROXY }
function Set-DeadProxy { $env:HTTPS_PROXY = 'http://127.0.0.1:9'; $env:HTTP_PROXY = 'http://127.0.0.1:9'; $env:ALL_PROXY = 'http://127.0.0.1:9' }
function Reset-Proxy { $env:HTTPS_PROXY = $oldProxy.HTTPS_PROXY; $env:HTTP_PROXY = $oldProxy.HTTP_PROXY; $env:ALL_PROXY = $oldProxy.ALL_PROXY }
$mainCache = Join-Path $Main 'downloads'

try {
    # ------------------------------------------------------------------ T00 static checks
    if (Wants 'T00') {
        $setupDir = Join-Path $PSScriptRoot '..'
        $en = [System.IO.File]::ReadAllText((Join-Path $setupDir 'strings.en.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $de = [System.IO.File]::ReadAllText((Join-Path $setupDir 'strings.de.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $ke = @($en.PSObject.Properties.Name | Sort-Object); $kd = @($de.PSObject.Properties.Name | Sort-Object)
        $diff = @(Compare-Object $ke $kd)
        $badArgs = @($ke | Where-Object { $kd -contains $_ } | Where-Object {
                $a = ([regex]::Matches($en.$_, '\{\d+\}') | ForEach-Object Value | Sort-Object -Unique) -join ','
                $b = ([regex]::Matches($de.$_, '\{\d+\}') | ForEach-Object Value | Sort-Object -Unique) -join ','
                $a -ne $b })
        $used = @([regex]::Matches([System.IO.File]::ReadAllText((Join-Path $setupDir 'eqlmaker-setup.ps1')), "\(T '([a-z0-9.]+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $unknown = @($used | Where-Object { $ke -notcontains $_ })
        Add-Result 'T00' 'resource texts en/de: identical keys, same placeholders, every key used by the script exists' ($diff.Count -eq 0 -and $badArgs.Count -eq 0 -and $unknown.Count -eq 0) "$($ke.Count) keys en, $($kd.Count) keys de, differences: $($diff.Count), placeholder mismatches: $($badArgs.Count), unknown keys in script: $($unknown.Count)"
    }

    # ------------------------------------------------------------------ T01 fresh folder, restricted user
    if (Wants 'T01') {
        New-Package $Main
        if ($SeedFirstTest) { Copy-Cache $SeedDir $Main @('ffmpeg', 'node', 'pg') }
        $who = Join-Path $Evidence 'T01-whoami.txt'
        $role = Join-Path $Evidence 'T01-isadmin.txt'
        $line = "`"$sys\whoami.exe`" /groups /fo csv > `"$who`" & `"$sys\whoami.exe`" /priv >> `"$who`" & `"$sys\WindowsPowerShell\v1.0\powershell.exe`" -NoProfile -Command `"(New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.SecurityIdentifier]'S-1-5-32-544')`" > `"$role`" & `"$Main\eqlmaker-setup.cmd`" -unattended -lang en"
        $run = Run-Cmd 'T01-setup' $line $Main
        $whoText = [System.IO.File]::ReadAllText($who)
        $isAdmin = ([System.IO.File]::ReadAllText($role)).Trim()
        $adminLine = ($whoText -split "`n" | Where-Object { $_ -match 'S-1-5-32-544' } | Select-Object -First 1)
        $notElevated = ($isAdmin -eq 'False') -and ($whoText -match 'S-1-16-8192') -and ($whoText -notmatch 'S-1-16-12288')
        Add-Result 'T01a' 'the setup process is not elevated (restricted token, evidence taken inside the run)' $notElevated "member of the admin group (S-1-5-32-544) enabled: $isAdmin; integrity level S-1-16-8192 (Medium) present: $($whoText -match 'S-1-16-8192'); entry of the admin group: $("$adminLine".Trim()); T01-whoami.txt"
        $downloaded = @([regex]::Matches($run.Text, ': downloaded, SHA-256 verified')).Count
        $speechByProgram = $run.Text -match 'ok\s+speech recognition'
        Add-Result 'T01b' "fresh folder with blank and umlaut ('$mainName'), eqlmaker-setup.cmd -unattended as ordinary user: downloads, default PostgreSQL without psql, self test with speech and logical decoding" ($run.Code -eq 0 -and $speechByProgram -and $run.Text -match 'wal_level logical' -and $run.Text -match 'verify db') "$(Describe-Exit $run); archives downloaded in this run: $downloaded; the program's self test checked the speech recognition: $speechByProgram; T01-setup.log"
        $need = 'eqlmaker-setup.cmd', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md', 'README.en.md', 'MANIFEST.json', 'docs\setup.md', 'docs\de\setup.md', 'setup\COMPONENTS.md',
            'tools\ffmpeg\bin\ffmpeg.exe', 'tools\ffmpeg\LICENSE.txt', 'tools\node\node.exe', 'tools\node\LICENSE', 'tools\zstd\zstd.exe', 'tools\zstd\LICENSE',
            'tools\pgsql\bin\postgres.exe', 'tools\pgsql\bin\initdb.exe', 'tools\pgsql\bin\pg_ctl.exe', 'tools\pgsql\lib\postgresql\test_decoding.dll', 'tools\pgsql\share\licenses\postgresql\COPYRIGHT', 'tools\pgsql\share\licenses\icu\LICENSE', 'tools\pgsql\share\licenses\lz4\LICENSE', 'tools\pgsql\share\licenses\openssl\LICENSE', 'tools\pgsql\share\licenses\libgcc\COPYING.RUNTIME',
            'tools\whisper\whisper-cli.exe', 'tools\whisper\LICENSE', 'tools\whisper\models\LICENSE', 'data\pg\PG_VERSION', 'data\pg-port.txt', 'data\pgpass.conf', 'project\eqlmaker.json', 'examples\sample-app\web\node_modules\pg\package.json', 'eqlmaker.cmd', 'eqlmaker-npm.cmd', 'eqlmaker-db-start.cmd', 'eqlmaker-db-stop.cmd'
        $absent = 'tools\pgsql\bin\psql.exe', 'tools\pgsql\bin\libreadline8.dll', 'tools\pgsql\bin\libtermcap-0.dll', 'tools\pgsql\bin\gettext.exe', 'tools\pgsql\bin\lz4.exe', 'tools\pgsql\include', 'tools\pgsql\share\licenses\readline', 'tools\node\install_tools.bat', 'tools\ffmpeg\bin\ffplay.exe', 'tools\vcruntime'
        $missing = @($need | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Main $_)) })
        $unwanted = @($absent | Where-Object { Test-Path -LiteralPath (Join-Path $Main $_) })
        Add-Result 'T01c' 'layout of the package after the setup; no psql and no GPL library in the default' ($missing.Count -eq 0 -and $unwanted.Count -eq 0) "$($need.Count) expected files present (missing: $($missing -join ', ')); unwanted parts absent (found: $($unwanted -join ', '))"
        $port = Get-Port $Main
        $expected = '{"source":"postgres","connection":"host=127.0.0.1 port=' + $port + ' user=postgres dbname=postgres","passfile":"..\\data\\pgpass.conf","capture":{"roles":[],"databases":["eqlmaker_sample"],"changes":true},"input":"background","tools":{}}'
        $actual = ([System.IO.File]::ReadAllText((Join-Path $Main 'project\eqlmaker.json'))).Trim()
        Add-Result 'T01d' 'project\eqlmaker.json has the agreed content' ($actual -eq $expected) "port $port; content equal: $($actual -eq $expected)"
        $listen = Get-Listeners $port
        Add-Result 'T01e' 'the portable PostgreSQL listens on 127.0.0.1 only' ($listen.Count -eq 1 -and $listen[0] -eq '127.0.0.1') "listeners on port ${port}: $($listen -join ', ')"
        if ($Psql) {
            $settings = Invoke-Psql $Main 'postgres' 'postgres' "select current_setting('listen_addresses') || '|' || current_setting('wal_level') || '|' || current_setting('logging_collector') || '|' || current_setting('password_encryption') || '|' || current_setting('server_version') || '|' || (select string_agg(distinct auth_method, ',') from pg_hba_file_rules) || '|' || (select count(*) from pg_timezone_names) || '|' || (current_setting('data_directory') ~ '^[\x20-\x7E]+$')"
            Add-Result 'T01f' 'cluster settings asked from the server: listen address, wal_level, logging collector, scram-sha-256, version, time zones, data directory reached through an ASCII path' ($settings.Code -eq 0 -and $settings.Out -match '^127\.0\.0\.1\|logical\|on\|scram-sha-256\|18\.\d+\|scram-sha-256\|\d{3,}\|(t|true)$') $settings.Out
            $good = Invoke-Psql $Main 'postgres' 'postgres' 'select 1'
            $wrong = Invoke-Psql $Main 'postgres' 'postgres' 'select 1' 'certainly-wrong'
            Add-Result 'T01g' 'generated password works, a wrong password is refused' ($good.Code -eq 0 -and $good.Out -eq '1' -and $wrong.Code -ne 0 -and $wrong.Out -match 'password authentication failed') "password file: exit $($good.Code); wrong password: exit $($wrong.Code), '$(($wrong.Out -split "`n")[0])'"
            $sample = Invoke-Psql $Main 'sample_app' 'eqlmaker_sample' 'select (select count(*) from customer) > 0 and (select count(*) from article) > 0'
            Add-Result 'T01h' 'sample database present after the self test, login as sample_app with the password of data\pgpass.conf' ($sample.Code -eq 0 -and $sample.Out -eq 't') "exit $($sample.Code), result '$($sample.Out)'"
            $slot = Invoke-Psql $Main 'postgres' 'postgres' "select slot_name from pg_create_logical_replication_slot('setup_test', 'test_decoding')"
            $drop = Invoke-Psql $Main 'postgres' 'postgres' "select pg_drop_replication_slot('setup_test')"
            Add-Result 'T01k' 'logical decoding: a slot with the plugin test_decoding can be created and dropped' ($slot.Code -eq 0 -and $slot.Out -eq 'setup_test' -and $drop.Code -eq 0) "create: exit $($slot.Code), '$($slot.Out)'; drop: exit $($drop.Code)"
        } else {
            foreach ($id in 'T01f', 'T01g', 'T01h', 'T01k') { Add-Result $id 'questions to the server' $false 'no -Psql given: not tested' 'SKIP' }
        }
        $acl = (& (Join-Path $sys 'icacls.exe') (Join-Path $Main 'data\pgpass.conf')) -join ' '
        $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $entries = @([regex]::Matches($acl, '([^\s\\]+\\[^:]+):\(') | ForEach-Object { $_.Groups[1].Value.Trim() })
        Add-Result 'T01i' 'data\pgpass.conf is readable by the current user only' ($entries.Count -eq 1 -and $acl -match [regex]::Escape($me)) "access entries: $($entries.Count) (the current user)"
        $pwLeak = @(Get-ChildItem -LiteralPath (Join-Path $Main 'downloads') -Recurse -File | Where-Object { $_.Name -eq 'pw.txt' }).Count
        $pass = [System.IO.File]::ReadAllLines((Join-Path $Main 'data\pgpass.conf'))[0].Split(':')[4]
        $inLog = ([System.IO.File]::ReadAllText((Join-Path $Main 'downloads\setup.log')) + $run.Text).Contains($pass)
        Add-Result 'T01j' 'the generated password is neither left in a temporary file nor written to the log' ($pwLeak -eq 0 -and -not $inLog -and $pass.Length -eq 32) "temporary password files: $pwLeak; password in log or output: $inLog; length $($pass.Length)"
        $foreign = @()
        foreach ($d in 'tools\pgsql', 'tools\ffmpeg', 'tools\zstd', 'tools\whisper') { $foreign += Get-ForeignImports (Join-Path $Main $d) }
        $foreign += @(Get-PeImports (Join-Path $Main 'tools\node\node.exe') | Where-Object { -not (Test-WindowsLibrary $_) })
        $optional = @($foreign | Where-Object { $_ -match '^(libpython|perl|tcl|libxslt)' })
        $real = @($foreign | Where-Object { $_ -notmatch '^(libpython|perl|tcl|libxslt)' })
        Add-Result 'T01l' 'import tables of all installed programs (PostgreSQL, FFmpeg, zstd, whisper, node): nothing needed that is neither part of Windows nor in the package; no Visual C++ run-time' ($real.Count -eq 0) "missing libraries: $($real.Count) $($real -join '; '); optional language plug-ins left out: $($optional.Count)"
    }

    # ------------------------------------------------------------------ T02 second run
    if (Wants 'T02') {
        $state1 = Get-TreeState $Main
        Set-DeadProxy
        try { $run = Run-Setup 'T02-second-run' $Main '-unattended -lang en' } finally { Reset-Proxy }
        $state2 = Get-TreeState $Main
        $changed = @(Compare-Object $state1 $state2).Count
        $noDownload = ($run.Text -notmatch ': downloading ') -and ($run.Text -notmatch 'unpacking to') -and ($run.Text -notmatch 'continuing at')
        Add-Result 'T02' 'second run is idempotent (no download, no unpacking, same files; network blocked by proxy 127.0.0.1:9)' ($run.Code -eq 0 -and $noDownload -and $changed -eq 0) "$(Describe-Exit $run); downloads or unpacking in the log: $(-not $noDownload); changed files in tools, bin, setup, project configuration, password file: $changed of $($state1.Count)"
        Set-DeadProxy
        try { $quick = Run-Setup 'T02-second-run-no-selftest' $Main '-unattended -lang en -no-selftest' } finally { Reset-Proxy }
        Add-Result 'T02b' 'second run with -no-selftest (verification only)' ($quick.Code -eq 0) (Describe-Exit $quick)
    }

    # ------------------------------------------------------------------ T03 transcription through the packaged whisper
    if (Wants 'T03') {
        $model = (Get-Content -LiteralPath (Join-Path $Main 'tools\whisper\model.txt') -TotalCount 1).Trim()
        $samples = @(
            @{ Lang = 'en'; Text = 'First I open the customer list. Then I create a new invoice with three lines and save it.'; Words = @('customer', 'invoice', 'lines', 'save') },
            @{ Lang = 'de'; Text = 'Zuerst oeffne ich die Kundenliste. Dann lege ich eine neue Rechnung mit drei Zeilen an und speichere sie.'.Replace('oe', [string][char]0xF6); Words = @('Kundenliste', 'Rechnung', 'Zeilen', 'speichere') }
        )
        foreach ($s in $samples) {
            $wav = Join-Path $Evidence "T03-$($s.Lang).wav"
            $textFile = Join-Path $Evidence "T03-$($s.Lang).txt"
            [System.IO.File]::WriteAllText($textFile, $s.Text, (New-Object System.Text.UTF8Encoding($false)))
            $audio = Run-Cmd "T03-audio-$($s.Lang)" "`"$sys\WindowsPowerShell\v1.0\powershell.exe`" -NoProfile -ExecutionPolicy Bypass -File `"$Main\setup\make-test-audio.ps1`" -TextFile `"$textFile`" -Language $($s.Lang) -OutFile `"$wav`"" $Main 120
            if ($audio.Code -eq 3) { Add-Result "T03$($s.Lang)" "transcription $($s.Lang)" $false 'no Windows voice for this language: not measured' 'SKIP'; continue }
            $base = Join-Path $Evidence "T03-$($s.Lang)"
            $tr = Run-Cmd "T03-whisper-$($s.Lang)" ".\whisper-cli.exe -m models\$model -f `"$wav`" -l $($s.Lang) -oj -of `"$base`" -np" (Join-Path $Main 'tools\whisper') 600
            $text = ''
            if (Test-Path -LiteralPath "$base.json") {
                $j = [System.IO.File]::ReadAllText("$base.json", [System.Text.Encoding]::UTF8) | ConvertFrom-Json
                $text = (@($j.transcription | ForEach-Object { $_.text }) -join ' ').Trim()
            }
            $hits = @($s.Words | Where-Object { $text -match [regex]::Escape($_) })
            Add-Result "T03$($s.Lang)" "transcription of generated $($s.Lang) audio through the packaged whisper ($model)" ($tr.Code -eq 0 -and $hits.Count -eq $s.Words.Count) "expected words found: $($hits.Count) of $($s.Words.Count); text: '$text'; $($tr.Seconds) s"
            Remove-Item -LiteralPath $wav -Force -ErrorAction SilentlyContinue
        }
    }

    # ------------------------------------------------------------------ T04 npm helper
    if (Wants 'T04') {
        $app = Join-Path $Main 'downloads\npm-helper-test'
        if (Test-Path -LiteralPath $app) { Remove-Item -LiteralPath $app -Recurse -Force }
        New-Item -ItemType Directory -Force $app | Out-Null
        Copy-Item -LiteralPath (Join-Path $Main 'examples\sample-app\web\package.json'), (Join-Path $Main 'examples\sample-app\web\package-lock.json') $app
        Set-DeadProxy
        try { $run = Run-Cmd 'T04-npm-helper' "`"$Main\eqlmaker-npm.cmd`" ci --offline" $app 600 } finally { Reset-Proxy }
        $ok = ($run.Code -eq 0) -and (Test-Path -LiteralPath (Join-Path $app 'node_modules\pg\package.json'))
        Add-Result 'T04' 'eqlmaker-npm.cmd ci --offline in another folder (package-local cache, network blocked)' $ok "exit code $($run.Code); node_modules\pg present: $(Test-Path -LiteralPath (Join-Path $app 'node_modules\pg\package.json'))"
        Remove-Item -LiteralPath $app -Recurse -Force
        # the web template of the program, copied like a generated application
        $app = Join-Path $Main 'downloads\npm-template-test'
        if (Test-Path -LiteralPath $app) { Remove-Item -LiteralPath $app -Recurse -Force }
        Invoke-AsUser "Copy-Item -LiteralPath '$(Join-Path $Main 'bin\share\templates\web')' -Destination '$app' -Recurse"
        Set-DeadProxy
        try { $run = Run-Cmd 'T04-npm-template' "`"$Main\eqlmaker-npm.cmd`" ci --offline" $app 600 } finally { Reset-Proxy }
        $present = Test-Path -LiteralPath (Join-Path $app 'node_modules\pg\package.json')
        Add-Result 'T04b' 'eqlmaker-npm.cmd ci --offline in a copy of the web template bin\share\templates\web (network blocked)' ($run.Code -eq 0 -and $present) "exit code $($run.Code); node_modules\pg present: $present"
        Remove-Item -LiteralPath $app -Recurse -Force
    }

    # ------------------------------------------------------------------ T05 optional model
    if (Wants 'T05') {
        $run = Run-Setup 'T05-model-base' $Main '-unattended -lang en -no-selftest -model base'
        $name = (Get-Content -LiteralPath (Join-Path $Main 'tools\whisper\model.txt') -TotalCount 1).Trim()
        $back = Run-Setup 'T05-model-back' $Main '-unattended -lang en -no-selftest -model small-q5_1'
        $name2 = (Get-Content -LiteralPath (Join-Path $Main 'tools\whisper\model.txt') -TotalCount 1).Trim()
        $unknown = Run-Setup 'T05-model-unknown' $Main '-unattended -lang en -no-selftest -model does-not-exist'
        Add-Result 'T05' '-model <name>: downloads and selects another model, back again, unknown name refused' ($run.Code -eq 0 -and $name -eq 'ggml-base.bin' -and $back.Code -eq 0 -and $name2 -eq 'ggml-small-q5_1.bin' -and $unknown.Code -eq 1) "base: exit $($run.Code), model.txt=$name ($($run.Seconds) s); back: exit $($back.Code), model.txt=$name2; unknown: exit $($unknown.Code)"
    }

    # ------------------------------------------------------------------ T06 stop and start scripts
    if (Wants 'T06') {
        $port = Get-Port $Main
        $stop = Run-Cmd 'T06-db-stop' "`"$Main\eqlmaker-db-stop.cmd`"" $Main 180
        Start-Sleep -Seconds 2
        $after = Get-Listeners $port
        $procs = Get-PackageProcesses $Main
        Add-Result 'T06a' 'eqlmaker-db-stop.cmd stops the server, nothing of the package keeps running' ($stop.Code -eq 0 -and $after.Count -eq 0 -and $procs.Count -eq 0) "exit code $($stop.Code); listeners on port ${port}: $($after.Count); processes started from the package folder: $($procs.Count)"
        $start = Run-Cmd 'T06-db-start' "`"$Main\eqlmaker-db-start.cmd`"" $Main 240
        $listen = Get-Listeners $port
        $clock = Run-Cmd 'T06-clock' "`"$Main\eqlmaker.cmd`" clock --project `"$Main\project`"" $Main 120
        $again = Run-Cmd 'T06-db-start-again' "`"$Main\eqlmaker-db-start.cmd`"" $Main 240
        Add-Result 'T06b' 'eqlmaker-db-start.cmd starts it again (127.0.0.1 only), the program can log in, a second start is harmless' ($start.Code -eq 0 -and $listen.Count -eq 1 -and $listen[0] -eq '127.0.0.1' -and $clock.Code -eq 0 -and $again.Code -eq 0) "start: exit $($start.Code); listeners: $($listen -join ', '); eqlmaker clock: exit $($clock.Code); second start: exit $($again.Code)"
    }

    # ------------------------------------------------------------------ T19 psql on request
    if (Wants 'T19') {
        $run = Run-Setup 'T19-with-psql' $Main '-unattended -lang en -no-selftest -with-psql'
        $named = ($run.Text -match 'readline \(libreadline8\.dll, GPL-3\.0-or-later\)') -and ($run.Text -match 'termcap \(libtermcap-0\.dll, GPL-2\.0-or-later\)')
        $namedAt = $run.Text.IndexOf('-with-psql: psql and the two libraries')
        $firstDownload = $run.Text.IndexOf(': downloading ')
        $before2 = ($namedAt -ge 0) -and ($firstDownload -lt 0 -or $firstDownload -gt $namedAt)
        $files = @('tools\pgsql\bin\psql.exe', 'tools\pgsql\bin\libreadline8.dll', 'tools\pgsql\bin\libtermcap-0.dll', 'tools\pgsql\share\licenses\readline\COPYING', 'tools\pgsql\share\licenses\termcap\LICENSE' | Where-Object { Test-Path -LiteralPath (Join-Path $Main $_) }).Count
        $q = Invoke-Psql $Main 'sample_app' 'eqlmaker_sample' 'select count(*) > 0 from customer' $null (Join-Path $Main 'tools\pgsql\bin\psql.exe')
        $keep = Run-Setup 'T19-keeps-psql' $Main '-unattended -lang en -no-selftest'
        $still = Test-Path -LiteralPath (Join-Path $Main 'tools\pgsql\bin\psql.exe')
        Add-Result 'T19' '-with-psql on the installed package: both GPL libraries and their licences are named before the download, psql is installed over the running cluster and works; a later run without the option keeps it' ($run.Code -eq 0 -and $named -and $before2 -and $files -eq 5 -and $q.Out -eq 't' -and $keep.Code -eq 0 -and $still) "$(Describe-Exit $run); libraries and licences named: $named, before the download: $before2; files present: $files of 5; query with the installed psql: '$($q.Out)'; later run: exit $($keep.Code), psql kept: $still"
    }

    # ------------------------------------------------------------------ T06c final stop of the main package
    if (Wants 'T06') {
        $port = Get-Port $Main
        $stop2 = Run-Cmd 'T06-db-stop-2' "`"$Main\eqlmaker-db-stop.cmd`"" $Main 180
        Start-Sleep -Seconds 2
        $procs = Get-PackageProcesses $Main
        Add-Result 'T06c' 'after the final stop no process of the package is left' ($stop2.Code -eq 0 -and $procs.Count -eq 0 -and (Get-Listeners $port).Count -eq 0) "exit code $($stop2.Code); processes: $($procs.Count); listeners: $((Get-Listeners $port).Count)"
    }

    # ------------------------------------------------------------------ T07 offline with a complete cache
    if (Wants 'T07') {
        New-Package $Offline
        Copy-Cache $mainCache $Offline @('ffmpeg', 'node', 'pg')
        Set-DeadProxy
        try { $run = Run-Setup 'T07-offline-complete' $Offline '-offline -unattended -lang en' } finally { Reset-Proxy }
        $q = 'not asked (no -Psql)'
        if ($Psql -and (Test-Path -LiteralPath (Join-Path $Offline 'data\pg-port.txt'))) { $q = (Invoke-Psql $Offline 'sample_app' 'eqlmaker_sample' 'select count(*) > 0 from customer').Out }
        Add-Result 'T07' '-offline with a complete downloads folder, network blocked (proxy variables point to 127.0.0.1:9)' ($run.Code -eq 0 -and ($q -eq 't' -or -not $Psql)) "$(Describe-Exit $run); sample database query: '$q'; T07-offline-complete.log"
        Stop-Db $Offline
    }

    # ------------------------------------------------------------------ T08 offline with a missing archive
    if (Wants 'T08') {
        New-Package $Partial
        Copy-Cache $mainCache $Partial @('ffmpeg', 'pg')
        $node = Get-Component $Main 'node'
        $run = Run-Setup 'T08-offline-missing' $Partial '-offline -unattended -lang en'
        $nothing = -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg')) -and -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\node')) -and -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\pgsql')) -and -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\zstd')) -and -not (Test-Path -LiteralPath (Join-Path $Partial 'data'))
        Add-Result 'T08' '-offline with a missing archive: plain message with file and address, exit code 3, nothing unpacked' ($run.Code -eq 3 -and $run.Text.Contains($node.file) -and $run.Text.Contains($node.url) -and $nothing) "exit code $($run.Code); names the file: $($run.Text.Contains($node.file)); names the address: $($run.Text.Contains($node.url)); nothing unpacked: $nothing"
        # a damaged (cut) archive in offline mode is reported and left as it is
        $ff = Get-Component $Main 'ffmpeg'
        $cut = Join-Path $Partial "downloads\$($ff.file)"
        Invoke-AsUser "`$f = [System.IO.File]::Open('$cut', 'Open'); `$f.SetLength(5MB); `$f.Dispose()"
        $run = Run-Setup 'T08-offline-damaged' $Partial '-offline -unattended -lang en'
        $size = (Get-Item -LiteralPath $cut).Length
        $nothing = -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg')) -and -not (Test-Path -LiteralPath "$cut.part")
        Add-Result 'T08b' '-offline with a damaged archive: reported, exit code 3, the file is left untouched' ($run.Code -eq 3 -and $run.Text.Contains($ff.file) -and $size -eq 5MB -and $nothing) "exit code $($run.Code); names the file: $($run.Text.Contains($ff.file)); file still $size bytes; nothing unpacked or renamed: $nothing"
        Copy-AsUser (Join-Path $mainCache $ff.file) $cut
    }

    # ------------------------------------------------------------------ T09 broken download is resumed
    if (Wants 'T09') {
        $node = Get-Component $Main 'node'
        $target = Join-Path $Partial "downloads\$($node.file)"
        Invoke-AsUser "`$src = [System.IO.File]::OpenRead('$(Join-Path $mainCache $node.file)'); `$dst = [System.IO.File]::Create('$target'); `$buffer = New-Object byte[] (10MB); `$done = 0; while (`$done -lt `$buffer.Length) { `$n = `$src.Read(`$buffer, `$done, `$buffer.Length - `$done); if (`$n -le 0) { break }; `$done += `$n }; `$dst.Write(`$buffer, 0, `$done); `$dst.Dispose(); `$src.Dispose()"
        $run = Run-Setup 'T09-truncated' $Partial '-unattended -lang en -no-postgres -no-selftest'
        $hash = ''
        if (Test-Path -LiteralPath $target) { $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() }
        Add-Result 'T09' 'archive cut to 10 MB in downloads\: the download is continued and verified' ($run.Code -eq 0 -and $run.Text -match 'continuing at 10\.0 MB' -and $hash -eq $node.sha256 -and (Test-Path -LiteralPath (Join-Path $Partial 'tools\node\node.exe'))) "exit code $($run.Code); 'continuing at 10.0 MB' in the log: $($run.Text -match 'continuing at 10\.0 MB'); SHA-256 correct afterwards: $($hash -eq $node.sha256); $($run.Seconds) s"
    }

    # ------------------------------------------------------------------ T10 wrong checksum
    if (Wants 'T10') {
        $file = Join-Path $Partial 'setup\components.json'
        $original = [System.IO.File]::ReadAllText($file)
        $node = Get-Component $Main 'node'
        [System.IO.File]::WriteAllText($file, $original.Replace($node.sha256, ('0' * 64)))
        Remove-Item -LiteralPath (Join-Path $Partial 'tools\node') -Recurse -Force
        $run = Run-Setup 'T10-wrong-checksum' $Partial '-unattended -lang en -no-postgres -no-selftest'
        $unpacked = (Test-Path -LiteralPath (Join-Path $Partial 'tools\node')) -or (Test-Path -LiteralPath (Join-Path $Partial 'tools\node.new'))
        Add-Result 'T10' 'wrong checksum in components.json of the test copy: clear refusal, nothing unpacked' ($run.Code -eq 1 -and $run.Text -match 'Refused: .* does not have the expected SHA-256' -and -not $unpacked) "exit code $($run.Code); refusal in the log: $($run.Text -match 'Refused: .* does not have the expected SHA-256'); tools\node created: $unpacked; $($run.Seconds) s"
        [System.IO.File]::WriteAllText($file, $original)
    }

    # ------------------------------------------------------------------ T11 archive entries outside the target
    if (Wants 'T11') {
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $evil = Join-Path $Partial 'downloads\evil.zip'
        if (Test-Path -LiteralPath $evil) { Remove-Item -LiteralPath $evil -Force }
        $fs = [System.IO.File]::Create($evil)
        $evilZip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
        foreach ($name in 'top/bin/ffmpeg.exe', 'top/../../../evil-outside.txt') {
            $writer = New-Object System.IO.StreamWriter($evilZip.CreateEntry($name).Open()); $writer.Write('test'); $writer.Dispose()
        }
        $evilZip.Dispose(); $fs.Dispose()
        $file = Join-Path $Partial 'setup\components.json'
        $original = [System.IO.File]::ReadAllText($file)
        $json = $original | ConvertFrom-Json
        $ff = $json.components | Where-Object { $_.id -eq 'ffmpeg' }
        $ff.file = 'evil.zip'; $ff.sha256 = (Get-FileHash -LiteralPath $evil -Algorithm SHA256).Hash.ToLowerInvariant(); $ff.size = (Get-Item -LiteralPath $evil).Length
        [System.IO.File]::WriteAllText($file, ($json | ConvertTo-Json -Depth 8))
        if (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg')) { Remove-Item -LiteralPath (Join-Path $Partial 'tools\ffmpeg') -Recurse -Force }
        Copy-Cache $mainCache $Partial @('node')
        $run = Run-Setup 'T11-traversal' $Partial '-offline -unattended -lang en -no-postgres -no-selftest'
        $outside = @(Get-ChildItem -LiteralPath $WorkDir -Recurse -File -Filter 'evil-outside.txt' -ErrorAction SilentlyContinue).Count + [int](Test-Path -LiteralPath (Join-Path (Split-Path -Parent $WorkDir) 'evil-outside.txt'))
        $unpacked = (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg')) -or (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg.new'))
        Add-Result 'T11' 'zip archive with an entry that leaves the target folder (..\..): refused, nothing written' ($run.Code -eq 1 -and $run.Text -match 'outside the target folder' -and $outside -eq 0 -and -not $unpacked) "exit code $($run.Code); refusal in the log: $($run.Text -match 'outside the target folder'); files written outside: $outside; tools\ffmpeg created: $unpacked"
        [System.IO.File]::WriteAllText($file, $original)
        Remove-Item -LiteralPath $evil -Force

        # the same for a package archive (tar, compressed with Zstandard) of the PostgreSQL build
        New-Package $TarCheck
        Copy-Cache $mainCache $TarCheck @('ffmpeg', 'node', 'pg')
        $tar = Join-Path $Evidence 'evil.tar'
        New-EvilTar $tar
        $pkgFile = Join-Path $Evidence 'evil.pkg.tar.zst'
        & (Join-Path $Main 'tools\zstd\zstd.exe') -q -f $tar -o $pkgFile
        Copy-AsUser $pkgFile (Join-Path $TarCheck 'downloads\evil.pkg.tar.zst')
        $file = Join-Path $TarCheck 'setup\components.json'
        $json = [System.IO.File]::ReadAllText($file) | ConvertFrom-Json
        $first = ($json.components | Where-Object { $_.id -eq 'postgresql-mingw' }).packages[0]
        $first.file = 'evil.pkg.tar.zst'; $first.sha256 = (Get-FileHash -LiteralPath $pkgFile -Algorithm SHA256).Hash.ToLowerInvariant(); $first.size = (Get-Item -LiteralPath $pkgFile).Length
        [System.IO.File]::WriteAllText($file, ($json | ConvertTo-Json -Depth 8))
        $run = Run-Setup 'T11-traversal-tar' $TarCheck '-offline -unattended -lang en -no-selftest'
        $outside = @(Get-ChildItem -LiteralPath $WorkDir -Recurse -File -Filter 'evil-outside-tar.txt' -ErrorAction SilentlyContinue).Count + [int](Test-Path -LiteralPath (Join-Path (Split-Path -Parent $WorkDir) 'evil-outside-tar.txt'))
        $unpacked = (Test-Path -LiteralPath (Join-Path $TarCheck 'tools\pgsql')) -or (Test-Path -LiteralPath (Join-Path $TarCheck 'data'))
        Add-Result 'T11b' 'package archive (tar) with an entry that leaves the target folder: refused, no PostgreSQL folder, nothing written outside' ($run.Code -eq 1 -and $run.Text -match 'outside the target folder' -and $outside -eq 0 -and -not $unpacked) "exit code $($run.Code); refusal in the log: $($run.Text -match 'outside the target folder'); files written outside: $outside; tools\pgsql or data created: $unpacked"
        Remove-Item -LiteralPath $tar, $pkgFile -Force
    }

    # ------------------------------------------------------------------ T12 options, language, blocked PowerShell
    if (Wants 'T12') {
        $help = Run-Setup 'T12-help' $Partial '-help -lang en'
        $bad = Run-Setup 'T12-bad-option' $Partial '-unattended -bogus'
        $badBuild = Run-Setup 'T12-bad-build' $Partial '-unattended -lang en -postgres oracle'
        $de = Run-Setup 'T12-german' $Partial '-offline -unattended -lang de -no-postgres -no-selftest'
        $bytes = [System.IO.File]::ReadAllBytes($de.Log)
        $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
        Add-Result 'T12a' '-help (exit 0), unknown option and unknown PostgreSQL build (exit 1), German texts in redirected output are plain ASCII' ($help.Code -eq 0 -and $help.Text -match '-unattended' -and $help.Text -match '-with-psql' -and $bad.Code -eq 1 -and $badBuild.Code -eq 1 -and $de.Code -eq 0 -and $nonAscii -eq 0 -and $de.Text -match 'Zusammenfassung' -and $de.Text -match 'Pruefung der Programme') "help: exit $($help.Code); bad option: exit $($bad.Code); unknown build: exit $($badBuild.Code); German run: exit $($de.Code), bytes above 127 in the output: $nonAscii, umlauts written as ae/oe/ue: $($de.Text -match 'Pruefung der Programme')"
        $script = Join-Path $Partial 'setup\eqlmaker-setup.ps1'
        $original = [System.IO.File]::ReadAllText($script)
        [System.IO.File]::WriteAllText($script, "this is { not a script`r`n" + $original)
        $blocked = Run-Setup 'T12-script-does-not-load' $Partial '-unattended'
        [System.IO.File]::WriteAllText($script, $original)
        Add-Result 'T12b' 'PowerShell does not run the script (simulated by a script that cannot be loaded): plain message of the batch file, exit 1' ($blocked.Code -eq 1 -and $blocked.Text -match 'did not run the setup script') "exit code $($blocked.Code); message present: $($blocked.Text -match 'did not run the setup script'). A real policy block was not produced (it would need a system setting)."
    }

    # ------------------------------------------------------------------ T13 long paths
    if (Wants 'T13') {
        $limit = 140
        $fill = $limit - $WorkDir.Length - 1
        if ($fill -lt 8) { Add-Result 'T13' 'long path' $false "work folder too long for this test ($($WorkDir.Length))" 'SKIP' }
        else {
            $atLimit = Join-Path $WorkDir ('L' * $fill)
            $over = Join-Path $WorkDir ('M' * ($fill + 1))
            New-Package $atLimit
            Copy-Cache $mainCache $atLimit @('ffmpeg', 'node', 'pg')
            $run = Run-Setup 'T13-path-at-limit' $atLimit '-offline -unattended -lang en -no-selftest'
            $deep = @(Get-ChildItem -LiteralPath (Join-Path $atLimit 'tools') -Recurse -File | ForEach-Object { $_.FullName.Length } | Measure-Object -Maximum).Maximum
            $null = Run-Cmd 'T13-stop' "`"$atLimit\eqlmaker-db-stop.cmd`"" $atLimit 180
            New-Item -ItemType Directory -Force (Join-Path $over 'setup'), (Join-Path $over 'bin') | Out-Null
            Copy-Item -LiteralPath (Join-Path $Main 'eqlmaker-setup.cmd') $over
            Copy-Item -Path (Join-Path $Main 'setup\*') -Destination (Join-Path $over 'setup')
            $run2 = Run-Setup 'T13-path-over-limit' $over '-offline -unattended -lang en -no-postgres -no-selftest'
            Add-Result 'T13' "long paths: folder of $limit characters works (with the portable PostgreSQL), $($limit + 1) characters is refused with a plain message" ($run.Code -eq 0 -and $run2.Code -eq 1 -and $run2.Text -match 'too long') "at the limit: exit $($run.Code), longest file path created $deep characters; over the limit: exit $($run2.Code), message present: $($run2.Text -match 'too long')"
            # below the folder at the limit npm made longer paths than PowerShell can delete
            foreach ($d in $atLimit, $over) { & $cmdExe /d /c rd /s /q ('\\?\' + $d) }
        }
    }

    # ------------------------------------------------------------------ T14 EnterpriseDB build, Visual C++ run-time missing
    if (Wants 'T14') {
        New-Package $Rt
        Copy-Cache $mainCache $Rt @('ffmpeg', 'node')
        Copy-Cache $SeedDir $Rt @('edb')
        $env:EQLMAKER_TEST_NO_SYSTEM_VCRUNTIME = '1'
        try {
            $hadRuntime = Test-Path -LiteralPath (Join-Path $Rt 'tools\vcruntime')
            if ($hadRuntime) { Rename-Item -LiteralPath (Join-Path $Rt 'tools\vcruntime') 'vcruntime.off' }
            $run = Run-Setup 'T14-runtime-missing' $Rt '-unattended -lang en -postgres edb'
            $nothing = -not (Test-Path -LiteralPath (Join-Path $Rt 'tools\pgsql')) -and -not (Test-Path -LiteralPath (Join-Path $Rt 'tools\node'))
            $ways = ($run.Text -match 'aka\.ms/vc14/vc_redist\.x64\.exe') -and ($run.Text -match '-no-postgres') -and ($run.Text -match '-IncludeVcRuntime')
            Add-Result 'T14a' '-postgres edb with the Visual C++ run-time treated as missing and not in the package: plain message with the ways out, before anything is downloaded or unpacked' ($run.Code -eq 1 -and $run.Text -match 'Visual C\+\+' -and $ways -and $nothing -and $run.Text -notmatch ': downloading ') "exit code $($run.Code); message present: $($run.Text -match 'Visual C\+\+'); names installer address, -no-postgres and -IncludeVcRuntime: $ways; nothing unpacked: $nothing"
            $default = Run-Setup 'T14-default-needs-no-runtime' $Rt '-offline -unattended -lang en -no-selftest'
            Add-Result 'T14c' 'the default build with the Visual C++ run-time treated as missing: no such check, the setup goes on (here: stops only because its archives are not in this folder, exit 3)' ($default.Code -eq 3 -and $default.Text -notmatch 'Visual C\+\+') "exit code $($default.Code); run-time message present: $($default.Text -match 'Visual C\+\+')"
            if ($hadRuntime) {
                Rename-Item -LiteralPath (Join-Path $Rt 'tools\vcruntime.off') 'vcruntime'
                $run = Run-Setup 'T14-runtime-app-local' $Rt '-unattended -lang en -postgres edb'
                $copied = @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll' | Where-Object { Test-Path -LiteralPath (Join-Path $Rt "tools\pgsql\bin\$_") }).Count
                $loaded = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Rt, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1 | ForEach-Object { (Get-Process -Id $_.ProcessId).Modules | Where-Object { $_.ModuleName -eq 'VCRUNTIME140.dll' } | ForEach-Object { $_.FileName } })
                Add-Result 'T14b' '-postgres edb with the run-time shipped in tools\vcruntime: copied next to the PostgreSQL programs and used by the server' ($run.Code -eq 0 -and $copied -eq 3 -and "$loaded".StartsWith($Rt, [System.StringComparison]::OrdinalIgnoreCase)) "$(Describe-Exit $run); libraries copied: $copied of 3; the server process loaded: $loaded"
                Stop-Db $Rt
            } else { Add-Result 'T14b' 'run-time shipped in tools\vcruntime' $false 'this package was built without -IncludeVcRuntime: not tested' 'SKIP' }
        } finally { $env:EQLMAKER_TEST_NO_SYSTEM_VCRUNTIME = $null }
    }

    # ------------------------------------------------------------------ T20 EnterpriseDB build as an option
    if (Wants 'T20') {
        New-Package $Edb
        Copy-Cache $mainCache $Edb @('ffmpeg', 'node')
        Copy-Cache $SeedDir $Edb @('edb')
        $run = Run-Setup 'T20-edb' $Edb '-unattended -lang en -postgres edb'
        $version = ''
        if (Test-Path -LiteralPath (Join-Path $Edb 'tools\pgsql\bin\psql.exe')) { $version = (Invoke-Psql $Edb 'sample_app' 'eqlmaker_sample' "select current_setting('server_version') || '|' || (select count(*) > 0 from customer)" $null (Join-Path $Edb 'tools\pgsql\bin\psql.exe')).Out }
        $again = Run-Setup 'T20-edb-again' $Edb '-unattended -lang en -no-selftest'
        $stays = $again.Text -match 'binaries of EnterpriseDB'
        Add-Result 'T20' '-postgres edb: the EnterpriseDB build with its psql, self test passes; a later run without the option stays with the installed build' ($run.Code -eq 0 -and $version -match '^18\.\d+\|(t|true)$' -and $again.Code -eq 0 -and $stays) "$(Describe-Exit $run); server version and sample data asked with its psql: '$version'; later run: exit $($again.Code), same build: $stays"
        Stop-Db $Edb
    }

    # ------------------------------------------------------------------ T17 processor without AVX2 (simulated)
    if (Wants 'T17') {
        New-Package $Cpu
        Copy-Cache $mainCache $Cpu @('ffmpeg', 'node', 'pg')
        $env:EQLMAKER_TEST_NO_AVX2 = '1'
        try { $run = Run-Setup 'T17-no-avx2' $Cpu '-offline -unattended -lang en' } finally { $env:EQLMAKER_TEST_NO_AVX2 = $null }
        $warnAt = $run.Text.IndexOf('this processor has no AVX2')
        $componentsAt = $run.Text.IndexOf('== Components')
        $early = ($warnAt -ge 0 -and $componentsAt -gt $warnAt)
        $noSpeech = ($run.Text -match 'speech recognition: not checked') -and ($run.Text -notmatch 'speech recognition (en|de): ') -and ($run.Text -notmatch 'ok\s+speech recognition')
        $rest = (Test-Path -LiteralPath (Join-Path $Cpu 'tools\ffmpeg\bin\ffmpeg.exe')) -and (Test-Path -LiteralPath (Join-Path $Cpu 'data\pg\PG_VERSION'))
        Add-Result 'T17' 'processor without AVX2 (simulated): warning before anything is downloaded or unpacked, the setup continues without speech recognition, everything else is set up and the self test passes' ($run.Code -eq 0 -and $early -and $noSpeech -and $rest) "$(Describe-Exit $run); warning before the component step: $early; speech checks left out: $noSpeech; other parts set up: $rest"
        Stop-Db $Cpu
    }

    # ------------------------------------------------------------------ T18 the release files as a downloader uses them
    if ((Wants 'T18') -and $ReleaseDir) {
        if (Test-Path -LiteralPath $Dl) { Remove-Item -LiteralPath $Dl -Recurse -Force }
        Invoke-AsUser "New-Item -ItemType Directory -Force -Path '$Dl' | Out-Null"
        foreach ($f in 'eqlmaker-windows-x64.zip', 'eqlmaker-windows-x64.zip.sha256', 'eqlmaker-version.json') { Copy-AsUser (Join-Path $ReleaseDir $f) (Join-Path $Dl $f) }
        # 1. the checksum file, checked with the tool every Windows has
        $hashRun = Run-Cmd 'T18-certutil' "`"$sys\certutil.exe`" -hashfile eqlmaker-windows-x64.zip SHA256" $Dl 300
        $actual = (($hashRun.Text -split "`r?`n") | Where-Object { $_ -match '^[0-9a-fA-F ]{64,}$' } | Select-Object -First 1) -replace ' ', ''
        $line = ([System.IO.File]::ReadAllText((Join-Path $Dl 'eqlmaker-windows-x64.zip.sha256'))).TrimEnd("`r", "`n")
        $shaOk = ($line -match '^([0-9a-f]{64})  eqlmaker-windows-x64\.zip$') -and ($Matches[1] -eq "$actual".ToLowerInvariant())
        # 2. the version file
        $info = [System.IO.File]::ReadAllText((Join-Path $Dl 'eqlmaker-version.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $zipItem = Get-Item -LiteralPath (Join-Path $Dl 'eqlmaker-windows-x64.zip')
        $componentsOk = (@($info.components).Count -gt 0) -and (@($info.components | Where-Object { -not $_.name -or -not $_.version -or -not $_.licence -or -not $_.delivered }).Count -eq 0)
        $infoOk = ($info.name -eq 'eqlmaker') -and ($info.version -match '^\d+\.\d+\.\d+') -and ($info.asset -eq 'eqlmaker-windows-x64.zip') -and ($info.sha256 -eq "$actual".ToLowerInvariant()) -and ([long]$info.size -eq $zipItem.Length) -and $componentsOk
        Add-Result 'T18a' 'release files: .sha256 (hash, two blanks, name) matches certutil; eqlmaker-version.json has name, version, asset, sha256, size, components' ($shaOk -and $infoOk) "checksum file matches: $shaOk; version file correct: $infoOk (version $($info.version), size $($info.size), $(@($info.components).Count) components)"
        # 3. unpack with the tar of Windows, cache filled, set up offline and unattended
        $Pkg = Join-Path $Dl 'eqlmaker'
        Invoke-AsUser "New-Item -ItemType Directory -Force -Path '$Pkg' | Out-Null"
        $un = Run-Cmd 'T18-unpack' "`"$sys\tar.exe`" -xf eqlmaker-windows-x64.zip -C eqlmaker" $Dl 600
        $inRoot = Test-Path -LiteralPath (Join-Path $Pkg 'eqlmaker-setup.cmd')
        Copy-Cache $mainCache $Pkg @('ffmpeg', 'node', 'pg')
        Set-DeadProxy
        try { $run = Run-Setup 'T18-setup' $Pkg '-unattended -offline' } finally { Reset-Proxy }
        Add-Result 'T18b' 'download-and-run sequence: unpack with tar, eqlmaker-setup.cmd in the root, -unattended -offline with a filled cache, network blocked' ($un.Code -eq 0 -and $inRoot -and $run.Code -eq 0) "unpack: exit $($un.Code); eqlmaker-setup.cmd in the root: $inRoot; setup: exit code $($run.Code), $($run.Seconds) s; T18-setup.log"
        Stop-Db $Pkg
    }

    # ------------------------------------------------------------------ T15 nothing outside the folder changed
    if (Wants 'T15') {
        $after = Get-SystemSnapshot
        $diff = @(Compare-SystemSnapshot $before $after)
        $counts = ($before.Keys | ForEach-Object { "$_ $(@($before[$_]).Count)" }) -join '; '
        # Other programs of the machine may change things while the tests run. A difference counts
        # against the package when it names the test folder or the product; for a new firewall
        # rule the program it is for is looked up.
        $detail = @(); $ours = @()
        foreach ($d in $diff) {
            $text = $d
            if ($d -match '^Firewall rules: added: ([^|]+)\|') {
                try {
                    $rule = Get-NetFirewallRule -Name $Matches[1] -ErrorAction Stop
                    $text = "$d -> '$($rule.DisplayName)' for $(($rule | Get-NetFirewallApplicationFilter).Program)"
                } catch { }
            }
            $detail += $text
            if ($text -match [regex]::Escape($WorkDir) -or $text -match '(?i)eqlmaker') { $ours += $text }
        }
        Set-Content -LiteralPath (Join-Path $Evidence 'T15-system-diff.txt') -Value (@("compared: $counts", '', "differences that name the test folder or the product: $($ours.Count)", "all differences during the run (other programs of this machine included): $($diff.Count)", '') + $detail) -Encoding UTF8
        Add-Result 'T15a' 'registry autostart and uninstall keys, PATH, environment, services, firewall rules, scheduled tasks: nothing added, removed or changed by the package' ($ours.Count -eq 0) "differences caused by the package: $($ours.Count); differences by other programs of this machine during the run: $($diff.Count - $ours.Count); compared entries: $counts; T15-system-diff.txt"
        $profile = $env:USERPROFILE
        $places = [ordered]@{ 'TEMP' = [System.IO.Path]::GetTempPath().TrimEnd('\'); 'APPDATA' = $env:APPDATA; 'LOCALAPPDATA' = $env:LOCALAPPDATA; 'USERPROFILE (without AppData)' = $profile }
        $lines = @(); $summary = @()
        foreach ($name in $places.Keys) {
            $exclude = @()
            if ($name -eq 'LOCALAPPDATA') { $exclude = @([System.IO.Path]::GetTempPath().TrimEnd('\'), (Join-Path $env:LOCALAPPDATA 'Temp')) }
            if ($name -like 'USERPROFILE*') { $exclude = @((Join-Path $profile 'AppData')) }
            $new = @(Get-NewFiles $places[$name] $started $exclude)
            $summary += "${name}: $($new.Count)"
            $lines += "== $name ($($places[$name])): $($new.Count) files written since $($started.ToString('s'))"
            $lines += $new
        }
        Set-Content -LiteralPath (Join-Path $Evidence 'T15-new-files.txt') -Value $lines -Encoding UTF8
        Add-Result 'T15b' 'files written outside the package during the tests (listed for review)' $true "$($summary -join '; '); list: T15-new-files.txt" 'INFO'
    }
} finally {
    Reset-Proxy
    # ------------------------------------------------------------------ T16 uninstall = stop, delete
    foreach ($p in $allFolders) { if (Test-Path -LiteralPath $p) { try { Stop-Db $p } catch { } } }
    if ((Wants 'T16') -and -not $KeepFolders) {
        $left = @(); $count = 0
        foreach ($p in ($allFolders + $Dl)) {
            if (Test-Path -LiteralPath $p) {
                # deleted by the restricted user as well
                $count++
                $null = Invoke-Restricted "`"$cmdExe`" /d /c rd /s /q `"$p`"" $WorkDir 300
                if (Test-Path -LiteralPath $p) { $left += $p }
            }
        }
        Add-Result 'T16' 'remove: after eqlmaker-db-stop.cmd the folders can be deleted completely by the ordinary user' ($left.Count -eq 0 -and $count -gt 0) "folders deleted: $($count - $left.Count) of $count"
    }
}
$failed = @($results | Where-Object { $_.result -eq 'FAIL' }).Count
Write-Host ''
Write-Host ("{0} results, {1} failed" -f $results.Count, $failed)
exit ([int]($failed -gt 0))
