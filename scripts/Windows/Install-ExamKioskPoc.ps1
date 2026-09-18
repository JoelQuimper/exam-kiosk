#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$WebAppUrl,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$serviceName = 'ExamKioskDeviceAgent'
$installRoot = Join-Path $env:ProgramFiles 'ExamKiosk'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$stagingRoot = Join-Path $env:TEMP "ExamKiosk-$([guid]::NewGuid())"
$configurationRoot = Join-Path $env:ProgramData 'ExamKiosk'
$deploymentConfigurationPath = Join-Path $configurationRoot 'deployment.settings.json'

if (-not $PSBoundParameters.ContainsKey('WebAppUrl')) {
    if (Test-Path -LiteralPath $deploymentConfigurationPath -PathType Leaf) {
        $deploymentConfiguration = Get-Content `
            -LiteralPath $deploymentConfigurationPath `
            -Raw |
            ConvertFrom-Json
        $WebAppUrl = $deploymentConfiguration.webAppUrl
    }
    else {
        $WebAppUrl = Read-Host 'Enter the Exam Kiosk web application HTTPS origin'
    }
}

$webAppUri = $null
if (-not [Uri]::TryCreate($WebAppUrl, [UriKind]::Absolute, [ref]$webAppUri) -or
    $webAppUri.Scheme -cne 'https' -or
    -not $webAppUri.Host -or
    $webAppUri.UserInfo -or
    $webAppUri.Query -or
    $webAppUri.Fragment -or
    $webAppUri.AbsolutePath -cne '/') {
    throw 'WebAppUrl must be an absolute HTTPS origin without credentials, a path, query, or fragment.'
}
$normalizedWebAppUrl = $webAppUri.GetLeftPart([UriPartial]::Authority)

New-Item -ItemType Directory -Path $configurationRoot -Force | Out-Null
$logsRoot = Join-Path $configurationRoot 'Logs'
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null
& icacls.exe $logsRoot /grant '*S-1-5-32-545:(OI)(CI)(M)' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Granting write access to the diagnostic log directory failed with exit code $LASTEXITCODE."
}

[ordered]@{
    webAppUrl = $normalizedWebAppUrl
} |
    ConvertTo-Json |
    Set-Content -LiteralPath $deploymentConfigurationPath -Encoding utf8

$webViewRuntime = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
) |
    ForEach-Object { Get-ItemProperty -Path $_ -ErrorAction SilentlyContinue } |
    Where-Object DisplayName -Like 'Microsoft Edge WebView2 Runtime*' |
    Select-Object -First 1
if (-not $webViewRuntime) {
    throw 'Microsoft Edge WebView2 Runtime is required. Install it through device management before installing Exam Kiosk.'
}

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

    $recoveryDirectory = Join-Path $installRoot 'Recovery'
    New-Item -ItemType Directory -Path $recoveryDirectory -Force | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot 'Recover-ExamKioskDevice.ps1') `
        -Destination $recoveryDirectory

    [ordered]@{
        webAppUrl = $normalizedWebAppUrl
    } |
        ConvertTo-Json |
        Set-Content `
            -LiteralPath (Join-Path $installRoot 'Launcher\launcher.settings.json') `
            -Encoding utf8

    [ordered]@{
        webAppUrl = $normalizedWebAppUrl
    } |
        ConvertTo-Json |
        Set-Content `
            -LiteralPath (Join-Path $installRoot 'RestrictedClient\restricted-client.settings.json') `
            -Encoding utf8

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

    $recoveryShortcut = $shell.CreateShortcut(
        (Join-Path $shortcutDirectory 'Recover Exam Kiosk Device.lnk'))
    $recoveryShortcut.TargetPath = Join-Path `
        $env:SystemRoot `
        'System32\WindowsPowerShell\v1.0\powershell.exe'
    $recoveryScriptPath = Join-Path `
        $recoveryDirectory `
        'Recover-ExamKioskDevice.ps1'
    $recoveryShortcut.Arguments = (
        '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f
        $recoveryScriptPath)
    $recoveryShortcut.WorkingDirectory = $recoveryDirectory
    $recoveryShortcut.Description = 'Remove Exam Kiosk restrictions from an administrator session.'
    $recoveryShortcut.Save()

    $desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Reset Exam Kiosk PoC.lnk'
    $resetShortcut = $shell.CreateShortcut($desktopShortcutPath)
    $resetShortcut.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $resetShortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $repositoryRoot 'scripts\Windows\Reset-ExamKioskPoc.ps1')`""
    $resetShortcut.WorkingDirectory = Join-Path $repositoryRoot 'scripts\Windows'
    $resetShortcut.Description = 'Uninstall, install, and launch the Exam Kiosk PoC.'
    $resetShortcut.Save()

    Start-Service -Name $serviceName
    Write-Output "Exam Kiosk PoC installed at $installRoot."
    Write-Output "Native client diagnostics are written under $logsRoot."
    Write-Output 'The normal launcher is available in the Start menu under Exam Kiosk.'
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}