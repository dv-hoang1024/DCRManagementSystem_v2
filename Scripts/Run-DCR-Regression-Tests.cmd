@echo off
setlocal EnableExtensions
set "ROOT=%~dp0.."
set "PROJECT=%ROOT%\DCRManagementSystem.RegressionTests\DCRManagementSystem.RegressionTests.csproj"
set "RESULTS=%ROOT%\..\..\BuildLogs\DCRRegressionTests"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet SDK not found in PATH.
  exit /b 1
)

if not exist "%PROJECT%" (
  echo [ERROR] Regression test project not found:
  echo         %PROJECT%
  exit /b 1
)

if not exist "%RESULTS%" mkdir "%RESULTS%" >nul 2>&1

echo ============================================================
echo DCR Management - Automated Regression Tests
echo ============================================================
echo.
echo [1/2] Restoring test and referenced projects...
dotnet restore "%PROJECT%" --nologo
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo [2/2] Running regression tests...
dotnet test "%PROJECT%" -c Release --no-restore --nologo ^
  --logger "console;verbosity=normal" ^
  --logger "trx;LogFileName=DCRRegressionTests.trx" ^
  --results-directory "%RESULTS%"
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo DCR REGRESSION TESTS: PASS
echo Results: %RESULTS%
exit /b 0
