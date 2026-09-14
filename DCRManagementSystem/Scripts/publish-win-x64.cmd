@echo off
setlocal
pushd "%~dp0..\DCRManagementSystem"
dotnet restore
if errorlevel 1 exit /b 1
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
set ERR=%ERRORLEVEL%
popd
exit /b %ERR%
