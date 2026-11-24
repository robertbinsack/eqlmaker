@echo off
rem SPDX-License-Identifier: AGPL-3.0-or-later
rem Copyright (c) 2026 Robert Binsack
rem
rem Runs npm of the Node.js inside this package in the current folder, with the package cache
rem inside the package folder (downloads\npm-cache). Example, in the folder of a generated web
rem application:   <package>\eqlmaker-npm.cmd ci --offline
rem PATH is changed for this call only.
setlocal DisableDelayedExpansion
if exist "%~dp0tools\node\npm.cmd" goto :run
echo Node.js is not installed in this package yet. Run eqlmaker-setup.cmd first.
exit /b 1
:run
set "PATH=%~dp0tools\node;%PATH%"
set "npm_config_cache=%~dp0downloads\npm-cache"
set "npm_config_update_notifier=false"
set "npm_config_fund=false"
set "npm_config_audit=false"
call "%~dp0tools\node\npm.cmd" %*
exit /b %ERRORLEVEL%
