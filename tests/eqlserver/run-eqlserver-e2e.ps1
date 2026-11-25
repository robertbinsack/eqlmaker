# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Optional end-to-end test against an ALREADY RUNNING server: an application that speaks SQL Server
  (TDS) works against EQLServer, a database server of the same author that stores in PostgreSQL, while
  EQLMaker captures. The time line must contain the T-SQL batches and RPC calls and the rows they changed.

  Skipped (exit code 0) when the TDS port, the PostgreSQL port, the statistics port or a password is not given.

  The script installs, starts and stops nothing. The server needs: the statement trace on its statistics
  port, wal_level = logical, a T-SQL login that may create a database, a PostgreSQL superuser.

.PARAMETER ServerHost
  Host of the server (default 127.0.0.1 or EQLMAKER_E2E_HOST).
.PARAMETER TdsPort
  Port of the SQL Server protocol (EQLMAKER_E2E_TDS_PORT).
.PARAMETER PgPort
  Port of PostgreSQL (EQLMAKER_E2E_PG_PORT).
.PARAMETER StatsPort
  Statistics port with the statement trace (EQLMAKER_E2E_STATS_PORT).
.PARAMETER Database
  PostgreSQL database the T-SQL data is stored in (EQLMAKER_E2E_DATABASE).
.PARAMETER TdsUser
  T-SQL login (EQLMAKER_E2E_TDS_USER, default sa).
.PARAMETER PgUser
  PostgreSQL superuser (EQLMAKER_E2E_PG_USER, default postgres).
.PARAMETER Psql
  Path of psql.exe for the checks of the server settings (EQLMAKER_E2E_PSQL); without it these checks are left out.
.PARAMETER LogFile
  Log file of the server (EQLMAKER_E2E_LOGFILE); without it the error check looks at the trace only.
.NOTES
  Passwords only through the environment: EQLMAKER_E2E_TDS_PASSWORD and EQLMAKER_E2E_PG_PASSWORD.
  Result: a line per check, exit code 1 when a check fails.
#>
param(
    [string]$ServerHost = '',
    [int]$TdsPort = 0,
    [int]$PgPort = 0,
    [int]$StatsPort = 0,
    [string]$Database = '',
    [string]$TdsUser = '',
    [string]$PgUser = '',
    [string]$Psql = '',
    [string]$LogFile = '',
    [string]$Work = '',
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
function Env([string]$n) { return [Environment]::GetEnvironmentVariable('EQLMAKER_E2E_' + $n) }
if (-not $ServerHost) { $ServerHost = Env 'HOST' }; if (-not $ServerHost) { $ServerHost = '127.0.0.1' }
if (-not $TdsPort) { $TdsPort = [int]('0' + (Env 'TDS_PORT')) }
if (-not $PgPort) { $PgPort = [int]('0' + (Env 'PG_PORT')) }
if (-not $StatsPort) { $StatsPort = [int]('0' + (Env 'STATS_PORT')) }
if (-not $Database) { $Database = Env 'DATABASE' }; if (-not $Database) { $Database = 'postgres' }
if (-not $TdsUser) { $TdsUser = Env 'TDS_USER' }; if (-not $TdsUser) { $TdsUser = 'sa' }
if (-not $PgUser) { $PgUser = Env 'PG_USER' }; if (-not $PgUser) { $PgUser = 'postgres' }
if (-not $Psql) { $Psql = Env 'PSQL' }
if (-not $LogFile) { $LogFile = Env 'LOGFILE' }
$tdsPw = Env 'TDS_PASSWORD'; $pgPw = Env 'PG_PASSWORD'
if (-not $TdsPort -or -not $PgPort -or -not $StatsPort -or -not $tdsPw -or -not $pgPw) {
    Write-Host 'SKIP  no running server given (EQLMAKER_E2E_TDS_PORT, _PG_PORT, _STATS_PORT, _TDS_PASSWORD, _PG_PASSWORD)'; exit 0
}
$exe = Join-Path $root 'bin\eqlmaker.exe'
if (-not (Test-Path $exe)) { Write-Host 'FAIL  bin\eqlmaker.exe is missing: run build.cmd first'; exit 1 }
if (-not $Work) { $Work = Join-Path ([IO.Path]::GetTempPath()) ('eqlmaker-e2e-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
New-Item -ItemType Directory -Force $Work | Out-Null
$Work = (Resolve-Path $Work).Path
$summary = Join-Path $Work 'summary.txt'
$script:pass = 0; $script:fail = 0
function Check([string]$name, [bool]$ok, [string]$info = '') {
    $line = if ($ok) { "ok    $name" } else { "FAIL  $name $info" }
    if ($ok) { $script:pass++ } else { $script:fail++ }
    Write-Host $line; Add-Content $summary $line
}
$utf8 = New-Object Text.UTF8Encoding $false
$havePsql = [bool]$Psql -and (Test-Path -LiteralPath $Psql)
function Psql([string]$sql, [string]$db = 'postgres') { $env:PGPASSWORD = $pgPw; try { return (& $Psql -h $ServerHost -p $PgPort -U $PgUser -d $db -X -A -t -c $sql) } finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue } }

try {
    Check "the server answers (TDS $TdsPort, PostgreSQL $PgPort, statistics $StatsPort)" ((Invoke-WebRequest -UseBasicParsing "http://${ServerHost}:$StatsPort/" -TimeoutSec 5).StatusCode -eq 200)
    if ($havePsql) { Check 'wal_level = logical (changed rows need logical decoding)' ((Psql 'SHOW wal_level').Trim() -eq 'logical') }

# ---- a T-SQL database with one table, made before the capture
    Add-Type -AssemblyName System.Data
    $cs = "Server=$ServerHost,$TdsPort;User Id=$TdsUser;Password=$tdsPw;Encrypt=False;TrustServerCertificate=True;Application Name=setup;Initial Catalog="
    function Exec-Tsql([string]$db, [string]$sql) { $c = New-Object System.Data.SqlClient.SqlConnection ($cs + $db); $c.Open(); try { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; [void]$cmd.ExecuteNonQuery() } finally { $c.Close() } }
    Exec-Tsql 'master' 'CREATE DATABASE shopdb'
    Exec-Tsql 'shopdb' 'CREATE TABLE dbo.customer (id int IDENTITY(1,1) PRIMARY KEY, name nvarchar(60) NOT NULL, city nvarchar(40) NULL)'

    # ---- the application: a small T-SQL client (batches, calls with parameters, an error)
    $client = Join-Path $Work 'legacy-client.ps1'
    $clientText = @'
param([string]$Step)
Add-Type -AssemblyName System.Data
$c = New-Object System.Data.SqlClient.SqlConnection ("Server={HOST},{POOL};User Id={USER};Password={PW};Encrypt=False;TrustServerCertificate=True;Initial Catalog=shopdb;Application Name=legacy-client")
$c.Open()
function X([string]$sql, [hashtable]$p) {
    $cmd = $c.CreateCommand(); $cmd.CommandText = $sql
    if ($p) { foreach ($k in $p.Keys) { [void]$cmd.Parameters.AddWithValue($k, $p[$k]) } }
    try { [void]$cmd.ExecuteNonQuery() } catch { }
    Start-Sleep -Milliseconds 400
}
switch ($Step) {
    'create' {
        X "INSERT INTO dbo.customer (name, city) VALUES (N'Harbour Books', N'Rostock')"
        X "INSERT INTO dbo.customer (name, city) VALUES (@n, @c)" @{ '@n' = 'Anna Berger'; '@c' = 'Wien' }
    }
    'change' { X "UPDATE dbo.customer SET city = @c WHERE name = @n" @{ '@n' = 'Anna Berger'; '@c' = 'Graz' } }
    'remove' {
        X "SELECT * FROM dbo.no_such_table"
        X "DELETE FROM dbo.customer WHERE name = N'Harbour Books'"
    }
}
$c.Close()
'@
    [IO.File]::WriteAllText($client, $clientText.Replace('{HOST}', $ServerHost).Replace('{POOL}', "$TdsPort").Replace('{USER}', $TdsUser).Replace('{PW}', $tdsPw), $utf8)

    # ---- EQLMaker: project, scenario, record, correlate, spec, export
    $proj = Join-Path $Work 'project'; New-Item -ItemType Directory $proj | Out-Null
    $projCfg = [ordered]@{ source = 'eqlserver'; connection = "host=$ServerHost port=$PgPort user=$PgUser dbname=postgres"
        capture = [ordered]@{ roles = @($TdsUser); databases = @($Database); changes = $true }; eqlserver = [ordered]@{ statsPort = $StatsPort } }
    if ($LogFile) { $projCfg.capture.logFile = $LogFile.Replace('\', '/') }
    [IO.File]::WriteAllText((Join-Path $proj 'eqlmaker.json'), ($projCfg | ConvertTo-Json -Depth 5), $utf8)
    $env:EQLMAKER_PGPASSWORD = $pgPw; $env:EQLMAKER_LANG = 'en'
    $steps = @(); $i = 0
    foreach ($s in @(@('create', 'New customer'), @('change', 'Change customer'), @('remove', 'Remove customer'))) {
        $steps += [ordered]@{ caption = $s[1] }
        $steps += [ordered]@{ run = 'powershell.exe'; args = "-NoProfile -ExecutionPolicy Bypass -File `"$client`" $($s[0])" }
        $steps += [ordered]@{ wait = 5500 }
    }
    $sc = [ordered]@{ name = 'e2e'; input = 'background'; video = $false; shots = $false; steps = $steps }
    $scFile = Join-Path $Work 'scenario.json'
    [IO.File]::WriteAllText($scFile, ($sc | ConvertTo-Json -Depth 6), $utf8)

    $out = & $exe record --scenario $scFile --project $proj --quiet 2>&1 | Out-String
    Check 'record --scenario ends without error' ($LASTEXITCODE -eq 0) $out
    $rec = Join-Path $proj 'recordings\e2e'
    $man = Get-Content -Raw -Encoding UTF8 (Join-Path $rec 'recording.json') | ConvertFrom-Json
    Check 'the recording names the source and that the statement trace is on' ($man.source -eq 'eqlserver' -and $man.capture.trace -eq 'on') "source=$($man.source) trace=$($man.capture.trace)"
    $warn = @($man.warnings) -join ' | '
    Check 'no warning about changed rows' ($man.capture.changes -eq 'on') $warn

    $events = @(Get-Content -Encoding UTF8 (Join-Path $rec 'events.jsonl') | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    $trace = @($events | Where-Object { $_.via -eq 'trace' })
    $tx = @($events | Where-Object { $_.kind -eq 'transaction' })
    Check 'statement trace: the batch with literals' (@($trace | Where-Object { $_.kind -eq 'statement' -and $_.sql -like "INSERT INTO dbo.customer*Harbour Books*" }).Count -eq 1)
    Check 'statement trace: calls with parameters (RPC)' (@($trace | Where-Object { $_.kind -eq 'execute' -and $_.sql -like '*@n*' }).Count -eq 2)
    Check 'statement trace: user, T-SQL database and application of the client' (@($trace | Where-Object { $_.user -eq $TdsUser -and $_.database -eq 'shopdb' -and $_.application -eq 'legacy-client' }).Count -eq $trace.Count -and $trace.Count -ge 5) "events: $($trace.Count)"
    Check 'the failing statement is in the trace and, with a log file, the error in the log' (@($trace | Where-Object { $_.sql -like '*no_such_table*' }).Count -eq 1 -and (-not $LogFile -or @($events | Where-Object { $_.kind -eq 'error' -and $_.error -like '*no_such_table*' }).Count -ge 1))
    $rows = @($tx | ForEach-Object { $_.rows })
    Check 'changed rows: insert, update and delete of the customer table' ((@($rows | Where-Object { $_.op -eq 'insert' }).Count -eq 2) -and (@($rows | Where-Object { $_.op -eq 'update' }).Count -ge 1) -and (@($rows | Where-Object { $_.op -eq 'delete' }).Count -eq 1)) "rows: $($rows.Count)"
    Check 'changed rows carry the values' (@($rows | Where-Object { $_.op -eq 'insert' -and $_.new.name -eq 'Harbour Books' -and $_.new.city -eq 'Rostock' }).Count -eq 1 -and @($rows | Where-Object { $_.op -eq 'update' -and $_.new.city -eq 'Graz' }).Count -ge 1)
    Check 'the table is the physical one (schema = database_schema)' (@($rows | Where-Object { $_.table -eq 'shopdb_dbo.customer' }).Count -eq $rows.Count)

    $o2 = & $exe correlate e2e --project $proj 2>&1 | Out-String
    Check 'correlate' ($LASTEXITCODE -eq 0) $o2
    $tl = Get-Content -Raw -Encoding UTF8 (Join-Path $rec 'timeline.json') | ConvertFrom-Json
    $chap = @($tl.chapters)
    Check 'time line: three chapters' ($chap.Count -eq 3 -and $chap[0].caption -eq 'New customer') "chapters: $($chap.Count)"
    function Count-In($c, [string]$what) { $n = 0; foreach ($a in @($c.actions)) { $n += @($a.$what).Count }; return $n }
    Check 'time line: statements and transactions under their chapters' ((Count-In $chap[0] 'statements') -ge 2 -and (Count-In $chap[0] 'transactions') -ge 2 -and (Count-In $chap[1] 'transactions') -ge 1 -and (Count-In $chap[2] 'transactions') -ge 1) "statements $((Count-In $chap[0] 'statements'))/$((Count-In $chap[1] 'statements'))/$((Count-In $chap[2] 'statements')), transactions $((Count-In $chap[0] 'transactions'))/$((Count-In $chap[1] 'transactions'))/$((Count-In $chap[2] 'transactions'))"
    $att = @($tl.attribution)
    Check 'every transaction is tied to a statement by time and table' (@($att | Where-Object { $_.method -eq 'time+table' -and @($_.statements).Count -ge 1 }).Count -eq $att.Count -and $att.Count -eq $tx.Count) "ties: $($att.Count)"

    $o3 = & $exe export-sql e2e --project $proj --json 2>&1 | Out-String
    Check 'export-sql' ($LASTEXITCODE -eq 0) $o3
    $tsv = @(Get-Content -Encoding UTF8 (Join-Path $rec 'sql.tsv'))
    $kinds = @($tsv | Select-Object -Skip 1 | ForEach-Object { ($_ -split "`t")[6] })
    Check 'sql.tsv: tsql-batch, tsql-rpc and error rows' ($tsv[0] -eq "offset`ttime`tpid`tuser`tdatabase`tapplication`tkind`tstatement" -and ($kinds -contains 'tsql-batch') -and ($kinds -contains 'tsql-rpc') -and ($kinds -contains 'error')) ($kinds -join ',')

    $o4 = & $exe spec e2e --project $proj 2>&1 | Out-String
    Check 'spec: the customer table is written by three steps' ($LASTEXITCODE -eq 0 -and (Get-Content -Raw -Encoding UTF8 (Join-Path $proj 'spec\spec.md')) -match 'shopdb_dbo\.customer') $o4
    $o5 = & $exe reset --project $proj 2>&1 | Out-String
    $clean = ($LASTEXITCODE -eq 0)
    if ($havePsql) { $clean = $clean -and (Psql "SELECT count(*) FROM pg_replication_slots").Trim() -eq '0' -and (Psql 'SELECT count(*) FROM pg_publication' $Database).Trim() -eq '0' -and (Psql "SELECT count(*) FROM pg_db_role_setting WHERE array_to_string(setconfig, ',') LIKE '%log_statement%'").Trim() -eq '0' }
    Check 'reset leaves nothing behind (no slot, no publication, no log_statement setting)' $clean $o5
} catch {
    Check 'no unexpected error' $false ($_ | Out-String)
} finally {
    Remove-Item Env:EQLMAKER_PGPASSWORD -ErrorAction SilentlyContinue
    if (-not $Keep) { try { Remove-Item -Recurse -Force $Work } catch { Write-Host "note: could not remove $Work" } }
}
$line = "$script:pass passed, $script:fail failed"
Write-Host $line
if ($script:fail) { exit 1 }
