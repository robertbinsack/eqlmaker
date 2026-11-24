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
  Folder with already downloaded archives (names as in components.json). Tests that are not
  about downloading take their archives from here; without it everything is downloaded.
.PARAMETER SeedOnlyPostgres
  In the first test only the PostgreSQL archive is taken from -SeedDir, FFmpeg and Node.js are
  really downloaded.
.PARAMETER Only
  Run only these tests (ids such as T01, T05). Tests after T01 need the package of T01.
.PARAMETER KeepFolders
  Do not delete the test packages at the end.
.NOTES
  The tests read the registry, the service list and the firewall rules, and they change nothing
  outside -WorkDir. The proxy variables are set for the test processes only.
  While "eqlmaker selftest" does not exist in a build, the setup ends with exit code 2 and the
  text "unknown command"; the tests count exactly that case as "self test not available".
#>
param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [Parameter(Mandatory = $true)][string]$WorkDir,
    [string]$SeedDir,
    [switch]$SeedOnlyPostgres,
    [string[]]$Only,
    [switch]$KeepFolders
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'run-restricted.ps1')
. (Join-Path $PSScriptRoot 'snapshot-system.ps1')

$WorkDir = [System.IO.Path]::GetFullPath($WorkDir).TrimEnd('\')
$Evidence = Join-Path $WorkDir 'evidence'
New-Item -ItemType Directory -Force $WorkDir, $Evidence | Out-Null
$sys = Join-Path $env:WINDIR 'System32'
$cmdExe = Join-Path $sys 'cmd.exe'
$results = New-Object System.Collections.Generic.List[object]
$umlaut = [string][char]0xFC
$mainName = "EQL Maker Pr${umlaut}fung"
$Main = Join-Path $WorkDir $mainName

function Add-Result([string]$Id, [string]$Test, [bool]$Ok, [string]$EvidenceText, [string]$State) {
    if (-not $State) { $State = if ($Ok) { 'PASS' } else { 'FAIL' } }
    $results.Add([pscustomobject]@{ id = $Id; test = $Test; result = $State; evidence = $EvidenceText })
    Write-Host ("{0} {1,-5} {2} - {3}" -f $Id, $State, $Test, $EvidenceText)
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $WorkDir 'results.json') -Encoding UTF8
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
# exit code 0, or exit code 2 only because this build has no "selftest" command yet
function Test-SetupOk($Run) {
    if ($Run.Code -eq 0) { return $true }
    return ($Run.Code -eq 2 -and $Run.Text -match "unknown command 'selftest'" -and $Run.Text -notmatch 'speech recognition \w+: FAILED')
}
function Describe-Exit($Run) {
    if ($Run.Code -eq 2 -and $Run.Text -match "unknown command 'selftest'") { return "exit code 2 (self test not available in this build), $($Run.Seconds) s" }
    return "exit code $($Run.Code), $($Run.Seconds) s"
}
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
function Seed([string]$Package, [string[]]$Ids) {
    if (-not $SeedDir) { return }
    $comp = Get-Content -LiteralPath (Join-Path $Package 'setup\components.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($c in $comp.components) {
        if ($Ids -contains $c.id) { Copy-AsUser (Join-Path $SeedDir $c.file) (Join-Path $Package "downloads\$($c.file)") }
    }
}
function Get-Component([string]$Package, [string]$Id) {
    $comp = Get-Content -LiteralPath (Join-Path $Package 'setup\components.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    return ($comp.components | Where-Object { $_.id -eq $Id })
}
function Get-Port([string]$Package) { return [int]((Get-Content -LiteralPath (Join-Path $Package 'data\pg-port.txt') -TotalCount 1).Trim()) }
function Get-Listeners([int]$Port) {
    return , @([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -eq $Port } | ForEach-Object { $_.Address.ToString() })
}
function Get-PackageProcesses([string]$Package) {
    return @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Package, [System.StringComparison]::OrdinalIgnoreCase) })
}
function Invoke-Psql([string]$Package, [string]$User, [string]$Database, [string]$Sql, [string]$Password) {
    $old = @{ PGPASSFILE = $env:PGPASSFILE; PGPASSWORD = $env:PGPASSWORD }
    $env:PGPASSFILE = Join-Path $Package 'data\pgpass.conf'
    if ($Password) { $env:PGPASSWORD = $Password } else { $env:PGPASSWORD = $null }
    $ea = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        $out = & (Join-Path $Package 'tools\pgsql\bin\psql.exe') -X -At -w -h 127.0.0.1 -p (Get-Port $Package) -U $User -d $Database -c $Sql 2>&1 | ForEach-Object { "$_" }
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

$started = Get-Date
$before = Get-SystemSnapshot
$oldProxy = @{ HTTPS_PROXY = $env:HTTPS_PROXY; HTTP_PROXY = $env:HTTP_PROXY; ALL_PROXY = $env:ALL_PROXY }
function Set-DeadProxy { $env:HTTPS_PROXY = 'http://127.0.0.1:9'; $env:HTTP_PROXY = 'http://127.0.0.1:9'; $env:ALL_PROXY = 'http://127.0.0.1:9' }
function Reset-Proxy { $env:HTTPS_PROXY = $oldProxy.HTTPS_PROXY; $env:HTTP_PROXY = $oldProxy.HTTP_PROXY; $env:ALL_PROXY = $oldProxy.ALL_PROXY }

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
        $used = @([regex]::Matches([System.IO.File]::ReadAllText((Join-Path $setupDir 'eqlmaker-setup.ps1')), "\(T '([a-z.]+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $unknown = @($used | Where-Object { $ke -notcontains $_ })
        Add-Result 'T00' 'resource texts en/de: identical keys, same placeholders, every key used by the script exists' ($diff.Count -eq 0 -and $badArgs.Count -eq 0 -and $unknown.Count -eq 0) "$($ke.Count) keys en, $($kd.Count) keys de, differences: $($diff.Count), placeholder mismatches: $($badArgs.Count), unknown keys in script: $($unknown.Count)"
    }

    # ------------------------------------------------------------------ T01 fresh folder, restricted user
    if (Wants 'T01') {
        New-Package $Main
        if ($SeedOnlyPostgres) { Seed $Main @('postgresql') } else { Seed $Main @('postgresql', 'ffmpeg', 'node') }
        $who = Join-Path $Evidence 'T01-whoami.txt'
        $role = Join-Path $Evidence 'T01-isadmin.txt'
        $line = "`"$sys\whoami.exe`" /groups /fo csv > `"$who`" & `"$sys\whoami.exe`" /priv >> `"$who`" & `"$sys\WindowsPowerShell\v1.0\powershell.exe`" -NoProfile -Command `"(New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.SecurityIdentifier]'S-1-5-32-544')`" > `"$role`" & `"$Main\eqlmaker-setup.cmd`" -unattended -lang en"
        $run = Run-Cmd 'T01-setup' $line $Main
        $whoText = [System.IO.File]::ReadAllText($who)
        $isAdmin = ([System.IO.File]::ReadAllText($role)).Trim()
        $adminLine = ($whoText -split "`n" | Where-Object { $_ -match 'S-1-5-32-544' } | Select-Object -First 1)
        $notElevated = ($isAdmin -eq 'False') -and ($whoText -match 'S-1-16-8192') -and ($whoText -notmatch 'S-1-16-12288')
        Add-Result 'T01a' 'the setup process is not elevated (restricted token, evidence taken inside the run)' $notElevated "member of the admin group (S-1-5-32-544) enabled: $isAdmin; integrity level S-1-16-8192 (Medium) present: $($whoText -match 'S-1-16-8192'); entry of the admin group: $("$adminLine".Trim()); T01-whoami.txt"
        Add-Result 'T01b' "fresh folder with blank and umlaut ('$mainName'), setup as ordinary user" (Test-SetupOk $run) "$(Describe-Exit $run); T01-setup.log"
        $need = 'tools\ffmpeg\bin\ffmpeg.exe', 'tools\ffmpeg\LICENSE.txt', 'tools\node\node.exe', 'tools\node\LICENSE', 'tools\pgsql\bin\postgres.exe', 'tools\pgsql\server_license.txt', 'tools\whisper\whisper-cli.exe', 'tools\whisper\LICENSE', 'tools\whisper\models\LICENSE', 'data\pg\PG_VERSION', 'data\pg-port.txt', 'data\pgpass.conf', 'project\eqlmaker.json', 'examples\sample-app\web\node_modules\pg\package.json', 'eqlmaker.cmd', 'eqlmaker-npm.cmd', 'eqlmaker-db-start.cmd', 'eqlmaker-db-stop.cmd'
        $absent = 'tools\pgsql\pgAdmin 4', 'tools\pgsql\StackBuilder', 'tools\pgsql\doc', 'tools\pgsql\include', 'tools\pgsql\bin\stackbuilder.exe', 'tools\node\install_tools.bat', 'tools\ffmpeg\bin\ffplay.exe'
        $missing = @($need | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Main $_)) })
        $unwanted = @($absent | Where-Object { Test-Path -LiteralPath (Join-Path $Main $_) })
        Add-Result 'T01c' 'layout of the package after the setup' ($missing.Count -eq 0 -and $unwanted.Count -eq 0) "$($need.Count) expected files present (missing: $($missing -join ', ')); unwanted parts absent (found: $($unwanted -join ', '))"
        $port = Get-Port $Main
        $expected = '{"source":"postgres","connection":"host=127.0.0.1 port=' + $port + ' user=postgres dbname=postgres","passfile":"..\\data\\pgpass.conf","capture":{"roles":[],"databases":["eqlmaker_sample"],"changes":true},"input":"background","tools":{}}'
        $actual = ([System.IO.File]::ReadAllText((Join-Path $Main 'project\eqlmaker.json'))).Trim()
        Add-Result 'T01d' 'project\eqlmaker.json has the agreed content' ($actual -eq $expected) "port $port; content equal: $($actual -eq $expected)"
        $listen = Get-Listeners $port
        Add-Result 'T01e' 'the portable PostgreSQL listens on 127.0.0.1 only' ($listen.Count -eq 1 -and $listen[0] -eq '127.0.0.1') "listeners on port ${port}: $($listen -join ', ')"
        $settings = Invoke-Psql $Main 'postgres' 'postgres' "select current_setting('listen_addresses') || '|' || current_setting('wal_level') || '|' || current_setting('logging_collector') || '|' || current_setting('password_encryption') || '|' || current_setting('server_version') || '|' || (select string_agg(distinct auth_method, ',') from pg_hba_file_rules)"
        Add-Result 'T01f' 'cluster settings: listen address, wal_level, logging collector, scram-sha-256' ($settings.Code -eq 0 -and $settings.Out -match '^127\.0\.0\.1\|logical\|on\|scram-sha-256\|18\.\d+\|scram-sha-256$') $settings.Out
        $good = Invoke-Psql $Main 'postgres' 'postgres' 'select 1'
        $wrong = Invoke-Psql $Main 'postgres' 'postgres' 'select 1' 'certainly-wrong'
        Add-Result 'T01g' 'generated password works, a wrong password is refused' ($good.Code -eq 0 -and $good.Out -eq '1' -and $wrong.Code -ne 0 -and $wrong.Out -match 'password authentication failed') "password file: exit $($good.Code); wrong password: exit $($wrong.Code), '$(($wrong.Out -split "`n")[0])'"
        $sample = Invoke-Psql $Main 'sample_app' 'eqlmaker_sample' 'select (select count(*) from customer) > 0 and (select count(*) from article) > 0'
        Add-Result 'T01h' 'sample database loaded, login as sample_app' ($sample.Code -eq 0 -and $sample.Out -eq 't') "exit $($sample.Code), result '$($sample.Out)'"
        $acl = (& (Join-Path $sys 'icacls.exe') (Join-Path $Main 'data\pgpass.conf')) -join ' '
        $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $entries = @([regex]::Matches($acl, '([^\s\\]+\\[^:]+):\(') | ForEach-Object { $_.Groups[1].Value.Trim() })
        Add-Result 'T01i' 'data\pgpass.conf is readable by the current user only' ($entries.Count -eq 1 -and $acl -match [regex]::Escape($me)) "access entries: $($entries.Count) (the current user)"
        $pwLeak = @(Get-ChildItem -LiteralPath (Join-Path $Main 'downloads') -Recurse -File | Where-Object { $_.Name -eq 'pw.txt' }).Count
        $pass = [System.IO.File]::ReadAllLines((Join-Path $Main 'data\pgpass.conf'))[0].Split(':')[4]
        $inLog = ([System.IO.File]::ReadAllText((Join-Path $Main 'downloads\setup.log')) + $run.Text).Contains($pass)
        Add-Result 'T01j' 'the generated password is neither left in a temporary file nor written to the log' ($pwLeak -eq 0 -and -not $inLog -and $pass.Length -eq 32) "temporary password files: $pwLeak; password in log or output: $inLog; length $($pass.Length)"
    }

    # ------------------------------------------------------------------ T02 second run
    if (Wants 'T02') {
        $state1 = Get-TreeState $Main
        Set-DeadProxy
        try { $run = Run-Setup 'T02-second-run' $Main '-unattended -lang en' } finally { Reset-Proxy }
        $state2 = Get-TreeState $Main
        $changed = @(Compare-Object $state1 $state2).Count
        $noDownload = ($run.Text -notmatch ': downloading ') -and ($run.Text -notmatch 'unpacking to') -and ($run.Text -notmatch 'continuing at')
        Add-Result 'T02' 'second run is idempotent (no download, no unpacking, same files; network blocked by proxy 127.0.0.1:9)' ((Test-SetupOk $run) -and $noDownload -and $changed -eq 0) "$(Describe-Exit $run); downloads or unpacking in the log: $(-not $noDownload); changed files in tools, bin, setup, project configuration, password file: $changed of $($state1.Count)"
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
        $q = Invoke-Psql $Main 'postgres' 'postgres' 'select 1'
        $again = Run-Cmd 'T06-db-start-again' "`"$Main\eqlmaker-db-start.cmd`"" $Main 240
        Add-Result 'T06b' 'eqlmaker-db-start.cmd starts it again (127.0.0.1 only), a second start is harmless' ($start.Code -eq 0 -and $listen.Count -eq 1 -and $listen[0] -eq '127.0.0.1' -and $q.Out -eq '1' -and $again.Code -eq 0) "start: exit $($start.Code); listeners: $($listen -join ', '); query: '$($q.Out)'; second start: exit $($again.Code)"
        $stop2 = Run-Cmd 'T06-db-stop-2' "`"$Main\eqlmaker-db-stop.cmd`"" $Main 180
        Start-Sleep -Seconds 2
        $procs = Get-PackageProcesses $Main
        Add-Result 'T06c' 'after the final stop no process of the package is left' ($stop2.Code -eq 0 -and $procs.Count -eq 0 -and (Get-Listeners $port).Count -eq 0) "exit code $($stop2.Code); processes: $($procs.Count); listeners: $((Get-Listeners $port).Count)"
    }

    # ------------------------------------------------------------------ T07 offline with a complete cache
    $Offline = Join-Path $WorkDir 'offline ok'
    if (Wants 'T07') {
        New-Package $Offline
        foreach ($id in 'ffmpeg', 'node', 'postgresql') {
            $c = Get-Component $Main $id
            Copy-AsUser (Join-Path $Main "downloads\$($c.file)") (Join-Path $Offline "downloads\$($c.file)")
        }
        Set-DeadProxy
        try { $run = Run-Setup 'T07-offline-complete' $Offline '-offline -unattended -lang en' } finally { Reset-Proxy }
        $q = [pscustomobject]@{ Out = '' }
        if (Test-Path -LiteralPath (Join-Path $Offline 'data\pg-port.txt')) { $q = Invoke-Psql $Offline 'sample_app' 'eqlmaker_sample' 'select count(*) > 0 from customer' }
        Add-Result 'T07' '-offline with a complete downloads folder, network blocked (proxy variables point to 127.0.0.1:9)' ((Test-SetupOk $run) -and $q.Out -eq 't') "$(Describe-Exit $run); sample database query: '$($q.Out)'; T07-offline-complete.log"
        Stop-Db $Offline
    }

    # ------------------------------------------------------------------ T08 offline with a missing archive
    $Partial = Join-Path $WorkDir 'partial'
    if (Wants 'T08') {
        New-Package $Partial
        foreach ($id in 'ffmpeg', 'postgresql') {
            $c = Get-Component $Main $id
            Copy-AsUser (Join-Path $Main "downloads\$($c.file)") (Join-Path $Partial "downloads\$($c.file)")
        }
        $node = Get-Component $Main 'node'
        $run = Run-Setup 'T08-offline-missing' $Partial '-offline -unattended -lang en'
        $nothing = -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg')) -and -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\node')) -and -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\pgsql')) -and -not (Test-Path -LiteralPath (Join-Path $Partial 'data'))
        Add-Result 'T08' '-offline with a missing archive: plain message with file and address, exit code 3, nothing unpacked' ($run.Code -eq 3 -and $run.Text.Contains($node.file) -and $run.Text.Contains($node.url) -and $nothing) "exit code $($run.Code); names the file: $($run.Text.Contains($node.file)); names the address: $($run.Text.Contains($node.url)); nothing unpacked: $nothing"
        # a damaged (cut) archive in offline mode is reported and left as it is
        $ff = Get-Component $Main 'ffmpeg'
        $cut = Join-Path $Partial "downloads\$($ff.file)"
        Invoke-AsUser "`$f = [System.IO.File]::Open('$cut', 'Open'); `$f.SetLength(5MB); `$f.Dispose()"
        $run = Run-Setup 'T08-offline-damaged' $Partial '-offline -unattended -lang en'
        $size = (Get-Item -LiteralPath $cut).Length
        $nothing = -not (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg')) -and -not (Test-Path -LiteralPath "$cut.part")
        Add-Result 'T08b' '-offline with a damaged archive: reported, exit code 3, the file is left untouched' ($run.Code -eq 3 -and $run.Text.Contains($ff.file) -and $size -eq 5MB -and $nothing) "exit code $($run.Code); names the file: $($run.Text.Contains($ff.file)); file still $size bytes; nothing unpacked or renamed: $nothing"
        Copy-AsUser (Join-Path $Main "downloads\$($ff.file)") $cut
    }

    # ------------------------------------------------------------------ T09 broken download is resumed
    if (Wants 'T09') {
        $node = Get-Component $Main 'node'
        $target = Join-Path $Partial "downloads\$($node.file)"
        Invoke-AsUser "`$src = [System.IO.File]::OpenRead('$(Join-Path $Main "downloads\$($node.file)")'); `$dst = [System.IO.File]::Create('$target'); `$buffer = New-Object byte[] (10MB); `$done = 0; while (`$done -lt `$buffer.Length) { `$n = `$src.Read(`$buffer, `$done, `$buffer.Length - `$done); if (`$n -le 0) { break }; `$done += `$n }; `$dst.Write(`$buffer, 0, `$done); `$dst.Dispose(); `$src.Dispose()"
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

    # ------------------------------------------------------------------ T11 archive entry outside the target
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
        if (-not (Test-Path -LiteralPath (Join-Path $Partial "downloads\$((Get-Component $Main 'node').file)"))) { Copy-AsUser (Join-Path $Main "downloads\$((Get-Component $Main 'node').file)") (Join-Path $Partial "downloads\$((Get-Component $Main 'node').file)") }
        $run = Run-Setup 'T11-traversal' $Partial '-offline -unattended -lang en -no-postgres -no-selftest'
        $outside = @(Get-ChildItem -LiteralPath $WorkDir -Recurse -File -Filter 'evil-outside.txt' -ErrorAction SilentlyContinue).Count + [int](Test-Path -LiteralPath (Join-Path (Split-Path -Parent $WorkDir) 'evil-outside.txt'))
        $unpacked = (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg')) -or (Test-Path -LiteralPath (Join-Path $Partial 'tools\ffmpeg.new'))
        Add-Result 'T11' 'archive with an entry that leaves the target folder (..\..): refused, nothing written' ($run.Code -eq 1 -and $run.Text -match 'outside the target folder' -and $outside -eq 0 -and -not $unpacked) "exit code $($run.Code); refusal in the log: $($run.Text -match 'outside the target folder'); files written outside: $outside; tools\ffmpeg created: $unpacked"
        [System.IO.File]::WriteAllText($file, $original)
        Remove-Item -LiteralPath $evil -Force
    }

    # ------------------------------------------------------------------ T12 options, language, blocked PowerShell
    if (Wants 'T12') {
        $help = Run-Setup 'T12-help' $Partial '-help -lang en'
        $bad = Run-Setup 'T12-bad-option' $Partial '-unattended -bogus'
        $de = Run-Setup 'T12-german' $Partial '-offline -unattended -lang de -no-postgres -no-selftest'
        $bytes = [System.IO.File]::ReadAllBytes($de.Log)
        $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
        Add-Result 'T12a' '-help (exit 0), unknown option (exit 1), German texts in redirected output are plain ASCII' ($help.Code -eq 0 -and $help.Text -match '-unattended' -and $bad.Code -eq 1 -and $de.Code -eq 0 -and $nonAscii -eq 0 -and $de.Text -match 'Zusammenfassung' -and $de.Text -match 'Pruefung der Programme') "help: exit $($help.Code); bad option: exit $($bad.Code); German run: exit $($de.Code), bytes above 127 in the output: $nonAscii, umlauts written as ae/oe/ue: $($de.Text -match 'Pruefung der Programme')"
        $script = Join-Path $Partial 'setup\eqlmaker-setup.ps1'
        $original = [System.IO.File]::ReadAllText($script)
        [System.IO.File]::WriteAllText($script, "this is { not a script`r`n" + $original)
        $blocked = Run-Setup 'T12-script-does-not-load' $Partial '-unattended'
        [System.IO.File]::WriteAllText($script, $original)
        Add-Result 'T12b' 'PowerShell does not run the script (simulated by a script that cannot be loaded): plain message of the batch file, exit 1' ($blocked.Code -eq 1 -and $blocked.Text -match 'did not run the setup script') "exit code $($blocked.Code); message present: $($blocked.Text -match 'did not run the setup script'). A real policy block was not produced (it would need a system setting)."
    }

    # ------------------------------------------------------------------ T13 long paths
    if (Wants 'T13') {
        $comp = Get-Content -LiteralPath (Join-Path $Main 'setup\components.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $limit = 140
        $fill = $limit - $WorkDir.Length - 1
        if ($fill -lt 8) { Add-Result 'T13' 'long path' $false "work folder too long for this test ($($WorkDir.Length))" 'SKIP' }
        else {
            $atLimit = Join-Path $WorkDir ('L' * $fill)
            $over = Join-Path $WorkDir ('M' * ($fill + 1))
            New-Package $atLimit
            foreach ($id in 'ffmpeg', 'node') { $c = Get-Component $Main $id; Copy-AsUser (Join-Path $Main "downloads\$($c.file)") (Join-Path $atLimit "downloads\$($c.file)") }
            $run = Run-Setup 'T13-path-at-limit' $atLimit '-offline -unattended -lang en -no-postgres -no-selftest'
            $deep = @(Get-ChildItem -LiteralPath (Join-Path $atLimit 'tools\node') -Recurse -File | ForEach-Object { $_.FullName.Length } | Measure-Object -Maximum).Maximum
            New-Item -ItemType Directory -Force (Join-Path $over 'setup'), (Join-Path $over 'bin') | Out-Null
            Copy-Item -LiteralPath (Join-Path $Main 'eqlmaker-setup.cmd') $over
            Copy-Item -Path (Join-Path $Main 'setup\*') -Destination (Join-Path $over 'setup')
            $run2 = Run-Setup 'T13-path-over-limit' $over '-offline -unattended -lang en -no-postgres -no-selftest'
            Add-Result 'T13' "long paths: folder of $limit characters works, $($limit + 1) characters is refused with a plain message" ($run.Code -eq 0 -and $run2.Code -eq 1 -and $run2.Text -match 'too long') "at the limit: exit $($run.Code), longest file path created $deep characters; over the limit: exit $($run2.Code), message present: $($run2.Text -match 'too long')"
            # below the folder at the limit npm made longer paths than PowerShell can delete
            foreach ($d in $atLimit, $over) { & $cmdExe /d /c rd /s /q ('\\?\' + $d) }
        }
    }

    # ------------------------------------------------------------------ T14 Visual C++ run-time missing
    if (Wants 'T14') {
        $Rt = Join-Path $WorkDir 'runtime'
        New-Package $Rt
        foreach ($id in 'ffmpeg', 'node', 'postgresql') { $c = Get-Component $Main $id; Copy-AsUser (Join-Path $Main "downloads\$($c.file)") (Join-Path $Rt "downloads\$($c.file)") }
        $env:EQLMAKER_TEST_NO_SYSTEM_VCRUNTIME = '1'
        try {
            $hadRuntime = Test-Path -LiteralPath (Join-Path $Rt 'tools\vcruntime')
            if ($hadRuntime) { Rename-Item -LiteralPath (Join-Path $Rt 'tools\vcruntime') 'vcruntime.off' }
            $run = Run-Setup 'T14-runtime-missing' $Rt '-offline -unattended -lang en'
            $nothing = -not (Test-Path -LiteralPath (Join-Path $Rt 'tools\pgsql')) -and -not (Test-Path -LiteralPath (Join-Path $Rt 'tools\node'))
            Add-Result 'T14a' 'Visual C++ run-time treated as missing and not in the package: plain message before anything is unpacked' ($run.Code -eq 1 -and $run.Text -match 'Visual C\+\+' -and $nothing) "exit code $($run.Code); message present: $($run.Text -match 'Visual C\+\+'); nothing unpacked: $nothing"
            if ($hadRuntime) {
                Rename-Item -LiteralPath (Join-Path $Rt 'tools\vcruntime.off') 'vcruntime'
                $run = Run-Setup 'T14-runtime-app-local' $Rt '-offline -unattended -lang en'
                $copied = @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll' | Where-Object { Test-Path -LiteralPath (Join-Path $Rt "tools\pgsql\bin\$_") }).Count
                $loaded = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Rt, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1 | ForEach-Object { (Get-Process -Id $_.ProcessId).Modules | Where-Object { $_.ModuleName -eq 'VCRUNTIME140.dll' } | ForEach-Object { $_.FileName } })
                Add-Result 'T14b' 'run-time shipped in tools\vcruntime: copied next to the PostgreSQL programs and used by the server' ((Test-SetupOk $run) -and $copied -eq 3 -and "$loaded".StartsWith($Rt, [System.StringComparison]::OrdinalIgnoreCase)) "$(Describe-Exit $run); libraries copied: $copied of 3; the server process loaded: $loaded"
                Stop-Db $Rt
            } else { Add-Result 'T14b' 'run-time shipped in tools\vcruntime' $false 'this package was built without -IncludeVcRuntime: not tested' 'SKIP' }
        } finally { $env:EQLMAKER_TEST_NO_SYSTEM_VCRUNTIME = $null }
        if (-not $KeepFolders) { Remove-Item -LiteralPath $Rt -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ------------------------------------------------------------------ T15 nothing outside the folder changed
    if (Wants 'T15') {
        $after = Get-SystemSnapshot
        $diff = @(Compare-SystemSnapshot $before $after)
        $counts = ($before.Keys | ForEach-Object { "$_ $(@($before[$_]).Count)" }) -join '; '
        Set-Content -LiteralPath (Join-Path $Evidence 'T15-system-diff.txt') -Value (@("compared: $counts", '') + $diff) -Encoding UTF8
        Add-Result 'T15a' 'registry autostart and uninstall keys, PATH, environment, services, firewall rules, scheduled tasks unchanged' ($diff.Count -eq 0) "differences: $($diff.Count); compared entries: $counts; T15-system-diff.txt"
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
    foreach ($p in @($Main, $Offline, $Partial)) { if ($p -and (Test-Path -LiteralPath $p)) { try { Stop-Db $p } catch { } } }
    if ((Wants 'T16') -and -not $KeepFolders) {
        $left = @()
        foreach ($p in @($Main, $Offline, $Partial)) {
            if ($p -and (Test-Path -LiteralPath $p)) {
                # deleted by the restricted user as well
                $null = Invoke-Restricted "`"$cmdExe`" /d /c rd /s /q `"$p`"" $WorkDir 300
                if (Test-Path -LiteralPath $p) { $left += $p }
            }
        }
        Add-Result 'T16' 'remove: after eqlmaker-db-stop.cmd the folders can be deleted completely by the ordinary user' ($left.Count -eq 0) "folders that could not be deleted: $($left.Count)"
    }
}
$failed = @($results | Where-Object { $_.result -eq 'FAIL' }).Count
Write-Host ''
Write-Host ("{0} results, {1} failed" -f $results.Count, $failed)
exit ([int]($failed -gt 0))
