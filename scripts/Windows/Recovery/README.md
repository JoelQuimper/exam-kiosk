# Exam Kiosk recovery

Use this recovery path when an exam transition fails, Assigned Access remains
configured, or the normal Restricted Client cannot complete the exam.

## Run recovery

Prefer the installed **Recover Exam Kiosk Device** Start-menu shortcut. From an
elevated PowerShell 7 terminal, the equivalent command is:

```powershell
& "$env:ProgramFiles\ExamKiosk\Recovery\Recover-ExamKioskDevice.ps1"
```

From a repository checkout:

```powershell
pwsh -File .\scripts\Windows\Recovery\Recover-ExamKioskDevice.ps1
```

After successful recovery, restart Windows:

```powershell
shutdown.exe /r /t 0
```

If recovery reports that the configured Assigned Access profile is not owned
by Exam Kiosk, do not remove it until an administrator has independently
verified the configuration. Only then use:

```powershell
& "$env:ProgramFiles\ExamKiosk\Recovery\Recover-ExamKioskDevice.ps1" `
    -ForceForeignAssignedAccess
```

## Execution order

1. `Recover-ExamKioskDevice.ps1` starts in PowerShell 7 and requests
   administrator elevation when necessary.
2. It stops the Device Agent service to prevent concurrent transitions.
3. It registers a temporary scheduled task running as `LocalSystem`.
4. The task starts `Recover-ExamKioskDeviceAgent.ps1` with Windows PowerShell
   5.1.
5. The Agent recovery script reads Assigned Access through the MDM Bridge,
   verifies Exam Kiosk ownership, clears and verifies the configuration, and
   removes only Exam Kiosk tool shortcuts.
6. The Agent recovery script writes a bounded result under
   `%ProgramData%\ExamKiosk\Recovery`.
7. The PowerShell 7 orchestrator reads that result, resets `agent-state.json`
   to `available`, appends `ManualRecovery` to the session journal, removes the
   temporary task, and restarts the Device Agent if it was previously running.
8. The administrator restarts Windows.

## Why the scripts are separate

`Recover-ExamKioskDevice.ps1` is the administrator-facing orchestrator. It
uses PowerShell 7 consistently with the other installation and administration
scripts and owns elevation, service coordination, state persistence, and
operator messages.

`Recover-ExamKioskDeviceAgent.ps1` is a minimal privileged worker. It runs only
as `LocalSystem` and uses the inbox Windows PowerShell 5.1 runtime required by
the Device Agent recovery path. This keeps MDM Bridge recovery available
without depending on a PowerShell 7 installation in the `LocalSystem`
environment.

Do not run `Recover-ExamKioskDeviceAgent.ps1` directly. It rejects callers
that are not `NT AUTHORITY\SYSTEM` and communicates its result through the
path supplied by the orchestrator.
