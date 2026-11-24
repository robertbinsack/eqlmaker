@echo off
rem SPDX-License-Identifier: AGPL-3.0-or-later
rem Copyright (c) 2026 Robert Binsack
rem
rem Starts the portable PostgreSQL of this package (no Windows service). The server gets its
rem own hidden console, so it keeps running when this window is closed; it ends when you log
rem off or run eqlmaker-db-stop.cmd. Exit code 0: the server is running.
setlocal DisableDelayedExpansion
rem the short name (8.3) of the folder is used: PostgreSQL cannot work below a path with characters outside ASCII
cd /d "%~dps0"
if exist "data\pg\PG_VERSION" goto :have_cluster
echo There is no portable database in this package. Run eqlmaker-setup.cmd first.
exit /b 1
:have_cluster
if exist "tools\pgsql\bin\pg_ctl.exe" goto :have_programs
echo tools\pgsql is missing. Run eqlmaker-setup.cmd first.
exit /b 1
:have_programs
set "LC_MESSAGES=C"
"tools\pgsql\bin\pg_ctl.exe" -D "data\pg" status >nul 2>&1
if not errorlevel 1 goto :running
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "$p = Start-Process -FilePath 'tools\pgsql\bin\pg_ctl.exe' -ArgumentList '-D data\pg -l data\pg-start.log -w -t 90 start' -WindowStyle Hidden -PassThru; $null = $p.Handle; if (-not $p.WaitForExit(120000)) { exit 1 }; exit $p.ExitCode"
if not errorlevel 1 goto :running
echo The portable PostgreSQL did not start. See data\pg-start.log and data\pg\log.
exit /b 1
:running
set /p EQLMAKER_PGPORT=<"data\pg-port.txt"
echo The portable PostgreSQL is running on 127.0.0.1:%EQLMAKER_PGPORT%
exit /b 0
