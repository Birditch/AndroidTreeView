@echo off
setlocal enabledelayedexpansion
rem ---------------------------------------------------------------------------
rem  build-msi.cmd - convenience wrapper around build-msi.ps1.
rem
rem  Usage:
rem      build-msi.cmd [x64^|x86] [selfcontained]
rem
rem  Examples:
rem      build-msi.cmd x64
rem      build-msi.cmd x86 selfcontained
rem
rem  Defaults to x64, framework-dependent.
rem ---------------------------------------------------------------------------

set "ARCH=%~1"
if "%ARCH%"=="" set "ARCH=x64"

set "SC="
if /I "%~2"=="selfcontained"  set "SC=-SelfContained"
if /I "%~2"=="-selfcontained" set "SC=-SelfContained"
if /I "%~2"=="sc"             set "SC=-SelfContained"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-msi.ps1" -Arch %ARCH% %SC%
exit /b %ERRORLEVEL%
