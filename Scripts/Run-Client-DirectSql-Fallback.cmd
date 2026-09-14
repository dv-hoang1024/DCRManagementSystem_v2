@echo off
setlocal
set DCR_DATA_ACCESS_MODE=DirectSql
if not "%~1"=="" set DCR_CONNECTION_STRING=%~1

if exist "%~dp0..\Publish\RemoteClient\DCRManagementSystem.exe" (
  "%~dp0..\Publish\RemoteClient\DCRManagementSystem.exe"
) else (
  echo Published client not found. Run Scripts\Publish-Remote-Client.cmd first.
  exit /b 1
)
endlocal
