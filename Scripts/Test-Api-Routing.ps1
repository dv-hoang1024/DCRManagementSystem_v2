param(
    [string]$LocalUrl = "http://172.168.8.183:5080",
    [string]$RemoteUrl = "https://dcr.ggpcontrol.cloud",
    [int]$TimeoutSeconds = 5
)

function Test-DcrApi([string]$Name, [string]$BaseUrl) {
    try {
        $uri = $BaseUrl.TrimEnd('/') + "/api/health"
        $response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec $TimeoutSeconds
        [pscustomobject]@{
            Endpoint = $Name
            Url = $BaseUrl
            Reachable = ($response.status -eq 'ok' -and $response.database -eq 'connected')
            Database = $response.database
            ServerTimeUtc = $response.serverTimeUtc
            Error = ''
        }
    }
    catch {
        [pscustomobject]@{
            Endpoint = $Name
            Url = $BaseUrl
            Reachable = $false
            Database = ''
            ServerTimeUtc = ''
            Error = $_.Exception.Message
        }
    }
}

$local = Test-DcrApi "Local API" $LocalUrl
$remote = Test-DcrApi "Cloudflare API" $RemoteUrl
$local
$remote

if ($local.Reachable) {
    Write-Host "Selected by DCR client: Local API" -ForegroundColor Green
}
elseif ($remote.Reachable) {
    Write-Host "Selected by DCR client: Cloudflare API" -ForegroundColor Yellow
}
else {
    Write-Host "Neither endpoint is currently available." -ForegroundColor Red
    exit 1
}
