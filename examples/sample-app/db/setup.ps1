# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Creates the database of the sample application "Sample Shop": role sample_app, database
  eqlmaker_sample, tables (schema.sql) and invented data (seed.sql).
.DESCRIPTION
  Passwords are never written to a file. The password of sample_app comes from -AppPassword or
  from the environment variable SAMPLE_APP_PASSWORD; the password of the admin user comes
  from -AdminPassword, from PGPASSWORD or from the password file of PostgreSQL.
  Without -Recreate an existing database is left as it is. With -Recreate the database is
  dropped and built again, so that every run ends in the same state. Needs psql 15 or newer.
.EXAMPLE
  $env:SAMPLE_APP_PASSWORD = Read-Host 'password for sample_app'
  .\setup.ps1 -Psql 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -AdminPort 5432 -Recreate
#>
param(
    [Parameter(Mandatory = $true)][string]$Psql,
    [string]$AdminHost = '127.0.0.1',
    [int]$AdminPort = 5432,
    [string]$AdminUser = 'postgres',
    [string]$AdminPassword,
    [string]$AppPassword,
    [switch]$Recreate
)
$ErrorActionPreference = 'Stop'
$database = 'eqlmaker_sample'
$role = 'sample_app'

if (-not (Test-Path -LiteralPath $Psql)) { throw "psql not found: $Psql" }
if ($AppPassword) { $env:SAMPLE_APP_PASSWORD = $AppPassword }
if (-not $env:SAMPLE_APP_PASSWORD) { throw 'the password of sample_app is missing: give -AppPassword or set SAMPLE_APP_PASSWORD' }

$oldPassword = $env:PGPASSWORD
$oldEncoding = $env:PGCLIENTENCODING
if ($AdminPassword) { $env:PGPASSWORD = $AdminPassword }
$env:PGCLIENTENCODING = 'UTF8'

function Invoke-Psql([string]$Db, [string[]]$More, [string]$Script) {
    $a = @('-X', '-q', '-v', 'ON_ERROR_STOP=1', '-h', $AdminHost, '-p', "$AdminPort", '-U', $AdminUser, '-d', $Db) + $More
    if ($Script) { $out = $Script | & $Psql @a } else { $out = & $Psql @a }
    if ($LASTEXITCODE -ne 0) { throw "psql failed with exit code $LASTEXITCODE" }
    return $out
}

try {
    # role: created when missing; the password is read by psql from the environment
    $roleScript = @"
\getenv pw SAMPLE_APP_PASSWORD
SELECT 'CREATE ROLE $role LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$role') \gexec
ALTER ROLE $role LOGIN PASSWORD :'pw';
"@
    Invoke-Psql 'postgres' @('-f', '-') $roleScript | Out-Null
    Write-Host "role $role is ready"

    if ($Recreate) {
        Invoke-Psql 'postgres' @('-c', "DROP DATABASE IF EXISTS $database WITH (FORCE)") | Out-Null
    }
    $exists = Invoke-Psql 'postgres' @('-At', '-c', "SELECT 1 FROM pg_database WHERE datname = '$database'")
    if ("$exists".Trim() -eq '1') {
        Write-Host "database $database exists and was left unchanged (use -Recreate to build it again)"
    } else {
        Invoke-Psql 'postgres' @('-c', "CREATE DATABASE $database OWNER $role") | Out-Null
        # the tables belong to sample_app
        $files = @('-c', "SET ROLE $role", '-f', (Join-Path $PSScriptRoot 'schema.sql'), '-f', (Join-Path $PSScriptRoot 'seed.sql'))
        Invoke-Psql $database $files | Out-Null
        $counts = Invoke-Psql $database @('-At', '-c', 'SELECT (SELECT count(*) FROM customer), (SELECT count(*) FROM article), (SELECT count(*) FROM invoice)')
        Write-Host "database $database created: customers|articles|invoices = $counts"
    }
} finally {
    $env:PGPASSWORD = $oldPassword
    $env:PGCLIENTENCODING = $oldEncoding
}
