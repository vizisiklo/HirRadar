@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if errorlevel 1 (
    echo HIBA: Nem lehet a projekt mappajaba lepni.
    pause
    exit /b 1
)
title HirRadar v1.0.2 - Release build
cls
echo ============================================================
echo  HirRadar v1.0.2 - Windows x64 self-contained Release build
 echo ============================================================
echo.
echo A buildhez .NET 8 SDK szukseges, a celgepre nem kell .NET.
echo A hibakat a BUILD_RELEASE.log fajlba naplozzuk.
echo.
if not exist "%~dp0HirRadar.csproj" (
    echo HIBA: Nem talalhato HirRadar.csproj ebben a mappaban.
    echo Csak a TELJESEN kicsomagolt projektbol futtasd a CMD fajlt.
    pause
    exit /b 1
)
if not exist "%~dp0build.ps1" (
    echo HIBA: Nem talalhato build.ps1 ebben a mappaban.
    pause
    exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
set "BUILD_EXIT=%ERRORLEVEL%"
echo.
if not "%BUILD_EXIT%"=="0" (
    echo ============================================================
    echo HIBA: A Release build nem sikerult, kod: %BUILD_EXIT%
    echo A reszletek a BUILD_RELEASE.log fajlban is megtalalhatok.
    echo ============================================================
) else (
    echo ============================================================
    echo KESZ: publish\HirRadar.exe es dist\HirRadar_v1.0.2_Windows_x64.zip
    echo ============================================================
)
echo.
echo Az ablakot az ENTER megnyomasaval zarhatod be.
pause >nul
exit /b %BUILD_EXIT%
