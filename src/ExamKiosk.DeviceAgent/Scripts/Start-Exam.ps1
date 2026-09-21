[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ConfigurationPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$WebShortcutsPath
)

$ErrorActionPreference = 'Stop'

if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'Starting an exam through the MDM Bridge must run as LocalSystem.'
}

$shortcutRoot = Join-Path `
    $env:ProgramData `
    'Microsoft\Windows\Start Menu\Programs\Exam Kiosk\Tools'
$webShortcuts = @(
    Get-Content -LiteralPath $WebShortcutsPath -Raw |
        ConvertFrom-Json
)

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
try {
    foreach ($webShortcut in $webShortcuts) {
        $entryUrl = $null
        if (-not [Uri]::TryCreate(
                $webShortcut.entryUrl,
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
    foreach ($createdShortcutPath in $createdShortcutPaths) {
        if (Test-Path -LiteralPath $createdShortcutPath -PathType Leaf) {
            Remove-Item -LiteralPath $createdShortcutPath -Force
        }
    }
    throw
}

Write-Output (
    'Exam mode started: {0} web shortcut(s) created and Assigned Access applied successfully.' -f
    $createdShortcutPaths.Count)