# ExitPass Management Payment and Reconciliation Reporting Foundation v1.0

## Boundary

This foundation adds a read-only Central PMS reporting facade for internal payment activity and consistency. It does not implement the Management Platform UI and does not establish provider settlement, merchant payout, bank deposit, cash custody, MDR or fees, refunds, chargebacks, disputes, or fiscal remittance.

## API

`GET /v1/management-platform/dashboard/payment-reconciliation-summary`

- Contract: `management-platform-payment-reconciliation-reporting:v1`
- Permission: `reconciliation.view`
- Policy: `ManagementPlatformPaymentReconciliationSummaryRead`
- Feature gates: `ManagementPlatform:DashboardReporting:Enabled` and `ManagementPlatform:DashboardReporting:PaymentReconciliation:Enabled`; both default disabled.
- Required scope: explicit `SITE` or `SITE_GROUP` plus a non-empty authorized reference.
- Required period: explicit UTC `periodStart` and `periodEnd`, interpreted as `[periodStart, periodEnd)`, with a maximum span of 31 days.

Null, empty, GLOBAL, inferred, local-time, reversed, or overlong requests fail closed. Scope-not-found and scope-denied use the same concealed response.

## Source And Semantic Inventory

| Source | Owner | Relevant fields | Status values | Timestamp meaning | Reporting use | Unsupported interpretation |
| --- | --- | --- | --- | --- | --- | --- |
| `core.payment_attempts` | Central PMS core payment control | payment rail, currency, exact amount, attempt status | `REQUESTED`, `PENDING_PROVIDER`, `PENDING_FINALIZATION`, `CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED` | `requested_at` selects the period; `updated_at` contributes to `dataAsOf` | Attempt counts/amounts, status, channel/provider dimensions | Existence is not payment confirmation or finality |
| `core.payment_confirmations` | Central PMS core payment control | attempt link, outcome link, rail, provider reference, currency, exact confirmed amount, status | `RECORDED`, `VOIDED` | `confirmed_at` selects the period; `created_at` contributes to `dataAsOf` | Recorded-confirmation counts/amounts and consistency checks | A provider request, callback receipt, or pending attempt is not confirmation |
| `payments.provider_outcomes` | Central PMS payments domain | attempt/rail links, normalized outcome, currency, exact amount | `CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED`, `REJECTED`, `UNKNOWN` | `verified_at` selects the period; `updated_at` contributes to `dataAsOf` | Canonical outcome status and confirmed-outcome consistency | Not a provider-live query or settlement proof |
| `payments.payment_rails` | Central PMS payments configuration | stable rail code/type, provider code, currency | canonical rail enums and lifecycle | Configuration metadata only | Channel/provider grouping; canonical `CASH` remains cash | Cash is not forced into external-provider semantics |
| `core.parking_sessions` | Central PMS session control | Site and Site Group links | canonical session lifecycle | Not used as payment currency | Joins payment records to authorized scope | No plate, ticket, vehicle, or session detail is returned |
| `sites.sites`, `sites.site_groups` | Central PMS Site registry | safe scope identity and lifecycle | canonical Site lifecycle | Latest scope update | Server-side scope resolution | Missing scope never means GLOBAL |

All money is PostgreSQL `numeric` and .NET `decimal`. ExitPass accepts and reports PHP only. A non-PHP source record causes the report to fail closed instead of exposing an unsupported currency.

## Status Normalization

| Record type | Known canonical values | Unknown/new value |
| --- | --- | --- |
| Payment attempt | `REQUESTED`, `PENDING_PROVIDER`, `PENDING_FINALIZATION`, `CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED` | `OTHER` |
| Payment confirmation | `RECORDED`, `VOIDED` | `OTHER` |
| Provider outcome | `CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED`, `REJECTED`, `UNKNOWN` | `OTHER` |

Unknown values are retained in aggregate counts as `OTHER`; they are never discarded. Open and pending attempts remain status facts and are not automatically reconciliation exceptions.

## Reconciliation Definitions

| Stable category | Definition | Source requirement | Counting and monetary treatment | Limitation |
| --- | --- | --- | --- | --- |
| `ATTEMPT_CONFIRMATION_AMOUNT_MISMATCH` | A recorded confirmation differs from its attempt amount while currency matches | attempt plus recorded confirmation | Count mismatched confirmations; sum absolute differences by currency | Does not repair either record |
| `ATTEMPT_CONFIRMATION_CURRENCY_MISMATCH` | A recorded confirmation currency differs from its attempt currency | attempt plus recorded confirmation | The stable category remains present; valid PHP-only records produce zero findings | Non-PHP source data fails the report closed and no conversion is attempted |
| `DUPLICATE_AUTHORITATIVE_PROVIDER_REFERENCE` | Multiple recorded confirmations use the same non-empty reference for the same canonical provider | confirmations plus rail/provider metadata | Count involved confirmation records; no monetary total | Reference values are never returned |
| `CONFIRMED_OUTCOME_WITHOUT_CONFIRMATION` | A verified `CONFIRMED` provider outcome has no confirmation linked by outcome ID | outcome, attempt, optional confirmation | Count outcomes and sum outcome amount by currency, separately from confirmed revenue | Does not create payment confirmation |
| `CONFIRMATION_ATTEMPT_STATUS_INCONSISTENT` | A recorded confirmation links to an attempt whose current status is not `CONFIRMED` | attempt plus recorded confirmation | Count confirmations and sum confirmation amount by currency | Does not change attempt status |

Confirmation without an attempt is prevented by the canonical foreign key and is not advertised as an observable category. Duplicate references are evaluated within canonical provider identity to avoid comparing unrelated provider namespaces.

## Availability And Freshness

The catalog classifies `payment-reconciliation-summary` as `PARTIAL`. Activity is backed by canonical sources, but external financial facts remain unavailable. A period with no relevant canonical records returns an explicit `NO_PAYMENT_ACTIVITY_IN_PERIOD` warning, empty aggregate arrays, and `NOT_APPLICABLE` freshness; no unavailable value is represented as numeric zero. `dataAsOf` is the latest relevant canonical record timestamp inside the authorized scope and selected period and is not provider-live freshness.

## Authentication, Authorization, And Audit

The H-006/I-020 Management Platform human session supplies the actor. Central PMS revalidates the live session, audience, active account, credential version, authorization epoch, expiry, `reconciliation.view`, and role-assignment scope. Browser-authored identity, role, permission, Site, Site Group, and epoch headers are not authority.

Safe audits cover success, invalid request, feature denial, permission/scope denial, and source/query failure. Audit metadata is limited to report ID, actor/session references, requested scope and period, result classification, correlation reference, and timestamp. No report payload, provider reference, credential, token, payer data, plate, or ticket detail is audited.

## Validation And Deferred Scope

Unit, hosted API, live-session, and PostgreSQL-backed tests cover contract shape, explicit scope/period validation, exact PHP decimal aggregation, non-PHP fail-closed behavior, half-open boundaries, known/unknown status handling, internal conditions, no-activity posture, feature gates, current session/epoch/account checks, permission/scope denial, safe audits, GET-only behavior, and sensitive-field exclusion.

Deferred work includes the Management Platform UI, exports, schedules, delivery, drill-down, external settlement/payout/bank/custody facts, fees, refunds, chargebacks/disputes, fiscal reporting, and reconciliation mutation. The v1.3 dashboard capability remains partial and not accepted end to end.
