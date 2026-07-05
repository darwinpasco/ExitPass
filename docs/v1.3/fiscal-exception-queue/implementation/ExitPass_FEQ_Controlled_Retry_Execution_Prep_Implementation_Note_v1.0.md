# ExitPass FEQ Controlled Retry Execution Prep Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Controlled Retry Execution Prep Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-controlled-retry-execution-prep |
| Scope | Central PMS FEQ retry execution preconditions and authorization preparation only |
| Status | implemented_for_review |

## Purpose

This slice prepares the final controlled retry execution guardrails needed before any future runtime execution slice can exist.

It does not execute retry, enqueue an executable job, call POS Server POST, expose a retry endpoint, change fiscal-gated ExitAuthorization behavior, edit fiscal numbers, create manual fiscal documents, or trigger gate behavior.

## Execution-Prep Model

The application model adds an execution-preparation posture with:

- execution preparation status;
- safe block reason;
- safe execution-preparation summary;
- authorization and dual-control posture;
- POS Server readiness gate posture;
- explicit no-execution side-effect flags.

`RetryExecutionAvailable` remains false.

## Authorization and Dual-Control Prep

`FiscalExceptionRetryExecutionPreparationOptions` is disabled by default.

When execution preparation is enabled for modeling, the service requires service identity authorization. Operator/support-triggered execution remains blocked under the current policy. Production-impacting retry execution requires dual-control before the posture can advance.

No UI wiring, public trigger, endpoint, or executable path is introduced.

## POS Server Readiness Gates

Execution preparation requires explicit confirmation for:

- POS Server numbering readiness;
- POS Server idempotency contract readiness;
- POS Server sequence policy readiness;
- POS Server fiscal identity readiness;
- production BIR readiness.

If any gate is not confirmed, the service returns `RequiresPosServerReadiness` with a safe block reason.

## Safety Envelope

Execution preparation blocks unless all modeled retry chain prerequisites are safe:

- latest readback classification is `not_found`;
- a durable readback attempt exists;
- retry eligibility is eligible;
- command preparation is `PreparedNonExecutable`;
- command preparation audit basis exists;
- scheduler preparation exists and is non-executable;
- scheduler preparation audit basis exists;
- semantic request hash is available and confirmed;
- upstream finality/idempotency context is unchanged;
- immutable fiscal request facts are represented by the same semantic hash;
- queue state is not manual-review, mismatch, closed, or reconciled.

## Persistence Approach

No new persistence table was added.

This slice reuses the existing retry command preparation audit and retry scheduling preparation audit as the durable basis for execution-precondition evaluation. The execution-prep result is exposed as read-only FEQ posture only.

## FEQ Detail Projection

FEQ detail now exposes safe execution-prep posture:

- retry execution preparation status;
- retry execution block reason;
- safe execution preparation summary;
- dual-control requirement;
- authorization posture;
- POS Server readiness posture.

This remains read-only visibility and does not expose an executable action.

## Explicit Non-Goals

This slice does not implement:

- retry execution;
- POS Server POST;
- executable retry scheduler/job;
- retry endpoint;
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

Coverage was added for disabled-by-default behavior, missing scheduler preparation, missing command preparation audit, missing semantic hash, unsafe readback basis, POS Server readiness gates, dual-control requirements, no execution side effects, no endpoint/worker/job dependency, and FEQ detail execution-prep posture.

## Follow-Up Slice

Recommended next branch:

`feature/central-pms-feq-controlled-retry-execution-worker-prep`

Purpose:

Prepare a future execution worker contract behind the established execution-precondition envelope, while keeping POS Server POST and actual retry execution disabled until explicitly approved.
