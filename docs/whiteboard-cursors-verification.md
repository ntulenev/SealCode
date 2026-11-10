# Whiteboard cursor review — 2026-11-10

Reviewed on `feature/whiteboard-participant-cursors`. Existing local application
settings and unrelated output files were preserved.

## Server style and architecture

The cursor endpoint follows the surrounding `RoomHub` presence methods: shared
wire-name constants, `ParseRoomIdOrThrow`, `HubErrors`, participant lookup through
`BroadcastPresenceAsync`, `GroupExcept` to exclude the sender, the connection's
cancellation token, and `ConfigureAwait(false)`. It adds no persistent room state
or new dependency. Existing hub resource limits also apply to cursor calls.

The review aligned XML comments with the surrounding contract constants and
replaced the unnecessary `y!.Value` suppression with explicit nullable pattern
checks. Tests now verify cancellation-token forwarding and exactly one broadcast,
and include the new method in the shared invalid-room-ID regression test.

The new endpoint uses the typed `InvalidInput` error format. Older cursor APIs
retain their legacy message format for compatibility; the new method has no
legacy client contract to preserve.

## Behavioral coverage

| Layer | What it verifies | Assessment |
| --- | --- | --- |
| C# unit | Coordinates and hide payload, sender exclusion, cancellation token, no document revision change, nonparticipant silence, invalid room IDs, partial coordinates, NaN and infinities | Good boundary coverage; all new endpoint branches execute |
| SignalR integration | Real hub binding/serialization of negative fractional coordinates and two nulls, no sender echo, no cross-room delivery, unchanged complete document sync state, calls before join/after leave/from another room, partial-coordinate errors | The main missing layer was added: six integration cases |
| JS unit | Latest-position throttling, a single in-flight invocation, heartbeat, refreshed expiry, stale-peer removal, hidden/blur handling, disconnect/reset, send failures, late completion from a previous connection, coordinate transforms, stable colors across arrival order, safe text labels | Good deterministic coverage of timing and lifecycle behavior |
| JS composition | UI/transport wiring, local sends, own-event suppression, participant removal and offline cleanup with the real room application controller | Covers adapter wiring in addition to isolated units |
| Chromium E2E | Real participants, names/colors, zoom/pan on either side, leaving canvas/room, reconnect, three concurrent clients, consistent colors between clients, constant label size and drawing beneath cursors | Three cursor-specific scenarios pass |
| Visual check | Real system-design diagram with two remote cursors in dark and light themes | Both README images rendered and were inspected |

The C# integration tests run the ASP.NET application and actual SignalR client
protocol against TestServer, rather than mocked `IClientProxy` calls. They use
event completions and ordered replies to check delivery without arbitrary sleeps.
The browser tests exercise real WebSocket transport in Chromium.

## Measured coverage

| Scope | Lines | Branches | Functions |
| --- | --- | --- | --- |
| New server `UpdateBoardCursorAsync`, Coverlet | 100% | 100% (10/10 conditions) | — |
| `whiteboard-presence.js`, c8 | 100% | 91.66% | 100% |
| Entire `whiteboard-view.js`, c8 | 100% | 98.15% | 94.44% |
| All client modules under existing c8 configuration | 92.72% | 91.05% | 91.1% |

Server figures come from 73 targeted hub, contract and cursor integration cases;
client figures come from all 180 client tests. View-wide percentages include
existing diagram tests and should not be read as cursor-only coverage. Browser
execution is not included in c8 percentages. No thresholds were lowered.

Final validation passed: all **314** tests in `SealCode.Tests` (no skips), all
**180** client tests with the configured coverage floors, all **three**
cursor-specific Chromium scenarios, JavaScript type checking, the C# formatting
check for changed server files, and `git diff --check`. The full browser suite
and other C# test projects were not rerun for this review.

Remaining limits: no Firefox/WebKit or large-room load test was run. Heartbeat,
expiry and slow-send timing are tested with a deterministic clock; they are not a
claim about behavior under every real network condition. CSS motion is inspected
in Chromium rather than measured as an animation-performance benchmark.

## Reproduction

```powershell
npm run test:client
npm run typecheck
dotnet test src/SealCode.Tests/SealCode.Tests.csproj -p:SkipClientBuild=true --no-restore
dotnet test src/SealCode.Tests/SealCode.Tests.csproj --filter 'FullyQualifiedName~WhiteboardCursor|FullyQualifiedName~RoomHubTests|FullyQualifiedName~HubContractTests' -p:SkipClientBuild=true --no-restore --collect:'XPlat Code Coverage' --results-directory test-results/cursors-server
npx playwright test tests/e2e/whiteboard-collaboration.spec.js --grep 'participant cursors'
node scripts/capture-readme.mjs --cursors-only
```

The screenshot command starts an isolated local server, creates a board and three
participants, draws through the UI, and moves pointers in participant pages. It
does not inject cursor elements or modify the developer's server settings. It
asserts both remote cursors are visible before writing
`docs/images/whiteboard-cursors-dark.png` and
`docs/images/whiteboard-cursors-light.png`.
