@echo off
setlocal
set ROOT=%~dp0..
set OUT=%ROOT%\Publish\RemoteClient

if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

dotnet publish "%ROOT%\DCRManagementSystem\DCRManagementSystem.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -p:DcrProvisionDirectSqlRuntime=false ^
  -o "%OUT%"

if errorlevel 1 exit /b %errorlevel%

echo.
echo Remote client published to:
echo %OUT%
echo.
echo DataAccessMode is RemoteApi with automatic Local API -> Cloudflare API selection. SQL credentials/signing key are NOT provisioned into this client package.
endlocal
