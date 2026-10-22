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

Use Node.js 24 for the test runner and its coverage flags. After `npm ci`, install Chromium once with `npx playwright install chromium`.
Run `npm run build:client` before browser tests. Browser tests build the server and
start their own isolated application instance. Set `E2E_PORT` to override the HTTP
port (5087) or the production HTTPS check port (5088).

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

The client reads the server's update, checkpoint and text-snapshot limits from
`/features`. It splits queued edits into batches within `MaxEncodedUpdateBytes`;
if a merged batch is refused, it retries each edit separately before blocking.
Checkpoint state and text are checked against `MaxEncodedCheckpointBytes` and
`MaxSnapshotCharacters` before transmission.

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
  edits continue syncing. After a size refusal, it is reconsidered only after
  another `CheckpointEveryRevisions` revisions.
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

### Admin credentials and sessions

Generate an Identity password hash offline:

```bash
dotnet run --project tools/password-hash
```

The console prompts without echo. Keep passwords out of command arguments, shell history, source control and logs; keep the generated hashes private too.

For each admin account:

- Configure `AdminUsers__0__Name` and `AdminUsers__0__PasswordHash` through environment variables or a deployment secret provider (adjust the index for additional accounts).
- Remove the legacy `Password` key entirely.

Existing name-only or older authentication tickets require signing in again. Changing a credential or removing an account invalidates authorization after the configuration takes effect.

Sessions last **eight hours**, without sliding extension. Persist ASP.NET Data Protection keys securely if sessions must survive a redeployment.

### HTTPS and reverse proxies

Run behind HTTPS. Production enables HSTS, HTTPS redirection and Secure admin cookies.

When using a reverse proxy:

- Set `TrustedProxies` to the proxy's exact IP addresses.
- Forward the original host and scheme.
- Set `AllowedHosts` to the deployment hostname.

Unlisted proxies cannot supply trusted forwarded headers. The browser WebSocket Origin must match the effective hostname and scheme.

### Deployment and trust boundaries

The application supports **one process only**. It has no database, multi-instance coordination or durable room storage; room data disappears when the process stops, even if Data Protection keys are persisted.

Anyone knowing a room link is a trusted participant under [ADR 0003](docs/adr/0003-crdt-recovery-lifecycle.md). The server validates membership, revisions, encoding and resource limits, but does not interpret Yjs updates to verify their document semantics.

### Admin requests and input validation

Admin POST/DELETE requests require an antiforgery token:

1. Fetch a token from `/admin/token`.
2. Send it in `X-CSRF-TOKEN`, or use `__RequestVerificationToken` for a form.
3. Fetch a fresh token after login.

The provided UI handles this automatically.

| Area | Behavior |
| --- | --- |
| API input exceptions | Exceptions handled by the API middleware return ProblemDetails. |
| Hub resource refusals | Stable `LimitExceeded` / `RateLimited` codes. |
| Room names | Trimmed and shortened to 20 characters plus an ellipsis; raw inputs over 1024 characters are rejected. |
| Participant names | Trimmed and limited to 64 characters. |

## Resource budgets

### Empty room expiration

Expiration is opt-in. Set `Limits:RoomIdleTimeoutMinutes` to enable it:

| Setting | Default | Valid range |
| --- | --- | --- |
| `Limits:RoomIdleTimeoutMinutes` | `0` (disabled) | 0–525600 minutes |

When enabled, a background service checks **once per minute** and deletes empty rooms whose last activity is at least the configured timeout old. The deletion reason is `Room expired`; expiration occurs at the first sweep after the timeout.

Every successful mutation, including joining and leaving, updates `LastActivityUtc`. `LastUpdatedUtc` continues to track document and metadata changes.

Eligibility is checked again under the room lock before deletion. A concurrent participant join preserves the room if it acquires that lock first.

### Server limits

Configure these values in the `Limits` section:

| Setting | Default | Budget |
| --- | --- | --- |
| `MaxEncodedUpdateBytes` | 4 MiB | Base64 characters per update |
| `MaxEncodedCheckpointBytes` | 4 MiB | Base64 characters per checkpoint |
| `MaxJournalBytes` | 4 MiB | Decoded journal bytes |
| `MaxJournalCount` | 2048 | Journal updates |
| `MaxSnapshotCharacters` | 1,048,576 | UTF-16 characters per text snapshot |
| `MaxRooms` | 32 | Rooms per process |
| `MaxConnections` | 160 | Concurrent hub connections |
| `MaxConnectionsPerSource` | `min(20, MaxConnections)` | Concurrent connections per normalized source (1-10000; no greater than `MaxConnections`) |
| `UnjoinedConnectionTimeoutSeconds` | 60 | Deadline to join a room (5-3600 seconds) |
| `NewConnectionsPerSourcePerMinute` | 30 | New connections per source per minute; range 1–10000 |
| `InvocationsPerSecond` | 100 | Hub invocations per connection per second |

For deployments behind shared NAT or proxies, increase `Limits:NewConnectionsPerSourcePerMinute`
and `Limits:MaxConnectionsPerSource` to accommodate reconnect bursts and concurrent users.
A source is one IPv4 address or one IPv6 /64 prefix. IPv4-mapped IPv6 addresses use
their IPv4 address; missing addresses share `unknown`. Configure `TrustedProxies`
so the source address reflects the intended client boundary. Connections that do
not join a room before `UnjoinedConnectionTimeoutSeconds` are closed.

Defaults were verified with 500,000-character documents. Combined limits must fit a worst-case **16 MiB synchronization response**; invalid settings fail startup.

### Client limits and quota handling

The browser pending/delivery budgets are **8 MiB / 2048 entries**.

- A permanent refusal retains visible unsynced text and stops retries. **Copy the text before reloading.**
- A valid checkpoint frees journal capacity.
- Accepted updates are never evicted to satisfy a quota.

Resource counters are available from the `SealCode.Rooms` and `SealCode.Hub` diagnostic meters without document content.

### Checkpoint thresholds

| Setting | Default | Valid range |
| --- | --- | --- |
| `CheckpointEveryRevisions` | 64 | 1–10000 revisions |
| `CheckpointEveryBase64Bytes` | 256 KiB | 1024–4194304 Base64 characters |

Startup validation requires both conditions:

```text
4 * CheckpointEveryRevisions <= MaxJournalCount
3 * CheckpointEveryBase64Bytes <= MaxJournalBytes
```

These constraints reserve headroom beyond the doubled fallback threshold for in-flight updates and account for Base64-to-decoded-byte conversion.

## Local assets, publishing and verification

### Building and publishing

The client build clears `wwwroot/assets` before writing the new bundle and chunks.
Stable URLs (HTML, JavaScript entry points and workers) use `Cache-Control: no-cache`
so browsers revalidate them using ETag/Last-Modified. Files with esbuild hashes in
their names are cached for one year with `immutable`.

On a clean checkout, install dependencies first:

```bash
npm ci
```

Build/Publish runs `npm run build:client` automatically. Generated `wwwroot/assets` is ignored by Git and included in publication. Use `SkipClientBuild=true` only when the client build is already prepared.

Monaco, Yjs, SignalR and workers load locally, with a single Yjs instance; no CDN is needed. CSP permits inline styles for Monaco, while scripts and workers must come from this origin.

### CI and test isolation

CI is defined in [verify.yml](.github/workflows/verify.yml).

Browser tests use an isolated temporary content root with disposable settings, never local admin credentials.

Coverage reports are separate:

- **Server:** ReportGenerator merges source lines and branches across projects, rather than averaging project percentages.
- **Client:** `c8 --all` includes every root `wwwroot/*.js` module, including
  untested browser entry points. Tests are excluded. The enforced floors are
  61% lines, 83% branches and 82% functions; LCOV is written to
  `coverage/client/lcov.info`. Native Node coverage is also reported for loaded modules.

### Production HTTPS check

Create a development HTTPS certificate, then run the additional production check:

```bash
dotnet dev-certs https
npx playwright test --config playwright.https.config.js
```

The browser test client ignores certificate trust only for that local test certificate.

### Further details

- [ADR 0004: Resource and security boundaries](docs/adr/0004-resource-and-security-boundaries.md) — contracts and constraints.
- [Implementation audit](docs/audit-implementation.md) — validation evidence.
