@echo off
rem SPDX-License-Identifier: AGPL-3.0-or-later
rem Copyright (c) 2026 Robert Binsack
rem
rem Starts EQLMaker of this package with the arguments given. PATH is not changed.
"%~dp0bin\eqlmaker.exe" %*
exit /b %ERRORLEVEL%
