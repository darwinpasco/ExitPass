# ExitPass FEQ Controlled Retry Execution UAT Runbook v1.0

## Purpose

This runbook validates that Central PMS FEQ controlled retry execution can be tested safely with disposable/local data. It is validation guidance only. It does not add a public endpoint, Operator Console UI, Management Dashboard projection, batch retry, scheduler job, fiscal-gated ExitAuthorization, gate behavior, payment finality mutation, fiscal number editing, manual fiscal document creation, or POS Server repository changes.

## Safety Rules

- Use a disposable Central PMS database only.
- Use a local or controlled POS Server instance only.
- Do not use production identifiers, customer data, payment provider payloads, secrets, or statutory evidence.
- Execute exactly one fiscal issuance reference per scenario.
- Do not enable batch retry or background scheduler execution.
- Keep `RetryExecutionAvailable` false in all evidence.
- Record every scenario in the evidence checklist.
- Re-disable controlled retry execution immediately after each scenario.

## Prerequisites

- Central PMS branch, commit, or checkpoint tag containing the controlled retry execution worker.
- POS Server local/controlled instance that supports fiscal document creation idempotency, replay, conflict behavior, and GET/readback fields for semantic hash and fiscal numbering.
- Disposable Central PMS database restored or migrated to the current branch schema.
- Disposable POS Server database or isolated POS Server test tenant/site context.
- Central PMS configuration points available for local override.
- Access to invoke the internal controlled worker path through an approved local harness, service-host command, or administrative backend process. Do not expose or use a public endpoint for this UAT.
- Access to query Central PMS audit/read model tables and POS Server readback results.
- Evidence storage folder outside source control for logs, screenshots, and query output.

## Configuration

Keep all controls disabled by default before starting:

```json
{
  "FiscalExceptionControlledRetryExecution": {
    "EnableControlledRetryExecution": false
  },
  "FiscalExceptionRetrySchedulingPreparation": {
    "EnableSchedulePreparation": false,
    "RetrySchedulePolicyConfigured": false,
    "RetryBackoffPolicyConfigured": false
  },
  "FiscalExceptionRetryExecutionPreparation": {
    "EnableExecutionPreparation": false,
    "ServiceIdentityAllowed": false,
    "ProductionImpacting": true
  }
}
```

For scenarios that need ready scheduler/execution-prep posture, use local-only overrides sufficient to create the required preparation audit basis. For the actual controlled retry scenario, enable only:

```json
{
  "FiscalExceptionControlledRetryExecution": {
    "EnableControlledRetryExecution": true
  }
}
```

Do not enable scheduler/background execution. Do not add a public route.

## Disposable Case Setup

1. Create one disposable parking/payment/fiscal issuance flow in the local Central PMS database.
2. Confirm the fiscal issuance reference is tied to one payment confirmation, one payment attempt, one parking session, one site, one site POS Server context, and one fiscal document type.
3. Confirm the upstream finality reference is durable and will be reused as the POS Server idempotency key source. Do not generate a replacement upstream finality reference.
4. Confirm Central PMS semantic hash metadata is current:
   - hash algorithm: SHA-256 compatible stored form;
   - source version: `sha256:v1`;
   - status: available/confirmed;
   - hash value present;
   - source summary/fact count present when modeled.
5. Confirm POS Server local/controlled instance can accept the mapped fiscal document create request and can expose readback fields for idempotency, semantic hash, and fiscal numbering.
6. Store the fiscal issuance reference id, upstream finality reference, semantic hash value/version, and source summary in the evidence checklist.

## Gate Confirmation

Before attempting controlled retry, capture evidence for each gate:

1. Latest readback classification is `not_found`.
2. Durable readback attempt exists and has a readback attempt id.
3. Retry eligibility is eligible for controlled retry planning.
4. Retry command preparation is `PreparedNonExecutable` and has a command preparation audit id.
5. Retry scheduling preparation is `ScheduledPrepared` and has a scheduler preparation audit id.
6. Retry execution preparation is `ReadyForExecutionWhenEnabled`.
7. POS Server readiness gates are confirmed by execution-prep.
8. Semantic hash readiness is current `sha256:v1`.
9. Original fiscal request facts still calculate the same semantic hash.
10. Upstream finality/idempotency reference is unchanged.
11. Service identity, approval reference, and dual-control reference are present.
12. FEQ posture is not mismatch, manual-review, fiscal-conflict, closed, or reconciled.
13. Retry execution audit persistence is available.

If any gate is missing, run the missing-gate scenario only. Do not call POS Server POST.

## Execution Method

Use the approved local/internal worker invocation path for the branch under test. The invocation must supply one fiscal issuance reference id and the exact command-prep, scheduler-prep, execution-prep, readback, semantic hash, idempotency, service identity, approval, dual-control, and correlation basis captured above.

The invocation must not:

- accept multiple fiscal issuance reference ids;
- create a retry job;
- schedule background execution;
- expose a public endpoint;
- generate a new upstream finality reference;
- retry automatically after conflict, timeout, or unknown result.

## Scenario 1: Disabled By Default

Setup:

- `FiscalExceptionControlledRetryExecution:EnableControlledRetryExecution = false`.
- Use a prepared FEQ case if available, but do not require all happy-path gates.

Steps:

1. Invoke the controlled retry worker once for the disposable fiscal issuance reference.
2. Capture worker status, block reason, POS Server call count, and side-effect flags.

Expected:

- Status is `Disabled`.
- Block reason is `controlled_retry_execution_disabled` or equivalent safe disabled reason.
- POS Server POST count is `0`.
- No retry execution attempt applies fiscal success evidence.
- `RetryExecutionAvailable` remains false.
- No payment finality, ExitAuthorization, gate behavior, fiscal number editing, or manual fiscal document creation occurs.

## Scenario 2: Missing Gate

Setup:

- `FiscalExceptionControlledRetryExecution:EnableControlledRetryExecution = true`.
- Deliberately remove one required gate, such as using readback classification other than `not_found` or using missing/incomplete semantic hash readiness.

Steps:

1. Invoke the controlled retry worker once.
2. Capture block reason and POS Server call count.
3. Re-disable controlled retry execution.

Expected:

- Status is `Blocked` or `Unavailable`.
- Block reason identifies the missing/unsafe gate, such as `readback_matched`, `readback_mismatch`, `readback_unknown`, `semantic_hash_not_ready`, or equivalent.
- POS Server POST count is `0`.
- Audit is persisted when the worker reaches the enabled audited path.
- No forbidden side effects occur.

## Scenario 3: Happy Controlled Retry

Setup:

- All gates in "Gate Confirmation" pass.
- `FiscalExceptionControlledRetryExecution:EnableControlledRetryExecution = true` only for this local run.
- POS Server local/controlled instance is ready for either newly-created success or same-key/same-hash replay.

Steps:

1. Invoke the controlled retry worker once for the single fiscal issuance reference.
2. Capture POS Server request correlation and response classification.
3. Query the retry execution audit record.
4. Query the fiscal issuance reference after execution.
5. Re-disable controlled retry execution immediately.

Expected:

- POS Server POST count is `1`.
- Worker status is `Executed` for newly-created evidence or `ReplayMatched` for same-key/same-hash replay.
- Execution attempt audit is persisted.
- Fiscal reference success/evidence is updated only through the existing fiscal issuance orchestration/live integration path.
- No retry loop occurs.
- `RetryExecutionAvailable` remains false.
- Payment finality, ExitAuthorization, gate behavior, fiscal number editing, and manual fiscal document creation flags remain false.

## Scenario 4: POS Server Idempotency Conflict

Setup:

- Use a disposable case that produces same idempotency scope/key with changed semantic facts, or use an approved local POS Server/test double that returns deterministic idempotency conflict.
- `FiscalExceptionControlledRetryExecution:EnableControlledRetryExecution = true` only for this run.

Steps:

1. Invoke the worker once.
2. Capture POS Server conflict response and retry execution audit.
3. Verify no second POST occurs.
4. Re-disable controlled retry execution.

Expected:

- Worker status is `Conflict`.
- Block reason indicates POS Server idempotency conflict.
- Execution audit is persisted.
- POS Server POST count is `1`.
- No retry loop or second POST occurs.
- Fiscal reference success is not recorded from conflicting evidence.
- No forbidden side effects occur.

## Scenario 5: Unknown Or Unavailable

Setup:

- Use a controlled timeout/unavailable/unknown POS Server response or test double.
- `FiscalExceptionControlledRetryExecution:EnableControlledRetryExecution = true` only for this run.

Steps:

1. Invoke the worker once.
2. Capture unknown/unavailable result and execution audit.
3. Verify no retry loop.
4. Schedule no automatic follow-up. A future readback must be performed before any future retry attempt.
5. Re-disable controlled retry execution.

Expected:

- Worker status is `Unknown`, `Unavailable`, or safe failed posture.
- Execution audit is persisted when the enabled audited path is reached.
- POS Server POST count is `0` for local unavailable/configuration blocks or `1` for timeout/unknown after call.
- No second POST occurs.
- Result summary indicates future readback is required.
- No forbidden side effects occur.

## Verification Queries

Use environment-specific read-only queries or application read models to capture:

- FEQ detail summary for the fiscal issuance reference.
- Latest readback attempt audit row.
- Retry eligibility result.
- Command preparation audit row.
- Scheduler preparation audit row.
- Execution preparation posture.
- Retry execution attempt audit row.
- Fiscal issuance reference before/after snapshot.
- POS Server readback result for fiscal document id or upstream finality/idempotency reference.

Do not export raw POS Server payloads, payment provider payloads, secrets, customer PII, statutory evidence, or full canonical source text.

## Forbidden Side-Effect Verification

For every scenario, record explicit confirmation that:

- no payment finality mutation occurred;
- no ExitAuthorization was issued;
- no gate behavior was triggered;
- no fiscal number was edited by Central PMS;
- no manual fiscal document was created;
- no batch retry path was used;
- no scheduler/background job was created;
- no public endpoint or UI path was used;
- no second POS Server POST occurred for conflict/unknown scenarios.

## Rollback And Cleanup

1. Disable `FiscalExceptionControlledRetryExecution:EnableControlledRetryExecution`.
2. Stop local Central PMS and POS Server test services if started only for UAT.
3. Preserve evidence exports and logs outside source control.
4. Drop or archive the disposable Central PMS and POS Server databases according to local test policy.
5. Do not reuse the same fiscal issuance reference for another controlled retry scenario unless the scenario explicitly requires replay/conflict behavior and the evidence is clear.

## Pass Criteria

The UAT pack passes only when:

- disabled and missing-gate scenarios block before POS Server POST;
- happy path performs exactly one POS Server POST and persists execution audit;
- success/replay evidence is applied only by the existing fiscal issuance orchestration path;
- conflict and unknown/unavailable scenarios persist safe audit posture and do not loop;
- all forbidden side-effect confirmations are negative;
- all evidence checklist fields are completed or marked not applicable with reason.
