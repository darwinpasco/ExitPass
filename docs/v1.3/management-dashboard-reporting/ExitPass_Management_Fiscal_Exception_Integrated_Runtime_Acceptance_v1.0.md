# ExitPass Management Fiscal Exception Integrated Runtime Acceptance v1.0

## Acceptance Record

| Field | Value |
| --- | --- |
| Acceptance ID | `MDR-FISCAL-ACCEPT-20260823T073441Z-8299A68F` |
| Result | `MANAGEMENT_FISCAL_EXCEPTION_RUNTIME_ACCEPTANCE_PASSED_SELF_REVIEWED` |
| Central PMS baseline | `771eeac3aaab0e4a760a7b80ee2f1a8f108291b4` |
| Management Platform baseline | `70c59d3ed375670afea9c0e4a7b866f43ccdb110` |
| Backend fiscal implementation | `7823b8a4d294f8b9d27307f98244c41c9ebdb5b4` (merge `44b49dd0be98ed80708f0f588d6cc6ae77932373`) |
| Frontend fiscal implementation | `27181b04ff69ce5b0eda319f794c5d8a5a9aaa7f` (merge `70c59d3ed375670afea9c0e4a7b866f43ccdb110`) |
| Started UTC | `2026-08-23T07:34:41.7821782Z` |
| Started Asia/Manila | `2026-08-23T15:34:41.7821782+08:00` |
| Completed UTC | `2026-08-23T08:19:23.2096381Z` |
| Completed Asia/Manila | `2026-08-23T16:19:23.2096381+08:00` |
| Review posture | `SELF-REVIEWED` |
| Accountable owner | Darwin Pasco |
| Technical executor | Codex H |
| Independent review | `NOT_PERFORMED`; no independent-review claim is made |
| External evidence | `D:\SourceCodes\ExitPass.local\management-platform-runtime-acceptance\MDR-FISCAL-ACCEPT-20260823T073441Z-8299A68F` |
| Evidence manifest SHA-256 | `527733eae8d399de6619a4347e85e89ed50d0190b6ebc95084f79485418d4f63` |

## Accepted Contract

- Frontend route: `/management-platform/reports/fiscal-exceptions`
- Backend route: `GET /v1/management-platform/dashboard/fiscal-exception-summary`
- Contract: `management-platform-fiscal-exception-reporting:v1`
- Permission: `sales-invoice-report.view`
- Policy: `ManagementPlatformFiscalExceptionSummaryRead`
- Feature controls: `ManagementPlatform:DashboardReporting:Enabled` and `ManagementPlatform:DashboardReporting:FiscalExceptions:Enabled`
- Scope: explicit authorized `SITE` or `SITE_GROUP`; no null, inferred, or GLOBAL scope
- Period: explicit UTC `[periodStart, periodEnd)` with a maximum span of 31 days
- Time basis: `FISCAL_ISSUANCE_REFERENCE_FIRST_RECORDED_AT`

## Runtime Topology

The acceptance ran the current merged source rather than a published image or mocked final backend. A task-owned Management Platform Vite process on `http://127.0.0.1:55179` proxied same-origin `/v1` requests to a task-owned Central PMS HTTPS process on `https://localhost:58081`. Central PMS used an isolated PostgreSQL 16 container on loopback port `57592`, database `exitpass_fiscal_accept`, network `mdr-fiscal-8299a68f-net`, and volume `mdr-fiscal-8299a68f-pgdata`.

The canonical schema and current v1.3 validation patches were applied before fixture creation. Actual H-006 password login issued and resolved server-owned secure sessions. Fixture identity and permission headers were disabled. No shared database, Production, standing UAT, POS Server, HikCentral, payment-provider, BIR, or other external business resource was accessed.

## Synthetic Data And Expected Results

The deterministic fixture contained `SITE-A`, `SITE-B`, and `SITE-C`. Site Group `GROUP-AB` contained `SITE-A` and `SITE-B`; `SITE-C` remained outside that group. The report user had `sales-invoice-report.view`, direct `SITE-A` authority, and `GROUP-AB` authority. Separate principals proved missing-permission, other-scope, disabled-account, stale-epoch, revoked-session, and expired-session behavior.

The fixture covered PHP and USD expected issuance values, every canonical source state, all three implemented exception categories, pending and issued references, records exactly at each period boundary, one superseded reference, one `SITE-C` reference, a no-activity period, and one payment confirmation with no fiscal issuance reference. Expectations were produced from the fixture definition before API calls and independently checked against PostgreSQL.

For `SITE-A`, 13 active, nonsuperseded references were included: PHP count 9 and expected amount 970.00; USD count 4 and expected amount 73.50. Exception counts were issuance failed 3, reference conflict 1, and outcome unavailable 2. For `GROUP-AB`, 17 references were included: PHP count 12 and expected amount 1135.00; USD count 5 and expected amount 88.50. Exception counts were issuance failed 3, reference conflict 2, and outcome unavailable 3. `SITE-C` was excluded.

## Accepted Scenarios

| Scenario | Result |
| --- | --- |
| Authentication | Actual H-006 login succeeded. The secure server session, CSRF control, idle expiry, revoked session, disabled account, authorization epoch, and logout behavior passed. |
| Permission | `sales-invoice-report.view` controlled navigation and backend access. Missing permission returned `403` without ending the authenticated session or leaking report facts. |
| Concealed scope | A `SITE-C`-only principal received the established concealed `404` for `SITE-A`; no scope or aggregate facts leaked. |
| SITE | `SITE-A` returned only its predetermined lifecycle, currency, exception, source, warning, limitation, and unavailable-fact results. |
| SITE_GROUP | `GROUP-AB` included `SITE-A` and `SITE-B`, excluded `SITE-C`, and matched all predetermined group results. |
| Explicit scope | Missing and GLOBAL scopes were rejected. Client-authored identity, permission, and Site headers did not authenticate a request. |
| Period | The `periodStart` record was included and the `periodEnd` record was excluded. Equal, reversed, and over-31-day periods were rejected. |
| Lifecycle | All observable normalized lifecycle states matched canonical persisted states. Pending remained a lifecycle fact and was not automatically an exception. |
| Exceptions | `SALES_INVOICE_ISSUANCE_FAILED`, `SALES_INVOICE_REFERENCE_CONFLICT`, and `SALES_INVOICE_OUTCOME_UNAVAILABLE` matched the deterministic source facts. |
| Cohort | Superseded references and payment confirmations without a fiscal issuance reference were excluded. Payment confirmation alone did not prove Sales Invoice issuance. |
| Currency | PHP and USD remained separate. No conversion, mixed-currency total, or client-side aggregate was produced. |
| Availability | Activity remained visibly `PARTIAL`. Source coverage, `dataAsOf`, warnings, limitations, and unavailable facts matched persisted Central PMS coverage. |
| No activity | The valid empty cohort returned `NO_ACTIVITY` without implying that no Sales Invoices or payments existed. |
| Feature disabled | The endpoint returned `503 MANAGEMENT_FISCAL_EXCEPTION_REPORTING_DISABLED`; the UI rendered a distinct disabled state with no aggregates. |
| Refresh and concurrency | A failed refresh retained prior data only with its original timestamps and a visible previously-loaded warning. Superseded requests could not overwrite current scope or period state. |
| Browser | Real Chromium passed desktop `1440`, tablet `768`, and mobile `390` layout, keyboard focus, accessible status text, internal table handling, and zero document-level horizontal overflow. |
| Storage and network | No report or authority facts were written to browser storage. No frontend-owned cookie, bearer token, authority header, POS Server call, provider call, or external request was observed. |

The canonical fiscal-state constraint maps every currently persistable source value to an explicit lifecycle other than `OTHER`. Runtime `OTHER` is therefore not constructible without weakening the schema. Defensive parser and presentation coverage for `OTHER` passed in the merged automated frontend tests.

## Automated Validation

- Central PMS restore and Release solution build: passed with zero errors; the existing warning baseline remained.
- Fiscal reporting unit tests: 19 passed.
- Dashboard/payment reporting unit regressions: 40 passed.
- H-006 human-authentication unit regressions: 27 passed.
- Fiscal PostgreSQL repository and hosted API tests: 10 passed.
- Hosted-session, dashboard, payment, H-006 repository/API, and cross-application authentication regressions: 44 passed.
- Management Platform locked dependency install and high-severity audit: passed; zero vulnerabilities.
- TypeScript validation: passed.
- Complete Vitest: 329 passed across 18 files.
- Focused Chromium: fiscal 7 passed, dashboard 8 passed, payment 6 passed.
- Complete Chromium with one worker: 79 passed.
- Production build: passed with 54 transformed modules.
- Real API comparisons: 21 passed; negative API scenarios: 9 passed; H-006 session-security scenarios: 6 passed.
- Real browser assertions: enabled 25 passed; disabled 2 passed.
- Contract JSON, UTF-8, control-byte, sensitive-value, storage, authority-header, hardcoded-URL, external-call, cleanup, and evidence-manifest checks: passed.

## Security And Reporting Boundary

Central PMS remained authoritative for identity, permission, authorization epoch, scope, fiscal coordination references, and persisted issuance outcomes. The browser did not mint authority or persist protected report data. Responses and rendered UI exposed no credentials, tokens, raw POS Server response, provider payload, private identity mapping, customer data, plate, ticket detail, Sales Invoice number, or unnecessary transaction identifier.

This acceptance proves the merged aggregate reporting foundation against facts persisted in Central PMS. It does not prove live POS Server status, printing, delivery, customer receipt, BIR certification, settlement, payout, deposit, remittance, or cash custody.

## Deferred Scope

Partial source coverage remains explicit. Printing, delivery, digital-copy availability, overdue detection, retry exhaustion, issued-document amount or currency comparison, duplicate Sales Invoice analysis, reprint, adjustment, void, BIR certification, exports, schedules, drill-down, and exception-resolution workflows remain unavailable or deferred.

The fiscal-exception backend and UI foundation is runtime accepted. The wider Management Dashboard and Reporting capability remains `PARTIAL` because management activity reporting, exports, schedules, and other approved v1.3 capabilities remain incomplete.

## Cleanup

The task-owned frontend, Central PMS, and browser processes; PostgreSQL container, database, network, and volume; listeners; cookies; temporary credentials; and temporary harness files were removed. The clean detached frontend acceptance worktree was removed. The documentation worktree and external evidence directory were retained for review. Zero task-owned runtime resource remained, and unrelated host and Docker resources were not changed.
