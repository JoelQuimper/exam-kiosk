[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ConfigurationPath
)

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Starting an exam through the MDM Bridge must run as LocalSystem.'
}

[xml]$configuration = Get-Content -LiteralPath $ConfigurationPath -Raw
$assignedAccess = Get-CimInstance `
    -Namespace 'root\cimv2\mdm\dmmap' `
    -ClassName 'MDM_AssignedAccess'
$assignedAccess.Configuration = [System.Net.WebUtility]::HtmlEncode($configuration.OuterXml)
Set-CimInstance -CimInstance $assignedAccess | Out-Null

Write-Output 'Exam mode started: Assigned Access configuration applied successfully.'