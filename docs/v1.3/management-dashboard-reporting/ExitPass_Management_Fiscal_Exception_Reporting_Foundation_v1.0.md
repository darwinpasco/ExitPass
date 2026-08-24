# ExitPass Management Fiscal Exception Reporting Foundation v1.0

## Purpose

This foundation adds a read-only, aggregate Management Platform report for Sales Invoice issuance coordination lifecycle and the exception conditions that Central PMS can prove from records it owns. It does not move Sales Invoice authority from POS Server and does not certify BIR compliance.

## Authority Boundary

POS Server remains authoritative for Sales Invoice issuance, number allocation, document status, printing, reprinting, adjustment, void, Electronic Journal, X Reading, Z Reading, BIR Sales Summary, and Annex reports. Central PMS reports only its persisted coordination reference and the latest POS Server outcome recorded through the existing issuance integration. Report generation performs no synchronous POS Server call and no Site POS Server database query.

Payment confirmation establishes the expected amount and currency linked to a coordination reference. It does not establish that a Sales Invoice was issued, printed, delivered, or made digitally available.

## Source Inventory

| Source | Owner and purpose | Scope and timestamps | Supported interpretation | Prohibited interpretation |
| --- | --- | --- | --- | --- |
| `core.fiscal_issuance_references` | Central PMS coordination reference and latest persisted issuance state | `site_id`; `first_recorded_at`, `last_updated_at`, and persisted outcome timestamps | Proves a Central PMS issuance expectation/request reference and its latest recorded coordination state | Does not independently prove printing, delivery, document value, or live POS Server state |
| `core.payment_confirmations` | Central PMS authoritative payment confirmation linked by `payment_confirmation_id` | Related parking session/Site; `created_at`; exact `confirmed_amount` and ISO currency | Supplies expected issuance amount and currency for aggregate reporting | Payment confirmation alone does not prove Sales Invoice issuance |
| Persisted POS Server outcome fields on the coordination reference | Minimum response evidence retained by the existing issuance coordination path | Bound to the reference and Site; latest update/completion timestamps | Recorded/replayed/reconciled states prove that Central PMS persisted a successful authoritative issuance outcome | A URL or request attempt does not prove printing or delivery; raw responses and Sales Invoice numbers are excluded |
| Retry and outbox records | Existing integration delivery and retry coordination | Operational timestamps and retry state | Supports the existing issuance workflow | No approved aggregate overdue deadline or deterministic retry-exhaustion reporting state is established for this report |

## Cohort And Time Basis

The cohort is active, non-superseded `core.fiscal_issuance_references` whose `first_recorded_at` falls in the requested half-open UTC interval `[periodStart, periodEnd)`. The response declares `timeBasis` as `FISCAL_ISSUANCE_REFERENCE_FIRST_RECORDED_AT` and evaluates each cohort member using its latest persisted state at query time.

`dataAsOf` is the latest relevant coordination-reference or linked payment-confirmation timestamp in the selected scope and cohort. It is not a claim of live POS Server freshness.

Pending work is a lifecycle state, not automatically an exception. The current architecture has no approved completion deadline suitable for aggregate overdue classification, so overdue detection is explicitly unavailable.

## Lifecycle Normalization

| Canonical state | Reporting lifecycle | Meaning |
| --- | --- | --- |
| `NOT_REQUIRED` | `NOT_REQUIRED` | The coordination record says issuance is not required. |
| `PENDING_FISCAL_ISSUANCE` | `PENDING` | Work is pending; this alone is not an exception. |
| `FISCAL_ISSUANCE_REQUESTED` | `REQUESTED` | The request was recorded; completion is not implied. |
| `FISCAL_ISSUANCE_RECORDED`, `FISCAL_ISSUANCE_REPLAYED`, `FISCAL_ISSUANCE_RECONCILED` | `ISSUED` | Central PMS persisted a successful authoritative issuance outcome. Printing and delivery are not implied. |
| `FISCAL_ISSUANCE_FAILED_REQUEST`, `FISCAL_ISSUANCE_FAILED_CONFIGURATION`, `FISCAL_ISSUANCE_FAILED_SERVICE` | `FAILED` | The latest coordination state is a supported failure. |
| `FISCAL_ISSUANCE_CONFLICT` | `CONFLICT` | The latest authoritative references conflict. |
| `FISCAL_ISSUANCE_UNKNOWN` | `OUTCOME_UNAVAILABLE` | Central PMS does not hold a conclusive latest outcome. |
| `FISCAL_ISSUANCE_MANUAL_REVIEW` | `MANUAL_REVIEW` | Existing workflow state requires manual review. |
| `FISCAL_ISSUANCE_EXCEPTION_RELEASED` | `EXCEPTION_RELEASED` | Existing exception handling released the record; success is not inferred. |
| Any future value | `OTHER` | Unknown values remain visible and are never discarded. |

## Exception Definitions

| Stable ID | Condition and sources | Counting and currency | Terminal and resolution boundary |
| --- | --- | --- | --- |
| `SALES_INVOICE_ISSUANCE_FAILED` | Latest state is one of the three canonical failed states. Requires a coordination reference and linked confirmation. | One count per reference; affected expected amount is summed in PHP. | Not declared permanently terminal; a later authoritative retry may resolve it. |
| `SALES_INVOICE_REFERENCE_CONFLICT` | Latest state is `FISCAL_ISSUANCE_CONFLICT`. | One count per reference; affected expected amount remains in PHP. | Not declared permanently terminal; authoritative conflict handling may resolve it. |
| `SALES_INVOICE_OUTCOME_UNAVAILABLE` | Latest state is `FISCAL_ISSUANCE_UNKNOWN` or `FISCAL_ISSUANCE_MANUAL_REVIEW`. | One count per reference; affected expected amount remains in PHP. | Can resolve when a conclusive outcome is persisted. It does not mean issuance failed. |

The current source does not support deterministic document amount mismatch, currency mismatch, duplicate Sales Invoice reference, missing expectation, retry exhausted, or overdue exception categories without an unacceptable false-positive risk. Those categories are not advertised.

## Scope, Period, And Money

`GET /v1/management-platform/dashboard/fiscal-exception-summary` requires explicit `SITE` or `SITE_GROUP` scope and an explicit authorized reference. Missing, empty, GLOBAL, and cross-scope requests fail closed. Site Group queries include only current member Sites resolved by Central PMS. The required UTC period has no default, uses half-open bounds, and may not exceed 31 days.

Money uses exact database decimal values in PHP. Expected issuance amounts are derived from linked payment confirmations. A non-PHP source record causes the report to fail closed. No settled amount, deposited revenue, net proceeds, or funds-received claim is made.

## Authorization And Feature Control

The endpoint requires the H-006/I-020 live Management Platform human session, active account, current authorization epoch, server-derived role assignment and scope, permission `sales-invoice-report.view`, and policy `ManagementPlatformFiscalExceptionSummaryRead`. Browser-authored actor, permission, role, Site, Site Group, and authorization-epoch headers are not authority.

Both `ManagementPlatform:DashboardReporting:Enabled` and the typed, default-disabled `ManagementPlatform:DashboardReporting:FiscalExceptions:Enabled` control must be enabled. A disabled feature returns no report payload.

## Availability And Freshness

Activity returns `PARTIAL` because Central PMS exposes only its bounded coordination projection. An empty cohort returns successful `NO_ACTIVITY`, not an error or an exception-free certification. A source failure returns a controlled unavailable error without zero-valued placeholders. Source coverage, warnings, limitations, unavailable facts, `generatedAt`, and `dataAsOf` remain explicit.

## Audit And Privacy

Successful access, permission denial, invalid scope/period, concealed scope, feature-disabled requests, partial results, and source failures produce safe audit evidence. Audit metadata is limited to report ID, actor/session reference, effective scope, period, time basis, result classification, aggregate count, correlation ID, and timestamp.

The response and audit exclude payer data, vehicle plates, tickets, Sales Invoice numbers, provider references, raw POS Server responses, raw fiscal documents, Electronic Journal content, credentials, tokens, statutory identifiers, and transaction-level detail.

## API And Failure Behavior

Contract: `management-platform-fiscal-exception-reporting:v1`.

The established Management Platform problem-details envelope covers unauthenticated, forbidden, invalid scope, concealed scope, invalid period, invalid or stale session, feature disabled, source unavailable, and unexpected failure. Raw exception details are never returned. The endpoint is GET-only and performs no report mutation.

## Test Evidence

Coverage includes contract determinism; explicit Site and Site Group scope; missing/GLOBAL/cross-scope denial; live hosted-session authorization; current authorization epoch; active account; UTC and 31-day period validation; half-open boundaries; no activity; every supported lifecycle and exception category; pending-not-exception behavior; unknown-state preservation; exact PHP aggregation; non-PHP fail-closed behavior; source failure; audit outcomes; sensitive-field exclusion; GET-only routing; and PostgreSQL-backed query behavior.

## Unavailable And Deferred Scope

The following remain unavailable: printing and delivery proof, digital-copy availability, overdue detection, retry-exhaustion reporting, document amount/currency comparison, duplicate Sales Invoice reference analysis, adjustments, voids, reprints, Electronic Journal, X/Z reports, BIR Sales Summary, Annex reports, transaction drill-down, exports, schedules, email delivery, exception resolution, and a Management Platform frontend.

The backend foundation is `IMPLEMENTED_NOT_ACCEPTED`. Frontend implementation and merged-source integrated runtime acceptance remain required. The overall Management Dashboard and Reporting capability remains `PARTIAL`.
