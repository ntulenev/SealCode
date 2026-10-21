# SealCode

SealCode is a self-hosted collaborative code editor for small, trusted groups, suitable for pair programming, interviews and discussing code together. Each room contains one shared text document, with live editing, participant presence and a shared syntax-highlighting language.

The backend uses ASP.NET Core Minimal API and SignalR on .NET 10; the browser uses Monaco Editor and Yjs for CRDT synchronization. Rooms live entirely in server memory. This is a personal project intended for private use.

![Screenshot](Room.png)

## Features

- Real-time multi-user editing with presence and cursors.
- Selective local undo/redo that preserves concurrent edits from other users.
- Shared language switching by any participant; the checked-in configuration offers C#, SQL and JavaScript.
- Participant list with cursor line/column and typing, selection and copy indicators.
- Automatic reconnection, room rejoin and synchronization recovery, retaining local undo history in the open tab.
- Admin panel to create rooms, copy invitation links, inspect participant counts and delete rooms. Regular admins can delete their own rooms; super admins can delete any room.
- Configurable capacity of 1–5 participants per room, resource budgets and optional expiration of empty rooms.
- Locally bundled browser dependencies and workers; no CDN is required at runtime.

## Scope and room lifetime

Participants join through a room link and a display name, without an account or password. Anyone with the link can edit the document and change its language, so share links only with trusted participants.

Each room is a single editing buffer: there is no project/file tree, code execution, terminal, Git integration or durable revision history. Copy out any text you want to keep. Closing a tab leaves the room; an empty room remains available until an admin deletes it, an enabled idle timeout expires, or the server stops. Restarting the application loses all rooms and their contents. Deployment supports a single application process.

## Collaboration

SealCode uses Yjs (CRDT) to merge concurrent edits. Each client produces incremental updates that the server broadcasts to the room. The server retains a checkpoint and an update journal for new participants and reconnecting tabs; clients periodically compact the journal with a new checkpoint. These checkpoints are in-memory synchronization state, not saved files or backups.

During a detected disconnection or room rejoin, the editor becomes read-only. After reconnecting, the client merges the room state and retries queued updates. Permanent size/quota failures display a warning to copy unsynced text before reloading. Deleting a room notifies connected participants and disables editing.

## Quick Start

Run the following from the repository root with Node.js 20+ and the .NET 10 SDK installed.

1. Install browser dependencies:

```bash
npm ci
```

2. Generate an admin password hash:

```bash
dotnet run --project tools/password-hash
```

Set `PasswordHash` for **every** admin in `src/SealCode/appsettings.json`, or remove unused admin records. The checked-in configuration has two accounts with empty hashes, so configuring only one is insufficient. See [Configuration](#configuration) for environment-variable overrides.

3. Prepare the local HTTPS certificate and start the application (the build also bundles browser assets):

```bash
dotnet dev-certs https --trust
dotnet run --project src/SealCode/SealCode.csproj
```

4. The checked-in launch profile uses `https://localhost:61670` and `http://localhost:61671`; use the URL shown in your console if overridden. Open `/admin/login`, sign in with a configured admin account, create a room and share its link.

## Tests

After `npm ci`, install Chromium once with `npx playwright install chromium`.
Browser tests start their own isolated application instance.

| Command | Coverage |
| --- | --- |
| `npm run test:smoke` | Admin login, room creation, multi-user editing, language switching, late join, selective undo, leaving and room deletion |
| `npm run test:client` | Client synchronization, recovery, undo, presence and lifecycle unit tests |
| `npm run test:e2e` | Browser collaboration, recovery, limits, security and UI tests, including smoke |
| `npm test` | Client unit tests and the browser suite |
| `npm run typecheck` | Static JavaScript type checking |
| `dotnet test src/SealCode.slnx` | Server domain, service, hub, API and configuration tests |

## Admin Workflow

1. Visit `/admin/login`.
2. Sign in with an admin account configured before startup.
3. Choose a room name and language, create the room and share its link.

The panel lists all rooms with language, user count, last update time and creator.
Use **Copy link** to invite participants and **Delete** to close a room you are allowed to manage.

## Join a Room

Open the room link in a browser and enter a display name. Names are trimmed,
limited to 64 characters and must be unique within the room (case-insensitive).
Joining a full room is rejected. Close the tab or navigate away to leave.

## Configuration

- `src/SealCode/appsettings.json`
- `AdminUsers`: unique admin names and Identity V3 `PasswordHash` values. Include `IsSuperAdmin` to allow deleting rooms created by other admins. Legacy `Password` is accepted only in Development.
- `Languages`: list of language identifiers supported by Monaco Editor (example: `csharp`, `sql`).
- `MaxUsersPerRoom`: integer from 1 to 5.
- `CollaborativeUndoEnabled`: optional feature flag; defaults to `true`.

The checked-in `appsettings.json` contains empty `PasswordHash` placeholders. Before starting the app, generate a hash for each admin with `dotnet run --project tools/password-hash` and set its `PasswordHash` locally or through environment variables (for example, `AdminUsers__0__PasswordHash`). Paste the generated hash, not the plain-text password. Empty hashes fail configuration validation. Do not commit your credentials.

## Undo and redo

- `Ctrl+Z` / `Cmd+Z` undoes the current user's latest grouped text operation.
- `Ctrl+Y`, `Ctrl+Shift+Z`, or `Cmd+Shift+Z` redoes it.
- Remote edits are never added to the local undo history.
- Each tab retains up to 100 grouped local operations; reconnect keeps the
  history, while leaving or reloading clears it.

## Synchronization limits

Checkpoint writers wait 1 second and compact at the server-advertised
`Limits:CheckpointEveryRevisions` (default 64) or
`Limits:CheckpointEveryBase64Bytes` (default 256 KiB of Base64 characters). The latest author writes first; other
participants use 2x thresholds as a fallback. Defaults remain well below
the 2048-entry / 4 MiB decoded journal budgets. Accepted checkpoints announce
`YjsCheckpointed(checkpointRevision)` to other participants; stale revisions
are rejected before Base64 decoding, with the locked check remaining authoritative.

- SignalR accepts messages up to 16 MiB. The client checks the complete UTF-8
  JSON message, including the full CRDT state and text in a checkpoint, before sending.
- A checkpoint over the limit is skipped with a visible warning; incremental
  edits continue syncing. It is reconsidered when the document revision changes.
- An edit over the limit remains local and queued, with a warning to copy the
  text before reloading. Oversized messages are not retried automatically.
- Admin sessions use protected authentication cookies; old name-only cookies
  require signing in again after upgrading.

## Endpoints

- `/` landing page
- `/about.html` about page with repository link
- `/room/{roomId}` room UI
- `/admin` admin panel
- `/admin/login` admin login
- `/admin/logout` admin logout (POST)
- `/admin/rooms` list/create rooms (GET/POST, admin only)
- `/admin/rooms/{roomId}` delete a room (DELETE, owner or super admin)
- `/admin/token` antiforgery token for admin requests
- `/languages` configured language identifiers
- `/features` collaborative undo flag and synchronization/resource limits
- `/health` health check
- `/version` version and commit of the running build (not cached)
- `/roomHub` SignalR hub

## Version shown in the footer

- MinVer stamps the application from Git tags during Build/Publish, including
  Visual Studio Publish. No deployment script or Git installation on the server is needed.
- Release tags use `v` followed by SemVer, for example `v1.2.0`. Publish after
  the tag is created on the release commit, from a checkout containing Git history and tags.
- Commits after a release receive a `dev` prerelease version automatically.
  The footer shows the version and short commit; its tooltip contains the full build version.
- Publishing without building uses the version already embedded in the existing binaries.
  A build is required after creating a release tag. The footer cannot determine
  uncommitted source changes; publish committed code for an identifiable release.


## UI Indication

The user's cursor position is displayed in the following format:

`User1 (14:25)`

Where:

- `User1` — the user  
- `14` — the line number  
- `25` — the column number (character position within the line)

🎨 The interface uses color cues to reflect user actions and interaction states.

![UIColors](UIColors.png)

| Color  | Meaning | Description |
|--------|----------|-------------|
| 🟢 Green | Editing | Recent text activity in the shared editor. |
| 🟠 Orange | Selection | A non-empty text selection, including a single-line selection. |
| 🔴 Red | Copy activity | A copy action with selected editor text; this does not verify clipboard write success. |

## Production setup and migration

Generate an Identity password hash offline using `dotnet run --project tools/password-hash`.
The console prompts without echo; do not put a password in command arguments,
shell history, source control, or logs. Configure `AdminUsers__0__Name` and
`AdminUsers__0__PasswordHash` through environment variables or a deployment secret
provider, and remove the old `Password` key entirely. Keep hashes private too.
Existing name-only/older tickets require signing in again. Changing a credential
or removing an account invalidates authorization after the configuration takes
effect. Sessions last eight hours without sliding extension.

Run behind HTTPS. For a reverse proxy, set `TrustedProxies` to its exact IP
addresses, forward the original host and scheme, and configure `AllowedHosts`
for the deployment hostname. Unlisted proxies cannot supply trusted forwarded
headers. Production enables HSTS/HTTPS redirection and Secure admin cookies.
The browser WebSocket Origin must match this effective hostname/scheme. Persist
ASP.NET Data Protection keys securely if sessions must survive a redeployment;
room data still disappears. The application supports one process only; it has
no database, multi-instance coordination or durable room storage. Anyone knowing
a room link is a trusted participant under [ADR 0003](docs/adr/0003-crdt-recovery-lifecycle.md).
The server validates membership, revisions, encoding and resource limits but does
not interpret Yjs updates to verify their document semantics.

Admin POST/DELETE requests require a token from `/admin/token` in `X-CSRF-TOKEN`
(or `__RequestVerificationToken` for a form). Fetch a fresh token after login.
The provided UI does this automatically. Input exceptions handled by the API middleware return ProblemDetails;
Hub resource refusals contain stable `LimitExceeded`/`RateLimited` codes.
Room names deliberately retain the existing trim/20-character-plus-ellipsis
normalization; raw inputs over 1024 characters are rejected. Participant names
are trimmed and limited to 64 characters.

## Resource budgets

Empty room expiration is opt-in: `Limits:RoomIdleTimeoutMinutes` defaults to `0`
(disabled), with a validated range of 0–525600 minutes. When enabled, a background
service checks once per minute and deletes empty rooms whose last activity is at
least the configured timeout old, using the reason `Room expired`. Every successful
mutation, including joining and leaving, updates `LastActivityUtc`; `LastUpdatedUtc`
keeps its existing document/metadata meaning. Eligibility is checked again under
the aggregate lock before deletion, so a concurrent participant join preserves
the room if it wins that lock. A room expires at the first sweep after its timeout.

The `Limits` section accepts `MaxEncodedUpdateBytes` and
`MaxEncodedCheckpointBytes` (4 MiB Base64 characters each), `MaxJournalBytes`
(4 MiB decoded), `MaxJournalCount` (2048), `MaxSnapshotCharacters` (1,048,576),
`MaxRooms` (32), `MaxConnections` (160), `NewConnectionsPerSourcePerMinute`
(30, range 1–10000), and `InvocationsPerSecond` (100).
Increase `Limits:NewConnectionsPerSourcePerMinute` for deployments behind shared
NAT/proxies to accommodate reconnect bursts. Configure `TrustedProxies` so the
source address reflects the intended client boundary.
Defaults are implementation choices verified with 500,000-character documents.
Combined limits must fit a worst-case 16 MiB synchronization response; invalid
settings fail startup. The browser pending/delivery budgets are 8 MiB/2048 entries.
A permanent refusal retains visible unsynced text and stops retries: copy it
before reloading. A valid checkpoint frees journal capacity; accepted updates
are never evicted for quota. Resource counters are available from the
`SealCode.Rooms` and `SealCode.Hub` diagnostic meters without document content.

Checkpoint thresholds are validated at startup: `4 * CheckpointEveryRevisions <=
MaxJournalCount` and `3 * CheckpointEveryBase64Bytes <= MaxJournalBytes`. This
reserves headroom beyond the doubled fallback threshold for in-flight updates
and accounts for Base64-to-decoded-byte conversion. The ranges are 1-10000
revisions and 1024-4194304 Base64 characters, respectively.

## Local assets, publishing and verification

Build/Publish runs `npm run build:client`; run `npm ci` first on a clean checkout.
Generated `wwwroot/assets` is ignored by Git and included in publication.
Monaco/Yjs/SignalR and workers load locally with a single Yjs instance; no CDN
is needed. CSP intentionally permits inline styles for Monaco, but scripts and
workers must come from this origin. `SkipClientBuild=true` is for an already
prepared client build only.

CI is defined in `.github/workflows/verify.yml`. Browser tests use an isolated
temporary content root with disposable settings, never local admin credentials.
For the additional production check, create a development HTTPS certificate
with `dotnet dev-certs https`, then run
`npx playwright test --config playwright.https.config.js`. The browser test client
ignores trust only for that local test certificate. Server coverage is merged
with ReportGenerator by source lines/branches, rather than averaging project
percentages; JS coverage is reported separately. See [ADR 0004](docs/adr/0004-resource-and-security-boundaries.md) for contracts
and [the implementation audit](docs/audit-implementation.md) for validation evidence.
