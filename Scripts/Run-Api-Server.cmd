@echo off
setlocal
set "API_DIR=%~dp0..\Publish\ApiServer"
if not exist "%API_DIR%\DCRManagementSystem.Api.exe" (
  echo DCRManagementSystem.Api.exe not found.
  echo Run Scripts\Publish-Api-Server.cmd first.
  pause
  exit /b 1
)
start "" /D "%API_DIR%" "%API_DIR%\DCRManagementSystem.Api.exe"
endlocal
exit /b 0
