# Exam Kiosk implementation plan

This plan divides the remaining exam lifecycle into small increments that can
be understood, tested, and demonstrated independently.

## Current foundation

- The Web application resolves the authenticated student's assignments.
- The Web application composes and validates platform-neutral effective exam
  intent, including tools and allowed URLs.
- The Launcher page atomically creates a `Starting` session and transports its
  ID and immutable intent through bridge protocol version 4.
- The Device Agent receives the session ID and immutable profile, persists
  their local receipt, validates the intent, detects the local Windows version,
  and generates and validates the Windows configuration locally.
- The native Restricted Client asks the LocalSystem Device Agent to activate
  the matching Web session and opens only the exam target persisted
  in the local receipt.

## Step 0 - Administrator recovery

Provide an out-of-band recovery path that does not depend on the Launcher,
Restricted Client, or a healthy Device Agent. Its only purpose is to return
control of Windows to an administrator when Assigned Access traps the device;
normal maintenance uses Reset, installation, or uninstallation.

### Scope

- Install an administrator-visible **Recover Exam Kiosk Device** shortcut.
- Stop the Device Agent to prevent concurrent transitions.
- Execute the Assigned Access removal through a temporary `LocalSystem` task.
- Remove only the known Exam Kiosk profile by default.
- Name generated profiles `EXAM — <student alias> — <exam ID>` for operator
  visibility and inventory. Recovery must not use that prefix alone as proof
  of ownership; deletion remains bound to the known profile GUID or an
  Agent-owned receipt.
- Verify that Assigned Access is clear.
- Preserve a recovery result for operator diagnostics.
- Leave the Agent stopped; reset or reinstall it after recovery.
- Require an explicit emergency option to remove a foreign profile.
- Leave Windows restart under operator control.

### Acceptance criteria

- Recovery works from an elevated administrator session when the Agent is
  unavailable.
- Running recovery when Assigned Access is already clear is idempotent.
- A foreign Assigned Access configuration is preserved by default.
- The script returns a nonzero result and a useful message when recovery fails.
- Successful recovery prints the explicit Windows restart command and leaves
  the Agent stopped.

When dynamic Edge policy and shortcuts are introduced, this step must be
extended to restore the Agent-owned Edge-policy backup and remove only the
shortcuts recorded in the enforcement receipt.

## Step 1 - Atomic Web session creation

Implemented on 2026-09-18:

- an in-memory exam-session store and the `Starting`, `Active`, `Completing`,
  `Completed`, `Cancelled`, and `Expired` states;
- an authenticated, antiforgery-protected atomic start operation;
- assignment ownership verification before profile composition;
- a unique session containing the immutable effective profile;
- one nonterminal session per student, matched case-insensitively;
- automatic expiration of abandoned `Starting` sessions after 15 minutes.

Step 2 replaced and removed the temporary profile retrieval endpoint.

## Step 2 - Session-aware Web-to-Launcher bridge

Implemented on 2026-09-18:

- the Launcher page creates a `Starting` session through the atomic start
  operation and sends its ID and immutable profile to the Launcher;
- bridge protocol version 4 requires a nonempty session ID and carries no
  generated Windows artifacts;
- student cancellation, Launcher busy state, and Agent failure cancel the
  prepared Web session;
- cancellation is authenticated, antiforgery-protected, owner-scoped, and
  idempotent;
- successful Agent acceptance leaves the session `Starting`;
- the temporary profile-only endpoint was removed.

## Step 3 - Launcher-to-Agent profile transport

Implemented on 2026-09-18:

- Agent protocol version 4 and pipe name `ExamKiosk.DeviceAgent.v4`;
- a typed `StartExam` payload containing the Web session ID and immutable
  effective exam intent;
- a 256 KiB request limit enforced by both client and service;
- command-specific payload validation;
- Launcher forwarding of the bridge-validated session and profile;
- a Launcher diagnostic snapshot containing the complete requested profile,
  allowed URLs, and deterministic digest, but no generated Windows artifact;
- local journal persistence of the Web session ID and deterministic profile
  SHA-256 before privileged application begins.

Step 4 added an atomically written `AssignedAccess.generated.temp.xml` beside
the packaged Agent configuration. Step 5A now passes that generated file to
`Start-Exam.ps1`, verifies the configured profile, and restarts Windows after
the application-owned countdown.

Before Step 3 is enabled outside the development PoC, complete the applicable
named-pipe identity work in
[SECURITY_HARDENING_PLAN.md](SECURITY_HARDENING_PLAN.md). Dynamic enforcement
remains gated on all pre-profile controls in that plan.

## Step 4 - Agent generation and enforcement receipt

- For this PoC, the Web is a controlled stub and its hard-coded effective exam
  intent is treated as valid rather than duplicated through a separate
  validation layer.
- The Agent detects the actual Windows version and
  uses its self-contained Windows configuration component to generate Assigned
  Access XML and shortcut artifacts locally. The Windows-specific validator
  verifies artifact metadata, Windows/schema versions, SHA-256, bounded secure
  XML parsing, profile/default IDs, profile naming, Restricted Client
  auto-launch, and absence of an exam shortcut before the preview is written.
- Persist a local enforcement receipt containing session, assignment, profile
  digest, Assigned Access digest, and completed steps.
- Keep signature and replay-protection fields ready for production hardening.

## Step 5 - Apply dynamic Assigned Access and shortcuts

- **Implemented in Step 5A:** apply the locally generated and validated XML
  through `Start-Exam.ps1`, then verify that the expected profile is configured.
- Verify its digest after writing.
- Apply that XML instead of the packaged fixed file.
- **Implemented for Web tools:** write every declared Web tool to a generated
  manifest. `Start-Exam.ps1` creates the shortcuts before applying Assigned
  Access and rolls them back on failure; `Stop-Exam.ps1` removes Assigned Access
  and only the Agent-owned `tool-*.lnk` shortcuts. Administrator recovery
  performs the same targeted cleanup. The exam
  itself is opened by the auto-launched Restricted Client and is not represented
  by a persistent `.lnk` file. Desktop tools with a Desktop Application ID are
  pinned directly; a `.lnk` is declared only as their fallback.
- Verify Assigned Access after application.
- Roll back partial changes after failure.

## Step 6 - Edge policy application and restoration

Deferred until after the Steps 7-8 vertical flow is validated on the managed
test device.

- Back up only the Edge policy values that Exam Kiosk will replace.
- Apply an Agent-owned deny-all baseline and allow the profile's generic
  `AllowedUrls`.
- Verify the written policy values.
- Record ownership and backup information in the enforcement receipt.
- Restore the previous values during finish, rollback, and administrator
  recovery.
- Never delete unrelated administrator or MDM policies.

## Step 7 - Restricted Client session activation

Implemented on 2026-09-21 and redesigned as a native flow:

- app-only activate and complete endpoints protected by the dedicated
  `AgentBearer` scheme and `ExamDevice.Agent` application role;
- exact session-ID and effective-profile-digest binding;
- certificate authentication performed only by the LocalSystem Device Agent;
- no Graph or SharePoint application permission;
- idempotent transition from `Starting` to `Active`;
- a native Restricted Client with no WebView or second Web-app sign-in;
- a 320-pixel Windows AppBar reserved on the right, with normal close blocked
  while the exam is active.

## Step 8 - Open the real exam destination

Implemented on 2026-09-21, pending managed-device validation:

- Agent protocol version 4 exposes the locally persisted exam metadata only to
  the installed Restricted Client after backend activation succeeds;
- the fixed example URL was removed and the prepared exam target is opened in
  a maximized InPrivate Edge window in the AppBar's remaining work area;
- **Open Exam** remains available for relaunch after Edge closes;
- the Restricted Client remains open while the Agent reports `InExam` and
  fails closed when Agent status cannot be verified;
- the student signs in to SharePoint only in Edge; the Agent application
  identity never impersonates the student;
- arbitrary URLs remain unavailable to Web content or the native panel.

## Step 9 - Coordinated completion

- Ask the Agent to remove Assigned Access, restore Edge policy, and remove
  recorded shortcuts.
- Persist local cleanup results.
- Ask the Agent to transition the exact backend session to `Completed` only
  after successful local cleanup.
- Preserve a recoverable non-success state if cleanup fails.
- Restart Windows after the application-owned countdown.

The current implementation fails closed if backend completion fails after
local cleanup: the Agent enters `Failed` and does not schedule the restart.
Retry/reconciliation across reboot remains a production design item.

## Recommended execution order

The Web session and local receipt were created before extending the Agent
protocol so the session identifier is bound to the transport and recovery
record. For the development PoC, Steps 7-8 were intentionally wired before
Step 6 to validate the complete real-destination user flow before introducing
temporary Edge policy mutation. Resume with Step 6 after managed-device
validation of the Steps 7-8 flow.

For the PoC, the installer owns and persists the Web application origin, while
`Initialize-ExamKioskDeviceAgentIdentity.ps1` owns the Agent tenant,
application, API, and certificate identifiers. Each script creates the shared
configuration when absent and merges only its own properties when present, so
configuration initialization is order-independent. The installer remains the
final validation boundary and refuses to deploy an Agent whose combined
configuration is incomplete. All managed-device scripts, including identity
initialization, use the inbox Windows PowerShell 5.1 runtime; PowerShell 7 is
not a device prerequisite. The bootstrap certificate is non-exportable in
`LocalMachine\My`, but the shared application identity is not sufficient for
production; replace it with per-device identity or equivalent device-bound
proof.
