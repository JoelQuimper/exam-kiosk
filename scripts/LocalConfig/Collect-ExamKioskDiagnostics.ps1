#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$timestamp = [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')
$desktopPath = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
$archivePath = Join-Path `
    $desktopPath `
    "ExamKiosk-Diagnostics-$env:COMPUTERNAME-$timestamp.zip"
$stagingRoot = Join-Path `
    $env:TEMP `
    "ExamKiosk-Diagnostics-$([guid]::NewGuid().ToString('N'))"
$dataRoot = Join-Path $env:ProgramData 'ExamKiosk'
$installRoot = Join-Path $env:ProgramFiles 'ExamKiosk'
$collected = [Collections.Generic.List[string]]::new()
$missing = [Collections.Generic.List[string]]::new()
$errors = [Collections.Generic.List[string]]::new()

function Copy-DiagnosticItem {
    param(
        [Parameter(Mandatory)]
        [string]$Source,

        [Parameter(Mandatory)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        $missing.Add($Source)
        return
    }

    try {
        $destinationPath = Join-Path $stagingRoot $Destination
        $destinationParent = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationParent -Force |
            Out-Null
        Copy-Item `
            -LiteralPath $Source `
            -Destination $destinationPath `
            -Recurse `
            -Force
        $collected.Add($Source)
    }
    catch {
        $errors.Add("Could not copy '$Source': $($_.Exception.Message)")
    }
}

function Write-DiagnosticJson {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [object]$Value
    )

    $destinationPath = Join-Path $stagingRoot $RelativePath
    New-Item `
        -ItemType Directory `
        -Path (Split-Path -Parent $destinationPath) `
        -Force |
        Out-Null
    $Value |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $destinationPath -Encoding UTF8
}

function Get-RegistryListPolicy {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return [pscustomobject]@{
            exists = $false
            values = @()
        }
    }

    $key = Get-Item -LiteralPath $Path
    $values = @(
        foreach ($name in @($key.GetValueNames() | Sort-Object)) {
            [pscustomobject]@{
                name = $name
                kind = $key.GetValueKind($name).ToString()
                data = $key.GetValue(
                    $name,
                    $null,
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            }
        }
    )
    return [pscustomobject]@{
        exists = $true
        values = $values
    }
}

try {
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

    $diagnosticItems = @(
        @(
            $dataRoot,
            'ProgramData\ExamKiosk'
        ),
        @(
            (Join-Path $installRoot 'Agent\Configuration'),
            'Installation\Agent\Configuration'
        ),
        @(
            (Join-Path $installRoot 'Agent\appsettings.Production.json'),
            'Installation\Agent\appsettings.Production.json'
        ),
        @(
            (Join-Path $installRoot 'Launcher\launcher.settings.json'),
            'Installation\Launcher\launcher.settings.json'
        )
    )
    foreach ($item in $diagnosticItems) {
        Copy-DiagnosticItem -Source $item[0] -Destination $item[1]
    }

    try {
        $service = Get-CimInstance `
            -ClassName Win32_Service `
            -Filter "Name='ExamKioskDeviceAgent'"
        if ($service) {
            $serviceDetails = $service |
                Select-Object `
                    Name,
                    DisplayName,
                    State,
                    StartMode,
                    StartName,
                    PathName,
                    ProcessId
        }
        else {
            $serviceDetails = [pscustomobject]@{ installed = $false }
        }
        Write-DiagnosticJson `
            -RelativePath 'System\device-agent-service.json' `
            -Value $serviceDetails
    }
    catch {
        $errors.Add(
            "Could not read the Device Agent service: $($_.Exception.Message)")
    }

    try {
        $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem
        Write-DiagnosticJson `
            -RelativePath 'System\operating-system.json' `
            -Value (
                $operatingSystem |
                    Select-Object `
                        Caption,
                        Version,
                        BuildNumber,
                        OSArchitecture,
                        LastBootUpTime)
    }
    catch {
        $errors.Add(
            "Could not read operating system details: $($_.Exception.Message)")
    }

    try {
        $edgeRoot = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
        Write-DiagnosticJson `
            -RelativePath 'System\edge-url-policies.json' `
            -Value ([ordered]@{
                urlBlocklist = Get-RegistryListPolicy `
                    -Path (Join-Path $edgeRoot 'URLBlocklist')
                urlAllowlist = Get-RegistryListPolicy `
                    -Path (Join-Path $edgeRoot 'URLAllowlist')
            })
    }
    catch {
        $errors.Add(
            "Could not read the Edge URL policies: $($_.Exception.Message)")
    }

    try {
        $assignedAccess = Get-CimInstance `
            -Namespace 'root\cimv2\mdm\dmmap' `
            -ClassName 'MDM_AssignedAccess'
        $assignedAccessPath = Join-Path `
            $stagingRoot `
            'System\assigned-access-current.xml'
        New-Item `
            -ItemType Directory `
            -Path (Split-Path -Parent $assignedAccessPath) `
            -Force |
            Out-Null
        [System.Net.WebUtility]::HtmlDecode($assignedAccess.Configuration) |
            Set-Content -LiteralPath $assignedAccessPath -Encoding UTF8
    }
    catch {
        $errors.Add(
            "Could not read Assigned Access: $($_.Exception.Message)")
    }

    Write-DiagnosticJson `
        -RelativePath 'manifest.json' `
        -Value ([ordered]@{
            createdAt = [DateTimeOffset]::Now
            computerName = $env:COMPUTERNAME
            collected = @($collected)
            missing = @($missing)
            errors = @($errors)
            sensitivity = 'Contains student identity, exam URLs, and device configuration. Do not share publicly.'
        })

    if (Test-Path -LiteralPath $archivePath) {
        throw "The diagnostic archive already exists at '$archivePath'."
    }
    Compress-Archive `
        -Path (Join-Path $stagingRoot '*') `
        -DestinationPath $archivePath `
        -CompressionLevel Optimal

    Write-Host "Diagnostic archive created: $archivePath" -ForegroundColor Green
    if ($missing.Count -gt 0) {
        Write-Warning (
            '{0} expected diagnostic item(s) were not present. See manifest.json.' -f
            $missing.Count)
    }
    if ($errors.Count -gt 0) {
        Write-Warning (
            '{0} diagnostic collection error(s) occurred. See manifest.json.' -f
            $errors.Count)
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot -PathType Container) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
