@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Open-Mail-Server-Config-On-ApiServer.ps1"
set ERR=%ERRORLEVEL%
if not "%ERR%"=="0" (
  echo.
  echo Khong mo duoc DCR Mail Server Console. ErrorLevel=%ERR%
  pause
)
endlocal & exit /b %ERR%
