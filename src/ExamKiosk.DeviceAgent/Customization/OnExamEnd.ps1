[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Exam shutdown customization must run as LocalSystem.'
}

$testKeyPath = 'HKLM:\SOFTWARE\ExamKiosk\CustomizationTest'
if (Test-Path -LiteralPath $testKeyPath) {
    Remove-Item -LiteralPath $testKeyPath -Recurse -Force
}

Write-Output "Exam shutdown customization ran as $([Security.Principal.WindowsIdentity]::GetCurrent().Name)."
