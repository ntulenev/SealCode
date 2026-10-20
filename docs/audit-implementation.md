# Audit implementation evidence

Date: 2026-10-20. Source: [Notion specification](https://app.notion.com/p/3ffac9758e1181909711f92ce3179b98).
Branch: `feature/audit-plan`, starting from `8c18615c4138b45f10168c17512e4203970aa5ad`.
Author/committer: Nikita Tulenev <n1tulenev@gmail.com>, matching the repository history.
User-local `src/SealCode/appsettings.json` was preserved and excluded from commits.
No push, merge, tag or deployment was performed.

## Requirement coverage

| ID | Result and regression evidence |
| --- | --- |
| SC-01 | Validated finite room/connection/update/checkpoint/journal/text budgets; atomic quota reservation; List journal; bounded browser queue/delivery buffer; source and invocation limits; stable refusal codes and metrics. Model boundary/concurrency/checkpoint tests and real Hub refusal/resumption integration pass. The 500,000-character browser fixture and oversized message scenarios pass. |
| SC-02 | Identity password hashes, hidden offline hash prompt, production startup policy, bounded source/name login limiting, antiforgery for all admin mutations, HTTPS/Secure cookies, trusted proxy configuration, credential-bound eight-hour tickets. Forged/expired ticket, CSRF, login flood and real HTTPS/hash login/logout tests pass. |
| SC-03 | Separate metadata/presence revisions; atomic join/sync snapshot; probes recover metadata without a text revision change. Deterministic reversed delivery and dropped final language event pass in Chromium alongside selective undo/recovery tests. |
| SC-04 | Closed aggregates reject old-reference mutations; deletion notifications use an independent five-second token; delivery failure cannot undo deletion. New group-registration failure rolls back membership, repeated join requires the same name, leave uses noncanceled cleanup and preserves retry context on group-removal failure. Closure received/missed scenarios pass. |
| SC-05 | Nested options, normalized admin uniqueness, hash work-factor/format and effective language validation; consistent name boundaries; defensive document buffers; API ProblemDetails and stable Hub failures. Validation, detached snapshots, name normalization, and boundary integration tests pass. |
| SC-06 | Participant mutation facade, atomic membership checks, lifecycle notification coordination in RoomManager, registry storage boundary, removed unused Transport references, separate admin routes. Sync queue/recovery/checkpoint module has explicit transport/timer/session callbacks and runs without DOM/CDN; separate connection, Monaco and revisioned UI adapters retain existing undo modules. |
| SC-07 | Reproducible local bundles and workers with a single Yjs assertion; browser-tested CSP and Origin checks; one CI workflow for server/client/Chromium/HTTPS, merged server coverage, separate JS coverage and failure traces. Public NuGet/npm advisory checks and clean-clone Release publication completed. |
| SC-08 | Conditional, not implemented. ADR 0003 trusted participants remains unchanged. No server-side CRDT integrity guarantee is claimed. |

## Executed validation

- `dotnet test src/SealCode.slnx --no-restore -p:SkipClientBuild=true --collect:"XPlat Code Coverage" --results-directory coverage/final-server`: **209 passed** (Models 110, Logic 19, Transport 8, SealCode 72).
- Node 24 test runner with separate LCOV output: **21 passed**. Executed-module coverage: **93.28% lines**, **73.98% branches**; this is not coverage of every UI file.
- Chromium full suite: **14 passed**, including actual TypeScript worker startup and zero CSP violations/external asset requests. After the final reconnect cleanup adjustment, the affected collaboration/recovery suite was rerun: **7 passed**.
- Real production Kestrel HTTPS suite: **2 passed**; checks hashed authentication, cookie flags, logout and foreign WebSocket Origin refusal.
- ReportGenerator 5.5.4 merged four final server reports by source: **88.8% lines**, **76.8% branches**. No arithmetic averaging across projects. Reports are local in `coverage/final-merged`.
- A separate clean local clone of code commit `8e826cf` ran `npm ci` and `dotnet publish ... -c Release` from absent generated assets. Publication succeeded; all seven required room/CSS/worker entries were present among 337 generated asset files. The later reconnect timer cleanup does not change the build/publish contract.
- The offline password-hash tool built successfully without warnings; no real credentials were supplied to it.
- `npm ci` audit: **0 advisories**. `dotnet list ... package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json`: **no reported vulnerable packages**. These are dated feed checks, not a guarantee of absence of unknown vulnerabilities.
- `git diff --check` passed. GitHub CI was added but was not remotely executed because nothing was pushed.

## Operational migration

Production needs PasswordHash settings and removal of legacy nonempty Password fields.
Use the offline prompt and a secret provider; do not copy local settings into test
artifacts. Existing sessions need a new sign-in. Refresh old browser tabs for the
coordinated metadata/presence protocol update. Build/publish requires Node 20+
and restored npm dependencies. Set HTTPS, exact trusted proxy IPs and AllowedHosts
for deployment; optionally persist Data Protection keys for session continuity.
Rooms remain in-memory, single-process, and disappear on restart. See README and
ADR 0004 for limits, antiforgery integration and retained trusted-participant risks.
