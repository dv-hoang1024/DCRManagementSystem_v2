@echo off
setlocal
cd /d "%~dp0"
dotnet restore DCRManagementSystem.sln
if errorlevel 1 exit /b %errorlevel%
dotnet build DCRManagementSystem.sln -c Release --no-restore
exit /b %errorlevel%
