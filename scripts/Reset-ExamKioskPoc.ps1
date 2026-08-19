[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = $MyInvocation.MyCommand.Path
$scriptDirectory = Split-Path -Parent $scriptPath
$repositoryRoot = Split-Path -Parent $scriptDirectory

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$administratorRole = [Security.Principal.WindowsBuiltInRole]::Administrator

if (-not $principal.IsInRole($administratorRole)) {
    $powershellPath = (Get-Process -Id $PID).Path
    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy', 'Bypass'
        '-File', $scriptPath
    )
    $elevatedProcess = Start-Process `
        -FilePath $powershellPath `
        -ArgumentList $arguments `
        -Verb RunAs `
        -Wait `
        -PassThru
    exit $elevatedProcess.ExitCode
}

Set-Location -LiteralPath $scriptDirectory

Write-Host 'Uninstalling the current Exam Kiosk installation...'
& (Join-Path $scriptDirectory 'Uninstall-ExamKioskPoc.ps1')

Write-Host 'Installing the current Exam Kiosk build...'
& (Join-Path $scriptDirectory 'Install-ExamKioskPoc.ps1')

$launcherPath = Join-Path $env:ProgramFiles 'ExamKiosk\Launcher\ExamKiosk.Launcher.exe'
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "The installed launcher was not found: $launcherPath"
}

Write-Host 'Launching the Exam Kiosk launcher...'
Start-Process -FilePath $launcherPath -WorkingDirectory (Split-Path -Parent $launcherPath)
