@echo off
rem SPDX-License-Identifier: AGPL-3.0-or-later
rem Copyright (c) 2026 Robert Binsack
rem Builds EQLMaker with the C# compiler of the .NET Framework that is part of Windows.
rem Result: the folder bin. Options are passed on to build.ps1 (for example -Lint).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
