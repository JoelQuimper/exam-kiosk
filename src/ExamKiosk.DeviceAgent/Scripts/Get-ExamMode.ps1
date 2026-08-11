[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\{[0-9A-Fa-f-]{36}\}$')]
    [string]$ExpectedProfileId
)

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Reading Assigned Access through the MDM Bridge must run as LocalSystem.'
}

$assignedAccess = Get-CimInstance `
    -Namespace 'root\cimv2\mdm\dmmap' `
    -ClassName 'MDM_AssignedAccess'

if ([string]::IsNullOrWhiteSpace($assignedAccess.Configuration)) {
    Write-Output 'NotConfigured'
}
else {
    $configuration = [System.Net.WebUtility]::HtmlDecode($assignedAccess.Configuration)
    if ($configuration.IndexOf($ExpectedProfileId, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        Write-Output 'Configured'
    }
    else {
        Write-Output 'ForeignConfiguration'
    }
}
