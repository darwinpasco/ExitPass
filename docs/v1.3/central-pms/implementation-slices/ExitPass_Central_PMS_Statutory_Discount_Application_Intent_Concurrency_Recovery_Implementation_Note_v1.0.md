# ExitPass Central PMS Statutory Discount Application Intent Concurrency Recovery Implementation Note v1.0

## Purpose

This note records the bounded Central PMS runtime correction for concurrent statutory-discount payable-basis application intent between the shared service-channel decision route and the Operator Console apply-payable-basis route.

The fix addresses the canonical-only runtime revalidation blocker where equivalent concurrent application intent could surface PostgreSQL SQLSTATE `40P01` as an unhandled HTTP 500 instead of a deterministic application replay, in-progress, or retryable recovery posture.

## Confirmed Blocker

The governing revalidation report selected `PAUSE_FOR_RUNTIME_REWORK` because concurrent application intent from:

- `POST /v1/statutory-discounts/decisions` with `applyPayableBasis=true`
- the Operator Console statutory-discount apply-payable-basis route

could contend on the same canonical application and payable-basis mutation path. The unsafe outcome was SQLSTATE `40P01` leaking as HTTP 500 with no client-safe recovery classification.

Local reproduction of the exact natural deadlock was timing-sensitive on the current branch, but the affected concurrency test and controlled SQLSTATE `40P01` fault seams proved the recovery boundary and canonical reconciliation behavior.

## Root Cause

Application-v1 creation and idempotency convergence were canonical, but the payable-basis writer/completion phase was not consistently serialized through the same canonical application identity for both callers.

The shared route and Operator Console route could therefore reach the writer and application completion with different route-level timing around the same decision and validation linkage. Under PostgreSQL lock contention, one transaction could lose with SQLSTATE `40P01`; the endpoint did not map that loser into a durable replay or retry posture.

## Lock Order and Transaction Findings

The selected lock boundary is the canonical application idempotency scope:

`statutory-discount-payable-basis-application:{statutoryDiscountDecisionCommandId}`

Both service-channel and Operator Console apply paths now converge through the staged-command service and use the same application-scoped advisory lock before writer mutation and application completion.

No table-level locks, global transaction isolation changes, broad PostgreSQL retry policy, or channel-specific application identity were introduced.

The durable boundaries remain separate:

1. request validation
2. decision resolution
3. application create or resolve
4. application `PROCESSING`
5. payable-basis mutation
6. mutation reconciliation
7. application `APPLIED`
8. response adaptation
9. GET readback

## Runtime Correction

The staged-command repository now exposes application-scope lock execution by idempotency scope. The application service exposes that lock for already-created application records.

The Operator Console payable-basis apply service uses the canonical application lock around:

- durable application re-read
- terminal applied/failed replay
- `PROCESSING` transition
- authoritative payable-basis writer call
- `APPLIED` completion
- SQLSTATE `40P01` reconciliation

The shared service-channel route still reaches the existing payable-basis writer through the same application service. It is allowed to complete the `PROCESSING` application stage it just advanced; an ordinary Operator Console caller observing a pre-existing `PROCESSING` application receives the established in-progress posture instead of duplicating the writer.

## SQLSTATE 40P01 Recovery

SQLSTATE `40P01` is handled only at statutory application-intent boundaries:

- staged application create/resolve
- payable-basis writer and application completion

On deadlock, Central PMS treats the failed transaction as rolled back and reconciles by canonical decision/application identity before any retry-like behavior is considered.

Recovery outcomes are:

- durable `APPLIED` application found: return the canonical application without reapplying
- durable `PROCESSING` application found: return in-progress recovery posture
- terminal failed application found: return the durable terminal failure
- no durable application found: return retryable temporary-unavailable posture
- semantic mismatch found: preserve deterministic semantic conflict

The safe temporary-unavailable code is:

`STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_TEMPORARILY_UNAVAILABLE`

The safe recovery classification is:

`WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY`

The endpoint maps this to a client-safe temporary-unavailable result rather than leaking SQL, table names, stack traces, or connection details.

## Bounded Retry Posture

This slice does not blindly rerun the payable-basis writer after deadlock.

The implemented posture is reconciliation-before-retry:

- if a durable winner exists, replay it
- if a durable in-progress command exists, instruct polling/recovery
- if no durable command exists, return a retryable result using the original idempotency key

No unbounded loop, random sleep, or global retry policy was added.

## Exactly-Once Proof

The fixed path preserves:

- one canonical decision-v2
- one canonical application-v1 per decision
- one statutory validation linkage
- one payable-basis mutation
- one applied tariff snapshot
- one approved final payable amount
- no duplicate discount
- no duplicate VAT effect

The focused concurrency test was executed 20 times, and the grouped canonical statutory integration suite passed twice plus one controlled parallel-capable pass with the formerly failing concurrency case included.

## Replay and GET Behavior

Replay behavior remains canonical:

- service-channel replay after Operator Console wins returns the same application
- Operator Console replay after service-channel wins returns the same application
- WebPay/APT equivalent replay converges on the same application
- different transport keys do not create another business application
- changed material facts remain semantic conflict

GET readback is not mutated by this slice. Existing GET behavior continues to read the durable canonical decision/application state.

## Security and Logging

The safe error mapping does not expose:

- PostgreSQL SQL text
- schema or table names
- connection strings or credentials
- raw statutory IDs
- raw evidence
- Base64 payloads
- reviewer-sensitive details
- payment-provider payloads
- HikCentral data
- internal stack traces

Operational logging may retain safe correlation and canonical command identifiers already used by the statutory-discount services.

## Database Posture

No database schema change was required.

The fix uses the canonical staged decision/application tables and the promoted statutory-discount schema from the current canonical database source. Validation used the canonical disposable PostgreSQL fixture built from:

`D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql`

Retired statutory app-local patches were not applied.

## Deferred Readback Fields

This slice deliberately does not implement the later channel-safe readback-hardening fields, including:

- `siteId`
- `siteGroupId`
- explicit VAT-exclusive facts
- explicit VAT amount facts
- channel-safe readiness adaptation

Those remain the next bounded backend task after this runtime fix.

## Channel Authorization

This task does not authorize WebPay or Assisted Payment Terminal integration.

WebPay integration: not authorized yet
APT integration: not authorized yet
