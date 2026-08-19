#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$serviceName = 'ExamKioskDeviceAgent'
$installRoot = Join-Path $env:ProgramFiles 'ExamKiosk'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$stagingRoot = Join-Path $env:TEMP "ExamKiosk-$([guid]::NewGuid())"

try {
    $projects = @{
        Agent = 'src\ExamKiosk.DeviceAgent\ExamKiosk.DeviceAgent.csproj'
        Launcher = 'src\ExamKiosk.Launcher\ExamKiosk.Launcher.csproj'
        RestrictedClient = 'src\ExamKiosk.RestrictedClient\ExamKiosk.RestrictedClient.csproj'
    }

    foreach ($component in $projects.GetEnumerator()) {
        $projectPath = Join-Path $repositoryRoot $component.Value
        $outputPath = Join-Path $stagingRoot $component.Key
        & dotnet publish $projectPath `
            --configuration $Configuration `
            --runtime $RuntimeIdentifier `
            --self-contained true `
            --output $outputPath
        if ($LASTEXITCODE -ne 0) {
            throw "Publishing $($component.Key) failed with exit code $LASTEXITCODE."
        }
    }

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        Stop-Service -Name $serviceName -Force
    }

    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    foreach ($componentName in $projects.Keys) {
        $destination = Join-Path $installRoot $componentName
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Recurse -Force
        }
        Copy-Item `
            -LiteralPath (Join-Path $stagingRoot $componentName) `
            -Destination $destination `
            -Recurse
    }

    $agentPath = Join-Path $installRoot 'Agent\ExamKiosk.DeviceAgent.exe'
    if ($service) {
        & sc.exe config $serviceName `
            'binPath=' "`"$agentPath`"" `
            'start=' 'auto' `
            'obj=' 'LocalSystem' | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Updating service $serviceName failed with exit code $LASTEXITCODE."
        }
    }
    else {
        New-Service `
            -Name $serviceName `
            -BinaryPathName "`"$agentPath`"" `
            -DisplayName 'Exam Kiosk Device Agent' `
            -Description 'Applies and removes the Exam Kiosk Assigned Access profile.' `
            -StartupType Automatic | Out-Null
    }

    $shortcutDirectory = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Exam Kiosk'
    New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $shortcutDirectory 'Exam Kiosk Launcher.lnk'))
    $shortcut.TargetPath = Join-Path $installRoot 'Launcher\ExamKiosk.Launcher.exe'
    $shortcut.WorkingDirectory = Join-Path $installRoot 'Launcher'
    $shortcut.Save()

    $desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Reset Exam Kiosk PoC.lnk'
    $resetShortcut = $shell.CreateShortcut($desktopShortcutPath)
    $resetShortcut.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $resetShortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $repositoryRoot 'scripts\Reset-ExamKioskPoc.ps1')`""
    $resetShortcut.WorkingDirectory = Join-Path $repositoryRoot 'scripts'
    $resetShortcut.Description = 'Uninstall, install, and launch the Exam Kiosk PoC.'
    $resetShortcut.Save()

    Start-Service -Name $serviceName
    Write-Output "Exam Kiosk PoC installed at $installRoot."
    Write-Output 'The normal launcher is available in the Start menu under Exam Kiosk.'
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}