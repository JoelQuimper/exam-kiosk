[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Stopping an exam through the MDM Bridge must run as LocalSystem.'
}

$assignedAccess = Get-CimInstance `
    -Namespace 'root\cimv2\mdm\dmmap' `
    -ClassName 'MDM_AssignedAccess'
$assignedAccess.Configuration = $null
Set-CimInstance -CimInstance $assignedAccess | Out-Null

Write-Output 'Exam mode stopped: Assigned Access configuration removed successfully.'