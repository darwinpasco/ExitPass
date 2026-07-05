# ExitPass FEQ Retry Command Persistence / Audit Prep Implementation Note v1.0

## Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass FEQ Retry Command Persistence / Audit Prep Implementation Note |
| Version | v1.0 |
| Branch | feature/central-pms-feq-retry-command-persistence-audit-prep |
| Scope | Central PMS FEQ retry command preparation audit persistence only |
| Status | implemented_for_review |

## Purpose

This slice persists durable FEQ retry command preparation audit intent records. It does not execute retry, schedule retry, expose a retry endpoint, call POS Server POST, mutate fiscal state, change payment finality, issue ExitAuthorization, or trigger gate behavior.

## Persistence Approach

No existing Central PMS fiscal issuance table safely preserved the full preparation history without overloading fiscal reference state. This slice adds the narrow audit table `core.fiscal_issuance_retry_command_preparations`.

The table stores only safe command-preparation facts:

- retry command preparation attempt identity;
- fiscal issuance reference identity;
- payment confirmation, payment attempt, and parking session identities when available;
- site and Site POS Server context when available;
- latest readback classification basis;
- retry eligibility decision basis;
- command preparation status and block reason;
- semantic request hash availability status;
- idempotency context availability status;
- attempted and created timestamps;
- safe summary;
- correlation and service identity context when available.

No raw POS Server payloads, semantic payload bodies, fiscal number edits, retry job state, or executable command queue fields are stored.

## Flow Changes

`FiscalExceptionRetryCommandPreparationService` now exposes an auditable async preparation path. When an audit repository is configured and the FEQ detail is tied to a known fiscal issuance reference, the service records the preparation result before the caller proceeds with the returned posture.

If audit persistence fails, preparation fails with `retry_command_preparation_audit_persistence_failed` and the FEQ detail flow does not continue as if the command-preparation state is durable.

## FEQ Detail Projection

FEQ detail can now include safe retry command preparation audit summary data:

- last command preparation status;
- last command preparation timestamp;
- preparation attempt count;
- last command block reason;
- semantic request hash availability status;
- idempotency context availability status;
- safe summary.

`RetryExecutionAvailable` remains false.

## Semantic Request Hash Gap

This slice preserves the prior command-model decision: the current model does not expose a durable semantic request hash. Command preparation remains unavailable at the semantic hash gate when the hash is required but missing or unconfirmed. The implementation does not invent hashes or infer hashes from partial payloads.

## Explicit Non-Goals

This slice does not implement:

- retry execution;
- retry scheduler or retry job;
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

## Validation Notes

Unit and integration coverage was added for command preparation audit persistence, unavailable and future non-executable prepared postures, persistence failure behavior, FEQ detail audit summary projection, and absence of retry execution/scheduler/endpoint behavior.

## Follow-Up Slice

Recommended next branch:

`feature/central-pms-feq-semantic-request-hash-source-prep`

Purpose:

Define the durable semantic request hash source and confirmation rules before any controlled retry execution or scheduler slice.
