@echo off
setlocal enabledelayedexpansion
rem ---------------------------------------------------------------------------
rem  build-msi.cmd - convenience wrapper around build-msi.ps1.
rem
rem  Usage:
rem      build-msi.cmd [app^|mini] [x64] [selfcontained]
rem
rem  Examples:
rem      build-msi.cmd x64
rem      build-msi.cmd mini x64
rem
rem  Defaults to App, x64, framework-dependent.
rem ---------------------------------------------------------------------------

set "PRODUCT=App"
set "ARCH=x64"
set "SC="

for %%A in (%*) do (
    if /I "%%~A"=="app" set "PRODUCT=App"
    if /I "%%~A"=="mini" set "PRODUCT=Mini"
    if /I "%%~A"=="x64" set "ARCH=x64"
    if /I "%%~A"=="x86" (
        echo x86 packages are no longer accepted. Use x64.
        exit /b 2
    )
    if /I "%%~A"=="selfcontained" set "SC=-SelfContained"
    if /I "%%~A"=="-selfcontained" set "SC=-SelfContained"
    if /I "%%~A"=="sc" set "SC=-SelfContained"
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-msi.ps1" -Product %PRODUCT% -Arch %ARCH% %SC%
exit /b %ERRORLEVEL%
