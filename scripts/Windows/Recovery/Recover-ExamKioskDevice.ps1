[CmdletBinding()]
param(
    [switch]$ForceForeignAssignedAccess,

    [Parameter(DontShow)]
    [switch]$SystemWorker,

    [Parameter(DontShow)]
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'
$VerbosePreference = 'Continue'
$serviceName = 'ExamKioskDeviceAgent'
$dataRoot = Join-Path $env:ProgramData 'ExamKiosk'
$recoveryRoot = Join-Path $dataRoot 'Recovery'
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

function Invoke-SystemRecovery {
    Write-Verbose 'Starting Assigned Access recovery as LocalSystem.'
    if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'NT AUTHORITY\SYSTEM') {
        throw 'The recovery worker must run as LocalSystem.'
    }
    if ([string]::IsNullOrWhiteSpace($ResultPath)) {
        throw 'The recovery worker requires a result path.'
    }

    try {
        Write-Verbose 'Reading Assigned Access through the MDM Bridge.'
        $assignedAccess = Get-CimInstance `
            -Namespace 'root\cimv2\mdm\dmmap' `
            -ClassName 'MDM_AssignedAccess'
        if ([string]::IsNullOrWhiteSpace($assignedAccess.Configuration)) {
            Write-WorkerResult `
                -Success $true `
                -Status 'AlreadyClear' `
                -Message 'Assigned Access was already clear.'
            return
        }

        $configuration = [System.Net.WebUtility]::HtmlDecode(
            $assignedAccess.Configuration)
        $isExamKioskProfile = $configuration.IndexOf(
            $profileId,
            [StringComparison]::OrdinalIgnoreCase) -ge 0
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
}

if ($SystemWorker) {
    Invoke-SystemRecovery
    exit 0
}

$scriptPath = $MyInvocation.MyCommand.Path
Write-Verbose "Starting Exam Kiosk recovery from '$scriptPath'."
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$administratorRole = [Security.Principal.WindowsBuiltInRole]::Administrator
if (-not $principal.IsInRole($administratorRole)) {
    Write-Verbose 'Administrator elevation is required. Opening the UAC prompt.'
    $powershellPath = (Get-Process -Id $PID).Path
    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy', 'Bypass'
        '-File', "`"$scriptPath`""
    )
    if ($ForceForeignAssignedAccess) {
        $arguments += '-ForceForeignAssignedAccess'
    }

    $elevatedProcess = Start-Process `
        -FilePath $powershellPath `
        -ArgumentList $arguments `
        -Verb RunAs `
        -Wait `
        -PassThru
    exit $elevatedProcess.ExitCode
}

Write-Verbose "Running elevated as '$($identity.Name)'."
New-Item -ItemType Directory -Path $recoveryRoot -Force | Out-Null
Write-Verbose "Pruning old recovery results under '$recoveryRoot'."
Get-ChildItem `
    -LiteralPath $recoveryRoot `
    -Filter 'recovery-*.json' `
    -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -Skip 19 |
    ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
    }
$recoveryId = [guid]::NewGuid().ToString('N')
$recoveryTimestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$resultPath = Join-Path `
    $recoveryRoot `
    "recovery-$recoveryTimestamp-$recoveryId.json"
$taskName = "ExamKioskRecovery-$recoveryId"
$powershellExecutable = Join-Path `
    $env:SystemRoot `
    'System32\WindowsPowerShell\v1.0\powershell.exe'
$taskArguments = (
    '-NoProfile -ExecutionPolicy Bypass -File "{0}" -SystemWorker -ResultPath "{1}"' -f
    $scriptPath,
    $resultPath)
if ($ForceForeignAssignedAccess) {
    Write-Warning 'Foreign Assigned Access removal is enabled for this recovery.'
    $taskArguments += ' -ForceForeignAssignedAccess'
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Verbose "Device Agent service state: $($service.Status)."
}
else {
    Write-Verbose 'The Device Agent service is not installed.'
}

try {
    if ($service -and $service.Status -ne 'Stopped') {
        Write-Host 'Stopping the Exam Kiosk Device Agent and leaving it stopped...'
        Stop-Service -Name $serviceName -Force
    }

    $action = New-ScheduledTaskAction `
        -Execute $powershellExecutable `
        -Argument $taskArguments
    $principal = New-ScheduledTaskPrincipal `
        -UserId 'SYSTEM' `
        -LogonType ServiceAccount `
        -RunLevel Highest
    $task = New-ScheduledTask -Action $action -Principal $principal
    Write-Verbose "Registering temporary recovery task '$taskName'."
    Register-ScheduledTask `
        -TaskName $taskName `
        -InputObject $task `
        -Force | Out-Null

    Write-Host 'Removing the Exam Kiosk restrictions as LocalSystem...'
    Write-Verbose 'Starting the LocalSystem worker and waiting up to 60 seconds.'
    Start-ScheduledTask -TaskName $taskName
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    while (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw 'The LocalSystem recovery worker did not finish within 60 seconds.'
        }
        Start-Sleep -Milliseconds 250
    }

    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    Write-Verbose "Recovery worker status: $($result.status)."
    if (-not $result.success) {
        throw $result.message
    }

    Write-Verbose "Recovery result saved to '$resultPath'."
    Write-Host $result.message -ForegroundColor Green
    Write-Host 'The Device Agent remains stopped. Reset or reinstall Exam Kiosk before using it again.' -ForegroundColor Yellow
    Write-Host 'Restart Windows to complete Assigned Access recovery: shutdown.exe /r /t 0' -ForegroundColor Yellow
}
finally {
    Write-Verbose "Removing temporary recovery task '$taskName'."
    Unregister-ScheduledTask `
        -TaskName $taskName `
        -Confirm:$false `
        -ErrorAction SilentlyContinue
}
