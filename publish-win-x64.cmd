@echo off
setlocal
cd /d "%~dp0"
if not exist publish mkdir publish
dotnet restore DCRManagementSystem.sln
if errorlevel 1 exit /b %errorlevel%
dotnet publish DCRManagementSystem\DCRManagementSystem.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
exit /b %errorlevel%
