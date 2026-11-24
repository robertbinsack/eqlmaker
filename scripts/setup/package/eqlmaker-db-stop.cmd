@echo off
rem SPDX-License-Identifier: AGPL-3.0-or-later
rem Copyright (c) 2026 Robert Binsack
rem
rem Stops the portable PostgreSQL of this package. Exit code 0: the server is stopped.
setlocal DisableDelayedExpansion
rem the short name (8.3) of the folder is used: PostgreSQL cannot work below a path with characters outside ASCII
cd /d "%~dps0"
if exist "data\pg\PG_VERSION" goto :have_cluster
echo There is no portable database in this package.
exit /b 0
:have_cluster
set "LC_MESSAGES=C"
"tools\pgsql\bin\pg_ctl.exe" -D "data\pg" status >nul 2>&1
if errorlevel 1 goto :stopped
"tools\pgsql\bin\pg_ctl.exe" -D "data\pg" -m fast -w -t 60 stop
if errorlevel 1 exit /b 1
:stopped
echo The portable PostgreSQL is stopped.
exit /b 0
