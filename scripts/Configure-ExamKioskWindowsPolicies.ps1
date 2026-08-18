#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Write-Host 'Applying the device-wide Windows policy for the kiosk flow...'

$windowsSystemPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'

# Suppress the first-sign-in animation shown while Windows initializes the exam session.
if (-not (Test-Path -LiteralPath $windowsSystemPath)) {
    New-Item -Path $windowsSystemPath -Force | Out-Null
}

Set-ItemProperty `
    -LiteralPath $windowsSystemPath `
    -Name 'EnableFirstLogonAnimation' `
    -Type DWord `
    -Value 0 `
    -Force

Write-Host 'Windows policy changes applied.'
Write-Host 'Reboot the device and validate the kiosk flow in the Assigned Access session.'
