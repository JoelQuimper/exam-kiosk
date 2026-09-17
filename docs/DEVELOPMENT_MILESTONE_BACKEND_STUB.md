# Development Milestone: Backend Stub and Dynamic Exam Policy

Date: 2026-09-16

## Status at This Milestone

The Phase 0 core device flow is working:

- the authenticated Web App displays assigned exams;
- the normal-session WPF Launcher hosts `/exams`;
- the Device Agent applies Assigned Access and owns privileged transitions;
- the Restricted Client hosts the authenticated `/exam-session` experience;
- both native clients use a single styled confirmation and restart countdown;
- the restart delay is five seconds;
- the Agent waits for the application countdown and then invokes an immediate
  Windows restart, avoiding the additional Windows restart notification;
- native English and French resources are synchronized;
- diagnostic logging and the native Restricted Client finish fallback remain
  available.

The latest full solution test run passed 105 unit tests. After making the exam
name mandatory in the `startExam` bridge contract, the eight targeted Launcher
bridge tests also passed. No live application, browser, deployment, restart, or
Assigned Access tests were run by the assistant.

The user performs all manual and live tests.

## Current Bridge Contract

An exam always has a name. The Launcher rejects a `startExam` request that does
not include a valid exam title.

Current shape:

```json
{
  "version": 1,
  "type": "startExam",
  "requestId": "00000000-0000-0000-0000-000000000000",
  "exam": {
    "title": "Mathématiques secondaire 4 — Modélisation financière"
  }
}
```

The exam title is currently used only to display the localized native
confirmation title:

```text
Démarrage de l'examen : <nom de l'examen>
Starting exam: <exam name>
```

The warning contains no implementation details. It asks the student to save
their work because the computer will restart.

The contract will be expanded in the next milestone to carry a complete
effective exam policy.

## Goal of the Next Milestone

Stub the backend inside the existing Web App to enrich both native clients and
generate Assigned Access dynamically for the selected student assignment.

The stub will represent the same domain objects that a future Web App
administration module and persistent database will manage. Replacing the stub
must not require redesigning the client or Device Agent contracts.

## Increment 1: Read-Only Assignment API

Implemented on 2026-09-17:

- immutable stub students, exams, assignments, and display-safe tool metadata;
- case-insensitive lookup using the authenticated Entra UPN;
- authenticated `GET /api/v1/exam-assignments`, which returns only the current
  student's assignments;
- controller authentication required by default, with only the health endpoint
  explicitly anonymous;
- API authentication failures returned as HTTP 401 instead of an identity
  provider redirect;
- Student 4 and unknown authenticated UPNs represented by an empty assignment
  collection;
- response DTOs that omit SharePoint URLs, executable paths, packaged
  application identifiers, and Web allowlist data.

The Razor exam list now uses the same assignment service and Entra UPN
resolution as the API. `/exams` is the single exam-list route for both normal
browsers and the WPF Launcher. The prototype bogus exam was removed from the
exam list and Restricted Client page. Exam-session state, effective-profile
generation, and the atomic start endpoint remain deferred to later increments.

## Stub Students

The following development accounts will be hardcoded into the Web App student
profiles for this milestone:

| Student | User principal name |
| --- | --- |
| Student 1 | `student1@jqdev.onmicrosoft.com` |
| Student 2 | `student2@jqdev.onmicrosoft.com` |
| Student 3 | `student3@jqdev.onmicrosoft.com` |
| Student 4 | `student4@jqdev.onmicrosoft.com` |

UPN matching must be case-insensitive and based on the authenticated Entra
identity.

## Stub Exams

| Exam ID | Display name | Card icon |
| --- | --- | --- |
| `exam-1` | Mathématiques secondaire 4 — Modélisation financière | Calculator |
| `exam-2` | Sciences secondaire 4 — Analyse de données | Chart |

The icon becomes exam data. It must no longer be a single hardcoded glyph shared
by every exam card.

## Student Assignments

| Student | Exam | Tools | SharePoint folder |
| --- | --- | --- | --- |
| Student 1 | Exam 1 | Calculator | `Student1-Exam1` |
| Student 1 | Exam 2 | None | `Student1-Exam2` |
| Student 2 | Exam 1 | Microsoft Word | `Student2-Exam1` |
| Student 3 | Exam 2 | Microsoft Word, Calculator, and Usito Dictionary | `Student3-Exam2` |
| Student 4 | None | None | None |

The folders are located in:

```text
https://jqdev.sharepoint.com/sites/ExamSite/Shared Documents
```

For this stub, **Open exam** opens the assigned student's SharePoint folder,
not a specific document. The effective policy must contain a normalized HTTPS
URL for the exact folder.

## Student 4 and Identity-Mismatch Scenario

Student 4 cannot normally enter exam mode from the Launcher because no exam is
assigned.

The intended test is:

1. another student selects an exam and enters restricted mode;
2. Student 4 authenticates inside the Restricted Client;
3. the Web App finds no active assignment for Student 4;
4. the Restricted Client displays a blocked **No exam assigned** experience;
5. no exam URL or **Open exam** action is available;
6. only the secure **Exit exam mode** action remains available.

The Assigned Access profile was created before the second authentication.
Applications allowed for the originally selected student can therefore remain
visible at the Windows level until the device exits exam mode. The mismatch
screen must not expose the selected exam or permit it to be opened.

## Domain Model

The stub should be organized as replaceable backend services rather than
scattered conditionals in Razor components.

Conceptual model:

```text
StudentProfile
├── StudentId
├── UserPrincipalName
├── DisplayName
└── Assignments

ExamDefinition
├── ExamId
├── Title
├── Course
├── Description
├── DurationMinutes
└── Icon

StudentExamAssignment
├── AssignmentId
├── StudentId
├── ExamId
├── SharePointFolderUrl
└── ToolIds

ToolDefinition
├── ToolId
├── DisplayName
├── Icon
├── Applications
├── TaskbarLaunchTargets
├── RequiredWebDestinations
└── Validation metadata

ToolLaunchTarget
├── Type: native or web
├── ApplicationId for a native target
└── WebEntryUrl for an Edge target

ApplicationDefinition
├── ApplicationId
├── Type: desktop or packaged
├── DesktopAppPath
├── AppUserModelId
├── Publisher or hash metadata
└── Required helper processes represented as unpinned applications

ExamSession
├── SessionId
├── StudentId
├── AssignmentId
├── State: starting, active, cancelled, or completed
├── EffectiveProfile
├── StartedAtUtc
└── ExpiresAtUtc
```

Suggested service boundaries:

```text
IStudentProfileStore
IExamDefinitionCatalog
IToolCatalog
IExamAssignmentService
IExamSessionStore
IEffectiveExamPolicyFactory
```

The first implementations can use immutable hardcoded collections and an
in-memory exam-session store.

## Tool Catalog Decision

Do not restrict the architecture to hardcoded capability enums such as
`calculator` or `microsoftWord`.

The future Web App administration module must let an authorized administrator
create and manage complete tool definitions. Exams and student assignments
reference those definitions by ID. The backend resolves the complete
definitions into the effective exam policy.

Initial tool definitions:

### Microsoft Word

The test devices use Microsoft 365 Apps 64-bit with the standard Office16
installation:

```text
%ProgramFiles%\Microsoft Office\root\Office16\WINWORD.EXE
```

### Windows Calculator

```text
Microsoft.WindowsCalculator_8wekyb3d8bbwe!App
```

### Usito Dictionary

Student 3 is assigned a third tool of type `web`:

```text
https://usito.usherbrooke.ca/
```

The tool definition contains:

```json
{
  "id": "usito-dictionary",
  "displayName": "Dictionnaire Usito",
  "icon": "dictionary",
  "applications": [],
  "taskbarLaunchTargets": [
    {
      "type": "web",
      "webEntryUrl": "https://usito.usherbrooke.ca/"
    }
  ],
  "requiredWebDestinations": [
    "https://usito.usherbrooke.ca"
  ]
}
```

The Agent creates a dedicated Edge taskbar shortcut only when this tool is
present in the effective profile. The shortcut target is generated from the
validated `webEntryUrl`; the browser cannot provide or replace the URL.

Assigned Access controls whether Edge can run, but it does not restrict which
sites Edge can visit. Enforcing a Web tool therefore also requires the Agent to
apply the exact temporary Edge policy supplied in the backend profile, then
restore the previous Edge policy when the exam ends. The backend merges the
exam SharePoint destinations, Microsoft authentication dependencies, and every
assigned Web tool destination. The resulting policy uses `URLBlocklist` with
`*` and a narrowly scoped `URLAllowlist`.

The model must support multiple application entries because a tool may require
helper processes or more than one executable. It must also support Web-only
tools that have no Windows application entry. Applications without a taskbar
launch target are allowed helper processes and remain hidden from the taskbar.

## Effective Exam Policy

The backend resolves:

```text
Exam definition
+ student assignment
+ assigned tool definitions
+ student identity
+ SharePoint folder
= effective exam policy
```

Conceptual payload:

```json
{
  "schemaVersion": 1,
  "sessionId": "00000000-0000-0000-0000-000000000000",
  "assignmentId": "student3-exam2",
  "student": {
    "userPrincipalName": "student3@jqdev.onmicrosoft.com"
  },
  "exam": {
    "id": "exam-2",
    "title": "Sciences secondaire 4 — Analyse de données",
    "icon": "chart",
    "sharePointFolderUrl": "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Student3-Exam2"
  },
  "tools": [
    {
      "id": "microsoft-word",
      "displayName": "Microsoft Word",
      "applications": [
        {
          "type": "desktop",
          "path": "%ProgramFiles%\\Microsoft Office\\root\\Office16\\WINWORD.EXE"
        }
      ]
    },
    {
      "id": "windows-calculator",
      "displayName": "Calculatrice",
      "applications": [
        {
          "type": "packaged",
          "appUserModelId": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
        }
      ]
    },
    {
      "id": "usito-dictionary",
      "displayName": "Dictionnaire Usito",
      "icon": "dictionary",
      "applications": [],
      "taskbarLaunchTargets": [
        {
          "type": "web",
          "webEntryUrl": "https://usito.usherbrooke.ca/"
        }
      ],
      "requiredWebDestinations": [
        "https://usito.usherbrooke.ca"
      ]
    }
  ],
  "edgePolicy": {
    "urlBlocklist": [
      "*"
    ],
    "urlAllowlist": [
      "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Student3-Exam2",
      "https://usito.usherbrooke.ca"
    ]
  },
  "issuedAtUtc": "2026-09-16T18:30:00Z",
  "expiresAtUtc": "2026-09-16T22:30:00Z"
}
```

The example allowlist is abbreviated. The effective profile must also contain
the validated Microsoft authentication and SharePoint dependency entries
required by the complete sign-in and exam flow.

The exact contract must be defined in the shared Contracts project and
versioned independently from the named-pipe envelope where practical. It must
be transported in a signature-ready envelope containing payload, signature
algorithm, signature, and key-identifier fields. Signing and Agent signature
verification are deferred to a later milestone; adding them must not require a
contract redesign.

## Component Responsibilities

### Web App

- require authenticated Entra identities for every endpoint except health
  checks;
- identify the authenticated student by UPN;
- return only assignments belonging to that student;
- authorize students to start, read, activate, or cancel only their own exam
  sessions;
- atomically create a `Starting` exam session and return its immutable
  effective profile;
- store exam sessions in memory for the stub;
- resolve full tool definitions;
- create the authoritative effective profile, taskbar targets, and exact Edge
  policy;
- change a matching `Starting` session to `Active` when the same student
  authenticates in the Restricted Client;
- cancel failed or declined starts and expire abandoned `Starting` sessions;
- later host the administrator UI and persistent catalogs;
- eventually sign effective policies.

### WPF Launcher

- display the hosted Launcher page;
- transport the structured effective profile unchanged;
- display the exam name in the native confirmation;
- never apply Windows configuration itself;
- send a typed apply request to the Device Agent;
- ensure a declined confirmation or Agent failure cancels the API session.

### Device Agent

- remain the only component that applies Assigned Access;
- remain an enforcement boundary rather than deciding which tools, taskbar
  targets, or Web destinations belong to an exam;
- validate the effective profile before changing Windows;
- expand and validate application paths;
- ensure desktop executables are installed;
- reject user-writable executable locations;
- validate packaged-app identifiers;
- generate Assigned Access XML dynamically;
- add baseline applications required by the Restricted Client, WebView2, and
  Edge;
- add assignment-specific Word, Calculator, and future native tool entries;
- generate the taskbar layout and Agent-owned native and Edge shortcuts from
  the supplied taskbar launch targets;
- validate the supplied Edge blocklist, allowlist, Web entry URLs, and URL
  limits without adding business destinations itself;
- apply and later restore the supplied temporary Edge policy;
- persist an enforcement and recovery receipt under `%ProgramData%\ExamKiosk`,
  including the session ID, profile hash, generated artifacts, and previous
  Edge policy state;
- remove the persisted enforcement receipt when the exam ends successfully.

### Restricted Client

- host the authenticated `/exam-session` page;
- call one authenticated API operation that finds the student's `Starting`
  session, changes it to `Active`, and returns its metadata;
- make activation idempotent for the same student's already-`Active` session
  so that refreshing the Restricted Client remains safe;
- display the selected exam and tools;
- obtain the effective SharePoint folder URL from the authenticated API
  response;
- retain the native finish fallback when Web content or assignment resolution
  fails.

## Active Selection and Persistence

For this stub, the Web App stores:

```text
SessionId
+ normalized UPN
+ selected AssignmentId
+ state
+ immutable effective profile
+ issue and expiry timestamps
```

An in-memory singleton exam-session store is sufficient. A single authenticated
start operation must verify the assignment, create the `Starting` session, and
return the exact effective profile atomically. The session must survive the
student device restart but is not required to survive an App Service restart or
Web App redeployment.

After reboot, the Restricted Client queries the API using the newly
authenticated identity through one atomic activation operation. A matching
`Starting` session becomes `Active` and its exam metadata and SharePoint folder
are returned. The same operation returns an already-`Active` matching session
without another transition. A different student receives no session or exam
URL. Abandoned `Starting` sessions expire after a bounded period.

The Device Agent persists only the local information needed to reconcile and
undo Windows enforcement. The API remains the authority for session state,
student identity, exam metadata, and URLs.

Known stub limitation: although sessions have unique IDs, they are not yet
bound to a device. A later backend must bind the active session to assignment,
device, student, and session identifiers.

## Security Boundary

The backend is intended to become the authority for administrator-defined tool
metadata, including executable paths. Therefore the final design cannot rely
only on a fixed Agent-side mapping of two capability names.

However, the Agent must not blindly trust arbitrary browser JSON.

For the stub:

- accept only a versioned schema;
- require a unique session ID and bounded validity period;
- enforce strict field and collection limits;
- allow only HTTPS SharePoint URLs under the configured tenant and site;
- require every Web tool to have an HTTPS entry URL whose origin is present in
  its bounded `requiredWebDestinations`;
- require the supplied Edge blocklist to deny all external URLs and validate
  every supplied allowlist entry;
- expand environment variables before validation;
- reject relative paths and user-writable locations;
- require referenced executable files to exist;
- reject unknown application types;
- generate XML locally rather than accepting raw Assigned Access XML;
- never accept arbitrary PowerShell, commands, or script paths.

For the production architecture, the effective policy must also be:

- signed by the backend;
- short-lived;
- bound to the student, assignment, session, and device;
- protected against replay;
- validated using a pinned or managed trust key on the Device Agent.

The shared contract introduced in this milestone must include a
signature-ready envelope. This milestone does not yet sign profiles or verify
their signatures.

Administrator-defined extension scripts, if introduced later, must be
preinstalled, signed district packages. The backend policy may reference an
approved package ID but must never deliver arbitrary PowerShell to execute as
`LocalSystem`.

## Intended Start Flow

1. The authenticated Launcher page loads the student's assignments.
2. The student chooses an assignment.
3. The page atomically asks the Web API to create a `Starting` exam session.
4. The Web API verifies assignment ownership, stores the session, and returns
   its immutable effective profile.
5. The page sends the versioned `startExam` JSON and profile to the native
   Launcher.
6. The Launcher displays the localized exam name and save/restart warning.
7. On confirmation, the Launcher sends the effective profile unchanged to the
   Agent.
8. The Agent validates the profile and persists its enforcement receipt.
9. The Agent generates and applies Assigned Access, taskbar, shortcuts, and
   Edge policy dynamically.
10. The Agent schedules the application-owned five-second restart countdown.
11. Windows restarts into the restricted account.
12. The student authenticates again.
13. The Restricted Client calls one authenticated API operation that
    atomically finds and activates the student's `Starting` session.
14. The operation returns the now-`Active` session, including its exam metadata
    and SharePoint folder.
15. **Open exam** uses the SharePoint folder URL returned by the authenticated
    API response.

Cancellation or Agent failure must cancel the prepared API session so that a
failed transition does not appear active. A bounded expiry handles Launcher or
device failure before cancellation can be reported.

## Recommended Implementation Sequence

1. Add shared signature-ready profile-envelope, effective-policy,
   tool-definition, application-definition, taskbar-target, Edge-policy, and
   exam-session contracts with validation-focused unit tests.
2. Use the implemented stub assignment service containing four student
   profiles, two exams, four assignments, and the initial tool catalog.
3. Resolve profiles from authenticated Entra UPN claims.
4. Render multiple assignments and exam-specific icons in the Launcher.
5. Require authentication on every endpoint except health checks, then add the
   in-memory exam-session store and atomic authenticated start endpoint with
   antiforgery protection and ownership authorization.
6. Expand the `startExam` bridge request to include the returned effective
   profile and add cancellation reporting.
7. Expand the Agent protocol to carry a typed apply request with bounded
   message size.
8. Add Agent-side profile validation and enforcement-receipt persistence.
9. Replace the fixed Assigned Access XML with dynamic Assigned Access and
   taskbar-layout builders.
10. Add Word and Calculator applications and taskbar targets based on the
    profile.
11. Add Web-tool definitions and Agent-generated Edge taskbar shortcuts.
12. Add exact temporary Edge blocklist/allowlist application and restoration.
13. Add the authenticated, atomic current-session activation-and-return
    operation for the Restricted Client.
14. Replace the fixed `https://www.example.com/` URL with the SharePoint folder
    URL returned by the authenticated API.
15. Implement the Student 4 no-session/mismatch screen with only the native
    finish action.
16. Update English/French resources and documentation.
17. Run unit tests only; leave all live Web, Windows, restart, and Assigned
    Access validation to the user.

## Resume Point

Resume by defining the shared effective-profile and signature-ready envelope
contracts before changing the Web App or Agent protocol.

The first implementation decision is the exact contract for:

- the profile and future signature envelope;
- exam-session identity and timestamps;
- desktop, packaged, and Web taskbar launch targets;
- helper processes;
- Web entry URLs and required-origin allowlists;
- the complete backend-resolved Edge blocklist and allowlist;
- executable publisher/hash validation;
- policy and collection size limits;
- the Agent enforcement and recovery receipt.

Do not begin by adding student-specific conditionals directly to Razor
components. Establish the domain model and validation boundary first.
