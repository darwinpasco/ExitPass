# ExitPass Management Platform Core Integrated Runtime Acceptance v1.0

## Acceptance Record

| Field | Value |
| --- | --- |
| Acceptance ID | `MP-CORE-ACCEPT-20260823T103835Z-03E1EF72` |
| Result | `MANAGEMENT_PLATFORM_CORE_RUNTIME_ACCEPTANCE_PASSED_SELF_REVIEWED` |
| Canonical conclusion | `CORE_REQUIRED_SCOPE_COMPLETE` |
| Central PMS baseline | `526c0799e05e5655b0c528c966230e7e58379850` |
| Management Platform baseline | `a59db335ad6b5d92a993d5284b3dd3b4dd142b65` |
| Started UTC | `2026-08-23T10:38:35.9361408+00:00` |
| Started Asia/Manila | `2026-08-23T18:38:35.9361408+08:00` |
| Completed UTC | `2026-08-23T12:05:38.1175093+00:00` |
| Completed Asia/Manila | `2026-08-23T20:05:38.1175093+08:00` |
| Review posture | `SELF-REVIEWED` |
| Accountable owner | Darwin Pasco |
| Technical executor | Codex H |
| Independent review | `NOT_PERFORMED`; no independent-review claim is made |
| External evidence | `D:\SourceCodes\ExitPass.local\management-platform-runtime-acceptance\MP-CORE-ACCEPT-20260823T103835Z-03E1EF72` |
| Evidence manifest SHA-256 | `10f14b8a3563adbdc00aa6d50f4fe7707582fc26a7432a01adc619c35b0a530b` |

## Conclusion

The agreed absolute-required Management Platform scope is complete on the exact baselines above. This conclusion covers H-006 authentication and server-owned sessions, H-007 User Administration, the core Management Dashboard, Payment and Reconciliation Reporting, Fiscal Exception Reporting, cross-module permission and SITE/SITE_GROUP enforcement, responsive browser operation, and browser authority/storage boundaries.

`CORE_REQUIRED_SCOPE_COMPLETE` does not mean every Management Platform v1.3 requirement is complete. The overall Management Platform and the wider Management Dashboard and Reporting domain remain `PARTIAL` where approved capabilities are deferred or only partially sourced.

| Classification | Scope |
| --- | --- |
| Core required and accepted | H-006; H-007 required workflows; core Dashboard; Payment and Reconciliation summary; Fiscal Exception summary; permission, SITE, and SITE_GROUP enforcement; responsive and security boundaries |
| Implemented but partial | Connector/projection source coverage; payment internal consistency without external settlement; fiscal persisted-outcome coverage without live POS Server facts; broader RBAC and operational administration |
| Deferred | Management activity reporting; exports; schedules; delivery; drill-down; saved filters; wider operational metrics; exception-resolution workflows |
| Not implemented | Provider settlement and payout; bank reconciliation; cash custody; MDR/fees; refunds, chargebacks, disputes; additional fiscal analyses and BIR report generation |

## Source Binding And Ancestry

The runtime used current merged source. No published Central PMS image or mocked final backend was substituted.

Backend ancestry included:

- H-006 human authentication implementation `3188e10a20d2df90295af455bda0f725ca9df8fd` and merge `d9d5afe`.
- H-007 correction `2f9e6502f1683ced1f347aab2bff41fceaf40ca6` and merge `e13069a`.
- Dashboard foundation `f24a0ec6b4a30ce29decac16c3efcae1184e8ff0` and merge `40f8a6cee41d182513d999b691dc4e4e8aeea583`.
- Payment foundation `26b882ee6d6066aa5ea17b9a100cae09a5fee4ed`, merge `ea6e0f039338a389598e28ff9ef28654c619a85b`, and runtime correction merge `8209d27`.
- Fiscal foundation `7823b8a4d294f8b9d27307f98244c41c9ebdb5b4` and merge `44b49dd0be98ed80708f0f588d6cc6ae77932373`.
- Multi-site adapter correction merge `89ddf5d`, projection identity correction merge `917af67`, and prior acceptance documentation merges through backend tip `526c0799e05e5655b0c528c966230e7e58379850`.

Frontend ancestry included:

- H-006 consumer `a367ab8cc71b22d230d597c91217ad12dcc975f1` and merge `b99bf0e`.
- H-007 workspace `d8015b1`, completion correction `c71bb28`, manual-test correction `952b45f`, and merge `5e66263`.
- Dashboard UI `ae058ae6f5aee4749458ff8530204d1914309746` and merge `c51bf309a60e48efe8cf6a9a544b1aa53f58f9a5`.
- Payment UI `a6b1eba4896527637e5983a7444b1f928b1a22f4` and merge `eed4aeef1b16116e847de701df843b14fb416516`.
- Fiscal UI `27181b04ff69ce5b0eda319f794c5d8a5a9aaa7f` and merge `70c59d3ed375670afea9c0e4a7b866f43ccdb110`.
- Dashboard catalog-version correction `7bcf32ec5e028c8a7c02688ca733301fa2c0648d` and merge at frontend tip `a59db335ad6b5d92a993d5284b3dd3b4dd142b65`.

All listed implementation and correction commits were ancestors of the accepted remote tips.

## Runtime Topology

The Management Platform ran from the detached frontend baseline on `http://127.0.0.1:55988` and proxied same-origin `/v1` requests to Central PMS HTTPS on `https://127.0.0.1:55987`. Central PMS used the task-owned PostgreSQL 16 container `mp-core-03e1ef72-postgres` on loopback port `55986`, database `mp_core_accept`, network `mp-core-03e1ef72-net`, and volume `mp-core-03e1ef72-pgdata`.

The current governed schema and required HikCentral projection patches were applied before deterministic fixture creation. Dashboard Reporting and its Payment Reconciliation and Fiscal Exceptions children were enabled only through task-owned runtime configuration. Fixture identity and permission headers were disabled. No shared database, Production, standing UAT, HikCentral, POS Server, payment provider, BIR system, or external business service was accessed.

## Principal And Permission Matrix

| Principal | Intended authority | Accepted result |
| --- | --- | --- |
| `mp.core.admin` | H-007, dashboard, catalog, payment, fiscal; SITE-A and GROUP-AB | All core navigation, reads, and governed H-007 mutations succeeded within assigned scope |
| `mp.core.report` | Dashboard and reports; no H-007 | Reporting navigation visible; User Administration hidden and direct route denied `403` while session remained active |
| `mp.core.useradmin` | H-007; no dashboard or reports | User Administration available; dashboard, payment, and fiscal returned controlled `403` |
| `mp.core.site` | Reporting; SITE-A only | SITE-A allowed; SITE-C concealed with `404` |
| `mp.core.group` | Reporting; GROUP-AB only | GROUP-AB allowed and contained only SITE-A and SITE-B |
| `mp.core.denied` | No required Management Platform permission | Protected modules denied without authority leakage |
| `mp.core.disabled` | Suspended account | Login rejected `401` |
| `mp.core.stale` | Reporting before epoch change | Initial read succeeded; authorization-epoch change caused `401` |
| `mp.created.crossmodule` | Atomically created Finance role and GROUP-AB | Real H-006 login and all three reporting modules succeeded; H-007 returned `403`; SITE-C returned concealed `404`; suspension invalidated the session and future login |

## Accepted Core Results

### H-006 Authentication And Session

Actual password login issued a Secure, HttpOnly, server-owned Management Platform session. Successful activity extended the 30-minute idle expiry while the eight-hour absolute expiry remained fixed. Missing CSRF returned `400`; idle expiry, absolute expiry, session revocation, stale authorization epoch, credential-version invalidation, account suspension, and logout returned `401`. Permission-specific denial returned `403` and retained the otherwise valid session. No bearer token or client-authored identity, role, permission, SITE, SITE_GROUP, or authorization-epoch header was accepted.

### H-007 User Administration

The live directory returned 50 records on page 1 and 28 on page 2, with exact search and status filtering. Browser presentation excluded H-007/synthetic/denied roles and filtered assignable roles by user type. An incompatible `SUPPORT_USER` and Finance role request returned controlled `400 USER_TYPE_ROLE_INCOMPATIBLE`. Invalid delegated scope returned `403` and created no user. Atomic creation of `mp.created.crossmodule` persisted one user, one compatible role, and one GROUP-AB grant; no partial row existed for failed create attempts. Authoritative refresh was required after a deliberate row-version `409` before activation. Site and Site Group controls, lifecycle, session revocation, neutral access wording, pagination, responsive master-detail behavior, and logout cleanup passed.

### Cross-Module Identity And Scope

The task-created user logged in through H-006 only after governed activation. Its server-derived Finance role and GROUP-AB grant authorized Dashboard, Payment Reporting, and Fiscal Reporting for GROUP-AB, denied H-007 with `403`, and concealed SITE-C with `404`. Suspension through H-007 invalidated the current session and rejected subsequent login. This proves that User Administration assignments become authoritative across the core modules without browser-owned authority.

### Core Dashboard

`SITE-A` returned one active Site and two active projections. `GROUP-AB` returned two Sites and three active projections and included only SITE-A and SITE-B. Site registry, connector target, projection freshness, generated-at, data-as-of, source authority, warnings, and limitations matched independently calculated PostgreSQL facts. Catalog and overview permissions remained separate. `AVAILABLE`, `PARTIAL`, `STALE`, `UNAVAILABLE`, `NOT_APPLICABLE`, and retained-data failure states remained distinct. Parent disablement returned `503 MANAGEMENT_DASHBOARD_REPORTING_DISABLED` without values.

### Payment And Reconciliation Reporting

SITE-A and GROUP-AB counts and exact amounts matched PostgreSQL by PHP and USD. Attempts and confirmations remained separate. All five implemented internal categories matched the seeded facts: amount mismatch `1`, currency mismatch `1`, duplicate authoritative provider reference `2`, confirmed outcome without confirmation `1`, and status inconsistency `1`. Pending attempts were not promoted to confirmations or exceptions merely because they remained pending. Period start was included and period end excluded. Empty activity succeeded with explicit warning and no fabricated totals. Child disablement returned `503 MANAGEMENT_PAYMENT_RECONCILIATION_REPORTING_DISABLED`.

### Fiscal Exception Reporting

SITE-A and GROUP-AB expected issuance counts and amounts matched PostgreSQL by PHP and USD. GROUP-AB presented all implemented lifecycle states and the three implemented categories: issuance failed `1`, reference conflict `1`, and outcome unavailable `2`. Pending remained a lifecycle fact, superseded references and the period-end record were excluded, and payment confirmation without an issuance reference was outside the cohort. Activity remained visibly `PARTIAL`, with persisted-source limitations and unavailable POS Server facts. Empty activity returned `NO_ACTIVITY`. Child disablement returned `503 MANAGEMENT_FISCAL_EXCEPTION_REPORTING_DISABLED`.

## Browser, Accessibility, And Security

Real Chromium covered login, administrator and restricted navigation, User Administration, Add User Site and Site Group forms, Dashboard, Payment, Fiscal, permission denial, retained-data refresh failure, feature disabled, and logout. Desktop `1440`, tablet `768`, and mobile `390` views had no document-level horizontal overflow or failed assets. Keyboard, labels, focus, cards, tables, forms, warnings, pagination, and status text were also covered by the complete deterministic Chromium suite.

Browser inspection recorded no localStorage, sessionStorage, IndexedDB, Cache Storage, or frontend-owned cookie authority/report state. Reporting requests were same-origin GET requests. The two expected console entries were the unauthenticated startup `401` and the deliberately induced failed-refresh network error. There was no unexpected console error, asset failure, external request, report mutation, direct HikCentral request, POS Server request, provider request, or sensitive rendering.

## Automated Validation

- Central PMS restore passed.
- Central PMS Release solution build passed with 0 errors; 10,330 existing documentation warnings remained.
- Focused Central PMS unit tests passed: 166 of 166.
- Focused PostgreSQL, hosted API, H-006, I-020/I-021, Dashboard, Payment, Fiscal, Site Group, and projection integration selection passed: 74 of 74 after the separate test-harness correction.
- Management Platform locked install passed: 166 packages and 0 vulnerabilities.
- TypeScript validation passed.
- Complete Vitest passed: 329 of 329 across 18 files.
- Complete Chromium passed with one worker: 79 of 79.
- Production build passed: 54 transformed modules.
- Real API/database reconciliation, real-browser execution, session invalidation, feature controls, responsive checks, storage inspection, network inspection, contract JSON, UTF-8, control-byte, sensitive-value, authority-header, hardcoded URL, external-request, mutation-request, cleanup, and evidence checks passed.
- The frontend repository has no configured formatting or lint scripts.

## Prior Evidence Revalidation

The Payment manifest revalidated 76 entries, the Fiscal manifest 86 entries, and the Dashboard manifest 142 entries with zero missing or mismatched files. Their manifest hashes are retained in the external evidence. No historical external H-007 package was present under the governed runtime-acceptance root. This is a historical-evidence limitation only; the current exact-commit runtime independently re-proved the required H-007 workflows.

## Separate Test-Harness Correction

Current backend source introduced mandatory vendor-adapter startup configuration after three H-007 Production-hosted integration tests were written. On the untouched baseline, those three tests failed before their identity assertions with an empty adapter provider. The separate branch `fix/management-platform-core-runtime-acceptance` applies the repository's established production-host test isolation: explicit task-only `SITE_ADAPTER` configuration and removal of background hosted services. It changes no product source or runtime policy. The corrected three tests passed, and the complete focused 74-test integration selection passed.

## Deferred Scope

This acceptance does not close management activity reporting, CSV/XLSX/PDF export, schedules, delivery, report builder, saved filters, transaction drill-down, additional dashboard metrics, provider settlement or payout, bank reconciliation, cash custody, MDR or fee reporting, refund/chargeback/dispute reporting, live POS Server status, printing/reprinting, adjustment/void workflows, Electronic Journal, X/Z reports, BIR Sales Summary, Annex reports, additional fiscal categories, or exception-resolution workflows.

The wider Management Platform v1.3 baseline and Management Dashboard and Reporting domain therefore remain `PARTIAL`.

## Evidence And Cleanup

The external evidence package contains source ancestry, redacted runtime configuration, schema and seed inventory, principal and permission matrices, independently calculated expectations, API/database comparisons, H-006/H-007 assertions, browser/network/storage results, screenshots, test logs, prior-manifest revalidation, cleanup proof, and a SHA-256 manifest. Credentials, passwords, TOTP material, cookies, CSRF tokens, connection strings, private identifiers, and raw sensitive payloads are excluded from the retained report and manifest.

Only task-owned processes, listeners, browser instances, PostgreSQL container, network, volume, database, credentials, and temporary harness files were removed. The documentation worktree, separate correction worktree, and external evidence directory were retained for review. No unrelated resource was changed.
