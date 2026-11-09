# Full regression verification — 2026-11-09

Validated source: `4b8f3e7`. No application-code changes were needed. Existing
local `src/SealCode/appsettings.json` changes were preserved and excluded from
the verification commit. Test hosts used disposable configuration and accounts.

## Results

| Suite | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| C# solution: models, logic, transport, persistence and API/SignalR integration | 654 | 0 | 0 |
| Complete JS client suite with c8 | 168 | 0 | 0 |
| Complete configured Chromium E2E suite | 75 | 0 | 0 |
| Production HTTPS suite | 2 | 0 | 0 |

Client asset generation, solution build, `npm run typecheck`,
`dotnet format --verify-no-changes --no-restore` and `git diff --check` passed.
The solution build produced zero warnings and errors.

This run enabled the PostgreSQL-dependent cases instead of skipping them. A
separate PostgreSQL 15.2 cluster was initialized on a temporary loopback port.
Disposable databases received both checked-in migrations and a seeded general/1
question. The C# isolated-database, populated-database and browser editor test
connection variables pointed only to that cluster. The existing PostgreSQL
service and live application configuration were not used or changed.

## Ordinary code-room regression evidence

No regression was observed in the ordinary code-room scenarios. The full run
included these behaviors, rather than only the whiteboard scenarios:

- **Editing and completion:** native completion accepted with Enter/Tab,
  concurrent insert/replace/delete around completion prefixes, and continued
  synchronization through undo/redo.
- **Local history:** undo affects the local participant's changes, preserves
  remote edits, survives reconnection, and stays unavailable while offline.
- **Recovery:** missing intermediate and final updates are recovered; interrupted
  revision probes recover on reconnect; closure stops in-flight retries.
- **Language and presence:** stale language events cannot roll metadata back;
  refused language changes restore the shared language; selection transitions,
  remote cursors and participant indicators remain functional.
- **Admission and lifecycle:** join waits for bundle/language readiness, duplicate
  clicks are ignored, full-room rejoin stays visibly read-only, released source
  slots permit retry, and deletion closes a joined room.
- **Resource budgets:** large documents checkpoint with working undo; oversized
  edits/snapshots retain local data and expose warnings; observer checkpoint
  fallback, update splitting after reconnect and full-journal recovery work.
- **Surrounding UI and security:** dark/light and mobile layouts, local assets and
  workers under CSP, invitation validation, admin CSRF/authentication, question
  library/editor and version footers pass their configured browser tests.

All eight dedicated whiteboard scenarios also passed in the full run. Production
HTTPS tests confirmed password-hash authentication, a Secure cookie, and rejection
of a foreign WebSocket Origin.

## Coverage and commands

ReportGenerator merged all five server coverage reports by source: **96.6% line
coverage**, **88.8% branch coverage**, **99.2% method coverage**. Client c8 coverage
was **92.72% lines**, **90.91% branches**, **90.84% functions**. Coverage filters and
thresholds were unchanged.

After building the client assets and solution, the executed test commands were:

```text
npm run test:client
dotnet test src/SealCode.slnx --no-build --no-restore --collect:"XPlat Code Coverage" --results-directory output/full-regression/server --verbosity normal
npx playwright test
npx playwright test --config playwright.https.config.js
```

Local artifacts:

- `output/full-regression-client.log`
- `output/full-regression-server.log`
- `output/full-regression-browser.log`
- `output/full-regression-https.log`
- `output/full-regression-build.log`
- `output/full-regression/merged/index.html` and `Summary.txt`
- `coverage/client/lcov.info`

The temporary PostgreSQL server was stopped after the run. Automatic approval
review rejected removal of its data directory with `blocked by policy`, so the
stopped cluster's files remain under
`output/full-regression-pg-1360015b0517428d950bb5b7274cd74f/data`. No running test
database was left behind.
