[CmdletBinding()]
param(
    [string]$CloudflaredExe = "C:\Cloudflare\cloudflared.exe",
    [string]$ConfigPath = "$env:USERPROFILE\.cloudflared\dcr-config.yml",
    [string]$TunnelId = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $CloudflaredExe)) {
    throw "Không tìm thấy cloudflared.exe: $CloudflaredExe"
}

$credentials = ""
if (Test-Path $ConfigPath) {
    $existing = Get-Content $ConfigPath -Raw

    if ([string]::IsNullOrWhiteSpace($TunnelId)) {
        $m = [regex]::Match($existing, '(?im)^\s*tunnel\s*:\s*["'']?([^\s"'']+)')
        if ($m.Success) { $TunnelId = $m.Groups[1].Value.Trim() }
    }

    $cm = [regex]::Match($existing, '(?im)^\s*credentials-file\s*:\s*(.+?)\s*$')
    if ($cm.Success) { $credentials = $cm.Groups[1].Value.Trim().Trim('"').Trim("'") }

    $backup = "$ConfigPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item $ConfigPath $backup -Force
    Write-Host "Đã backup config cũ: $backup" -ForegroundColor Yellow
}

if ([string]::IsNullOrWhiteSpace($TunnelId)) {
    throw "Không xác định được TunnelId. Hãy chạy lại với -TunnelId <UUID-cua-dcr-tunnel>."
}

if ([string]::IsNullOrWhiteSpace($credentials)) {
    $credentials = Join-Path $env:USERPROFILE ".cloudflared\$TunnelId.json"
}
$credentials = [Environment]::ExpandEnvironmentVariables($credentials)
if (-not (Test-Path $credentials)) {
    throw "Không tìm thấy credentials của dcr-tunnel: $credentials"
}

$dir = Split-Path $ConfigPath -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$content = @"
tunnel: $TunnelId
credentials-file: $credentials

ingress:
  - hostname: dcr.ggpcontrol.cloud
    service: http://127.0.0.1:5080
  - service: http_status:404
"@

Set-Content -Path $ConfigPath -Value $content -Encoding UTF8
Write-Host "Đã tách DCR khỏi WebDashboard và ghi lại: $ConfigPath" -ForegroundColor Green
Write-Host "dcr.ggpcontrol.cloud -> DCR API + Web Portal :5080" -ForegroundColor Green

& $CloudflaredExe tunnel --config $ConfigPath ingress validate
if ($LASTEXITCODE -ne 0) { throw "cloudflared ingress validate thất bại." }

& $CloudflaredExe tunnel --config $ConfigPath ingress rule "https://dcr.ggpcontrol.cloud/"
if ($LASTEXITCODE -ne 0) { throw "Không kiểm tra được ingress rule cho dcr.ggpcontrol.cloud." }
