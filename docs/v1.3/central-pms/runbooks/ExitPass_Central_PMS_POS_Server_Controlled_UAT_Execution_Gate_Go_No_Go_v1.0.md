# ExitPass Central PMS POS Server Controlled UAT Execution Gate Go/No-Go v1.0

## 1. Purpose

This record captures the execution gate decision for one Central PMS + POS Server controlled UAT run.

The decision is limited to the approved non-production scope in this document. It does not authorize production use, production fiscal sequencing, broad scenario execution, payment provider calls, HikCentral writes, ExitAuthorization, gate behavior, refund/reversal behavior, rendering artifacts, or final BIR statutory wording.

## 2. Reviewed Inputs

| Input | Path |
| --- | --- |
| Filled assignment record | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Record_Filled_v1.0.md` |
| Refreshed assignment review | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Review_Refresh_v1.0.md` |
| Dry-run checklist | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Dry_Run_Checklist_v1.0.md` |
| Dry-run checklist result | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Dry_Run_Checklist_Result_v1.0.md` |
| DR-13 non-production assertion/evidence record | `docs/v1.3/central-pms/runbooks/ExitPass_POS_Server_Fiscal_Sequence_Nonproduction_Evidence_Record_v1.0.md` |

## 3. Approved Scope

| Field | Approved value |
| --- | --- |
| Execution scope | One non-production controlled UAT run |
| Scenario | `newly_created` |
| Run id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Central PMS URL | `http://localhost:56065` |
| POS Server URL | `http://localhost:5000` |
| Site | `DEV-SITE-ATC-001` |
| Site POS Server | `DEV-POS-SERVER-ATC-001` |
| Fiscal document type | `sales_invoice` |
| Amount | PHP 100.00 / `10000` minor units |
| Upstream finality ref | `CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Evidence path | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |

Any change to these values requires a new gate decision or a written amendment before execution.

## 4. GO Decision

Decision: `GO`

This is a GO for one non-production controlled UAT execution only, subject to the exact approved scope above.

Darwin Pasco confirms this is a non-production controlled UAT environment and accepts the dry-run assumptions documented in the dry-run checklist result for purposes of this execution gate.

This GO does not authorize production execution, production fiscal sequence allocation, or any scenario outside `CPS-POS-UAT-20260709-DEV-ATC-001`.

## 5. Stop Criteria

Stop the run immediately if any of the following occurs:

- Central PMS URL differs from `http://localhost:56065`.
- POS Server URL differs from `http://localhost:5000`.
- The run id, upstream finality ref, scenario, site, Site POS Server, amount, or correlation id differs from the approved scope.
- Any production fiscal identity, policy, state, sequence, or fiscal number is detected or suspected.
- Any HikCentral write path is invoked or prepared.
- Any payment provider call is invoked or prepared.
- Any ExitAuthorization or gate behavior is invoked or prepared.
- Any refund/reversal behavior is invoked or prepared.
- Any PDF, HTML, QR, or statutory rendering behavior is invoked or prepared.
- Any final BIR statutory wording is introduced or required.
- Raw secrets, raw payment provider payloads, raw POS Server request/response bodies, raw statutory evidence payloads, customer PII, stack traces, or local environment dumps are exposed in evidence.
- The operator cannot save evidence to `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001`.
- The run produces ambiguous evidence that cannot be tied to correlation id `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df`.

If a stop criterion is hit, preserve non-sensitive logs/evidence, do not retry automatically, and move to post-run review with the result classified as stopped or failed.

## 6. Evidence Path

Evidence must be saved under:

```text
D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001
```

Do not save evidence in the source repository unless a separate approved evidence process explicitly allows it.

## 7. Required Evidence To Save After Run

Save the following after the run, with secrets and sensitive payloads redacted:

- execution timestamp and operator;
- command or manual procedure used;
- run id;
- scenario;
- Central PMS URL;
- POS Server URL;
- site and Site POS Server refs;
- fiscal document type;
- amount;
- upstream finality ref;
- correlation id;
- pre-run evidence folder check;
- run request summary, excluding raw sensitive payloads;
- Central PMS response summary;
- POS Server response summary, excluding raw POS Server request/response bodies;
- fiscal issuance reference/result metadata if produced;
- POS Server fiscal document id/number metadata if produced;
- side-effect check results;
- evidence package file list;
- SHA-256 checksum file for saved evidence package or summary file;
- stop criteria outcome;
- post-run reviewer notes.

## 8. Boundaries

This GO is constrained by these boundaries:

- no production fiscal sequence;
- no HikCentral write;
- no payment provider call;
- no ExitAuthorization;
- no gate behavior;
- no refund/reversal;
- no PDF/HTML/QR;
- no final BIR statutory wording.

The run remains non-production and controlled. It must not be treated as production certification.

## 9. Post-Run Required Review

After execution, create or update post-run evidence and review records covering:

- execution evidence review;
- side-effect check;
- dry-run/evidence package checksum.

The post-run review must explicitly state whether the run stayed inside the approved scope and whether any stop criterion was triggered.

## 10. Validation

`git diff --check` result: passed.
