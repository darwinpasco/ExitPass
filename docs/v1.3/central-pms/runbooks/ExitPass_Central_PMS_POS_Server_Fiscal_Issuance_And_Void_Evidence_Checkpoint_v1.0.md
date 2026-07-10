# ExitPass Central PMS POS Server Fiscal Issuance And Void Evidence Checkpoint v1.0

## 1. Scope

This checkpoint covers the completed controlled non-production fiscal issuance and fiscal void proof chain across:

- Central PMS
- POS Server
- Operator Console facade
- Operator Console UI

This is a concise merged-evidence checkpoint and index of proofs already completed. It is not a planning, readiness, or design document.

## 2. Fixed Approved Fixture Facts

| Field | Value |
| --- | --- |
| Fiscal issuance reference id | `14479d9a-844f-4dba-9578-e863ece93fbf` |
| POS Server fiscal document id | `9bdf2948-dadd-450b-8776-be688b579395` |
| Fiscal document number | `SI-00000002-UAT` |
| Fiscal sequence value | `2` |
| Run/profile id | `CPS-POS-UAT-20260709-DEV-ATC-001` |
| Correlation id | `b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df` |
| Evidence path | `D:\ExitPass-UAT-Evidence\CPS-POS-UAT-20260709-DEV-ATC-001` |

## 3. Completed Proof Chain

| Proof | Result | Key finding | Result doc |
| --- | --- | --- | --- |
| Fiscal issuance newly_created | passed | Fiscal number `SI-00000002-UAT` issued; sequence `2`. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Execution_Rerun_4_Result_v1.0.md` |
| Fiscal issuance replay/idempotency | passed | Replay returned existing fiscal result; no new fiscal number. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Replay_Result_v1.0.md` |
| Fiscal issuance conflict/fail-closed | passed | Amount conflict failed closed; no new fiscal number. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Conflict_Result_v1.0.md` |
| Reusable controlled fiscal smoke harness | passed | Replay and conflict smoke passed under allowlisted profile. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Smoke_Harness_Result_v1.0.md` |
| Metadata-only controlled fiscal void smoke | passed | Safety boundary proved before real POS Server void implementation. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Fiscal_Void_Smoke_Result_v1.0.md` |
| Real Central PMS -> POS Server fiscal void runtime | passed | POS Server returned `newly_voided`; Central PMS returned `pos_server_void_recorded`; sequence stayed `2`. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Real_Fiscal_Void_Runtime_Result_v1.0.md` |
| Real fiscal void replay/idempotency runtime | passed | POS Server returned `idempotent_replay`; Central PMS returned `pos_server_void_idempotent_replay`; sequence stayed `2`. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Real_Fiscal_Void_Replay_Runtime_Result_v1.0.md` |
| Real fiscal void conflict/fail-closed runtime | passed | Changed `reasonText` caused POS Server conflict and Central PMS `pos_server_void_conflict`; sequence stayed `2`. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Real_Fiscal_Void_Conflict_Runtime_Result_v1.0.md` |
| Read-after-void verification | passed | POS Server direct read, Central PMS status read, and Operator Console facade all exposed `voided`/`recorded` state. | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Read_After_Void_Verification_Result_v1.0.md` |
| Operator Console UI read-after-void headless smoke | passed | UI displayed `SI-00000002-UAT`, `Voided`, `Recorded`, `Available`, with no unsafe actions. | `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Read_After_Void_UI_Smoke_Result_v1.0.md` |
| Operator Console void-status UI source alignment | merged | Actual UI source displays read-after-void fields and has focused UI test coverage. | No separate runtime doc; see merged UI source behavior and UI smoke result above. |
| Operator Console manual browser read-after-void smoke | passed | Darwin manually verified the browser UI showed Fiscal document voided, Voided, Available, Recorded, `operator_error`, and no unsafe actions. | `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Read_After_Void_Manual_Browser_Smoke_Result_v1.0.md` |

## 4. Final Safety Assertions

- No fiscal number reuse.
- No fiscal sequence decrement/reset.
- No second fiscal document for `SI-00000002-UAT`.
- No payment finality mutation.
- No ExitAuthorization.
- No gate behavior.
- No refund/reversal.
- No HikCentral call/write.
- No payment provider call.
- No PDF/HTML/QR rendering.
- No final BIR statutory wording introduced.
- No production fiscal sequence used.

## 5. Final Operator-Visible State

| Field | Value |
| --- | --- |
| Document number | `SI-00000002-UAT` |
| POS Server read status | Available |
| Fiscal document status/posture | Voided |
| Void status | Recorded |
| Void reason code | `operator_error` |
| Sequence | `2` |

## 6. Remaining Limitations / Not Yet Covered

- These are controlled non-production proofs.
- Final BIR statutory wording/rendering is not covered by this checkpoint.
- Full X-read/Z-read/sales summary reporting is not covered.
- Refund/reversal fiscal flows are not covered.
- Production configuration/certification is not covered.
- Load/concurrency validation is not covered.
- Broader multi-site/multi-sequence scenarios are not covered.

## 7. Manual Test Note

Manual browser test completed and passed. No further manual test is required for this checkpoint document because it only indexes completed evidence.

Any future source/runtime change to fiscal status, void handling, Operator Console display, or POS Server readback should trigger a new manual UI check.

## 8. Validation

| Check | Result |
| --- | --- |
| Referenced result docs exist in the repo | Passed |
| `git diff --check` | Passed |
| Build | Not required; no source code changed |
