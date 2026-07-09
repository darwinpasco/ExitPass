# ExitPass Central PMS POS Server Controlled UAT Dry-Run Checklist v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Dry-Run Checklist |
| Version | v1.0 |
| Date | 2026-07-09 |
| Branch | `docs/controlled-uat-dry-run-checklist` |
| Scope | Documentation-only dry-run checklist before controlled UAT execution approval |
| Source decision | `ready_for_dry_run_checklist` |
| Execution posture | `not_ready_for_execution` |

This checklist is documentation-only. It does not modify source code, schema, tests, configuration, runtime state, Central PMS state, POS Server state, HikCentral state, payment provider state, fiscal state, ExitAuthorization state, gate state, refund/reversal state, rendering behavior, evidence files, or UAT runbooks.

This checklist must be completed before an execution gate/go-no-go record is created. Passing this checklist does not execute UAT and does not authorize execution.

## 2. Purpose

This checklist converts the refreshed Controlled UAT Data Assignment Review decision into concrete runtime, configuration, evidence, and stop-control checks.

The checklist verifies that Central PMS, POS Server, evidence handling, forbidden side-effect baselines, rollback/support contact, and execution window readiness are suitable for an execution gate review. It does not call any mutation path and does not create fiscal issuance, payment confirmation, ExitAuthorization, gate behavior, refund/reversal, or rendering artifacts.

## 3. Preconditions

Before using this checklist:

- the filled assignment record must be accepted for dry-run checklist preparation;
- the refreshed data assignment review decision must be `ready_for_dry_run_checklist`;
- execution posture must remain `not_ready_for_execution`;
- a dry-run operator must be assigned;
- an evidence owner must be available;
- a rollback/support owner must be reachable;
- the active environment must be confirmed non-production or explicitly approved for controlled execution review;
- all commands must be reviewed before use so they do not mutate payment, fiscal, POS Server, HikCentral, gate, refund/reversal, or rendering state.

## 4. Reviewed Inputs

| Input | Path |
| --- | --- |
| Filled assignment record | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Record_Filled_v1.0.md` |
| Refreshed assignment review | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Review_Refresh_v1.0.md` |
| Blocker closure plan | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Blocker_Closure_Plan_v1.0.md` |
| Fill pack | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Fill_Pack_v1.0.md` |
| Local runtime smoke record | `docs/v1.3/operator-console/checkpoints/ExitPass_Central_PMS_Operator_Console_Local_Runtime_Smoke_Record_v1.0.md` |

## 5. Filled Values Carried Forward

| Field | Value |
| --- | --- |
| Environment | `DEV-CONTROLLED-UAT-LOCAL` |
| Central PMS base URL | `http://localhost:56065` |
| Central PMS HTTPS URL | `https://localhost:56064` |
| POS Server base URL | `http://localhost:5000` |
| Site ref | `DEV-SITE-ATC-001` |
| Site POS Server ref | `DEV-POS-SERVER-ATC-001` |
| Fiscal identity ref | `DEV-FISCAL-IDENTITY-ATC-001` |
| Fiscal sequence policy ref | `DEV-SI-SEQUENCE-POLICY-ATC-001` |
| Fiscal sequence state ref | `DEV-SI-SEQUENCE-STATE-ATC-001` |
| Run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Upstream finality ref | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Expected scenario | `newly_created` |
| Evidence path | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |
| Execution window in assignment | `July 9, 2026 1:00 PM-3:00 PM PHT` |

If any filled value changes, update the assignment record and refreshed review before using this checklist.

## 6. Checklist Result Classification

Allowed dry-run checklist classifications:

| Classification | Meaning |
| --- | --- |
| `dry_run_checklist_passed` | All required checks passed and evidence references are captured. This allows creation of an execution gate/go-no-go record only. |
| `dry_run_checklist_failed` | One or more checks failed with known corrective action. Correct the assignment record, runtime config, or evidence setup before retrying. |
| `dry_run_checklist_blocked` | A required check cannot be performed because an owner, environment, runtime, evidence path, approval, or safe command is unavailable. |

This checklist must not produce `ready_for_execution`.

## 7. Dry-Run Checks

Use this table as the execution-readiness worksheet. Fill the `Pass/Fail` column only when the check is actually performed by a human operator in the approved dry-run review window.

| Check ID | Check | Command or manual verification | Expected result | Evidence to save | Pass/Fail | Blocker if failed |
| --- | --- | --- | --- | --- | --- | --- |
| DR-01 | Central PMS process can start | `dotnet run --no-build --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj` or approved service startup command | Central PMS starts without fatal error and logs listening URLs matching assignment or approved override | Startup log, timestamp, command used |  | Block execution gate; fix Central PMS runtime/config first |
| DR-02 | POS Server process can start | Approved POS Server local startup command from POS Server owner; do not call fiscal issuance endpoints | POS Server starts without fatal error and listens on approved non-production URL | Startup log, timestamp, command used |  | Block execution gate; fix POS Server runtime/config first |
| DR-03 | Central PMS base URL reachable | Safe GET/read-only health or root check approved by Central PMS owner, for example `Invoke-WebRequest http://localhost:56065/` if non-mutating | HTTP response proves process reachability; 404 is acceptable only if it proves server response and is documented | Command output and HTTP status |  | Block execution gate; Central PMS not reachable |
| DR-04 | POS Server base URL reachable | Safe GET/read-only health or root check approved by POS Server owner; do not call fiscal issuance creation endpoint | HTTP response proves process reachability; 404 is acceptable only if it proves server response and is documented | Command output and HTTP status |  | Block execution gate; POS Server not reachable |
| DR-05 | Central PMS diagnostic flags assigned correctly | Inspect approved config/env without secrets | `EnablePosServerFiscalIssuanceLiveCall=true` only for approved window and `EnableControlledUatDiagnosticPath=true` only for approved window | Redacted config/env evidence |  | Block execution gate; diagnostic path not safely configured |
| DR-06 | Payment-flow guard false | Inspect approved config/env without secrets | Payment-flow fiscal live-call guard is false or absent/default false | Redacted config/env evidence |  | Block execution gate; payment flow could mutate fiscal state |
| DR-07 | Exit-flow guard false | Inspect approved config/env without secrets | Exit-flow fiscal live-call guard is false or absent/default false | Redacted config/env evidence |  | Block execution gate; exit flow could mutate fiscal state |
| DR-08 | Fiscal gating enforcement false | Inspect approved config/env without secrets | Fiscal gating enforcement is false or absent/default false | Redacted config/env evidence |  | Block execution gate; execution could affect exit decisions |
| DR-09 | No retry/readback worker enabled | Inspect hosted services/config/job scheduler without invoking workers | Retry scheduler and GET readback worker are disabled or not present for this run | Redacted config/service evidence |  | Block execution gate; unexpected fiscal retry/readback behavior possible |
| DR-10 | POS Server non-production fiscal identity exists | POS Server owner verifies by approved read-only DB/config inspection | `DEV-FISCAL-IDENTITY-ATC-001` exists, is active/effective, and is non-production | Read-only query/config output, secrets redacted |  | Block execution gate; fiscal identity unsafe or missing |
| DR-11 | POS Server non-production sequence policy exists | POS Server owner verifies by approved read-only DB/config inspection | `DEV-SI-SEQUENCE-POLICY-ATC-001` exists, is active/effective, and is non-production | Read-only query/config output, secrets redacted |  | Block execution gate; sequence policy unsafe or missing |
| DR-12 | POS Server non-production sequence state exists | POS Server owner verifies by approved read-only DB/config inspection | `DEV-SI-SEQUENCE-STATE-ATC-001` exists, is configured, and is non-production | Read-only query/config output, secrets redacted |  | Block execution gate; fiscal numbering state unsafe or missing |
| DR-13 | POS Server fiscal sequence is non-production | POS Server owner confirms sequence cannot allocate production fiscal numbers | Sequence identity/policy/state are explicitly non-production, disposable, or otherwise approved under `NONPROD-FISCAL-ALLOC-001` | Fiscal numbering approval and config evidence |  | Block execution gate; production fiscal numbering risk |
| DR-14 | Evidence folder exists | `Test-Path 'D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001'` | Returns `True` | Command output |  | Block execution gate; evidence cannot be saved |
| DR-15 | Evidence folder write access works | Create and remove a harmless dry-run marker file, for example `New-Item ...\.dry-run-write-test.txt`; then remove it | Marker file can be written and removed; no evidence payload is created | Command output and timestamp |  | Block execution gate; evidence owner lacks write access |
| DR-16 | Checksum command works | Run `Get-FileHash -Algorithm SHA256 <dry-run-marker-file>` on a harmless marker file | SHA-256 hash is produced | Command output |  | Block execution gate; evidence integrity process unavailable |
| DR-17 | Side-effect baseline checks are defined | Review SQL/query/check commands without executing mutating operations | Commands exist for exit authorization, gate events, refund/reversal, payment mutation, and POS Server sequence classification baselines | Query/check list copied into evidence package |  | Block execution gate; cannot prove forbidden side effects remain absent |
| DR-18 | Rollback/support owner contact works | Manual contact confirmation by email/phone/chat bridge | Rollback/support owner acknowledges availability for current execution window | Timestamped contact confirmation reference |  | Block execution gate; no stop owner available |
| DR-19 | Execution window is current | Compare approved execution window to current date/time | Execution window is current and approved; if elapsed, assignment/review must be updated | Approval/window reference |  | Block execution gate; stale approval window |
| DR-20 | Runtime boundary reminder acknowledged | Operator reads non-goals and forbidden actions before any execution gate | Operator acknowledges no UAT execution occurs from this checklist | Checklist signoff |  | Block execution gate; unclear authority boundary |

## 8. Concrete Side-Effect Check Commands And Placeholders

These are placeholders for the dry-run operator to adapt to the actual approved database/schema names. They must be reviewed by engineering before use. They are read-only baseline checks and must not mutate data.

### 8.1 Central PMS Exit Authorization Count

Expected result before and after any later approved execution:

```text
0
```

Placeholder:

```sql
-- DR-SFX-01: exit authorization count expected zero for the approved run fixture.
SELECT COUNT(*) AS exit_authorization_count
FROM core.exit_authorizations
WHERE parking_session_ref = '<approved-parking-session-ref>'
   OR correlation_id = '<approved-correlation-id>';
```

Blocker if failed:

- Any count greater than zero before execution means the fixture is contaminated or the query scope is wrong.
- Any unexpected count greater than zero after later execution means forbidden ExitAuthorization behavior may have occurred.

### 8.2 Gate Event Count

Expected result before and after any later approved execution:

```text
0
```

Placeholder:

```sql
-- DR-SFX-02: gate event count expected zero for the approved run fixture.
SELECT COUNT(*) AS gate_event_count
FROM gates.gate_events
WHERE correlation_id = '<approved-correlation-id>'
   OR parking_session_ref = '<approved-parking-session-ref>';
```

Blocker if failed:

- Any count greater than zero indicates the fixture or run may be tied to forbidden gate behavior.

### 8.3 Refund / Reversal Count

Expected result before and after any later approved execution:

```text
0
```

Placeholder:

```sql
-- DR-SFX-03: refund/reversal count expected zero for the approved run fixture.
SELECT COUNT(*) AS refund_reversal_count
FROM payments.payment_reversals
WHERE payment_attempt_ref = '<approved-payment-attempt-ref>'
   OR correlation_id = '<approved-correlation-id>';
```

If the actual schema uses a different refund/reversal table, replace the table and column names through an engineering-approved read-only query before use.

### 8.4 Payment Mutation Outside Approved Fixture

Expected result before and after any later approved execution:

```text
0
```

Placeholder:

```sql
-- DR-SFX-04: payment mutation outside approved fixture expected zero.
SELECT COUNT(*) AS outside_fixture_payment_mutation_count
FROM payments.payment_events
WHERE correlation_id = '<approved-correlation-id>'
  AND payment_attempt_ref <> '<approved-payment-attempt-ref>';
```

If the actual schema uses payment finality tables rather than payment events, replace the table and column names through an engineering-approved read-only query before use.

### 8.5 POS Server Fiscal Sequence Is Non-Production

Expected result:

```text
non-production sequence confirmed
```

Placeholder:

```sql
-- DR-SFX-05: POS Server fiscal sequence must be non-production.
SELECT fiscal_sequence_policy_id,
       fiscal_sequence_state_id,
       environment_name,
       is_production_sequence
FROM fiscal_sequence_state
WHERE fiscal_sequence_policy_id = '<approved-fiscal-sequence-policy-ref>'
  AND fiscal_sequence_state_id = '<approved-fiscal-sequence-state-ref>';
```

Pass criteria:

- `is_production_sequence` is false, or the equivalent environment marker is explicitly non-production.
- If the POS Server schema uses different names, POS Server owner must provide the equivalent read-only query.

## 9. Evidence Package Requirements

The completed dry-run checklist evidence package should include:

- completed checklist table with pass/fail decisions;
- Central PMS startup log reference;
- POS Server startup log reference;
- Central PMS reachability evidence;
- POS Server reachability evidence;
- redacted Central PMS config evidence;
- POS Server non-production fiscal configuration evidence;
- evidence folder existence and write-test proof;
- checksum command proof;
- side-effect baseline query definitions;
- rollback/support owner contact confirmation;
- execution window approval confirmation;
- final dry-run checklist classification.

Do not store secrets, credentials, raw payment provider payloads, raw POS Server request/response bodies, PAN, CVV, unmanaged customer PII, raw statutory evidence, or local environment variable dumps in the evidence package.

## 10. Decision Rule

Passing this checklist still does not execute UAT.

Passing this checklist only allows creation of an execution gate/go-no-go record. That later record must independently decide whether execution can proceed and must capture explicit approval before any fiscal issuance UAT run.

Decision outcomes:

| Outcome | Rule |
| --- | --- |
| `dry_run_checklist_passed` | All required checks pass and evidence is saved. Create execution gate/go-no-go record next. |
| `dry_run_checklist_failed` | One or more checks fail with known corrective action. Update assignment record, runtime config, evidence path, or owner approvals first. |
| `dry_run_checklist_blocked` | Required runtime, owner, command, evidence path, or approval is unavailable. Do not proceed. |

## 11. Explicit Non-Goals

This checklist does not:

- execute UAT;
- create fiscal issuance;
- confirm payment;
- mutate POS Server;
- write to HikCentral;
- issue ExitAuthorization;
- trigger gate behavior;
- create refund/reversal;
- generate PDF;
- generate HTML;
- generate QR;
- define final BIR statutory wording;
- create payment, fiscal, gate, refund/reversal, or rendering transactions;
- modify source code;
- modify schema;
- modify tests.

## 12. Recommended Next Step

If this checklist passes:

1. Create a Controlled UAT execution gate/go-no-go record.
2. Attach the completed dry-run checklist evidence package.
3. Capture explicit execution approval or rejection.
4. Do not execute UAT until the execution gate document approves it.

If this checklist fails or is blocked:

1. Update the assignment record, runtime configuration, evidence setup, owner availability, or POS Server fiscal configuration first.
2. Refresh the data assignment review if any assigned value changes.
3. Repeat this dry-run checklist before requesting execution approval.
