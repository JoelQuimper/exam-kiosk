#Requires -Version 7.0
#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'ExamKioskDeviceAgent'
$installRoot = Join-Path $env:ProgramFiles 'ExamKiosk'
$dataRoot = Join-Path $env:ProgramData 'ExamKiosk'
$statePath = Join-Path $dataRoot 'agent-state.json'

if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($state.state -ne 'available') {
        throw "The agent state is '$($state.state)'. Finish or recover the exam before uninstalling."
    }
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service -Name $serviceName -Force
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Deleting service $serviceName failed with exit code $LASTEXITCODE."
    }
}

$shortcutDirectory = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Exam Kiosk'
Remove-Item -LiteralPath $shortcutDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Output 'Exam Kiosk PoC uninstalled. ProgramData state and history were preserved.'