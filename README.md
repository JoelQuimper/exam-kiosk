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

## Local device prototype

The current vertical slice contains three applications:

| Component | Purpose |
| --- | --- |
| Exam Kiosk Launcher | Runs as the normal student and displays one **Bogus exam**. |
| Exam Kiosk Device Agent | Runs as a `LocalSystem` Windows Service and owns privileged transitions. |
| Restricted Exam Client | Starts automatically in the exam account and provides **Open exam** and **Exam done** actions. |

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
- A separate local administrator recovery account that is not the kiosk
  account.

Do not first test this on a device that lacks an independently verified
administrator recovery path.

### Install

Open PowerShell as an administrator from the repository root and run:

```powershell
& .\scripts\Install-ExamKioskPoc.ps1
```

The installer publishes self-contained Windows applications under:

```text
%ProgramFiles%\ExamKiosk
```

It registers `ExamKioskDeviceAgent` as an automatic `LocalSystem` service and
adds **Exam Kiosk Launcher** to the all-users Start menu. In a managed rollout,
Intune would perform this administrator-controlled installation before exam
day.

### Run the flow

1. Sign in as a non-administrator student.
2. Open **Exam Kiosk Launcher** from the Start menu.
3. Select **Switch to exam** and confirm the restart.
4. After Windows restarts, Assigned Access signs in its managed **Exam Kiosk**
	 account and starts the Restricted Exam Client.
5. Select **Open exam** to open the placeholder URL in Edge.
6. Return to the Restricted Exam Client, select **Exam done**, and confirm.
7. The agent removes Assigned Access and restarts Windows.

The launcher and restricted client require no UAC prompt. The preinstalled
service performs the privileged operations.

### Agent state

The agent serializes transitions and persists its state at:

```text
%ProgramData%\ExamKiosk\agent-state.json
```

The PoC states are `available`, `enteringExam`, `inExam`, `exitingExam`, and
`failed`. The agent remains in `enteringExam` or `exitingExam` throughout the
restart countdown, so the opposite command is rejected. On service startup it
queries the Assigned Access CSP and reconciles every persisted state to
`inExam` when this PoC's profile is configured or `available` when Assigned
Access is absent. It enters `failed` rather than replacing an unrelated
Assigned Access configuration. Starting from `inExam` is idempotent: the agent
reapplies its profile and schedules the restart. A transition failure that does
not restart the device moves the agent to `failed`. For this disposable PoC,
replace or restore the test device rather than attempting an in-place repair.

### Uninstall

Finish any active exam, then run as administrator:

```powershell
& .\scripts\Uninstall-ExamKioskPoc.ps1
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
- The Assigned Access profile assumes standard machine-wide installation paths
	for the Restricted Exam Client and Microsoft Edge.
- Automatic cleanup of browser identity, documents, and cached student data is
	not implemented yet.
- Microsoft notes that removing Assigned Access might not reverse every
	customization, such as an existing Start layout.

Use a disposable test device or a restorable virtual-machine snapshot for this
phase.

## Architecture

See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for the architecture, security
boundaries, implementation stack, and phased development plan.

