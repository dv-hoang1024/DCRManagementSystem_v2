@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-DCR.ps1"
if errorlevel 1 (
  echo.
  echo Publish that bai. Xem loi phia tren.
  pause
  exit /b 1
)
echo.
echo Publish thanh cong.
pause
