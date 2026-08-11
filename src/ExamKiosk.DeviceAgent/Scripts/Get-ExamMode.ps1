[CmdletBinding()]
param()

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
    Write-Output 'Configured'
}