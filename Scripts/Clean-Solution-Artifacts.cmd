@echo off
setlocal
set ROOT=%~dp0..

echo Cleaning bin/obj/.vs artifacts...
if exist "%ROOT%\.vs" rmdir /s /q "%ROOT%\.vs"
if exist "%ROOT%\DCRManagementSystem\bin" rmdir /s /q "%ROOT%\DCRManagementSystem\bin"
if exist "%ROOT%\DCRManagementSystem\obj" rmdir /s /q "%ROOT%\DCRManagementSystem\obj"
if exist "%ROOT%\DCRManagementSystem.Api\bin" rmdir /s /q "%ROOT%\DCRManagementSystem.Api\bin"
if exist "%ROOT%\DCRManagementSystem.Api\obj" rmdir /s /q "%ROOT%\DCRManagementSystem.Api\obj"

echo Done.
echo Open DCRManagementSystem.sln again, then Rebuild Solution.
endlocal
