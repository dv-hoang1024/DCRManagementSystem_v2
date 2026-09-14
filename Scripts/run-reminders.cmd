@echo off
setlocal
set "APP=%~dp0..\DCRManagementSystem\bin\Release\net8.0-windows\win-x64\publish\DCRManagementSystem.exe"
if not exist "%APP%" set "APP=%~dp0..\DCRManagementSystem\bin\Release\net8.0-windows\DCRManagementSystem.exe"
if not exist "%APP%" (
  echo DCRManagementSystem.exe not found.
  echo Publish/build the application first, then update APP in this script if needed.
  exit /b 1
)
"%APP%" --run-reminders
exit /b %ERRORLEVEL%
