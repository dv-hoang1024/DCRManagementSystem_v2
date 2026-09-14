$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$persistentRoot = Join-Path $env:LOCALAPPDATA "DCRManagementSystem\Data"
$keyTarget = Join-Path $persistentRoot "Security\approval-signing.key"
$dbTarget = Join-Path $persistentRoot "Config\database.config.json"

function Find-FirstFile([string]$Name, [string]$RequiredSuffix) {
    $bin = Join-Path $projectRoot "bin"
    if (-not (Test-Path $bin)) { return $null }
    return Get-ChildItem -Path $bin -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName.EndsWith($RequiredSuffix, [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object @{ Expression = { if ($_.FullName -match "[\\/]Debug[\\/]") { 0 } else { 1 } } }, LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not (Test-Path $keyTarget)) {
    $sourceKey = Find-FirstFile "approval-signing.key" "Data\Security\approval-signing.key"
    if ($sourceKey) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $keyTarget) -Force | Out-Null
        Copy-Item -LiteralPath $sourceKey -Destination $keyTarget -Force
        Write-Host "Đã migrate signing key: $sourceKey -> $keyTarget" -ForegroundColor Green
    }
    else {
        Write-Warning "Không tìm thấy signing key cũ trong bin. Nếu Debug hiện tại vẫn chạy được, hãy chạy Debug một lần rồi chạy lại script trước khi Clean bin."
    }
}
else {
    Write-Host "Signing key ổn định đã tồn tại: $keyTarget" -ForegroundColor Cyan
}

if (-not (Test-Path $dbTarget)) {
    $sourceDb = Find-FirstFile "database.config.json" "Data\Config\database.config.json"
    if ($sourceDb) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $dbTarget) -Force | Out-Null
        Copy-Item -LiteralPath $sourceDb -Destination $dbTarget -Force
        Write-Host "Đã migrate SQL config: $sourceDb -> $dbTarget" -ForegroundColor Green
    }
}

Write-Host "Hoàn tất migration dữ liệu Debug/Release cục bộ." -ForegroundColor Green
Write-Host "Lưu ý: script KHÔNG migrate tài khoản đăng nhập/Windows SSO. Mỗi Windows profile phải đăng nhập DCR bằng username/password lần đầu và tự chọn ghi nhớ Windows/AD." -ForegroundColor Yellow
