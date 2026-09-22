# Exam Kiosk recovery

Use this emergency path only when Assigned Access leaves a device trapped in
the kiosk experience and an administrator must regain control of Windows.
Recovery is not an installation, uninstallation, reset, or Device Agent repair
workflow.

## Run recovery

Prefer the installed **Recover Exam Kiosk Device** Start-menu shortcut. From an
elevated Windows PowerShell 5.1 terminal, the equivalent command is:

```powershell
& "$env:ProgramFiles\ExamKiosk\Recovery\Recover-ExamKioskDevice.ps1"
```

From a repository checkout:

```powershell
& .\scripts\Windows\Recovery\Recover-ExamKioskDevice.ps1
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

1. `Recover-ExamKioskDevice.ps1` starts in Windows PowerShell 5.1 and requests
   administrator elevation when necessary.
2. It stops the Device Agent service to prevent concurrent transitions.
   The service remains stopped after recovery.
3. It registers a temporary scheduled task running as `LocalSystem`.
4. The task starts the same script in its internal `-SystemWorker` mode.
5. The LocalSystem mode reads Assigned Access through the MDM Bridge,
   verifies Exam Kiosk ownership, and clears and verifies the configuration.
6. If `%ProgramData%\ExamKiosk\edge-policy-backup.json` exists, it restores
   and verifies the saved `URLBlocklist` and `URLAllowlist`.
7. The LocalSystem mode writes a bounded result under
   `%ProgramData%\ExamKiosk\Recovery`.
8. The recovery orchestrator reads that result and removes the temporary task.
9. The administrator restarts Windows, then uses the normal Reset,
   installation, or uninstallation script as appropriate.

## Why the script has two execution modes

The administrator-facing mode owns elevation, service coordination, the
temporary scheduled task, and operator messages. The hidden `-SystemWorker`
mode performs only the Assigned Access MDM Bridge operation. It rejects
callers that are not `NT AUTHORITY\SYSTEM` and communicates its result through
the path supplied by the administrator-facing mode.

Both modes use inbox Windows PowerShell 5.1. Keeping them in one file avoids a
second recovery artifact while retaining the `LocalSystem` boundary required
by the device-scoped MDM Bridge.

## Recovery scope

Recovery currently:

- stops and leaves the Device Agent service stopped;
- removes and verifies Assigned Access;
- restores and verifies the Agent-owned Edge URL-policy backup when present.

Recovery does not modify `agent-state.json`, `session-journal.json`, generated
shortcuts, or backend session state. After the device is accessible again,
use `Reset-ExamKioskPoc.ps1`, `Install-ExamKioskPoc.ps1`, or
`Uninstall-ExamKioskPoc.ps1` for application maintenance.

Do not use Recovery as a substitute for those normal scripts when the
administrator can already access the device.

Recovery changes only `URLBlocklist` and `URLAllowlist` when the Agent-owned
backup exists. It does not delete other administrator or MDM Edge policies.
