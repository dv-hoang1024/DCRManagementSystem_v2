param(
    [string]$ApiExe = (Join-Path $PSScriptRoot "..\Publish\ApiServer\DCRManagementSystem.Api.exe"),
    [string]$TaskName = "DCR Management API"
)

$ErrorActionPreference = "Stop"
$ApiExe = [IO.Path]::GetFullPath($ApiExe)
if (-not (Test-Path $ApiExe)) { throw "API executable not found: $ApiExe" }

$workingDirectory = Split-Path $ApiExe -Parent
$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute $ApiExe -WorkingDirectory $workingDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
$principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName
Write-Host "Installed and started scheduled task '$TaskName' for $currentUser." -ForegroundColor Green
Write-Host "This intentionally runs under the same Windows profile so Network Share and Microsoft Graph token cache keep working."
