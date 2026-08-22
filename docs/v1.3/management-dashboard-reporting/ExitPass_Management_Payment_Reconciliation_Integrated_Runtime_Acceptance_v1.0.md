# ExitPass Management Payment and Reconciliation Integrated Runtime Acceptance v1.0

## Acceptance Record

| Field | Value |
| --- | --- |
| Acceptance ID | `MDR-PAY-ACCEPT-20260822T061921Z-8BA8B13B` |
| Result | `MANAGEMENT_PAYMENT_RECONCILIATION_RUNTIME_ACCEPTANCE_PASSED_SELF_REVIEWED` |
| Central PMS baseline | `89ddf5db462fab1e1694d2cf8320d37d5f6cbf5d` |
| Management Platform baseline | `eed4aeef1b16116e847de701df843b14fb416516` |
| Backend payment implementation | `26b882ee6d6066aa5ea17b9a100cae09a5fee4ed` |
| Frontend payment implementation | `a6b1eba4896527637e5983a7444b1f928b1a22f4` |
| Started UTC | `2026-08-22T06:19:21.4531282Z` |
| Started Asia/Manila | `2026-08-22T14:19:21.4531282+08:00` |
| Completed UTC | `2026-08-22T14:07:06.5150642Z` |
| Completed Asia/Manila | `2026-08-22T22:07:06.5150642+08:00` |
| External evidence | `D:\SourceCodes\ExitPass.local\management-platform-runtime-acceptance\MDR-PAY-ACCEPT-20260822T061921Z-8BA8B13B` |
| Evidence manifest SHA-256 | `2c497f0dee433031659bfdcc1959834a932288e53961b829d7c128dfc1fa4100` |

## Accepted Contract

- Route: `GET /v1/management-platform/dashboard/payment-reconciliation-summary`
- Contract: `management-platform-payment-reconciliation-reporting:v1`
- Permission: `reconciliation.view`
- Policy: `ManagementPlatformPaymentReconciliationSummaryRead`
- Feature controls: `ManagementPlatform:DashboardReporting:Enabled` and `ManagementPlatform:DashboardReporting:PaymentReconciliation:Enabled`
- Scope: explicit authorized `SITE` or `SITE_GROUP`; no null, inferred, or GLOBAL scope
- Period: explicit UTC `[periodStart, periodEnd)` with a maximum span of 31 days

## Runtime Topology

The acceptance used current merged source, not a published image or mocked backend. A task-owned Management Platform Vite process on loopback port `55178` proxied same-origin `/v1` requests to a task-owned Central PMS HTTPS process on loopback port `58080`. Central PMS used an isolated PostgreSQL 16 container on loopback port `55432`, database `mdr_pay_accept`, task network `mdr-pay-accept-8ba8b13b-net`, and task volume `mdr-pay-accept-8ba8b13b-pgdata`.

The canonical schema and current v1.3 Central PMS patches were applied and validated before fixture creation. No shared development, standing UAT, Production, POS Server, payment-provider, or customer resource was used.

## Synthetic Data

The deterministic fixture contained Sites `SITE-A`, `SITE-B`, and `SITE-C`; Site Group `GROUP-AB` contained `SITE-A` and `SITE-B`, while `SITE-C` remained outside that group. The authenticated report user had `reconciliation.view`, direct `SITE-A` authority, and `GROUP-AB` authority. Separate active users proved missing-permission and other-scope denial.

Payment fixtures covered PHP and USD, digital/WebPay and APT cash channels, canonical providers, pending, failed, confirmed, and unknown outcome states, plus records before, at, and after the selected half-open period. Expected aggregates were written from the fixture definition before report requests.

## Accepted Scenarios

| Scenario | Result |
| --- | --- |
| Startup and health | Live health returned `200`; readiness returned HTTP `200` with the expected local TOTP-protection degradation only; frontend and proxy were reachable. |
| Authentication | Actual H-006 password login issued the server-owned secure session cookie; the session endpoint resolved the current user; no browser storage identity or authority was created. Synthetic roles did not require TOTP. |
| Permission | The report user saw the route; the no-permission user did not. Direct navigation and backend access failed closed with `403` and no report facts. |
| SITE | `SITE-A` returned the predetermined PHP and USD attempt, confirmation, status, channel, provider, and reconciliation aggregates. |
| SITE_GROUP | `GROUP-AB` included `SITE-A` and `SITE-B`, excluded `SITE-C`, and matched predetermined group aggregates. |
| Concealed scope | The `SITE-C`-only user received `404 DASHBOARD_SCOPE_NOT_FOUND_OR_DENIED` for `SITE-A`/`GROUP-AB`; no scope name or aggregate leaked. |
| Period boundary | The record exactly at `periodStart` was included and the record exactly at `periodEnd` was excluded. Equal, reversed, and over-31-day periods were rejected. |
| Multiple currency | PHP and USD remained separate; no conversion or mixed-currency grand total appeared. |
| Reconciliation | All five supported internal consistency categories matched fixture expectations. A pending attempt remained a canonical status and was not treated as an exception. |
| No activity | A valid empty period returned success and the UI stated that no payment activity was recorded without claiming settlement or reconciliation. |
| Feature disabled | The endpoint returned `503 MANAGEMENT_PAYMENT_RECONCILIATION_REPORTING_DISABLED`; the UI showed the disabled state and no report values. |
| Refresh concurrency | Delayed older scope and period responses could not replace current results. A failed refresh retained prior values only with original timestamps and an explicit previously-loaded warning. |
| Responsive and accessible UI | Desktop `1440`, tablet `768`, and mobile `390` checks had no document-level horizontal overflow; filters, cards, internal table scrolling, keyboard focus, status text, warnings, and live states remained usable. |
| Logout | Logout cleared report state and the server session; report values did not survive as browser authority. |

## Deterministic Results

For `SITE-A`, the expected and returned currency summaries were PHP: 9 attempts totaling 1170.00 and 5 confirmations totaling 590.00; USD: 1 attempt totaling 10.50 and 2 confirmations totaling 13.50. For `GROUP-AB`, they were PHP: 11 attempts totaling 1545.00 and 6 confirmations totaling 890.00; USD: 1 attempt totaling 10.50 and 2 confirmations totaling 13.50.

The returned reconciliation counts matched the fixture: amount mismatch 1, currency mismatch 1, duplicate authoritative provider reference 2 involved confirmations, confirmed outcome without confirmation 1, and confirmation/attempt status inconsistency 1.

## Security Review

- Central PMS remained the identity, permission, authorization-epoch, and scope authority.
- No bearer token, browser-authored identity/permission/scope header, fabricated cookie, or browser storage authority was used.
- No report data was written to localStorage, sessionStorage, or IndexedDB.
- No provider payload, raw provider reference, payer data, vehicle plate, ticket detail, credential, token, or unnecessary transaction identifier was displayed or retained in evidence.
- Network inspection found no POS Server, payment-provider, or other external report-generation call.
- The UI did not describe the result as fully reconciled, provider settled, funds received, deposits matched, or revenue deposited.

## Automated Validation

- Central PMS solution build: passed with 0 errors; existing warning baseline remained.
- Central PMS focused unit tests: 67 passed.
- Central PMS focused PostgreSQL/hosted/security/authentication tests: 44 passed (38 non-hosted/persistence plus 6 production-hosted session tests).
- Management Platform dependency install: passed with 0 vulnerabilities.
- Management Platform typecheck: passed.
- Management Platform Vitest: 288 passed across 16 files.
- Management Platform Chromium: 72 passed.
- Management Platform production build: passed with 52 modules.
- Real API comparison, browser acceptance, disabled-feature, stale-response, storage, console, network, and responsive checks: passed.

Current Central PMS introduced a production-hosted test-composition dependency on vendor-adapter startup settings after the payment implementation was merged. A separate test-only correction branch makes those existing hosts explicit and removes unrelated hosted workers; it does not change the accepted product route, contract, schema, or runtime behavior.

## Evidence

The external evidence directory contains sanitized configuration and command inventories, schema and fixture records, expected values created before API requests, API headers and bodies, comparison output, frontend and backend test logs, browser summaries, screenshots, cleanup evidence, and a SHA-256 manifest. Cookie values, passwords, TOTP material, connection strings, and private tokens are excluded or redacted.

## Reporting Boundary And Deferred Scope

This acceptance establishes the merged Payment and Reconciliation Reporting backend and UI foundation for internal Central PMS consistency only. It does not establish provider settlement, merchant payout, bank deposit, cash custody, MDR or fees, refunds, chargebacks, disputes, fiscal remittance, exports, schedules, delivery, transaction drill-down, annotations, or correction workflows.

The wider Management Dashboard and Reporting capability remains `PARTIAL` because fiscal exceptions, management activity reporting, exports, schedules, and other approved v1.3 capabilities remain deferred.

## Cleanup

The task-owned frontend and Central PMS processes, browser contexts, PostgreSQL container, network, volume, database, listeners, and temporary harness were stopped or removed. Verification found zero remaining task-owned resources. The external evidence directory and intended Git worktrees were preserved; unrelated Docker and host resources were not changed.
