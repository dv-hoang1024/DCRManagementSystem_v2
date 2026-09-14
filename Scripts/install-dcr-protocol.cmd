@echo off
setlocal EnableExtensions

if "%~1"=="" (
  set "APP=%~dp0..\DCRManagementSystem\bin\Release\net8.0-windows\win-x64\publish\DCRManagementSystem.exe"
) else (
  set "APP=%~1"
)

if not exist "%APP%" (
  echo Application executable not found:
  echo %APP%
  echo.
  echo Usage: install-dcr-protocol.cmd "C:\Path\DCRManagementSystem.exe"
  exit /b 1
)

reg add "HKCU\Software\Classes\dcr" /ve /d "URL:DCR Management System" /f >nul
reg add "HKCU\Software\Classes\dcr" /v "URL Protocol" /d "" /f >nul
reg add "HKCU\Software\Classes\dcr\DefaultIcon" /ve /d "\"%APP%\",0" /f >nul
reg add "HKCU\Software\Classes\dcr\shell\open\command" /ve /d "\"%APP%\" \"%%1\"" /f >nul

if errorlevel 1 (
  echo Failed to register dcr:// protocol.
  exit /b 1
)

echo Registered dcr:// protocol for:
echo %APP%
exit /b 0
