[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AppId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EnterpriseApplicationObjectId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SenderMailbox,

    [string]$DisplayName = "DCR Management System SMTP OAuth"
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name ExchangeOnlineManagement)) {
    throw "ExchangeOnlineManagement module is not installed. Install it with: Install-Module ExchangeOnlineManagement -Scope CurrentUser"
}

Import-Module ExchangeOnlineManagement
Connect-ExchangeOnline -ShowBanner:$false

try {
    $servicePrincipal = Get-ServicePrincipal | Where-Object { $_.AppId -eq $AppId } | Select-Object -First 1
    if (-not $servicePrincipal) {
        Write-Host "Registering Entra service principal in Exchange Online..."
        New-ServicePrincipal -AppId $AppId -ObjectId $EnterpriseApplicationObjectId -DisplayName $DisplayName | Out-Null
        $servicePrincipal = Get-ServicePrincipal | Where-Object { $_.AppId -eq $AppId } | Select-Object -First 1
    }

    if (-not $servicePrincipal) {
        throw "Exchange service principal could not be resolved after registration."
    }

    Write-Host "Exchange Service Principal: $($servicePrincipal.Identity)"
    Write-Host "Granting FullAccess to $SenderMailbox ..."
    Add-MailboxPermission -Identity $SenderMailbox -User $servicePrincipal.Identity -AccessRights FullAccess -AutoMapping:$false -Confirm:$false | Out-Null

    Write-Host "Granting SendAs to $SenderMailbox ..."
    try {
        Add-RecipientPermission -Identity $SenderMailbox -Trustee $servicePrincipal.Identity -AccessRights SendAs -Confirm:$false | Out-Null
    }
    catch {
        if ($_.Exception.Message -notmatch 'already') { throw }
    }

    Write-Host "Checking mailbox SMTP AUTH setting..."
    Get-CASMailbox -Identity $SenderMailbox | Format-List DisplayName,PrimarySmtpAddress,SmtpClientAuthenticationDisabled

    Write-Host ""
    Write-Host "Exchange-side OAuth SMTP configuration completed." -ForegroundColor Green
    Write-Host "In the DCR application use smtp.office365.com, port 587, STARTTLS and Microsoft 365 OAuth2."
    Write-Host "If SmtpClientAuthenticationDisabled is True or tenant policy blocks SMTP AUTH, ask the Exchange administrator to review policy before testing."
}
finally {
    Disconnect-ExchangeOnline -Confirm:$false
}
