# ExitPass FEQ Retry Eligibility Evaluator Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Retry Eligibility Evaluator Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-retry-eligibility-evaluator |
| Scope | Central PMS retry eligibility evaluation only |
| Status | implemented_for_review |

## Purpose

This slice adds a Central PMS FEQ retry eligibility evaluator. The evaluator determines whether a fiscal exception case is eligible for controlled retry planning or blocked, and records a safe reason for the decision. It does not execute retry, schedule retry, expose a retry endpoint, call POS Server, or change fiscal-gated ExitAuthorization behavior.

## Behavior Implemented

The evaluator returns:

- eligibility status;
- decision class: eligible, blocked, unavailable, or not required;
- block reason code;
- safe human-readable summary;
- readback classification used;
- readback attempt timestamp and count when already available;
- `RetryExecutionAvailable = false`.

The FEQ read-only detail projection now includes the retry eligibility decision, block reason, safe summary, evaluation timestamp, and readback classification basis.

## Readback-Before-Retry Enforcement

Retry eligibility is blocked when durable readback attempt history is missing.

The latest readback classification must be `not_found` before the evaluator can proceed to other safety gates. These classifications block or make retry unavailable:

| Classification | Result |
| --- | --- |
| `matched` | blocked; record/reconcile instead of retry |
| `mismatch` | blocked; manual review required |
| `failed` | blocked |
| `unavailable` | blocked |
| `unknown` | blocked |
| `identifier_missing` | blocked |
| `not_supported_yet` | unavailable |

## Safety Gates

After the readback gate passes, the evaluator checks the modeled Central PMS context:

- original fiscal reference identity exists;
- payment confirmation, payment attempt, and parking session identities exist;
- Site POS Server context exists by id or safe ref;
- upstream finality/idempotency reference is present;
- known fiscal configuration failure states/reasons are not active;
- manual-review, mismatch, reconciled, or closed cases are not eligible.

The current model does not expose a durable semantic request hash field. This slice does not invent one; future retry execution work must add or confirm semantic request hashing before actual retry is allowed.

## Explicit Non-Goals

This slice does not implement:

- retry execution;
- retry scheduler;
- retry endpoint;
- POS Server mutation;
- fiscal document creation;
- fiscal number editing;
- payment finality mutation;
- ExitAuthorization issuance;
- gate behavior;
- Operator Console UI;
- Management Dashboard projection;
- fiscal-gated ExitAuthorization enforcement.

## Validation Notes

Unit tests cover:

- no readback attempt history blocks retry;
- matched blocks retry;
- mismatch blocks retry and keeps manual-review posture;
- failed, unavailable, and unknown block retry;
- identifier-missing blocks retry;
- not-supported readback makes retry unavailable;
- `not_found` passes the readback gate but still blocks when request/idempotency context is missing;
- eligible-for-planning is returned only for `not_found` plus safe modeled prerequisites;
- no POS Server dependency, retry scheduler, retry worker, or retry endpoint is introduced.

## Follow-Up Slice

Recommended next branch:

`feature/central-pms-feq-controlled-retry-scheduler-prep`

Purpose:

Prepare controlled retry scheduling and command modeling only after eligibility evaluation is available. The next slice should still avoid actual retry execution unless explicitly scoped and approved.
