#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Set-RegistryDword {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [int]$Value
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -Path $Path -Force | Out-Null
    }

    Set-ItemProperty -Path $Path -Name $Name -Type DWord -Value $Value -Force
}

Write-Host 'Applying the safe device-wide Windows policy for the kiosk flow...'

$windowsSystemPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'

# Safe machine-wide Windows policy: suppress first-user sign-in animation.
Set-RegistryDword -Path $windowsSystemPath -Name 'EnableFirstLogonAnimation' -Value 0

Write-Host 'Windows policy changes applied.'
Write-Host 'Reboot the device and validate the kiosk flow in the Assigned Access session.'
