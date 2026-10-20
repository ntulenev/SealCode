# ADR 0004: Bounded rooms, administration, and ordered snapshots

Accepted, 2026-10-20. Implements the SC-01â€“SC-07 audit plan. ADR 0001 and
0003 remain in force: participants supply trusted opaque CRDT checkpoints.
SC-08 is conditional and is not implemented by these changes.

## Resource contracts

`Limits` has validated defaults: 4 MiB Base64 characters per update/checkpoint,
4 MiB decoded journal payloads, 2048 journal entries, 1,048,576 UTF-16 snapshot
characters, 32 rooms, 160 concurrent hub connections, 100 invocations/second
per connection. Anonymous source buckets permit 30 new connections/minute by
`Limits:NewConnectionsPerSourcePerMinute` (validated range 1–10000). Increase this
for deployments behind NAT/proxies and configure `TrustedProxies` appropriately,
so reconnect bursts do not throttle an entire office.
These are implementation defaults, not previously agreed product commitments.
The 500,000-character fixture fits these defaults. Combined configurable limits
must fit the worst-case escaped 16 MiB synchronization response, including
checkpoint, journal entry framing and text. HTTP/SignalR message limits remain
additional boundaries, not substitutes for retained-memory limits.

A single aggregate lock serializes membership, close, mutations, quota
reservation and snapshots. Refusals leave revisions/timestamps/data unchanged.
The journal uses a List, avoiding immutable-array copying on append. Checkpoint
can free journal capacity at its exact document revision. No accepted journal
entry is dropped to satisfy a budget. `LimitExceeded` is permanent for the
current queued edit; the browser retains it, freezes local editing, shows a
copy-before-reload warning and stops sending. Temporary errors retry with a
bounded delay; `RateLimited` backs off for 1100ms. Pending and delivery buffers
are bounded by count and bytes; discarded delivery buffers recover from the
server journal. These budgets do not claim to bound Monaco/Yjs internal memory
or malicious decode CPU (see trusted participants).

`SealCode.Rooms` exposes journal byte/count up-down counters and rejected-update
counts. `SealCode.Hub` counts throttling. Login and connection source/name
limiters use 256 fixed hash buckets, bounding state; unrelated identities may
share a bucket. No document contents or credentials are logged by these metrics.

## Administration

Production startup requires Identity V3 PasswordHash records and rejects legacy
plaintext Password. Development retains Password temporarily for existing local
configuration, verifying it via PasswordHasher. The offline console tool prompts
without echo. Account names are unique after trim/case folding. Nested settings,
normalized language availability, hash format/work factors and resource budgets
are validated at startup. Hashes need the same protection as other credentials.

Admin mutations, including login/logout, require antiforgery tokens. The token
endpoint is uncached, and tokens are refreshed after identity changes. Login is
limited to 30 attempts/source/minute and 10/name/minute using bounded buckets.
Unknown accounts and wrong passwords receive the same external failure. Sessions
expire after eight hours without sliding renewal. Credential fingerprint changes
or account removal invalidate application authorization immediately after the
new configuration takes effect (normally restart). Old tickets lacking the
fingerprint require signing in again. Production cookies are Secure/HttpOnly;
HTTPS redirect/HSTS run after explicitly trusted forwarded-header processing.
No proxy is trusted implicitly. WebSocket browser Origin must match the effective
request authority; missing Origin remains valid for nonbrowser clients.

## Consistency and lifecycle

Language metadata and presence have independent monotonic revisions. Text edits
cannot suppress newer language events. Revision probes include all three streams;
recovery merges into the existing Y.Doc and reconciles metadata/users. Join and
sync take language, text, room/document/checkpoint versions and users under one
lock. Event payloads include their matching revisions; clients ignore older ones.
The protocol changed in coordination with the bundled client; deployments should
refresh already-open tabs.

Deletion commits by removing and closing the aggregate. Old references reject
mutations. The application service sends closure notifications with a separate
five-second timeout and logs delivery failure without turning committed deletion
into a failure. No durable delivery guarantee is claimed. Missing RoomKilled is
handled by participant probes returning Room not found. Failed group subscription
rolls back new membership. Rejoining with the same connection/name is idempotent;
renaming requires leaving first. Leave cleanup ignores the canceled HTTP/connection
token, and retains cleanup context if group removal fails.

## Boundaries and delivery

RoomSessionService coordinates participant mutations; RoomManager coordinates
lifecycle/authorization/notifications; RoomRegistry stores rooms. Transport no
longer references Logic/Abstractions. Admin route registration is separate from
startup/middleware. The client sync module owns the bounded queue, recovery and
checkpoint scheduling, with injected transport/timers/session callbacks. Monaco,
connection startup and revisioned UI adapters remain separate from CRDT transport.

Monaco, SignalR, Yjs and bindings are locked npm dependencies bundled locally.
The build asserts a single Yjs module. Worker URLs are explicit local assets.
CSP permits self scripts/workers/connections, and inline styles required by Monaco;
it does not permit inline scripts/eval or remote CDN scripts. Browser tests cover
worker execution and CSP violations. CI runs server and client tests, Chromium,
production HTTPS, merged server coverage, separate JS coverage, and dependency
audits. Traces/coverage contain disposable fixtures rather than user configuration.

Checkpoint writers wait 1 second and compact after 64 revisions or 256 KiB of
Base64 journal updates. The latest author writes first; other participants use
2x thresholds if the author leaves. These thresholds remain well below the
2048-entry / 4 MiB decoded journal budgets. Accepted checkpoints announce
`YjsCheckpointed(checkpointRevision)` to other participants; stale revisions
are rejected before Base64 decoding, with the locked check remaining authoritative.
