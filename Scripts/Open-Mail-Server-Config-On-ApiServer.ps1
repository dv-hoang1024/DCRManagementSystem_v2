$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Find-FirstExistingFile([string[]]$Candidates) {
    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $full = [System.IO.Path]::GetFullPath($candidate)
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            return $full
        }
    }
    return $null
}

function Escape-ConnectionStringValue([object]$Value) {
    $text = [string]$Value
    return '"' + $text.Replace('"', '""') + '"'
}

$ClientExe = Find-FirstExistingFile @(
    (Join-Path $Root "Publish\RemoteClient\DCRManagementSystem.exe"),
    (Join-Path $Root "DCRManagementSystem\bin\Release\net8.0-windows\DCRManagementSystem.exe"),
    (Join-Path $Root "DCRManagementSystem\bin\Debug\net8.0-windows\DCRManagementSystem.exe"),
    (Join-Path $Root "DCRManagementSystem\bin\Release\net8.0-windows\win-x64\DCRManagementSystem.exe"),
    (Join-Path $Root "DCRManagementSystem\bin\Debug\net8.0-windows\win-x64\DCRManagementSystem.exe"),
    (Join-Path $Root "DCRManagementSystem\bin\Release\net8.0-windows\win-x64\publish\DCRManagementSystem.exe"),
    (Join-Path $Root "DCRManagementSystem\bin\Debug\net8.0-windows\win-x64\publish\DCRManagementSystem.exe")
)

if (-not $ClientExe) {
    Write-Host ""
    Write-Host "Khong tim thay DCRManagementSystem.exe." -ForegroundColor Red
    Write-Host "Hay build project DCRManagementSystem hoac chay:" -ForegroundColor Yellow
    Write-Host "  Scripts\Publish-Remote-Client.cmd" -ForegroundColor Cyan
    Write-Host ""
    Read-Host "Nhan Enter de dong"
    exit 1
}

$ApiSettings = Find-FirstExistingFile @(
    (Join-Path $Root "Publish\ApiServer\api.appsettings.json"),
    (Join-Path $Root "DCRManagementSystem.Api\api.appsettings.json")
)

if (-not $ApiSettings) {
    Write-Host ""
    Write-Host "Khong tim thay api.appsettings.json cua DCR API." -ForegroundColor Red
    Write-Host "Da tim tai:" -ForegroundColor Yellow
    Write-Host "  Publish\ApiServer\api.appsettings.json"
    Write-Host "  DCRManagementSystem.Api\api.appsettings.json"
    Write-Host ""
    Read-Host "Nhan Enter de dong"
    exit 1
}

$config = Get-Content -LiteralPath $ApiSettings -Raw | ConvertFrom-Json

$connectionString = ""
if ($config.ConnectionString -and -not [string]::IsNullOrWhiteSpace([string]$config.ConnectionString)) {
    $connectionString = [string]$config.ConnectionString
}
elseif ($null -ne $config.Database) {
    $db = $config.Database

    if ([string]::IsNullOrWhiteSpace([string]$db.ServerAddress)) {
        throw "Database.ServerAddress trong api.appsettings.json dang trong."
    }
    if ([string]::IsNullOrWhiteSpace([string]$db.DatabaseName)) {
        throw "Database.DatabaseName trong api.appsettings.json dang trong."
    }

    $port = 0
    if ($null -ne $db.Port) { $port = [int]$db.Port }

    $dataSource = [string]$db.ServerAddress
    if ($port -gt 0) {
        $dataSource = "$dataSource,$port"
    }

    $connectTimeout = 10
    if ($null -ne $db.ConnectTimeoutSeconds) {
        $connectTimeout = [Math]::Max(3, [Math]::Min(120, [int]$db.ConnectTimeoutSeconds))
    }

    $minPool = 4
    if ($null -ne $db.MinPoolSize) {
        $minPool = [Math]::Max(0, [Math]::Min(32, [int]$db.MinPoolSize))
    }

    $maxPool = 64
    if ($null -ne $db.MaxPoolSize) {
        $maxPool = [Math]::Max(16, [Math]::Min(200, [int]$db.MaxPoolSize))
    }

    $encrypt = $true
    if ($null -ne $db.Encrypt) { $encrypt = [bool]$db.Encrypt }

    $trust = $true
    if ($null -ne $db.TrustServerCertificate) { $trust = [bool]$db.TrustServerCertificate }

    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add("Data Source=$(Escape-ConnectionStringValue $dataSource)")
    $parts.Add("Initial Catalog=$(Escape-ConnectionStringValue $db.DatabaseName)")
    $parts.Add("MultipleActiveResultSets=True")
    $parts.Add("Connect Timeout=$connectTimeout")
    $parts.Add("ConnectRetryCount=3")
    $parts.Add("ConnectRetryInterval=1")
    $parts.Add("Application Name=$(Escape-ConnectionStringValue 'DCR Management System')")
    $parts.Add("Pooling=True")
    $parts.Add("Min Pool Size=$minPool")
    $parts.Add("Max Pool Size=$maxPool")
    $parts.Add("Encrypt=$encrypt")
    $parts.Add("TrustServerCertificate=$trust")

    $authMode = [string]$db.AuthenticationMode
    if ($authMode -ieq "Windows") {
        $parts.Add("Integrated Security=True")
    }
    else {
        if ([string]::IsNullOrWhiteSpace([string]$db.Username)) {
            throw "Database.Username trong api.appsettings.json dang trong."
        }
        if ([string]::IsNullOrEmpty([string]$db.Password)) {
            throw "Database.Password trong api.appsettings.json dang trong."
        }

        $parts.Add("Integrated Security=False")
        $parts.Add("User ID=$(Escape-ConnectionStringValue $db.Username)")
        $parts.Add("Password=$(Escape-ConnectionStringValue $db.Password)")
        $parts.Add("Persist Security Info=False")
    }

    $connectionString = ($parts -join ";") + ";"
}
else {
    throw "api.appsettings.json khong co ConnectionString hoac Database."
}

$env:DCR_DATA_ACCESS_MODE = "DirectSql"
$env:DCR_CONNECTION_STRING = $connectionString

Write-Host ""
Write-Host "DCR Mail Server Console" -ForegroundColor Green
Write-Host "Client EXE : $ClientExe"
Write-Host "API config : $ApiSettings"
Write-Host "Mode       : DirectSql"
Write-Host ""
Write-Host "Mo Mail Server Console tren chinh may nay..." -ForegroundColor Cyan

try {
    & $ClientExe --mail-server-config
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:\DCR_CONNECTION_STRING -ErrorAction SilentlyContinue
    Remove-Item Env:\DCR_DATA_ACCESS_MODE -ErrorAction SilentlyContinue
}

if ($null -ne $exitCode -and $exitCode -ne 0) {
    Write-Host ""
    Write-Host "DCRManagementSystem.exe ket thuc voi ma loi $exitCode." -ForegroundColor Red
    Read-Host "Nhan Enter de dong"
    exit $exitCode
}
