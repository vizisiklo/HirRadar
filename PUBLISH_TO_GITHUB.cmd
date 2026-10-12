@echo off
setlocal
cd /d "%~dp0"
echo HIRRADAR v1.0.2 - GITHUB PRIVATE REPOSITORY PUBLISH
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-github.ps1"
set "EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%EXIT_CODE%"=="0" (
  echo FELTOLTES NEM FEJEZODOTT BE. Ellenorizd a fenti hibat!
) else (
  echo FELTOLTES BEFEJEZODOTT.
)
pause
exit /b %EXIT_CODE%
