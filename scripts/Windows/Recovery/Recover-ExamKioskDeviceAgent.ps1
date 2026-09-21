[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ResultPath,

    [switch]$ForceForeignAssignedAccess
)

$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'
$profileId = '{9A2A490F-10F6-4764-974A-43B19E722C23}'

function Write-WorkerResult {
    param(
        [bool]$Success,
        [string]$Status,
        [string]$Message
    )

    [ordered]@{
        success = $Success
        status = $Status
        message = $Message
        completedAtUtc = [DateTimeOffset]::UtcNow
    } |
        ConvertTo-Json |
        Set-Content -LiteralPath $ResultPath -Encoding utf8
}

function Remove-ExamKioskToolShortcuts {
    $shortcutRoot = Join-Path `
        $env:ProgramData `
        'Microsoft\Windows\Start Menu\Programs\Exam Kiosk\Tools'
    if (-not (Test-Path -LiteralPath $shortcutRoot -PathType Container)) {
        Write-Verbose 'No Exam Kiosk tool shortcut directory was found.'
        return
    }

    Write-Verbose "Removing Exam Kiosk tool shortcuts from '$shortcutRoot'."
    Get-ChildItem `
        -LiteralPath $shortcutRoot `
        -Filter '*.lnk' `
        -File |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force
        }

    $remaining = @(
        Get-ChildItem `
            -LiteralPath $shortcutRoot `
            -Filter '*.lnk' `
            -File
    )
    if ($remaining.Count -ne 0) {
        throw 'One or more Exam Kiosk tool shortcuts remained after recovery.'
    }
}

Write-Verbose 'Starting the LocalSystem recovery worker.'
if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
    throw 'The recovery worker must run as LocalSystem.'
}

try {
    Write-Verbose 'Reading Assigned Access through the MDM Bridge.'
    $assignedAccess = Get-CimInstance `
        -Namespace 'root\cimv2\mdm\dmmap' `
        -ClassName 'MDM_AssignedAccess'
    if ([string]::IsNullOrWhiteSpace($assignedAccess.Configuration)) {
        Write-Verbose 'Assigned Access is already clear.'
        Remove-ExamKioskToolShortcuts
        Write-WorkerResult `
            -Success $true `
            -Status 'AlreadyClear' `
            -Message 'Assigned Access was already clear.'
        exit 0
    }

    $configuration = [System.Net.WebUtility]::HtmlDecode(
        $assignedAccess.Configuration)
    $isExamKioskProfile = $configuration.IndexOf(
        $profileId,
        [StringComparison]::OrdinalIgnoreCase) -ge 0
    Write-Verbose "Exam Kiosk profile detected: $isExamKioskProfile."
    if (-not $isExamKioskProfile -and -not $ForceForeignAssignedAccess) {
        throw (
            'Assigned Access contains a profile that is not owned by Exam Kiosk. ' +
            'No configuration was removed. Use -ForceForeignAssignedAccess only ' +
            'after an administrator verifies that removing it is appropriate.')
    }

    Write-Verbose 'Clearing the Assigned Access configuration.'
    $assignedAccess.Configuration = $null
    Set-CimInstance -CimInstance $assignedAccess | Out-Null

    Write-Verbose 'Verifying that Assigned Access is clear.'
    $verification = Get-CimInstance `
        -Namespace 'root\cimv2\mdm\dmmap' `
        -ClassName 'MDM_AssignedAccess'
    if (-not [string]::IsNullOrWhiteSpace($verification.Configuration)) {
        throw 'Assigned Access remained configured after the recovery attempt.'
    }

    Remove-ExamKioskToolShortcuts
    Write-WorkerResult `
        -Success $true `
        -Status 'Removed' `
        -Message 'Assigned Access was removed and verified.'
}
catch {
    Write-WorkerResult `
        -Success $false `
        -Status 'Failed' `
        -Message $_.Exception.Message
    throw
}
