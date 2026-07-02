# ExitPass Central PMS POS Server Controlled UAT Evidence Template v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS POS Server Controlled UAT Evidence Template |
| Version | v1.0 |
| Date | 2026-07-02 |
| Status | Documentation/evidence-template only |
| Repository | `D:\SourceCodes\ExitPass` |
| Branch | `feature/central-pms-pos-server-controlled-uat-evidence-template` |
| Runtime reference | `D:\SourceCodes\ExitPass-PoSServer` on `dev` |

This template standardizes the evidence record for controlled Central PMS to POS Server fiscal issuance diagnostic runs. It does not execute a live call, expose an endpoint, add tooling, modify source code, write SQL, create migrations, or change POS Server runtime behavior.

## 2. Run Summary

| Field | Evidence |
| --- | --- |
| Run id |  |
| Run date/time |  |
| Run type | `newly_created` / `idempotent_replay` / `conflict` / `failure` / `unknown` / other |
| Environment |  |
| Site |  |
| Site POS Server |  |
| Operator/executor |  |
| UAT lead |  |
| Approver |  |
| Evidence owner |  |
| Intended outcome |  |
| Actual outcome |  |
| Final status | `passed` / `passed_with_notes` / `failed` / `aborted` / `inconclusive` |

Summary notes:

-  

## 3. Approval Record

| Approval role | Name | Approval date/time | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Engineering lead |  |  |  |  |
| Product/business owner |  |  |  |  |
| POS Server owner |  |  |  |  |
| Site owner |  |  |  |  |
| Compliance/accounting owner, if fiscal number may be allocated |  |  |  |  |
| Operations lead |  |  |  |  |
| Rollback owner |  |  |  |  |
| UAT lead |  |  |  |  |

Approval conditions:

- Fiscal number allocation risk accepted: yes / no / not applicable.
- Production environment use approved: yes / no / not applicable.
- Evidence retention location approved: yes / no.

## 4. Environment Record

| Field | Evidence |
| --- | --- |
| Central PMS environment |  |
| POS Server environment |  |
| Database/environment reference |  |
| Network path confirmed | yes / no |
| Log capture enabled | yes / no |
| Evidence repository/location |  |
| Production or non-production | production / non-production |
| Production fiscal sequence risk accepted | yes / no / not applicable |
| Rollback owner online | yes / no |
| Support/operations notified | yes / no |

Environment notes:

-  

## 5. Site / Site POS Server Record

| Field | Evidence |
| --- | --- |
| Site id/ref |  |
| Site name |  |
| Site POS Server id/ref |  |
| Site POS Server base URL or environment reference |  |
| Site Group, reporting only if applicable |  |
| Payment channel involved |  |
| Terminal/channel context |  |
| Site rollout owner |  |
| Rollback contact |  |

Site mapping verified: yes / no.

## 6. Configuration Readiness Record

| Configuration item | Expected | Evidence |
| --- | --- | --- |
| `EnablePosServerFiscalIssuanceLiveCall` | true for controlled run |  |
| `EnableControlledUatDiagnosticPath` | true for approved window only |  |
| `PosServerBaseUrl` configured | yes, do not print secret values |  |
| Timeout configured | positive value |  |
| Readiness status | `enabled_ready` |  |
| Payment-flow live-call guard | false |  |
| Exit-flow live-call guard | false |  |
| Fiscal gating enforcement flag | false |  |
| `EnforcementWiredForBlocking` | false |  |
| Expected diagnostic status before invocation |  |  |

Configuration evidence reference:

-  

## 7. POS Server Readiness Record

| Readiness item | Evidence |
| --- | --- |
| POS Server reachable | yes / no |
| API version / contract reference |  |
| Fiscal identity configured | yes / no |
| Fiscal sequence policy configured | yes / no |
| Fiscal sequence state configured | yes / no |
| POST endpoint validated | yes / no |
| GET endpoint available for manual verification | yes / no |
| Idempotency behavior understood | yes / no |
| Replay behavior understood | yes / no |
| Conflict behavior understood | yes / no |
| Fiscal number allocation consequence accepted | yes / no / not applicable |

POS Server readiness notes:

-  

## 8. Central PMS Readiness Record

| Readiness item | Evidence |
| --- | --- |
| Fiscal reference persistence patch applied | yes / no |
| Validation SQL passed | yes / no / not applicable |
| Repository tests passed | yes / no |
| Live-call seam tests passed | yes / no |
| Configuration readiness status |  |
| No payment-flow wiring confirmed | yes / no |
| No exit-flow wiring confirmed | yes / no |
| No fiscal gating enforcement confirmed | yes / no |
| Shadow/audit evidence available | yes / no |

Central PMS readiness notes:

-  

## 9. Test Data Record

| Test data field | Evidence |
| --- | --- |
| Payment attempt ref |  |
| Payment confirmation ref |  |
| Parking session ref |  |
| Payable basis ref |  |
| Upstream finality ref |  |
| Business day date |  |
| Currency |  |
| Amount minor units |  |
| Document lines summary |  |
| Tender summary |  |
| Tax/totals summary |  |
| Discount references, if applicable |  |
| Evidence this is approved test data |  |

Approved test data confirmation: yes / no.

## 10. Fiscal Request Facts Record

| Request fact | Present | Evidence |
| --- | --- | --- |
| Site POS Server id/ref | yes / no |  |
| Fiscal document type id/key | yes / no |  |
| Fiscal document status id/key | yes / no / not applicable |  |
| Business day date | yes / no |  |
| Parking session ref | yes / no |  |
| Payment attempt ref | yes / no |  |
| Payment confirmation ref | yes / no |  |
| Payable basis ref | yes / no |  |
| Upstream finality ref | yes / no |  |
| Currency code | yes / no |  |
| Amount minor units | yes / no |  |
| Line count |  |  |
| Tender count |  |  |
| Tax detail present | yes / no / not applicable |  |
| Totals present | yes / no |  |
| Correlation id | yes / no |  |
| Request semantic stability confirmed | yes / no |  |

Semantic stability confirmation:

- Same upstream finality reference is used only for the same semantic request: yes / no.
- Request facts were not changed to force a replay or bypass conflict: yes / no.

## 11. Sensitive Data Exclusion Confirmation

Confirm the run did not include the following:

| Prohibited data type | Excluded | Evidence / notes |
| --- | --- | --- |
| PAN | yes / no |  |
| CVV | yes / no |  |
| Tokens | yes / no |  |
| Credentials | yes / no |  |
| Secrets | yes / no |  |
| Raw provider callback payloads | yes / no |  |
| Raw entitlement evidence | yes / no |  |
| Uncontrolled images/files | yes / no |  |
| Unmanaged customer PII | yes / no |  |
| Free-form sensitive blobs | yes / no |  |

Redaction confirmation:

- Evidence attachments reviewed for sensitive data: yes / no.
- Redaction owner:  

## 12. Pre-Run Checklist Result

| Checklist item | Result | Evidence |
| --- | --- | --- |
| Approvals complete | pass / fail |  |
| Environment verified | pass / fail |  |
| Site/Site POS Server verified | pass / fail |  |
| Fiscal number allocation consequence accepted | pass / fail / not applicable |  |
| Readiness status `enabled_ready` | pass / fail |  |
| Diagnostic guard enabled only for approved window | pass / fail |  |
| Payment/exit flow guards false | pass / fail |  |
| Fiscal gating enforcement off | pass / fail |  |
| No retry scheduler | pass / fail |  |
| No GET readback worker | pass / fail |  |
| Evidence capture ready | pass / fail |  |
| Rollback owner online | pass / fail |  |
| Support/operations notified | pass / fail |  |

Pre-run decision: proceed / abort.

## 13. Invocation Record

| Field | Evidence |
| --- | --- |
| Invocation timestamp |  |
| Invoked by |  |
| Invocation mechanism | application-level seam / future endpoint / future CLI-tool |
| Current expected invocation mechanism | application-level seam only |
| Correlation id |  |
| Diagnostic status before call |  |
| Diagnostic status after call |  |
| POS Server call attempted | yes / no |
| No payment finality mutation confirmation | yes / no |
| No ExitAuthorization issued confirmation | yes / no |
| No gate behavior confirmation | yes / no |

Invocation notes:

-  

## 14. POS Server Response Record

| Response field | Evidence |
| --- | --- |
| HTTP status |  |
| Response code |  |
| Message summary, safe only |  |
| `resultClassification` |  |
| `fiscalIssuanceEvidenceStatus` |  |
| `fiscalNumberAssignmentState` |  |
| `fiscalDocumentStatusCodeId` |  |
| `fiscalDocumentId` |  |
| `fiscalIdentityId` |  |
| `fiscalSequencePolicyId` |  |
| `fiscalSequenceValue` |  |
| `fiscalDocumentNumber` |  |
| `fiscalSeries` |  |
| `fiscalNumberPrefixText` |  |
| `fiscalNumberSuffixText` |  |
| `fiscalNumberAssignedAt` |  |
| `fiscalNumberAssignedByRef` |  |
| `errorCode` |  |
| `errorPosture` |  |
| Response timestamp |  |
| Response classified as | `newly_created` / `replay` / `conflict` / `request_failure` / `configuration_failure` / `service_failure` / `unknown` / `fail_closed` |

Response evidence notes:

-  

## 15. Central PMS Fiscal Reference Result

| Central PMS result field | Evidence |
| --- | --- |
| Central PMS fiscal issuance reference id |  |
| Central PMS fiscal state after run |  |
| Result recorded/replayed/conflict/failure/unknown |  |
| Fiscal document id recorded |  |
| Fiscal document number recorded |  |
| Evidence status recorded |  |
| Assignment state recorded |  |
| Exception reason recorded |  |
| `errorPosture` recorded |  |
| Repository lookup by upstream finality ref passed | yes / no / not applicable |
| Repository lookup by POS Server fiscal document id passed | yes / no / not applicable |
| Duplicate reference check passed | yes / no |

Central PMS result notes:

-  

## 16. Idempotency / Replay Evidence

| Evidence item | Result | Notes |
| --- | --- | --- |
| Upstream finality reference reused for same semantic request | yes / no / not applicable |  |
| Replay expected | yes / no |  |
| Replay returned original fiscal document id | yes / no / not applicable |  |
| Replay returned original fiscal document number | yes / no / not applicable |  |
| Sequence advanced on replay | yes / no / not applicable; expected no |  |
| Duplicate Central PMS fiscal reference created | yes / no; expected no |  |
| Conflict avoided or detected | avoided / detected / not applicable |  |

Replay conclusion:

- passed / failed / inconclusive / not applicable.

## 17. Conflict / Failure Evidence

| Evidence item | Result |
| --- | --- |
| Conflict occurred | yes / no |
| Failure category | conflict / request_failure / configuration_failure / service_failure / none |
| Error code |  |
| `errorPosture` |  |
| Request correction required | yes / no |
| Configuration correction required | yes / no |
| Service recovery required | yes / no |
| Manual review required | yes / no |
| Automatic retry performed | yes / no; expected no unless separately approved |

Conflict/failure handling notes:

-  

## 18. Unknown Outcome Evidence

| Evidence item | Result |
| --- | --- |
| Unknown outcome occurred | yes / no |
| Fiscal document id known | yes / no / not applicable |
| Manual GET readback approved | yes / no / not applicable |
| Manual GET readback result |  |
| Reconciliation status | open / reconciled / escalated / not applicable |
| ExitAuthorization issued based on unknown result | yes / no; expected no |

Unknown outcome handling notes:

-  

## 19. Shadow / Audit Evidence

| Evidence item | Result |
| --- | --- |
| Shadow event emitted | yes / no |
| Shadow outcome |  |
| Future enforcement decision | allow / block / not_required_by_policy / exception_release_only / manual_review_required / not_evaluable / not applicable |
| Would allow normal ExitAuthorization | yes / no / not applicable |
| Would block normal ExitAuthorization | yes / no / not applicable |
| Missing context or evaluation failure, if applicable |  |
| Payload excludes sensitive data | yes / no |
| Activity/log reference |  |
| Event id/reference |  |

Audit evidence notes:

-  

## 20. Payment Finality Impact Confirmation

| Confirmation item | Result | Evidence |
| --- | --- | --- |
| Payment finality was not changed by diagnostic path | yes / no |  |
| Payment confirmation status unchanged unless approved test setup required otherwise | yes / no / not applicable |  |
| No provider callback mutation | yes / no |  |
| No payment reversal/refund created | yes / no |  |

Payment finality impact conclusion: pass / fail.

## 21. ExitAuthorization Impact Confirmation

| Confirmation item | Result | Evidence |
| --- | --- | --- |
| ExitAuthorization was not issued by diagnostic path | yes / no |  |
| Existing ExitAuthorization flow was not invoked by diagnostic path | yes / no |  |
| Fiscal gating enforcement remained off | yes / no |  |
| No blocking behavior occurred | yes / no |  |

ExitAuthorization impact conclusion: pass / fail.

## 22. Gate Behavior Impact Confirmation

| Confirmation item | Result | Evidence |
| --- | --- | --- |
| No gate command | yes / no |  |
| No barrier open | yes / no |  |
| No gate integration event | yes / no |  |
| No exit execution triggered | yes / no |  |

Gate impact conclusion: pass / fail.

## 23. Post-Run Reconciliation Record

| Reconciliation item | Result | Evidence |
| --- | --- | --- |
| POS Server fiscal evidence matched Central PMS fiscal reference | yes / no / not applicable |  |
| Fiscal document id matched | yes / no / not applicable |  |
| Fiscal document number matched | yes / no / not applicable |  |
| Fiscal identity/policy/sequence matched | yes / no / not applicable |  |
| No duplicate reference | yes / no |  |
| No duplicate ExitAuthorization | yes / no |  |
| No gate side effect | yes / no |  |
| Exception closed or escalated | closed / escalated / not applicable |  |
| Reviewer |  |  |
| Reconciliation timestamp |  |  |

Reconciliation conclusion: pass / fail / inconclusive.

## 24. Rollback / Cleanup Record

| Cleanup item | Result | Evidence |
| --- | --- | --- |
| Diagnostic guard disabled after run | yes / no |  |
| Live-call flag disabled after run, if applicable | yes / no / not applicable |  |
| Evidence preserved | yes / no |  |
| Logs preserved | yes / no |  |
| Fiscal records preserved | yes / no |  |
| POS Server fiscal documents not modified | yes / no |  |
| Fiscal numbers not reused | yes / no |  |
| Stakeholders notified | yes / no |  |
| Unresolved exceptions tracked | yes / no / not applicable |  |

Cleanup notes:

-  

## 25. Issues and Deviations

| Issue id | Description | Severity | Owner | Status | Decision | Follow-up branch/task |
| --- | --- | --- | --- | --- | --- | --- |
|  |  | low / medium / high / critical |  | open / closed / deferred |  |  |

Deviation approval, if any:

-  

## 26. Attachments / Log References

| Attachment/reference | Location | Redaction confirmed | Notes |
| --- | --- | --- | --- |
| Log reference |  | yes / no |  |
| Screenshot reference |  | yes / no |  |
| Request correlation id |  | yes / no |  |
| POS Server trace id, if available |  | yes / no / not applicable |  |
| Central PMS event id |  | yes / no / not applicable |  |
| UAT evidence folder/location |  | yes / no |  |

Redaction confirmation: passed / failed.

## 27. Final UAT Outcome

| Field | Evidence |
| --- | --- |
| Final status | `passed` / `passed_with_notes` / `failed` / `aborted` / `inconclusive` |
| Pass/fail/inconclusive reason |  |
| Approved for next step | yes / no |
| Next recommended action |  |
| Reviewer notes |  |

Final outcome statement:

-  

## 28. Reviewer Signoff

| Reviewer role | Name | Decision | Date/time | Notes |
| --- | --- | --- | --- | --- |
| UAT lead |  | approve / reject / conditional |  |  |
| Engineering lead |  | approve / reject / conditional |  |  |
| POS Server owner |  | approve / reject / conditional |  |  |
| Central PMS owner |  | approve / reject / conditional |  |  |
| Operations lead |  | approve / reject / conditional |  |  |
| Compliance/accounting observer, if applicable |  | approve / reject / conditional / not applicable |  |  |

## 29. Requirements Traceability Summary

| Requirement area | Template coverage |
| --- | --- |
| Run metadata | Section 2 |
| Approvals | Section 3 |
| Environment and Site/Site POS Server | Sections 4, 5 |
| Configuration readiness | Section 6 |
| POS Server readiness | Section 7 |
| Central PMS readiness | Section 8 |
| Test data and fiscal request facts | Sections 9, 10 |
| Sensitive data exclusion | Section 11 |
| Pre-run controls | Section 12 |
| Invocation evidence | Section 13 |
| POS Server response evidence | Section 14 |
| Central PMS fiscal reference evidence | Section 15 |
| Idempotency/replay/conflict/failure/unknown evidence | Sections 16-18 |
| Shadow/audit evidence | Section 19 |
| Payment, ExitAuthorization, and gate impact | Sections 20-22 |
| Reconciliation and cleanup | Sections 23, 24 |
| Issues, attachments, final outcome, signoff | Sections 25-28 |

Recommended next task:

`feature/central-pms-pos-server-controlled-uat-harness-planning`

Purpose: plan the safe internal harness or endpoint strategy for invoking the controlled diagnostic seam, using the runbook and evidence template, before implementing any tool or endpoint.
