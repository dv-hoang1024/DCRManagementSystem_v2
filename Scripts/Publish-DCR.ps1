param(
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "DCRManagementSystem.csproj"
if (-not (Test-Path $projectFile)) {
    throw "Không tìm thấy DCRManagementSystem.csproj tại $projectRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $projectRoot "PublishOutput"
}

$publishDir = Join-Path $OutputRoot $Runtime
$packageName = "DCRManagementSystem-$Runtime.zip"
$packagePath = Join-Path $OutputRoot $packageName
$provisioningDir = Join-Path $publishDir "Provisioning"

function Get-ValidSigningKey([string[]]$Candidates) {
    foreach ($candidate in $Candidates | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path $candidate)) { continue }
        try {
            $text = (Get-Content -LiteralPath $candidate -Raw).Trim()
            $bytes = [Convert]::FromBase64String($text)
            if ($bytes.Length -ge 32) { return (Resolve-Path $candidate).Path }
        }
        catch { }
    }
    return $null
}

$localAppDataKey = Join-Path $env:LOCALAPPDATA "DCRManagementSystem\Data\Security\approval-signing.key"
$legacyDebugKey = Join-Path $projectRoot "bin\Debug\net8.0-windows\Data\Security\approval-signing.key"
$legacyReleaseKey = Join-Path $projectRoot "bin\Release\net8.0-windows\Data\Security\approval-signing.key"
$sourceKey = Join-Path $projectRoot "Data\Security\approval-signing.key"

$recursiveKeys = @()
$binRoot = Join-Path $projectRoot "bin"
if (Test-Path $binRoot) {
    $recursiveKeys = Get-ChildItem -Path $binRoot -Filter "approval-signing.key" -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "[\\/]Data[\\/]Security[\\/]approval-signing\.key$" } |
        Select-Object -ExpandProperty FullName
}

$allKeyCandidates = @($localAppDataKey, $legacyDebugKey, $legacyReleaseKey, $sourceKey) + $recursiveKeys
$signingKey = Get-ValidSigningKey $allKeyCandidates
if (-not $signingKey) {
    throw @"
Không tìm thấy approval-signing.key hiện đang dùng.

Hãy chạy bản Debug đang hoạt động thành công ít nhất một lần, sau đó chạy lại script này.
KHÔNG tự tạo key mới và KHÔNG xóa ApprovalSigningKeyFingerprintSha256 trong database.
"@
}

$dbCandidates = @(
    (Join-Path $env:LOCALAPPDATA "DCRManagementSystem\Data\Config\database.config.json"),
    (Join-Path $projectRoot "bin\Debug\net8.0-windows\Data\Config\database.config.json"),
    (Join-Path $projectRoot "bin\Release\net8.0-windows\Data\Config\database.config.json")
)
$dbConfig = $dbCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

Write-Host "[1/5] Signing key: $signingKey" -ForegroundColor Cyan
Write-Host "[2/5] Xóa output cũ..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "[3/5] Publish Release self-contained $Runtime..." -ForegroundColor Cyan
$publishArgs = @(
    "publish", $projectFile,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:DcrSigningKeySource=$signingKey",
    "-o", $publishDir
)
if ($dbConfig) { $publishArgs += "-p:DcrDatabaseConfigSource=$dbConfig" }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish thất bại với exit code $LASTEXITCODE" }

Write-Host "[4/5] Gói cấu hình provisioning..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $provisioningDir -Force | Out-Null
Copy-Item -LiteralPath $signingKey -Destination (Join-Path $provisioningDir "approval-signing.key") -Force

if ($dbConfig) {
    Copy-Item -LiteralPath $dbConfig -Destination (Join-Path $provisioningDir "database.config.json") -Force
    Write-Host "      Đã kèm database.config.json hiện tại." -ForegroundColor DarkGray
}
else {
    Write-Host "      Không có database.config.json cục bộ; appsettings.json sẽ được dùng." -ForegroundColor DarkGray
}

$readme = @"
DCR Management System - $Runtime

1. Giải nén toàn bộ thư mục trước khi chạy.
2. Chạy DCRManagementSystem.exe.
3. Không xóa thư mục Provisioning. Nó chứa signing key dùng để đồng bộ chữ ký approval khi cài máy mới.
4. Dữ liệu SQL/DCR nằm trên SQL Server và không bị mất khi cập nhật chương trình.
5. Cấu hình cục bộ, draft recovery, token Graph và ngôn ngữ được giữ ở %LOCALAPPDATA%\DCRManagementSystem.
6. File ghi nhớ đăng nhập Windows/AD KHÔNG được đóng gói/provision. Mỗi Windows user trên mỗi máy phải đăng nhập DCR bằng username/password ít nhất một lần rồi chọn ghi nhớ Windows/AD.
"@
Set-Content -Path (Join-Path $publishDir "DEPLOYMENT_README.txt") -Value $readme -Encoding UTF8

if (-not $NoZip) {
    Write-Host "[5/5] Tạo ZIP triển khai..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
    if (Test-Path $packagePath) { Remove-Item $packagePath -Force }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $packagePath -CompressionLevel Optimal
    Write-Host "PACKAGE: $packagePath" -ForegroundColor Green
}
else {
    Write-Host "[5/5] Bỏ qua ZIP theo -NoZip." -ForegroundColor Cyan
}

Write-Host "PUBLISH FOLDER: $publishDir" -ForegroundColor Green
