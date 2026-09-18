# Exam Kiosk implementation plan

This plan divides the remaining exam lifecycle into small increments that can
be understood, tested, and demonstrated independently.

## Current foundation

- The Web application resolves the authenticated student's assignments.
- The Web application composes and validates platform-neutral effective exam
  intent, including tools and Edge policy.
- The Launcher page atomically creates a `Starting` session and transports its
  ID and immutable intent through bridge protocol version 4.
- The Device Agent receives the session ID and immutable profile, persists
  their local receipt, validates the intent, detects the local Windows version,
  and generates and validates the Windows configuration locally.
- The Restricted Client still relies on the local Agent state and opens a
  placeholder URL.

## Step 0 - Administrator recovery

Provide an out-of-band recovery path that does not depend on the Launcher,
Restricted Client, or a healthy Device Agent.

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
- Reset the local Agent state and append a manual-recovery journal step.
- Preserve a recovery result for operator diagnostics.
- Restart the Agent if it was running.
- Require an explicit emergency option to remove a foreign profile.
- Leave Windows restart under operator control.

### Acceptance criteria

- Recovery works from an elevated administrator session when the Agent is
  unavailable.
- Running recovery when Assigned Access is already clear is idempotent.
- A foreign Assigned Access configuration is preserved by default.
- The script returns a nonzero result and a useful message when recovery fails.
- Successful recovery prints the explicit Windows restart command.

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

- Agent protocol version 3 and pipe name `ExamKiosk.DeviceAgent.v3`;
- a typed `StartExam` payload containing the Web session ID and immutable
  effective exam intent;
- a 256 KiB request limit enforced by both client and service;
- command-specific payload validation;
- Launcher forwarding of the bridge-validated session and profile;
- a Launcher diagnostic snapshot containing the complete requested profile,
  Edge policy, and deterministic digest, but no generated Windows artifact;
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
- Create only the tool shortcut artifacts declared by the profile. The exam
  itself is opened by the auto-launched Restricted Client and is not represented
  by a persistent `.lnk` file. Desktop tools with a Desktop Application ID are
  pinned directly; a `.lnk` is declared only as their fallback.
- Verify Assigned Access after application.
- Roll back partial changes after failure.

## Step 6 - Edge policy application and restoration

- Back up only the Edge policy values that Exam Kiosk will replace.
- Apply the exact blocklist and allowlist from the effective profile.
- Verify the written policy values.
- Record ownership and backup information in the enforcement receipt.
- Restore the previous values during finish, rollback, and administrator
  recovery.
- Never delete unrelated administrator or MDM policies.

## Step 7 - Restricted Client session activation

- Add an authenticated atomic activation operation.
- Find the current student's non-expired `Starting` session.
- Transition it to `Active` only after restricted-session authentication.
- Return the active exam metadata and SharePoint folder URL.
- Render the active exam rather than a generic placeholder.

## Step 8 - Open the real exam destination

- Increment the Restricted Client bridge protocol.
- Retrieve the active session's authorized URL through the Device Agent and
  send it to native code.
- Remove the fixed example URL.
- Validate that the URL belongs to the active session before opening Edge.
- Keep **Open Exam** available for the full active session so the student can
  relaunch Edge after closing it. Disable it only while a launch request is in
  flight, then re-enable it whether that request succeeds or fails.
- Keep the Restricted Client itself open while the Agent reports `InExam`, and
  fail closed when Agent status cannot be verified.
- Keep all arbitrary URLs unavailable to untrusted Web content.

## Step 9 - Coordinated completion

- Transition the Web session to `Completing`.
- Ask the Agent to remove Assigned Access, restore Edge policy, and remove
  recorded shortcuts.
- Persist local cleanup results.
- Transition the Web session to `Completed` only after successful cleanup.
- Preserve a recoverable non-success state if cleanup fails.
- Restart Windows after the application-owned countdown.

## Recommended execution order

Implement the steps in numerical order. In particular, create the Web session
before extending the Agent protocol so the session identifier is part of the
transport and local recovery record from the beginning.
