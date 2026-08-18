[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Exam startup customization must run as LocalSystem.'
}

$testKeyPath = 'HKLM:\SOFTWARE\ExamKiosk\CustomizationTest'
if (-not (Test-Path -LiteralPath $testKeyPath)) {
    New-Item -Path $testKeyPath -Force | Out-Null
}

Set-ItemProperty `
    -LiteralPath $testKeyPath `
    -Name 'StartupHookRan' `
    -Value ([DateTime]::UtcNow.ToString('O')) `
    -Type String `
    -Force

Write-Output "Exam startup customization ran as $([Security.Principal.WindowsIdentity]::GetCurrent().Name)."
