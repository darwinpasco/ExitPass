# ExitPass Management Dashboard Integrated Runtime Acceptance v1.0

## Acceptance Record

| Field | Value |
| --- | --- |
| Acceptance ID | `MDR-DASHBOARD-ACCEPT-20260823T090629Z-1AC8AB9B` |
| Result | `MANAGEMENT_DASHBOARD_RUNTIME_ACCEPTANCE_PASSED_SELF_REVIEWED` |
| Central PMS baseline | `6168b8a58437419ce54a3b6e4076b33328b2e683` |
| Management Platform baseline | `70c59d3ed375670afea9c0e4a7b866f43ccdb110` |
| Backend dashboard implementation | `f24a0ec6b4a30ce29decac16c3efcae1184e8ff0` (merge `40f8a6cee41d182513d999b691dc4e4e8aeea583`) |
| Frontend dashboard implementation | `ae058ae6f5aee4749458ff8530204d1914309746` (merge `c51bf309a60e48efe8cf6a9a544b1aa53f58f9a5`) |
| Started UTC | `2026-08-23T09:06:29.6682490Z` |
| Started Asia/Manila | `2026-08-23T17:06:29.6682490+08:00` |
| Completed UTC | `2026-08-23T10:10:42.3814296Z` |
| Completed Asia/Manila | `2026-08-23T18:10:42.3814296+08:00` |
| Review posture | `SELF-REVIEWED` |
| Accountable owner | Darwin Pasco |
| Technical executor | Codex H |
| Independent review | `NOT_PERFORMED`; no independent-review claim is made |
| External evidence | `D:\SourceCodes\ExitPass.local\management-platform-runtime-acceptance\MDR-DASHBOARD-ACCEPT-20260823T090629Z-1AC8AB9B` |
| Evidence manifest SHA-256 | `35ef916fed739244ced0a0f0f0a8981679167ed15d7a8437269912f1622dd6b7` |

The acceptance ran against the exact baselines above. Central PMS `origin/dev` advanced after the runtime window; those later commits were not substituted into this acceptance record.

## Accepted Contract

- Frontend route: `/management-platform/overview`, navigation label `Dashboard`
- Catalog route: `GET /v1/management-platform/dashboard/catalog`
- Catalog permission and policy: `reports.view` and `ManagementPlatformDashboardCatalogRead`
- Operational route: `GET /v1/management-platform/dashboard/operational-overview`
- Operational permission and policy: `dashboard.view` and `ManagementPlatformOperationalOverviewRead`
- Contract: `management-platform-dashboard-reporting:v1`
- Feature control: `ManagementPlatform:DashboardReporting:Enabled`
- Scope: explicit authorized `SITE` or `SITE_GROUP`; no omitted, inferred, malformed, or GLOBAL scope

## Runtime Topology

The acceptance ran current merged source rather than a published image or mocked final backend. A task-owned Management Platform Vite process on `http://127.0.0.1:55180` proxied same-origin `/v1` requests to a task-owned Central PMS HTTPS process on `https://localhost:58082`. Central PMS used an isolated PostgreSQL 16 container on loopback port `57594`, database `exitpass_dashboard_accept`, network `mdr-dashboard-1ac8ab9b-net`, and volume `mdr-dashboard-1ac8ab9b-pgdata`.

The current schema was applied before deterministic fixture creation. Actual H-006 password login issued and resolved server-owned sessions. Fixture identity and permission headers were disabled. No shared database, Production, standing UAT, HikCentral, POS Server, payment-provider, or other external business resource was accessed.

## Synthetic Data And Expected Results

The deterministic fixture contained `SITE-A`, `SITE-B`, and `SITE-C`. Site Group `GROUP-AB` contained `SITE-A` and `SITE-B`; `SITE-C` remained outside that group. The main report principal had `dashboard.view`, `reports.view`, direct `SITE-A` authority, and `GROUP-AB` authority. Separate principals proved dashboard-only, catalog-only, missing-permission, other-scope, disabled-account, stale-epoch, revoked-session, and expired-session behavior.

`SITE-A` was active and payment enabled, with one healthy connector target and three active projected parking sessions. `SITE-B` was suspended and payment disabled, with one degraded and one failing connector target and no active projected sessions. `SITE-C` was active, had no applicable projection target, and represented the controlled not-applicable/no-activity case. Expected results were recorded from the fixture definition and independently queried from PostgreSQL before comparison with API responses.

The exact operational results were:

- `SITE-A`: 1 Site, 1 active Site, 0 suspended Sites, 1 payment-enabled Site; 1 enabled/healthy target; 3 active projections; initially `AVAILABLE` and `CURRENT`.
- `SITE-B`: 1 Site, 0 active Sites, 1 suspended Site, 0 payment-enabled Sites; 2 enabled targets, 1 degraded and 1 failing; 0 active projections; `PARTIAL` and `STALE`.
- `GROUP-AB`: 2 Sites, 1 active and 1 suspended, 1 payment-enabled; 3 enabled targets, 1 healthy, 1 degraded, and 1 failing; 3 active projections; `PARTIAL` and `STALE`.
- `SITE-C`: Site registry metrics remained authoritative while connector and projection sections were `NOT_APPLICABLE`; unavailable active-projection facts were not rendered as fabricated zero metrics.

Projection timestamps were intentionally fixed. During the browser run, the `SITE-A` projection crossed the configured 15-minute freshness threshold and correctly changed to stale without changing its persisted `dataAsOf` value.

## Accepted Scenarios

| Scenario | Result |
| --- | --- |
| Authentication and session | Actual H-006 login succeeded. The HttpOnly server session, audience, CSRF control, idle expiry, revoked session, disabled account, authorization epoch, and logout behavior passed. |
| Permission separation | `dashboard.view` and `reports.view` were enforced independently. Dashboard-only access remained usable when catalog access returned `403`; catalog-only access exposed no operational facts. |
| Scope | `SITE-A` and `SITE-B` returned only their own facts. `GROUP-AB` included only `SITE-A` and `SITE-B`; `SITE-C` was excluded. Missing and malformed scopes were rejected, and unauthorized cross-scope access returned concealed `404` without dashboard facts. |
| Operational data | Site registry, payment-enabled configuration, connector health, projection freshness, and active projection counts matched independently calculated PostgreSQL results. |
| Availability and freshness | `AVAILABLE`, `PARTIAL`, `STALE`, `UNAVAILABLE`, and `NOT_APPLICABLE` presentation retained source authority, warnings, limitations, generated time, and authoritative `dataAsOf` semantics. Missing facts were never represented as success-looking zero values. |
| Catalog | The corrected browser rendered the four merged entries: operational overview `PARTIAL`, payment and reconciliation `PARTIAL`, fiscal exception `PARTIAL`, and management activity `UNAVAILABLE`. No report payload or sensitive transaction fact appeared in the catalog. |
| Feature control | Parent feature enabled allowed access. Disabled configuration returned `503 MANAGEMENT_DASHBOARD_REPORTING_DISABLED` and the UI displayed a distinct disabled state without retained facts presented as current. |
| Refresh and concurrency | Failed refresh retained prior data only with original timestamps and an explicit previously-loaded warning. Superseded requests could not overwrite a newly selected scope. |
| Responsive and accessible UI | Real Chromium at desktop `1440`, tablet `768`, and mobile `390` had no document-level horizontal overflow, clipping, or overlapping controls. Keyboard focus, form labels, status text, cards, warnings, limitations, and report catalog remained usable. |
| Logout | Logout removed protected dashboard content and invalidated the server session; no report or authority facts remained in browser storage. |

## Acceptance Correction

Real merged-source browser execution found one frontend parser defect: the dashboard catalog parser incorrectly required every catalog entry to use the top-level dashboard contract version. The merged backend correctly returns each report's authoritative contract version. The separate frontend branch `fix/management-dashboard-runtime-acceptance` validates catalog entries against their report-specific dashboard, payment, or fiscal contract version while keeping top-level catalog and operational-overview validation strict.

The complete corrected runtime and frontend regression suites passed. This acceptance documentation must merge only after that frontend correction is reviewed and merged.

## Automated Validation

- Central PMS restore: passed.
- Central PMS Release build: passed with 0 errors; 2,400 existing warnings remained.
- Dashboard/reporting unit tests: 88 passed.
- Site, adapter, and projection unit regressions: 65 passed.
- PostgreSQL, hosted API, authentication, dashboard, payment, and fiscal integration regressions: 54 passed.
- Management Platform locked dependency install and high-severity audit: passed; 166 packages and 0 vulnerabilities.
- TypeScript validation: passed.
- Focused corrected dashboard tests: 23 passed across 2 files.
- Complete Vitest: 329 passed across 18 files.
- Focused Chromium: dashboard 8 passed, payment 6 passed, fiscal 7 passed.
- Complete Chromium with one worker: 79 passed.
- Production build: passed with 54 transformed modules.
- Real corrected browser: enabled dashboard, permission variants, logout, disabled feature, responsive, storage, console, and network checks passed.
- Contract JSON, UTF-8, control-byte, sensitive-value, browser-storage, authority-header, hardcoded-URL, external-request, mutation-request, cleanup, and evidence-manifest checks passed.
- The Management Platform has no configured formatting or lint scripts.

## Security And Reporting Boundary

Central PMS remained authoritative for identity, permission, authorization epoch, scope, Site registry facts, connector health, and persisted vendor projections. The browser did not mint authority, use a bearer token, or persist protected dashboard facts. Responses and rendered UI exposed no credential, token, raw cookie, connection string, provider secret, private identity mapping, customer data, plate, ticket detail, payment payload, or unnecessary transaction identifier.

Network inspection found only same-origin read operations for the dashboard. No mutation, HikCentral, POS Server, payment-provider, or external business request occurred.

## Deferred Scope

This acceptance closes the core dashboard route, explicit SITE/SITE_GROUP scoping, current phase-1 operational sections, catalog presentation, feature controls, and browser behavior. It does not close active-vehicle detail, occupancy approximation, session age, long-stay visibility, connector latency/raw diagnostics, management activity reporting, exports, schedules, delivery, report builder, drill-down, settlement, payout, cash custody, fees, refunds, chargebacks, disputes, or additional fiscal capabilities.

The wider Management Dashboard and Reporting domain remains `PARTIAL`. Connector and vendor projection coverage is also explicitly partial where current authoritative sources do not provide the broader approved metrics.

## Evidence And Cleanup

The external evidence directory contains sanitized runtime configuration and commands, schema and fixture inventories, predetermined expectations, direct PostgreSQL calculations, API exchanges, browser assertions, screenshots, test logs, source-binding records, cleanup proof, and a 142-entry SHA-256 manifest. Credentials, passwords, session cookies, CSRF values, connection strings, and private tokens are excluded or redacted.

The task-owned frontend, Central PMS, and browser processes; PostgreSQL container, database, network, volume; listeners; temporary credentials; generated test/build outputs; and detached frontend runtime worktree were removed. The documentation worktree, separate correction worktree, and external evidence directory were retained for review. Zero task-owned runtime resource remained, and unrelated host and Docker resources were not changed.
