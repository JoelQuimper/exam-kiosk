[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EdgePolicyBackupPath
)

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Stopping an exam through the MDM Bridge must run as LocalSystem.'
}

. (Join-Path $PSScriptRoot 'EdgePolicy.ps1')

$assignedAccess = Get-CimInstance `
    -Namespace 'root\cimv2\mdm\dmmap' `
    -ClassName 'MDM_AssignedAccess'
$assignedAccess.Configuration = $null
Set-CimInstance -CimInstance $assignedAccess | Out-Null

$edgePolicyRoot = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
Restore-ExamEdgePolicyBackup `
    -BackupPath $EdgePolicyBackupPath `
    -PolicyRoot $edgePolicyRoot

$shortcutRoot = Join-Path `
    $env:ProgramData `
    'Microsoft\Windows\Start Menu\Programs\Exam Kiosk\Tools'
$removedShortcutCount = 0
if (Test-Path -LiteralPath $shortcutRoot -PathType Container) {
    $shortcuts = @(
        Get-ChildItem `
            -LiteralPath $shortcutRoot `
            -Filter '*.lnk' `
            -File
    )
    foreach ($shortcut in $shortcuts) {
        Remove-Item -LiteralPath $shortcut.FullName -Force
    }
    $removedShortcutCount = $shortcuts.Count

    $remaining = @(
        Get-ChildItem `
            -LiteralPath $shortcutRoot `
            -Filter '*.lnk' `
            -File
    )
    if ($remaining.Count -ne 0) {
        throw 'One or more Exam Kiosk tool shortcuts remained after cleanup.'
    }
}

Write-Output (
    'Exam mode stopped: Assigned Access removed, Edge policy restored, and {0} web shortcut(s) deleted.' -f
    $removedShortcutCount)