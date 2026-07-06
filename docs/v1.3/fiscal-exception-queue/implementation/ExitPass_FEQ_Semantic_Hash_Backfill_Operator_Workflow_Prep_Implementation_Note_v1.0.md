# ExitPass FEQ Semantic Hash Backfill Operator Workflow Prep Implementation Note v1.0

## Scope

This slice adds an internal governed workflow model for requesting and recording one guarded semantic hash backfill request for one fiscal issuance reference. It does not add public UI, public endpoints, batch backfill, retry execution, retry workers, executable jobs, POS Server POST calls, payment finality changes, ExitAuthorization changes, gate behavior, fiscal number editing, or manual fiscal document creation.

## Workflow Model

The workflow request records one support/admin/operator intent and carries safe metadata only:

- fiscal issuance reference id;
- recalculation preview audit id;
- mutation-preparation audit id;
- approval reference;
- dual-control reference;
- actor/service identity;
- reason code and safe justification;
- correlation id;
- request mode, limited to single-record operation;
- dry-run or execute-controlled-mutation intent.

The workflow service validates request shape, actor identity, explicit approval reference, required dual-control reference, preview audit basis, mutation-preparation audit basis, approval/precondition posture, and single-record mode.

## Default Configuration

`FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions.EnableControlledMutationInvocation` defaults to false.

When invocation is disabled, an otherwise valid request returns `PreparedButMutationInvocationDisabled`, persists a workflow audit record, and does not call the guarded mutation service. Dry-run requests return `ReadyForOperatorApproval` and also do not mutate the fiscal issuance reference.

## Guarded Mutation Invocation

The workflow service calls the existing guarded backfill mutation service only when:

- the request explicitly asks to execute controlled mutation;
- dry-run is false;
- workflow mutation invocation is enabled;
- approval/precondition posture is ready;
- preview and mutation-preparation audit ids match the supplied basis;
- actor, approval, dual-control, and single-record gates pass.

The guarded mutation service remains responsible for the transaction-bound row re-read/lock and semantic-hash-only update safety checks.

## Audit Persistence

A narrow append-only workflow audit table was added:

`core.fiscal_issuance_semantic_hash_backfill_workflow_requests`

It stores safe request facts, workflow status, block reason, invocation posture, optional guarded mutation audit basis, requested timestamp, and safe summary. It does not store raw POS Server payloads, payment provider payloads, secrets, customer PII, statutory evidence, or canonical source text.

If invocation is enabled, the workflow records an authorization/intention audit before calling guarded mutation and records a final workflow audit with the guarded mutation result basis after the call.

## FEQ Detail Impact

FEQ read-only detail can surface the latest workflow request posture:

- latest workflow status;
- latest workflow request id;
- approval reference;
- dual-control posture;
- mutation invocation posture;
- safe summary.

`RetryExecutionAvailable` remains false.

## Remaining Blockers Before Retry Execution

Retry execution is still not implemented. A future slice would need a separately governed retry execution entrypoint and production readiness controls. This workflow only governs semantic hash backfill request/audit posture and optional invocation of the already guarded single-record semantic-hash-only mutation path.
