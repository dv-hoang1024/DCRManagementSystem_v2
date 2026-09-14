@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Repair-Dcr-Tunnel-Config.ps1" %*
if errorlevel 1 (
  echo.
  echo Sua dcr-config.yml that bai.
  pause
  exit /b 1
)
echo.
echo Da sua dcr-config.yml. Hay Restart DCR API de khoi dong lai dcr-tunnel.
pause
