# exam-kiosk

> [!WARNING]
> This project is experimental and intended for demonstration, evaluation, and
> development purposes only. It is not production-ready, is not officially
> supported, and comes with no guarantees of reliability, security, or fitness
> for any particular purpose. Use it at your own risk. The maintainers are not
> responsible for data loss, service disruption, security incidents, or other
> damages resulting from its use.

## Description

Exam Kiosk is an experimental toolkit for moving a managed Windows laptop from
a normal student session into a restricted exam session. The current code is a
Phase 0 feasibility prototype, not a production-ready exam system.

## Exam list web application

The first web phase is available in `src\ExamKiosk.Web`. It provides:

- automatic Microsoft Entra sign-in when the application opens;
- Microsoft Entra single-tenant authentication with a server-owned session
  cookie;
- an authenticated exam list at `/exams` populated from immutable development
  student, exam, assignment, and tool data;
- an authenticated current-student assignments API at
  `/api/v1/exam-assignments`;
- the same `/exams` experience hosted inside the installed WPF Launcher;
- English and French localization selected from the browser's language
  preferences, with English as the fallback;
- a sign-in-required recovery page when authentication is canceled; and
- antiforgery-protected sign-out.

In a normal browser, exam launching remains disabled. The installed WPF
Launcher hosts `/exams`, verifies the web origin and bridge messages,
requires native confirmation, and then calls the Device Agent through the
existing named pipe. Web content never receives direct access to privileged
device operations.

For local Entra configuration and Azure deployment instructions, see
[`infra\README.md`](infra/README.md).

## Local device prototype

The current vertical slice contains three applications:

| Component | Purpose |
| --- | --- |
| Exam Kiosk Launcher | Runs as the normal student and securely hosts the authenticated exam list. |
| Exam Kiosk Device Agent | Runs as a `LocalSystem` Windows Service and owns privileged transitions. |
| Restricted Exam Client | Runs as a native, permanent right-side panel in the exam account and owns **Open exam** and **Exam done**. |

The two WPF applications communicate with the agent over a local named pipe.
They never elevate and do not receive administrator credentials.

```text
Normal student launcher
	-> StartExam
	-> LocalSystem Device Agent
	-> apply Assigned Access
	-> restart into managed exam account
	-> Restricted Exam Client
	-> FinishExam
	-> LocalSystem Device Agent
	-> remove Assigned Access
	-> restart into normal Windows
```

The agent accepts only `GetStatus`, `StartExam`, `GetActiveExam`, and
`FinishExam`. Assigned
Access XML and PowerShell script paths are agent-owned resources; clients
cannot substitute scripts, commands, account names, or XML. `StartExam`
transports backend-owned executable metadata as part of the immutable exam
intent, which the Agent validates before generating Windows configuration.
The service resolves each named-pipe client's process ID and executable path:
only the installed launcher can request `StartExam`, and only the installed
restricted client can request `FinishExam`. Both must run in an interactive
Windows session.
The agent invokes `Start-Exam.ps1` to apply Assigned Access and
`Stop-Exam.ps1` to remove it.

### Prerequisites

- A disposable, district-managed Windows 11 test device or virtual machine.
- A Windows edition that supports the configured Assigned Access experience.
- .NET 10 SDK to build and install the prototype.
- Inbox Windows PowerShell 5.1 for all managed-device installation,
  configuration, recovery, and Device Agent scripts. PowerShell 7 is not
  required on student devices. Azure deployment scripts run from the
  administrator workstation and require PowerShell 7.
- Microsoft Edge installed in its standard machine-wide location.
- Microsoft Edge WebView2 Runtime installed machine-wide.
- A separate local administrator recovery account that is not the kiosk
  account.

Do not first test this on a device that lacks an independently verified
administrator recovery path.

### Install

The installer owns the Web application origin, while the identity bootstrap
owns the Device Agent authentication settings. Both scripts create
`%ProgramData%\ExamKiosk\deployment.settings.json` when needed and preserve
properties owned by the other script.

On a new device, sign in to Azure CLI with permission to update the
environment's app registrations and grant admin consent, then initialize the
Device Agent identity:

```powershell
az login
& .\scripts\Windows\Initialize-ExamKioskDeviceAgentIdentity.ps1 `
    -Environment dev
```

Run the identity bootstrap elevated on the disposable test device. It adds the
`ExamDevice.Agent` application role to the Web API, creates the environment's
Device Agent app registration, assigns that role, and creates a non-exportable
certificate in `LocalMachine\My`. It adds only the Device Agent authentication
settings to:

```text
%ProgramData%\ExamKiosk\deployment.settings.json
```

After Entra changes have propagated, install the applications and add the Web
origin:

```powershell
& .\scripts\Windows\Install-ExamKioskPoc.ps1 `
    -WebAppUrl 'https://exam-kiosk.example.org'
```

Later installations reuse the saved identity and Web origin. The uninstaller
preserves the complete `%ProgramData%\ExamKiosk` directory, including
configuration, agent state, certificate reference, and session history.

The installer publishes self-contained Windows applications under:

```text
%ProgramFiles%\ExamKiosk
```

It validates the WebView2 Runtime used by the Launcher, copies the
administrator-owned Web origin into the installed Launcher, writes the Device
Agent's API and certificate settings, registers
`ExamKioskDeviceAgent` as an automatic `LocalSystem` service, and adds
**Exam Kiosk Launcher** and **Recover Exam Kiosk Device** to the all-users
Start menu. In a managed rollout, Intune would perform this
administrator-controlled installation before exam day.

### Optional local Windows configuration

The installer does not change general Windows sign-in policies. For a local
test device, an administrator may separately disable the Windows first-sign-in
animation before validating the kiosk flow:

```powershell
& .\scripts\LocalConfig\Configure-ExamKioskWindowsPolicies.ps1
```

This optional machine-wide configuration is kept separate from installation so
district deployment tooling can own the equivalent policy.

To collect the local PoC state after a managed-device test, run the diagnostic
collector from the **Collect Exam Kiosk Diagnostics** shortcut created on the
installing administrator's Desktop. It requests elevation automatically. The
equivalent repository command is:

```powershell
& .\scripts\LocalConfig\Collect-ExamKioskDiagnostics.ps1
```

It creates an `ExamKiosk-Diagnostics-<computer>-<timestamp>.zip` archive on the
current administrator's Desktop. The archive includes the local Agent state,
session journal, Launcher logs, generated Windows artifacts, Edge URL-policy
state, Recovery results, service state, and basic Windows details. It can
contain student identity and exam URLs and must not be shared publicly.

### School-board customization hooks

The agent package includes two administrator-owned PowerShell hooks:

```text
src/ExamKiosk.DeviceAgent/Customization/OnExamStart.ps1
src/ExamKiosk.DeviceAgent/Customization/OnExamEnd.ps1
```

`OnExamStart.ps1` runs as `LocalSystem` when the Device Agent initializes and
confirms that Assigned Access is active. `OnExamEnd.ps1` runs as `LocalSystem`
before Assigned Access is removed and Windows restarts into the normal session. The
hooks are fixed agent-owned files; the launcher and restricted client cannot
select scripts, arguments, registry paths, or commands.

Boards may customize these files before publishing the agent to apply approved
device policies, such as Edge allowlists, sign-out restrictions, or Office
settings. Every temporary change made by `OnExamStart.ps1` must be restored by
`OnExamEnd.ps1`. The scripts run as `LocalSystem`, so they must be reviewed,
digitally controlled, and writable only by administrators or the deployment
system. A script failure aborts the transition and leaves the current session
in place for recovery.

### Run the flow

1. Sign in as a non-administrator student.
2. Open **Exam Kiosk Launcher** from the Start menu.
3. Select **Switch to exam** and confirm the restart.
4. After Windows restarts, Assigned Access signs in its managed **Exam Kiosk**
   account and starts the Restricted Exam Client.
5. The native Restricted Client reserves a permanent 320-pixel panel on the
   right and asks the Device Agent to activate the exact prepared session.
6. Select **Open exam**. The configured browser opens the session-bound exam
   target maximized in the remaining workspace; the student signs in there
   when required.
7. Return to the Restricted Exam Client, select **Exam done**, and confirm.
8. The agent removes Assigned Access and restarts Windows.

For normal entry and exit transitions, the Agent waits five seconds after
applying or removing Assigned Access and then requests an immediate Windows
restart. The active native client shows one Exam Kiosk-styled confirmation
window; after confirmation, that same window shows preparation progress and
then the synchronized countdown. Windows does not schedule a separate
countdown notification.

The launcher and restricted client require no UAC prompt. The preinstalled
service performs the privileged operations.

### Agent state

The agent serializes transitions and persists its state at:

```text
%ProgramData%\ExamKiosk\agent-state.json
```

The agent also persists the ordered local transition journal at:

```text
%ProgramData%\ExamKiosk\session-journal.json
```

The journal supports recovery across reboot or network loss. It records the
local session ID, current state, timestamps, and completion status for each
transition step. It is a local recovery record, not the proctor dashboard
telemetry channel.

The Launcher writes bounded JSON-lines diagnostics to:

```text
%ProgramData%\ExamKiosk\Logs\launcher.jsonl
```

Navigation entries contain only the origin and path. Query strings, fragments,
page content, cookies, and authentication tokens are not recorded. When an
exam start is prepared, the Launcher log records the Web session ID,
deterministic profile digest, complete effective exam intent, allowed URLs, and
Agent transition result. These configuration entries contain student identity,
exam URLs, executable paths, and policy data and must be handled as sensitive
operational diagnostics. Generated Assigned Access XML and shortcut artifacts
do not cross the Web-to-Launcher or Launcher-to-Agent boundaries.

The log rotates at 5 MB and retains one previous file. This student-writable
diagnostic log is useful for troubleshooting but is not an authoritative
audit record. The Device Agent enforcement receipt is the local authority for
what it accepted, and the backend must eventually receive protected audit
events. Once dynamic enforcement is implemented, the Agent must record the
exact configuration it actually applied; a Launcher entry records what was
requested, not proof that Windows applied it.

For development comparison, the Device Agent compiles the Windows
configuration from the validated intent and detected Windows version, then
writes the generated Assigned Access XML beside the packaged configuration:

```text
%ProgramFiles%\ExamKiosk\Agent\Configuration\AssignedAccess.generated.temp.xml
```

The file is atomically replaced on each valid start request and its SHA-256 is
recorded in the local session journal. `Start-Exam.ps1` applies this generated
file and the Agent verifies that its profile is configured. Windows then
restarts automatically after the application-owned countdown.

Desktop tool definitions may provide a Desktop Application ID for Start and
taskbar pins. The Agent uses that ID directly, as it does for Microsoft Word,
and declares a `.lnk` shortcut artifact only when a desktop tool has no such ID.
The Agent writes every declared Web tool to a generated shortcut manifest.
`Start-Exam.ps1` backs up and applies the generated Edge policy, creates those
shortcuts, and then applies Assigned Access. `Stop-Exam.ps1` removes Assigned
Access, restores the Edge policy backup, and removes only the Agent-owned
dynamic `tool-*.lnk` files during normal exam completion.

For this PoC, the Web is a controlled stub and its hard-coded exam intent is
treated as valid. The Device Agent's self-contained Windows configuration
component selects the supported generator and validates its generated artifact
SHA-256 integrity, bounded secure XML parsing, Exam Kiosk profile identity and
naming, Restricted Client auto-launch, and the absence of an exam shortcut
before writing the preview.

### Administrator recovery

Recovery is only an emergency escape from a device trapped in Assigned Access.
If Windows is already accessible, use the normal Reset, installation, or
uninstallation scripts instead.

If a test device remains restricted after a failed transition, sign in to an
administrator session and run **Recover Exam Kiosk Device** from the Start
menu, or run:

```powershell
& "$env:ProgramFiles\ExamKiosk\Recovery\Recover-ExamKioskDevice.ps1"
```

The script stops and leaves stopped the Device Agent, runs a one-time recovery
worker as `LocalSystem`, and removes and verifies only the known Exam Kiosk
Assigned Access profile. When an Agent-owned Edge policy backup exists, it
also restores and verifies that backup. It does not modify Agent state or
session history; use the normal Reset, installation, or uninstallation script
afterward. It writes a bounded recovery result under
`%ProgramData%\ExamKiosk\Recovery`. It does not restart Windows automatically;
after successful recovery, run:

```powershell
shutdown.exe /r /t 0
```

See [`scripts\Windows\Recovery\README.md`](scripts/Windows/Recovery/README.md)
for the execution order, engine separation, and emergency override procedure.

If the configured Assigned Access profile is not owned by Exam Kiosk, the
script refuses to remove it. `-ForceForeignAssignedAccess` is an emergency
override for administrators who have independently verified that the foreign
configuration must be removed. Recovery restores only the two Edge URL-policy
lists recorded in the Agent-owned backup and does not delete other machine
policies.

The PoC states are `available`, `enteringExam`, `inExam`, `exitingExam`, and
`failed`. The agent remains in `enteringExam` or `exitingExam` throughout the
restart countdown, so the opposite command is rejected. On service startup it
queries the Assigned Access CSP and reconciles every persisted state to
`inExam` when this PoC's profile is configured or `available` when Assigned
Access is absent. It enters `failed` rather than replacing an unrelated
Assigned Access configuration. A transition failure that does not restart the
device moves the agent to `failed`. Use the administrator recovery command for
the known Exam Kiosk profile; restore the test device from a trusted snapshot
if recovery cannot verify that restrictions were removed.

### Uninstall

Finish any active exam, then run as administrator:

```powershell
& .\scripts\Windows\Uninstall-ExamKioskPoc.ps1
```

The uninstaller refuses to continue unless the persisted agent state is
`available`.

### Prototype limitations

- Authenticated local users can connect to the named pipe, but the service
	authorizes commands only from the expected installed client executable. The
	PoC does not yet validate a signed, device-bound exam assignment or verify
	application signatures.
- The Device Agent uses an app-only certificate credential to activate and
  complete only the session whose ID and effective-profile digest match its
  local receipt. It has no Microsoft Graph or SharePoint permission.
- The Restricted Client receives no certificate, token, secret, or arbitrary
  URL. The Agent returns only the exam metadata persisted before restart.
- The PoC shares one application certificate on a device. Production requires
  a per-device identity or equivalent device-bound proof, certificate
  lifecycle management, and protected backend audit records.
- The backend session store is in memory; restarting the Web application loses
  active sessions.
- The Assigned Access profile assumes standard machine-wide installation paths
	for the Restricted Exam Client and Microsoft Edge.
- Automatic cleanup of browser identity, documents, and cached student data is
	not implemented yet.
- Microsoft notes that removing Assigned Access might not reverse every
	customization, such as an existing Start layout.

Use a disposable test device or a restorable virtual-machine snapshot for this
phase.

## Architecture

See [DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md) for the architecture and
security boundaries, and
[IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) for the incremental
implementation sequence. Deferred named-pipe and privileged profile controls
are tracked in
[SECURITY_HARDENING_PLAN.md](docs/SECURITY_HARDENING_PLAN.md).
