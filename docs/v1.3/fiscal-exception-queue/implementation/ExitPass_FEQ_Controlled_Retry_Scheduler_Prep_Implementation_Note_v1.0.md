# ExitPass FEQ Controlled Retry Scheduler Prep Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Controlled Retry Scheduler Prep Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-controlled-retry-scheduler-prep |
| Scope | Central PMS FEQ retry scheduling preparation only |
| Status | implemented_for_review |

## Purpose

This slice prepares the controlled retry scheduling model, non-executable schedule envelope, audit intent persistence, and disabled-by-default feature/config guard for a future retry execution slice.

It does not execute retry, enqueue an executable job, call POS Server POST, expose a retry endpoint, change fiscal-gated ExitAuthorization behavior, edit fiscal numbers, create manual fiscal documents, or trigger gate behavior.

## Scheduler-Prep Model

The application model adds a retry scheduling preparation posture with:

- scheduling preparation status;
- safe block reason;
- safe scheduling summary;
- non-executable schedule preparation envelope;
- retry command preparation audit basis;
- retry eligibility basis;
- readback basis;
- semantic request hash basis;
- idempotency/upstream finality basis;
- requested and earliest-eligible timestamps;
- correlation and service identity context when available;
- explicit no-execution side-effect flags.

`RetryExecutionAvailable` remains false.

## Feature and Config Guard

`FiscalExceptionRetrySchedulingPreparationOptions` is disabled by default.

Scheduling preparation returns `Disabled` unless `EnableSchedulePreparation` is explicitly enabled. Even when preparation mode is enabled, the service still requires retry schedule policy and retry backoff policy to be configured before it can persist a scheduled-prepared intent.

No execution mode is introduced.

## Persistence Approach

This slice adds the narrow table `core.fiscal_issuance_retry_schedule_preparations`.

The table stores only safe scheduling-preparation audit facts:

- retry schedule preparation attempt identity;
- fiscal issuance reference identity;
- retry command preparation attempt identity;
- payment, parking session, site, and Site POS Server context when available;
- latest readback classification basis;
- retry eligibility decision basis;
- semantic request hash availability basis;
- idempotency context basis;
- scheduling preparation status and block reason;
- requested and earliest eligible timestamps;
- safe summary;
- correlation and service identity context.

The table is an audit/intention surface only. It is not an executable job queue.

## Blocking Rules

Scheduler prep blocks or returns unavailable when:

- scheduling preparation is disabled;
- command preparation is not `PreparedNonExecutable`;
- command preparation audit basis is missing;
- retry eligibility is not eligible;
- readback basis is missing or not `not_found`;
- semantic request hash is missing or unconfirmed;
- idempotency/upstream finality context is unavailable;
- a new upstream finality reference is requested;
- manual-review, mismatch, closed, or reconciled posture is present;
- retry schedule policy is missing;
- retry backoff policy is missing;
- audit persistence is unavailable or fails.

## FEQ Detail Projection

FEQ detail can now expose safe scheduler-prep posture:

- retry scheduling preparation status;
- retry scheduling block reason;
- safe scheduling summary;
- last scheduling preparation timestamp;
- scheduling preparation attempt count.

This remains read-only visibility and does not expose a retry endpoint or executable action.

## Explicit Non-Goals

This slice does not implement:

- retry execution;
- executable retry scheduler/job;
- retry endpoint;
- POS Server POST;
- POS Server repository/runtime changes;
- fiscal-gated ExitAuthorization enforcement;
- payment finality mutation;
- fiscal reference success recording;
- ExitAuthorization issuance;
- gate behavior;
- fiscal number editing;
- manual fiscal document creation;
- Operator Console UI;
- Management Dashboard projection.

## Validation Notes

Coverage was added for disabled-by-default behavior, unsafe command-prep blocking, semantic hash blocking, readback blocking, missing command-prep audit blocking, policy/config blocking, audit persistence requirements, safe audit intent persistence, no execution side effects, and FEQ detail scheduler posture.

## Follow-Up Slice

Recommended next branch:

`feature/central-pms-feq-controlled-retry-execution-prep`

Purpose:

Prepare controlled retry execution preconditions and operator/service authorization without enabling runtime retry execution until an explicit execution slice is approved.
