#Requires -RunAsAdministrator
param(
    [int]$Port = 5080
)

$ruleName = "DCR Management System Local API"
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
if ($existing) {
    Set-NetFirewallRule -DisplayName $ruleName -Enabled True -Profile Domain,Private -Action Allow
    Get-NetFirewallPortFilter -AssociatedNetFirewallRule $existing | Set-NetFirewallPortFilter -Protocol TCP -LocalPort $Port
    Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $existing | Set-NetFirewallAddressFilter -RemoteAddress LocalSubnet
} else {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Domain,Private -RemoteAddress LocalSubnet | Out-Null
}

Write-Host "DCR Local API firewall rule is enabled on TCP $Port for LocalSubnet only." -ForegroundColor Green
