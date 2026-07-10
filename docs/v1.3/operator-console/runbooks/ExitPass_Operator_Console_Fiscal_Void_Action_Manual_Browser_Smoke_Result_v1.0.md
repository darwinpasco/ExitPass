# ExitPass Operator Console Fiscal Void Action Manual Browser Smoke Result v1.0

## Result

Result: `PASSED`

This records Darwin's manual browser smoke of the Operator Console fiscal void action workflow against a fresh disposable local fixture.

## Scope

This result covers the merged Operator Console fiscal void action path:

- UI route: `/operator-console/fiscal-issuance-status`
- Operator Console facade endpoint: `POST /v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId}/void`
- RBAC/action policy: `FiscalIssuanceVoidCommand`
- Permission: `fiscal-issuance.void.command`
- Operator action: `VOID_FISCAL_DOCUMENT`

This is a post-test manual browser smoke result record. It is not a planning, readiness, checkpoint, or design document.

## Environment

| Item | Value |
| --- | --- |
| ExitPass repo | `D:\SourceCodes\ExitPass` |
| POS Server repo | `D:\SourceCodes\ExitPass-PoSServer` |
| POS Server URL | `http://localhost:5000` |
| Central PMS URL | `http://localhost:56065` |
| Operator Console UI URL | `http://localhost:5175/operator-console/fiscal-issuance-status` |
| Central PMS DB | `centralpms_feq_retry_uat_local` |
| POS Server DB | `posserver_api_smoke_validation_local` |
| Local DB port | `5433` |
| Local DB user | `exitpass` |

## Fixture

| Field | Value |
| --- | --- |
| Fiscal issuance reference ID | `7f4a7d36-2e6e-4f2c-aad6-2d98e8e1b501` |
| POS Server fiscal document ID | `3cddbc8e-28f8-49d2-93cf-b4a28a947501` |
| Fiscal document number | `SI-OCVOID-0001-UAT` |
| Fiscal sequence value before void | `9101` |
| POS Server status before void | `issued` |
| POS Server void status before void | Not recorded / `null` |
| Central PMS state before void | `FISCAL_ISSUANCE_RECORDED` |
| POS Server read status before void | `AVAILABLE` / Available |
| Fiscal action before void | Available |

## Pre-Void Observations

| Browser field | Observed value |
| --- | --- |
| Fiscal status result | Issued |
| Fiscal document number | `SI-OCVOID-0001-UAT` |
| Result classification | `NEWLY_CREATED` |
| Evidence status | `FISCAL_DOCUMENT_NUMBER_ASSIGNED` |
| Number assignment | `ASSIGNED` |
| POS Server document read status | Available |
| Fiscal document status | Issued |
| Fiscal action | Available |
| Fiscal void request section | Visible |
| Safety wording | Visible; stated this only requests fiscal void/cancellation in POS Server |

## Action Submitted

| Field | Value |
| --- | --- |
| Reason code | `operator_error` |
| Reason text | Manual browser smoke for Operator Console fiscal void action. |
| Confirmation phrase | `VOID FISCAL DOCUMENT` |

The fiscal void request was submitted from the browser through the Operator Console workflow.

## Post-Void Observations

| Browser field | Observed value |
| --- | --- |
| Fiscal status result | Fiscal document voided |
| Badge/status | Voided |
| Warning | The view is observational only and does not authorize payment, exit, gate, refund, or replacement action |
| Fiscal state | `FISCAL_ISSUANCE_RECORDED` |
| Sales Invoice / fiscal document number | `SI-OCVOID-0001-UAT` |
| Result classification | `NEWLY_CREATED` |
| Evidence status | `FISCAL_DOCUMENT_NUMBER_ASSIGNED` |
| Number assignment | `ASSIGNED` |
| POS Server document read status | Available |
| Fiscal document status | Voided |
| Void status | Recorded |
| Fiscal action | Not voidable |
| Non-action reason | Fiscal document is already voided or has a recorded void status. |

## Passed Assertions

| Assertion | Result |
| --- | --- |
| Operator void action was visible/enabled for a fresh unvoided fixture | Passed |
| Fiscal void request could be submitted from the browser | Passed |
| Read-after-void refreshed on the same page | Passed |
| POS Server read status remained Available | Passed |
| Fiscal document status became Voided | Passed |
| Void status became Recorded | Passed |
| Fiscal document number remained `SI-OCVOID-0001-UAT` | Passed |
| Fiscal sequence remained `9101` | Passed |
| Repeat void was blocked after success by Not voidable state | Passed |
| Safety wording did not authorize payment, exit, gate, refund, or replacement action | Passed |

## Safe Side-Effect Assertions

No unsafe operator action appeared:

- No payment refund action
- No gate action
- No HikCentral action
- No replacement fiscal document action
- No rendering/final BIR action

## Additional Safe Handling Observed

Searching the old reference `14479d9a-844f-4dba-9578-e863ece93fbf` returned Fiscal reference not found in the current local DB state.

This is acceptable because the old fixture was not present in the current local Central PMS DB. The already-voided negative behavior was still proven by the fresh fixture after successful void, because the page then showed Not voidable with the already-voided/recorded reason.

## UX Observations / Follow-Up Candidates

These observations are not blockers:

- The input textbox is too short for a full GUID, even though the full GUID can be entered.
- The page still labels itself Read-only / Read-only fiscal issuance status even though it now includes a controlled fiscal void action.
- A future high-value UX improvement should add operator-friendly lookup by fiscal document number / SI number, ticket/card number, payment reference, or upstream finality reference instead of GUID-only lookup.

## Files Changed

| File | Change |
| --- | --- |
| `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Fiscal_Void_Action_Manual_Browser_Smoke_Result_v1.0.md` | Created manual browser smoke result record |

## Validation

| Check | Result |
| --- | --- |
| `git diff --check` | Passed |
| `git status --short --untracked-files=all` | Passed; only this result document is untracked/changed |
| Backend/UI test suites | Not run; doc-only change |
