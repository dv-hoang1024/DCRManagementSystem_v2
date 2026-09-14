param(
    [string]$Url = "https://dcr.ggpcontrol.cloud/api/health"
)

$ErrorActionPreference = "Stop"
Write-Host "Testing $Url ..."
$result = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 20
$result | Format-List
if ($result.status -ne "ok") {
    throw "DCR API returned unexpected status: $($result.status)"
}
Write-Host "DCR API is reachable and SQL health check succeeded." -ForegroundColor Green
