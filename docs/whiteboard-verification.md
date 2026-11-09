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
| Client unit tests with c8 | 157 passed |
| Affected Chromium regression suites | 18 passed |
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
| All modules counted by the existing c8 configuration | 74.44% | 91.46% | 93.92% |
| Whiteboard model | 100% | 100% | 100% |
| Whiteboard view/controller | 100% | 98.22% | 93.1% |
| Required CI floors | 68% | 87% | 90% |

The current CI floors are satisfied. Overall client line coverage remains below
the documented **80% improvement goal**. In particular, room-application,
room-connection, monaco-adapter and the whiteboard entry point are exercised by
browser scenarios, but have no Node unit coverage. Browser execution is not
silently counted as coverage in the c8 figures above.

Local artifacts from this verification:

- `output/whiteboard-review/merged/index.html` and `Summary.txt`: merged server coverage.
- `coverage/client/lcov.info`: client coverage under the existing npm command.
- `output/whiteboard-review-server-final.log`: final successful C# solution run.
- `output/whiteboard-review-client.log`: final successful client test run.
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
