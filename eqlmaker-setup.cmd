@echo off
rem SPDX-License-Identifier: AGPL-3.0-or-later
rem Copyright (c) 2026 Robert Binsack
rem
rem Sets up the unpacked EQLMaker package inside this folder: downloads the pinned portable
rem components, checks them, unpacks them, creates the portable database and runs the self test.
rem No installer is run, no admin rights are needed, nothing is written outside this
rem folder. Options: eqlmaker-setup.cmd -help    Documentation: docs\setup.md
rem Exit codes: 0 ready, 1 failed, 2 self test failed, 3 offline and files are missing.
setlocal DisableDelayedExpansion
set "EQLMAKER_HOME=%~dp0"
set "EQLMAKER_SCRIPT=%~dp0setup\eqlmaker-setup.ps1"
set "EQLMAKER_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "EQLMAKER_RC=1"

rem pause at the end only when started by a double click: the command line of this command
rem processor then names this file, and there are no arguments
set "EQLMAKER_PAUSE=0"
setlocal EnableDelayedExpansion
set "EQLMAKER_CL=!cmdcmdline!"
if not "!EQLMAKER_CL:%~nx0=!"=="!EQLMAKER_CL!" (endlocal & set "EQLMAKER_PAUSE=1") else endlocal
if not "%~1"=="" set "EQLMAKER_PAUSE=0"

if exist "%EQLMAKER_SCRIPT%" goto :have_script
echo This is not an unpacked EQLMaker package: setup\eqlmaker-setup.ps1 is missing.
echo In a source checkout build the package first: scripts\pack-release.ps1
echo Dies ist kein entpacktes EQLMaker-Paket: setup\eqlmaker-setup.ps1 fehlt.
goto :finish

:have_script
if exist "%EQLMAKER_PS%" goto :have_ps
echo Windows PowerShell 5.1 was not found: %EQLMAKER_PS%
echo Windows PowerShell 5.1 wurde nicht gefunden.
goto :finish

:have_ps
if not exist "%EQLMAKER_HOME%downloads" mkdir "%EQLMAKER_HOME%downloads"
set "EQLMAKER_SETUP_MARK=%EQLMAKER_HOME%downloads\setup-started.flag"
if exist "%EQLMAKER_SETUP_MARK%" del "%EQLMAKER_SETUP_MARK%"
"%EQLMAKER_PS%" -NoProfile -ExecutionPolicy Bypass -File "%EQLMAKER_SCRIPT%" %*
set "EQLMAKER_RC=%ERRORLEVEL%"
if exist "%EQLMAKER_SETUP_MARK%" goto :ran
set "EQLMAKER_RC=1"
echo.
echo Windows PowerShell did not run the setup script. Script execution is probably blocked
echo by a policy of this computer (execution policy set by group policy, or application
echo control). Ask your IT support to allow this script, or set the package up on another
echo computer and copy the whole folder.
echo.
echo Windows PowerShell hat das Einrichtungsskript nicht ausgefuehrt. Vermutlich ist die
echo Skriptausfuehrung durch eine Richtlinie dieses Rechners gesperrt. Bitten Sie Ihren
echo IT-Betreuer um Freigabe, oder richten Sie das Paket auf einem anderen Rechner ein und
echo kopieren Sie den ganzen Ordner.
goto :finish

:ran
del "%EQLMAKER_SETUP_MARK%"

:finish
if "%EQLMAKER_PAUSE%"=="1" pause
exit /b %EQLMAKER_RC%
