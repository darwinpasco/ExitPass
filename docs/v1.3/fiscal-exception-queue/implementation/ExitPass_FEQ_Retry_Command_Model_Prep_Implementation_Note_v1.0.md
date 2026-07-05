# ExitPass FEQ Retry Command Model Prep Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Retry Command Model Prep Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-retry-command-model-prep |
| Scope | Central PMS FEQ retry command model preparation only |
| Status | implemented_for_review |

## Purpose

This slice prepares the immutable FEQ retry command/request model and safety envelope for a future retry execution slice. It does not execute retry, schedule retry, expose a retry endpoint, call POS Server, mutate fiscal state, change payment finality, issue ExitAuthorization, or trigger gate behavior.

## Behavior Added

Added read-only Central PMS application models for:

- retry eligibility decision metadata;
- retry command preparation status;
- idempotency context availability;
- semantic request hash availability;
- future retry command envelope facts;
- retry command preparation result.

Added `FiscalExceptionRetryCommandPreparationService`, which evaluates an FEQ case detail and returns a safe blocked or unavailable result. The service has no POS Server, scheduler, payment, ExitAuthorization, or gate dependencies.

## Preconditions and Block Reasons

Command preparation is blocked when:

- retry eligibility is not eligible for controlled retry planning;
- a caller treats the prepared command as executable while `RetryExecutionAvailable` is false;
- latest durable readback is not `not_found`;
- durable readback attempt history is missing;
- payment confirmation, payment attempt, parking session, or fiscal reference context is missing;
- Site POS Server context is missing;
- upstream finality/idempotency reference is missing;
- a new upstream finality reference is supplied;
- fiscal configuration, manual-review, mismatch, closed, or reconciled posture is present.

The service always reports:

- `PosServerPostCalled = false`;
- `RetryScheduled = false`;
- `PaymentFinalityChanged = false`;
- `ExitAuthorizationIssued = false`;
- `GateBehaviorTriggered = false`;
- `FiscalNumberEdited = false`;
- `ManualFiscalDocumentCreated = false`.

## Semantic Request Hash Gap

The prior retry eligibility slice stated that the current model does not expose a durable semantic request hash field. This slice preserves that decision.

Command preparation returns unavailable when the otherwise safe case reaches the semantic request hash gate. It does not invent a hash, infer a hash from partial payloads, or treat missing hash data as safe.

## FEQ Detail Projection

FEQ detail now exposes read-only retry command preparation posture:

- command preparation status;
- command block reason;
- safe command preparation summary;
- semantic request hash availability status;
- idempotency context availability status.

`RetryExecutionAvailable` remains false.

## Validation Notes

Unit tests cover:

- retry eligibility not eligible blocks command preparation;
- unsafe readback classifications block command preparation;
- missing durable readback attempt blocks command preparation;
- missing or changed upstream finality/idempotency context blocks command preparation;
- missing semantic request hash returns unavailable;
- fiscal config/manual-review/mismatch/reconciled states block preparation;
- command preparation has no POS Server, scheduler, payment, ExitAuthorization, or gate dependency;
- FEQ detail returns safe read-only command preparation posture.

## Explicit Non-Goals

This slice does not implement:

- retry execution;
- retry scheduler;
- retry endpoint;
- POS Server POST;
- POS Server repository/runtime changes;
- fiscal-gated ExitAuthorization enforcement;
- payment finality mutation;
- ExitAuthorization issuance;
- gate behavior;
- Operator Console UI;
- Management Dashboard projection;
- fiscal number editing;
- manual fiscal document creation.

## Follow-Up Slice

Recommended next branch:

`feature/central-pms-feq-retry-command-persistence-audit-prep`

Purpose:

Define durable retry command attempt/audit storage and semantic request hash source before any retry execution or scheduler work.
