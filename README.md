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
| Restricted Exam Client | Hosts the authenticated `/exam-session` page in the exam account and owns the native **Open exam** and **Exam done** actions. |

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

The agent accepts only `GetStatus`, `StartExam`, and `FinishExam`. Assigned
Access XML and PowerShell script paths are fixed agent-owned resources; clients
cannot provide scripts, commands, executable paths, account names, or XML.
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
- Microsoft Edge installed in its standard machine-wide location.
- Microsoft Edge WebView2 Runtime installed machine-wide.
- A separate local administrator recovery account that is not the kiosk
  account.

Do not first test this on a device that lacks an independently verified
administrator recovery path.

### Install

Open PowerShell as an administrator from the repository root and run:

```powershell
& .\scripts\Windows\Install-ExamKioskPoc.ps1
```

On the first installation, the installer prompts for the Exam Kiosk web
application HTTPS origin and saves it in:

```text
%ProgramData%\ExamKiosk\deployment.settings.json
```

Later installations and resets reuse that machine configuration without
prompting. The uninstaller preserves the complete `%ProgramData%\ExamKiosk`
directory, including configuration, agent state, and session history. For unattended deployment,
administrators can provide or replace the saved value with:

```powershell
& .\scripts\Windows\Install-ExamKioskPoc.ps1 `
    -WebAppUrl 'https://exam-kiosk.example.org'
```

The installer publishes self-contained Windows applications under:

```text
%ProgramFiles%\ExamKiosk
```

It validates the WebView2 Runtime, copies the administrator-owned web
application origin into the installed Launcher and Restricted Client
directories, registers
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
5. The Restricted Client loads `/exam-session` and asks the student to sign in
   again because its isolated profile cannot reuse the Launcher's cookie.
6. After the assigned exam appears, select **Open exam** to open the
   native-owned placeholder URL in Edge.
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

The Launcher and Restricted Client write bounded JSON-lines diagnostics to:

```text
%ProgramData%\ExamKiosk\Logs\launcher.jsonl
%ProgramData%\ExamKiosk\Logs\restricted-client.jsonl
```

Navigation entries contain only the origin and path. Query strings, fragments,
page content, cookies, and authentication tokens are not recorded. When an
exam start is prepared, the Launcher log records the Web session ID,
deterministic profile digest, complete effective profile, exact Assigned Access
XML, Edge policy, shortcut artifacts, and Agent transition result. These
configuration entries contain student identity, exam URLs, executable paths,
and policy data and must be handled as sensitive operational diagnostics.

Each log rotates at 5 MB and retains one previous file. These student-writable
diagnostic logs are useful for troubleshooting but are not an authoritative
audit record. The Device Agent enforcement receipt is the local authority for
what it accepted, and the backend must eventually receive protected audit
events. Once dynamic enforcement is implemented, the Agent must record the
exact configuration it actually applied; a Launcher entry records what was
requested, not proof that Windows applied it.

### Administrator recovery

If a test device remains restricted after a failed transition, sign in to an
administrator session and run **Recover Exam Kiosk Device** from the Start
menu, or run:

```powershell
& "$env:ProgramFiles\ExamKiosk\Recovery\Recover-ExamKioskDevice.ps1"
```

The script stops the Device Agent, runs a one-time recovery worker as
`LocalSystem`, removes and verifies only the known Exam Kiosk Assigned Access
profile, resets the local Agent state, and restarts the service if it was
running. It writes a bounded recovery result under
`%ProgramData%\ExamKiosk\Recovery`. It does not restart Windows automatically;
after successful recovery, run:

```powershell
shutdown.exe /r /t 0
```

If the configured Assigned Access profile is not owned by Exam Kiosk, the
script refuses to remove it. `-ForceForeignAssignedAccess` is an emergency
override for administrators who have independently verified that the foreign
configuration must be removed. Future Edge-policy work must extend this
recovery script to restore the Agent-owned policy backup rather than deleting
machine policies indiscriminately.

The PoC states are `available`, `enteringExam`, `inExam`, `exitingExam`, and
`failed`. The agent remains in `enteringExam` or `exitingExam` throughout the
restart countdown, so the opposite command is rejected. On service startup it
queries the Assigned Access CSP and reconciles every persisted state to
`inExam` when this PoC's profile is configured or `available` when Assigned
Access is absent. It enters `failed` rather than replacing an unrelated
Assigned Access configuration. Starting from `inExam` is idempotent: the agent
reapplies its profile and schedules the restart. A transition failure that does
not restart the device moves the agent to `failed`. Use the administrator
recovery command for the known Exam Kiosk profile; restore the test device from
a trusted snapshot if recovery cannot verify that restrictions were removed.

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
- The placeholder exam is `https://www.example.com/`; SharePoint and Microsoft
	365 authentication are not implemented yet.
- `/exam-session` requires a second Entra sign-in and can retrieve the
  authenticated student's assigned exam metadata. It cannot provide an
  arbitrary exam URL to native code. Dynamic SharePoint URLs require a signed,
  device-bound effective policy resolved before restart.
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
