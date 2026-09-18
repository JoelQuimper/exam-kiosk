# Exam Kiosk security hardening plan

This plan records security work that is intentionally separated from the
current functional increments. The Device Agent runs as `LocalSystem`, so its
named-pipe boundary must be hardened before production use and before the
Agent trusts remotely composed profiles.

## Current controls

- The named-pipe ACL denies the Windows Network SID.
- Privileged commands are allow-listed by installed client executable path.
- Unknown commands and protocol versions are rejected.
- Requests and responses have bounded sizes and timeouts.
- Response request IDs must match their request.
- Privileged PowerShell uses fixed Agent-owned scripts and structured argument
  lists.
- Current requests do not provide script paths, commands, filesystem
  destinations, or Assigned Access XML to `LocalSystem`.

These controls reduce the attack surface but do not fully authenticate the
interactive client or the named-pipe server.

## Finding 1 - Bind commands to the authorized user and session

**Priority:** Medium  
**Confidence:** High

The Agent currently authorizes `StartExam` and `FinishExam` using the client
executable path and a nonzero Windows session ID. Any authenticated local user
who can launch the legitimate installed client executable can satisfy those
checks, including a user in another interactive or remote session.

### Required hardening

- Capture the connecting process through a stable process handle.
- Inspect its token rather than trusting only PID, path, and session number.
- Verify the expected signer or file identity for installed native clients.
- Bind `StartExam` to the active interactive student session.
- Bind `FinishExam` to the Windows session and Exam Kiosk session recorded in
  the Agent's enforcement receipt.
- Reject commands originating from unrelated local or RDP sessions.
- Restrict the pipe ACL to the narrowest practical principals while preserving
  the managed restricted-account flow.
- Add negative tests for copied binaries, alternate users, inactive sessions,
  RDP sessions, stale sessions, and mismatched exam-session IDs.

## Finding 2 - Authenticate the named-pipe server

**Priority:** Low before profile transport; higher once profiles cross the pipe  
**Confidence:** High

Clients currently connect using only the public pipe name. If the service is
stopped, another local process could create the pipe first and return forged
status or transition responses.

### Required hardening

- Create the service pipe with `PipeOptions.FirstPipeInstance`.
- On Windows, obtain and validate the connected pipe server process ID.
- Verify that the server is the expected installed Device Agent running as
  `LocalSystem`.
- Hold stable process handles during identity checks to reduce PID-reuse
  races.
- Add a cryptographic request/response binding if process validation cannot
  provide sufficient assurance.
- Test pipe squatting before service start, during recovery, and during service
  restart.

## Gate before dynamic profile enforcement

Before a Web-generated profile can affect privileged state, the Agent must:

- receive a session ID and a signature-ready profile envelope;
- authenticate and integrity-bind the profile to its backend assignment,
  student, device, and expiration;
- reject replayed, expired, unsigned, or mismatched envelopes;
- independently validate every semantic field rather than trust validation
  metadata supplied by the Web application;
- verify Assigned Access XML digest and allowed schema;
- canonicalize and contain every generated path within Agent-owned locations;
- prevent reparse-point traversal;
- validate executable publisher and hash independently;
- constrain shortcut and policy counts, sizes, identifiers, paths, and URLs;
- write files atomically with administrator-owned ACLs;
- never construct PowerShell command strings from profile data;
- persist an enforcement receipt that binds the applied state to the exact
  Exam Kiosk session and profile digest.

## Recommended milestone order

1. Add first-pipe-instance enforcement and client-side server validation.
2. Bind callers to the expected user, active Windows session, and Exam Kiosk
   session.
3. Add signed, short-lived, device-bound profile envelopes and replay
   protection.
4. Complete strict Agent-side profile validation.
5. Permit dynamic Assigned Access, shortcut, and Edge-policy enforcement.
6. Conduct a new focused security review before production deployment.

This milestone must be completed before treating the local Agent protocol as a
production security boundary.
