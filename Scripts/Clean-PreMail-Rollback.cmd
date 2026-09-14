@echo off
setlocal
cd /d "%~dp0.."

echo Cleaning stale build output and post-rollback mail experiment files...
if exist ".vs" rmdir /s /q ".vs"
if exist "DCRManagementSystem\bin" rmdir /s /q "DCRManagementSystem\bin"
if exist "DCRManagementSystem\obj" rmdir /s /q "DCRManagementSystem\obj"
if exist "DCRManagementSystem.Api\bin" rmdir /s /q "DCRManagementSystem.Api\bin"
if exist "DCRManagementSystem.Api\obj" rmdir /s /q "DCRManagementSystem.Api\obj"
if exist "DCRManagementSystem\Services\GraphApplicationMailService.cs" del /f /q "DCRManagementSystem\Services\GraphApplicationMailService.cs"
if exist "DCRManagementSystem\PublishOutput" rmdir /s /q "DCRManagementSystem\PublishOutput"
echo Done.
endlocal
