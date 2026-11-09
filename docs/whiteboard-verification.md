# Whiteboard verification

Verified on 2026-11-09 against the repository's existing .NET 10, JavaScript,
formatting and coverage configuration. Coverage thresholds and exclusions were
not relaxed. Local application settings were preserved.

## Standards and validation

- `dotnet format src/SealCode.slnx --verify-no-changes --no-restore` passed for the
  entire solution, including tests.
- `npm run typecheck` passed. The client keeps the established two-space
  indentation, single quotes, local bundles and a single Yjs instance.
- The application build completed with zero warnings and errors.
- `git diff --check` passed; all 22 image references in README resolve locally.

## Tests and coverage

| Validation | Result |
| --- | --- |
| C# solution with XPlat Code Coverage | 645 passed, 9 skipped |
| Client unit tests with c8 | 168 passed |
| Affected Chromium regression suites | 18 passed |
| Whiteboard collaboration follow-up (8 scenarios, 2 repetitions) | 16 passed |
| Full screenshot generation through the real UI | Passed without browser runtime errors or page overflow |

The Chromium suites cover appearance, admin rooms, local assets/CSP,
collaborative undo, selection state and whiteboards. This was a targeted
regression run, not a claim that the entire browser suite was rerun.
PostgreSQL tests were skipped because their optional database test connections
were not configured; whiteboards use the existing in-memory room registry.

ReportGenerator merged five server reports by source, rather than averaging
project percentages: **92.5% lines**, **86.9% branches**, **98% methods**.
RoomState, RoomView, RoomManager, RoomRegistry, CreateRoomRequest, JoinRoomResult,
RoomSummary and the public endpoint-routing module have **100% line coverage**.

| Client scope | Lines | Branches | Functions |
| --- | --- | --- | --- |
| All modules counted by the existing c8 configuration | 92.72% | 90.91% | 90.84% |
| Room application composition | 81.74% | 81.19% | 74.71% |
| Room connection | 100% | 100% | 80% |
| Monaco adapter | 100% | 100% | 100% |
| Whiteboard model | 100% | 100% | 100% |
| Whiteboard view/controller | 100% | 98.22% | 93.1% |
| Required CI floors | 68% | 87% | 90% |

The current CI floors and the **80% overall line-coverage goal** are satisfied.
The JS coverage follow-up added 11 tests without changing production code,
coverage exclusions or thresholds. It reran the complete client suite and
`npm run typecheck`; the C# and Chromium results above are from the preceding
review of the same production code.

The composition tests execute real Yjs sessions, undo history and room
controllers, with deterministic DOM, editor and network adapters at their
boundaries. They verify join/retry controls, local delivery, reconnects, stale
presence/metadata rejection, remote edits, language rollback, copy/selection
rules, whiteboard undo/redo and snapshot-size warnings. Dedicated adapter tests
cover pending connection starts, rejection/retry, connectivity-aware reconnect
policy, collaborative undo delegation and theme subscription cleanup.

Remaining gaps include the whiteboard entry point (browser-only coverage) and
some room composition recovery/error paths. Actual Monaco rendering is covered
by browser scenarios. Browser execution is not silently counted as coverage in
the c8 figures above.

Local artifacts from this verification:

- `output/whiteboard-review/merged/index.html` and `Summary.txt`: merged server coverage.
- `coverage/client/lcov.info`: client coverage under the existing npm command.
- `output/whiteboard-review-server-final.log`: final successful C# solution run.
- `output/js-80-coverage-final.log`: successful 168-test client coverage follow-up.
- `output/whiteboard-review-screenshots-final.log`: screenshot build and UI checks.

## Regression cases and corrections

- Legacy room-creation JSON retains the code-room default; explicit whiteboard
  requests and null kinds remain compatible. Unknown domain room kinds fail.
- Whiteboard language changes check membership first, reject the change and
  preserve the room's language, version, revisions and update timestamp.
- Code rooms and whiteboards share room and participant capacity and deletion
  rules. Two participants converge on the diagram after reconnecting.
- A late participant restores a whiteboard after a real server checkpoint has
  been accepted, then edits the restored document successfully.
- Separate elements merge through Yjs; local undo retains remote changes.
  Remote deletion during a local drag or label edit does not resurrect a shape.
- Toolbar Enter/Space now retain native button activation instead of opening
  labels or starting a pan. A second pointer cannot replace or cancel the
  first pointer's active gesture.
- Remote Monaco caret rendering is deferred and coalesced after the current
  decoration transaction. A unit regression checks transaction/reentrancy
  behavior; screenshot scenarios with four code participants no longer report
  recursive `deltaDecorations` errors.
- Screenshot generation now includes a mixed admin room list, both themes of
  both workspaces, and mobile code/whiteboard views. It rejects browser runtime
  errors instead of only logging them.

Same-element CRDT updates still resolve as whole-element map writes; this review
does not claim independent merging of every property or collaborative Y.Text
labels. Room state remains in memory, as documented in README.

## Additional whiteboard collaboration E2E scenarios

The [collaboration suite](../tests/e2e/whiteboard-collaboration.spec.js) adds five
browser scenarios using independent guest browser contexts and the real test
server. All diagram changes are made through drawing, label, mouse and keyboard
controls. Assertions compare element IDs, labels and complete SVG geometry;
local viewport and selection state are excluded.

- **Concurrent creation and separate-element edits:** outgoing updates from both
  clients are held until both have committed locally against the same baseline.
  Each initially sees only its own new figure or label change. Releasing the
  messages must preserve both edits in both browsers and for a late participant.
- **Concurrent edits of one element:** two different local labels exist before
  either update is delivered. Both clients and a late participant must converge
  to one complete proposal with unchanged geometry. The test does not assume
  which random Yjs client ID wins or claim per-property conflict merging.
- **Move, resize and delete:** exact position and dimension deltas are checked
  across clients; labels and an unrelated figure must remain intact. A late
  participant receives the resized geometry. Deletion propagates to all three
  participants and remains effective after reloading.
- **Disconnect before server acceptance:** the first in-flight UpdateYjs is
  intercepted before forwarding, then both ends of its WebSocket are closed.
- **Disconnect after acceptance, before acknowledgement:** the original update
  reaches the real server, but its broadcast back to the sender and invocation
  completion are withheld. Server acceptance is confirmed through the broadcast
  and the other participant's rendered scene before cutting the connection.

In both disconnect cases the sender has not acknowledged a document revision at
the cut. Rejoin is held while the other participant creates another figure.
After automatic reconnect, the pending update must be retried, both figures must
converge without duplicates, and a late participant must receive the same scene.
Undo/redo must still affect only the sender's figure and preserve the remote one.

Repeat the existing three whiteboard tests and these five scenarios with:

```bash
npx playwright test tests/e2e/whiteboard.spec.js tests/e2e/whiteboard-collaboration.spec.js --repeat-each=2
```

The local execution log is `output/whiteboard-e2e-followup.log`. The fixture also
fails on browser runtime errors and deletes its test room during teardown.
All eight whiteboard scenarios passed both repetitions (16 successful executions).
