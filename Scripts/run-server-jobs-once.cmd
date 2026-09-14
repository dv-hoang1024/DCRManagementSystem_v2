@echo off
setlocal
cd /d "%~dp0.."
set "EXE=DCRManagementSystem\bin\Release\net8.0-windows\DCRManagementSystem.exe"
if not exist "%EXE%" set "EXE=DCRManagementSystem\bin\Debug\net8.0-windows\DCRManagementSystem.exe"
if not exist "%EXE%" (
  echo Khong tim thay DCRManagementSystem.exe. Hay build/publish project truoc.
  pause
  exit /b 1
)
"%EXE%" --run-server-jobs
endlocal
