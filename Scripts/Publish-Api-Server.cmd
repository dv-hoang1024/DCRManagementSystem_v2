@echo off
setlocal
set ROOT=%~dp0..
set OUT=%ROOT%\Publish\ApiServer
set PERSIST=%LOCALAPPDATA%\DCRManagementSystem

if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

dotnet publish "%ROOT%\DCRManagementSystem.Api\DCRManagementSystem.Api.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -o "%OUT%"

if errorlevel 1 exit /b %errorlevel%

rem The WinForms project is a normal ProjectReference. Its appsettings.json can be
rem propagated as content by MSBuild, but the API intentionally uses api.appsettings.json.
rem Remove the client config from the SERVER package to avoid editing the wrong file.
if exist "%OUT%\appsettings.json" del /q "%OUT%\appsettings.json"
if not exist "%OUT%\api.appsettings.json" (
  echo ERROR: api.appsettings.json was not published.
  exit /b 1
)

rem Existing DCR databases are bound to the approval-signing key. Provision the
rem current working key into the SERVER package only. RemoteClient never receives it.
if exist "%PERSIST%\Data\Security\approval-signing.key" (
  if not exist "%OUT%\Provisioning" mkdir "%OUT%\Provisioning"
  copy /y "%PERSIST%\Data\Security\approval-signing.key" "%OUT%\Provisioning\approval-signing.key" >nul
  echo Provisioned existing approval-signing.key into API server package.
) else (
  echo WARNING: No existing approval-signing.key was found at:
  echo   %PERSIST%\Data\Security\approval-signing.key
  echo If the current SQL database already contains approved/signed DCRs, copy the
  echo exact key used by the working application before starting the API server.
)

rem Preserve the SQL connection that the currently working DirectSql client actually
rem uses. AppSettings can migrate this Provisioning file on first API-server run.
if exist "%PERSIST%\Data\Config\database.config.json" (
  if not exist "%OUT%\Provisioning" mkdir "%OUT%\Provisioning"
  copy /y "%PERSIST%\Data\Config\database.config.json" "%OUT%\Provisioning\database.config.json" >nul
  echo Provisioned current database.config.json into API server package.
)

echo.
echo API server published to:
echo %OUT%
echo.
echo IMPORTANT: This folder is SERVER-ONLY and may contain SQL configuration and the approval signing key.
echo Run DCRManagementSystem.Api.exe only on the server that can reach SQL Server and the DCR file share.
endlocal
