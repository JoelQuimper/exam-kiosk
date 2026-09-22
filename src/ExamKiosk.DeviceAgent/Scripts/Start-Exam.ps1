[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ConfigurationPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$WebShortcutsPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$EdgePolicyPath,

    [Parameter(Mandatory)]
    [string]$EdgePolicyBackupPath
)

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Starting an exam through the MDM Bridge must run as LocalSystem.'
}

. (Join-Path $PSScriptRoot 'EdgePolicy.ps1')

$edgePolicyRoot = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
$shortcutRoot = Join-Path `
    $env:ProgramData `
    'Microsoft\Windows\Start Menu\Programs\Exam Kiosk\Tools'
$webShortcutsDocument = Get-Content -LiteralPath $WebShortcutsPath -Raw |
    ConvertFrom-Json
$webShortcuts = @($webShortcutsDocument)

$edgeCandidates = @()
if (${env:ProgramFiles(x86)}) {
    $edgeCandidates += Join-Path `
        ${env:ProgramFiles(x86)} `
        'Microsoft\Edge\Application\msedge.exe'
}
if ($env:ProgramFiles) {
    $edgeCandidates += Join-Path `
        $env:ProgramFiles `
        'Microsoft\Edge\Application\msedge.exe'
}
$edgePath = $edgeCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ($webShortcuts.Count -gt 0 -and -not $edgePath) {
    throw 'Microsoft Edge could not be found for the Exam Kiosk web shortcuts.'
}

$createdShortcutPaths = [Collections.Generic.List[string]]::new()
$edgePolicyBackupCreated = $false
try {
    Save-ExamEdgePolicyBackup `
        -BackupPath $EdgePolicyBackupPath `
        -PolicyRoot $edgePolicyRoot
    $edgePolicyBackupCreated = $true
    Set-ExamEdgePolicy `
        -PolicyPath $EdgePolicyPath `
        -PolicyRoot $edgePolicyRoot

    foreach ($webShortcut in $webShortcuts) {
        $entryUrl = $null
        $entryUrlValue = [string]$webShortcut.entryUrl
        if (-not [Uri]::TryCreate(
                $entryUrlValue,
                [UriKind]::Absolute,
                [ref]$entryUrl) -or
            $entryUrl.Scheme -cne 'https') {
            throw 'Exam Kiosk web shortcuts require an absolute HTTPS URL.'
        }

        $resolvedLinkPath = [Environment]::ExpandEnvironmentVariables(
            $webShortcut.linkPath)
        $resolvedLinkPath = [IO.Path]::GetFullPath($resolvedLinkPath)
        $resolvedRoot = [IO.Path]::GetFullPath($shortcutRoot)
        $fileName = [IO.Path]::GetFileName($resolvedLinkPath)
        if (-not [string]::Equals(
                [IO.Path]::GetDirectoryName($resolvedLinkPath),
                $resolvedRoot,
                [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace($fileName) -or
            -not $fileName.EndsWith(
                '.lnk',
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "The shortcut path '$resolvedLinkPath' is not an Exam Kiosk tool shortcut."
        }

        New-Item -ItemType Directory -Path $resolvedRoot -Force | Out-Null
        $temporaryPath = Join-Path `
            $resolvedRoot `
            ".$fileName.$([guid]::NewGuid().ToString('N')).tmp.lnk"
        $shell = $null
        $shortcut = $null
        try {
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut($temporaryPath)
            $shortcut.TargetPath = $edgePath
            $shortcut.Arguments = '--new-window --no-first-run --inprivate "{0}"' -f $entryUrl.AbsoluteUri
            $shortcut.WorkingDirectory = Split-Path -Parent $edgePath
            $shortcut.Description = $webShortcut.label
            $shortcut.IconLocation = if ($webShortcut.iconLocation) {
                [Environment]::ExpandEnvironmentVariables(
                    $webShortcut.iconLocation)
            }
            else {
                "$edgePath,0"
            }
            $shortcut.Save()

            if (-not (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
                throw "The shortcut '$temporaryPath' was not created."
            }

            Move-Item `
                -LiteralPath $temporaryPath `
                -Destination $resolvedLinkPath `
                -Force
            if (-not (Test-Path -LiteralPath $resolvedLinkPath -PathType Leaf)) {
                throw "The shortcut '$resolvedLinkPath' was not installed."
            }
            $createdShortcutPaths.Add($resolvedLinkPath)
        }
        finally {
            if ($shortcut) {
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
            }
            if ($shell) {
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
            }
            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }

    [xml]$configuration = Get-Content -LiteralPath $ConfigurationPath -Raw
    $assignedAccess = Get-CimInstance `
        -Namespace 'root\cimv2\mdm\dmmap' `
        -ClassName 'MDM_AssignedAccess'
    $assignedAccess.Configuration = [System.Net.WebUtility]::HtmlEncode(
        $configuration.OuterXml)
    Set-CimInstance -CimInstance $assignedAccess | Out-Null
}
catch {
    $startFailure = $_
    $cleanupFailures = [Collections.Generic.List[string]]::new()
    foreach ($createdShortcutPath in $createdShortcutPaths) {
        try {
            if (Test-Path -LiteralPath $createdShortcutPath -PathType Leaf) {
                Remove-Item -LiteralPath $createdShortcutPath -Force
            }
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }
    }
    if ($edgePolicyBackupCreated) {
        try {
            Restore-ExamEdgePolicyBackup `
                -BackupPath $EdgePolicyBackupPath `
                -PolicyRoot $edgePolicyRoot
        }
        catch {
            $cleanupFailures.Add($_.Exception.Message)
        }
    }

    if ($cleanupFailures.Count -gt 0) {
        throw (
            "Exam mode start failed: {0} Cleanup also failed: {1}" -f
            $startFailure.Exception.Message,
            [string]::Join(' ', $cleanupFailures))
    }
    throw $startFailure
}

Write-Output (
    'Exam mode started: Edge policy applied, {0} web shortcut(s) created, and Assigned Access applied successfully.' -f
    $createdShortcutPaths.Count)