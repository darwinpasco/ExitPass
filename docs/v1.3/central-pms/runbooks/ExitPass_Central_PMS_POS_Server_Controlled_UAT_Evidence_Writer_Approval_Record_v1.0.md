# ExitPass Central PMS POS Server Controlled UAT Evidence Writer Approval Record v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS POS Server Controlled UAT Evidence Writer Approval Record |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-evidence-writer-approval-record |
| Scope | Fillable approval record before controlled UAT evidence writer implementation |
| Status | Template only; not an implementation approval until completed and signed |

## 2. Purpose and Scope

This fillable record captures the owner approvals, rejections, deferrals, conditions, evidence references, dates, and final go/no-go decision required before implementing any controlled UAT evidence file writer.

This record closes the gap between:

- controlled UAT evidence writer approval checklist;
- evidence file writer planning document;
- evidence retention/governance plan;
- any future implementation of an application-level writer or CLI writer.

This record must be completed before any evidence file writer implementation starts. An incomplete record means the project remains manual-save only.

## 3. Current Implementation Baseline

Current Central PMS implementation and documentation baseline includes:

- controlled UAT operator runbook;
- controlled UAT evidence template;
- controlled UAT harness planning;
- controlled UAT evidence retention/governance plan;
- evidence file writer planning;
- evidence writer approval checklist;
- application-level controlled UAT harness;
- safe evidence JSON exporter;
- no endpoint;
- no CLI/tooling;
- no automatic file-writing;
- no payment confirmation wiring;
- no ExitAuthorization wiring;
- no fiscal gating enforcement;
- no retry scheduler;
- no GET readback worker.

## 4. Authority Boundaries

This approval record preserves these authority boundaries:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- Evidence files are audit artifacts only and do not create operational authority.

Approving this record must not be interpreted as approval for fiscal gating enforcement, payment mutation, ExitAuthorization issuance, gate behavior, retry scheduling, readback automation, endpoint exposure, or operator tooling.

## 5. Non-Goals

This task does not:

- approve implementation by itself;
- implement evidence file-writing;
- write evidence files;
- expose endpoint/tooling;
- execute live POS Server calls;
- enable production payment/exit flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry;
- implement readback worker;
- implement Operator Console queue;
- implement Dashboard projection;
- modify source code;
- modify SQL;
- modify POS Server runtime.

## 6. Approval Record Summary

Decision values:

- `approved`
- `rejected`
- `deferred`
- `not_applicable`

| Approval area | Required decision | Owner | Deputy owner | Decision | Approval date/time | Approval reference | Evidence/reference link | Conditions | Expiry/review date | Notes | Signature/name |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Evidence repository ownership | Owner and deputy approved |  |  |  |  |  |  |  |  |  |  |
| Output location | Root/location approved |  |  |  |  |  |  |  |  |  |  |
| Retention | Retention and archival approved |  |  |  |  |  |  |  |  |  |  |
| Redaction owner | Owner/workflow approved |  |  |  |  |  |  |  |  |  |  |
| Hash/checksum/signature | Hash/signature posture approved |  |  |  |  |  |  |  |  |  |  |
| Run ID sequence ownership | Run ID owner/process approved |  |  |  |  |  |  |  |  |  |  |
| Access control | Access matrix approved |  |  |  |  |  |  |  |  |  |  |
| Writer approach | App-level/CLI/manual decision approved |  |  |  |  |  |  |  |  |  |  |
| Local dry-run folder | Allow/deny approved |  |  |  |  |  |  |  |  |  |  |
| Source repository write prohibition | Prohibition approved |  |  |  |  |  |  |  |  |  |  |
| Path allow-list | Allow-list controls approved |  |  |  |  |  |  |  |  |  |  |
| Overwrite/supersession | No-overwrite policy approved |  |  |  |  |  |  |  |  |  |  |
| Sensitive-data rejection | Rejection policy approved |  |  |  |  |  |  |  |  |  |  |
| Evidence lifecycle/status | Lifecycle/status approved |  |  |  |  |  |  |  |  |  |  |
| Reviewer/signoff workflow | Required reviewers approved |  |  |  |  |  |  |  |  |  |  |
| Incident/escalation | Incident owners/actions approved |  |  |  |  |  |  |  |  |  |  |
| Rollback/cleanup | Cleanup/preservation approved |  |  |  |  |  |  |  |  |  |  |
| Future writer test acceptance | Test acceptance approved |  |  |  |  |  |  |  |  |  |  |

## 7. Evidence Repository Ownership Approval Record

| Field | Record |
| --- | --- |
| Official evidence repository owner |  |
| Backup/deputy owner |  |
| Escalation contact |  |
| Maintenance cadence |  |
| Owner responsibilities accepted | yes / no |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 8. Output Location Approval Record

| Field | Record |
| --- | --- |
| Approved root path or repository location |  |
| Allowed environments |  |
| Site/Site POS Server folder pattern |  |
| Ticket/change linkage required | yes / no |
| Local dry-run allowed | yes / no |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 9. Retention Approval Record

| Field | Record |
| --- | --- |
| Minimum retention period |  |
| Extended retention for fiscal-number allocated evidence |  |
| Archival owner |  |
| Deletion approval workflow |  |
| Superseded evidence retention |  |
| Rejected evidence retention |  |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 10. Redaction Owner Approval Record

| Field | Record |
| --- | --- |
| Redaction owner |  |
| Unredacted evidence access roles |  |
| Redacted evidence approver |  |
| Redaction SLA |  |
| Redaction signoff format |  |
| Rejected sensitive evidence workflow |  |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 11. Hash/Checksum/Signature Approval Record

| Field | Record |
| --- | --- |
| SHA-256 minimum approved | yes / no |
| Digital signature/attestation | required / deferred / not_required |
| Hash file format |  |
| Hash storage location |  |
| Hash verification workflow |  |
| Hash reviewer |  |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 12. Run ID Sequence Ownership Approval Record

| Field | Record |
| --- | --- |
| Run ID format | `CPS-POS-UAT-YYYYMMDD-<site>-<sequence>` / other: |
| Sequence owner |  |
| Duplicate prevention process |  |
| Site/environment encoding |  |
| Correction/supersession suffix |  |
| Run ID allocator roles |  |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 13. Access Control Approval Record

| Role | Raw evidence access | Redacted evidence access | Writer execute access | Approval status | Approval reference | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| Engineering lead |  |  |  |  |  |  |
| Central PMS developer/operator |  |  |  |  |  |  |
| POS Server owner |  |  |  |  |  |  |
| UAT lead |  |  |  |  |  |  |
| Operations lead |  |  |  |  |  |  |
| Compliance/accounting observer |  |  |  |  |  |  |
| Support/helpdesk |  |  |  |  |  |  |
| Ordinary parking operator | no | no | no |  |  | Must not access raw UAT evidence or execute writer |

| Field | Record |
| --- | --- |
| Access control approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Signature/name |  |

## 14. App-Level Writer vs CLI Writer Decision Record

| Decision option | Decision | Approval reference | Notes |
| --- | --- | --- | --- |
| No writer/manual save remains in force | approved / rejected / deferred / not_applicable |  |  |
| Application-level writer approved | approved / rejected / deferred / not_applicable |  |  |
| CLI writer approved | approved / rejected / deferred / not_applicable |  |  |
| Endpoint/tooling writer rejected/deferred | approved / rejected / deferred / not_applicable |  |  |
| Evidence registry rejected/deferred | approved / rejected / deferred / not_applicable |  |  |

| Field | Record |
| --- | --- |
| Final selected approach | manual_save_only / application_level_writer / cli_writer / deferred |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 15. Local Dry-Run Evidence Folder Approval Record

| Field | Record |
| --- | --- |
| Local dry-run folder allowed | yes / no |
| Exact local root if allowed |  |
| Gitignore rule approved | yes / no / not_applicable |
| Local-only status accepted | yes / no |
| Official evidence prohibition accepted | yes / no |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 16. Source Repository Write Prohibition Approval Record

| Field | Record |
| --- | --- |
| Official evidence in source repo prohibited | yes / no |
| Default source repo write rejection approved | yes / no |
| Local dry-run exception approved | yes / no / not_applicable |
| Generated/DOCX/SQL/runtime output prohibition approved | yes / no |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 17. Path Allow-List Approval Record

| Field | Record |
| --- | --- |
| Output root allow-list |  |
| Path traversal rejection rule |  |
| Source repo write rejection |  |
| System/temp/default folder rule |  |
| Per-run subdirectory requirement |  |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 18. Overwrite/Supersession Policy Approval Record

| Field | Record |
| --- | --- |
| Fail-if-target-exists rule | approved / rejected / deferred |
| No overwrite by default | approved / rejected / deferred |
| Revision suffix format |  |
| Supersession reason required | yes / no |
| Supersession approver required | yes / no |
| Original/superseded retention |  |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 19. Sensitive-Data Rejection Approval Record

| Field | Record |
| --- | --- |
| Prohibited marker list approved | yes / no |
| Rejection status approved | yes / no |
| No-write-on-rejection rule | approved / rejected / deferred |
| Redaction-required status | approved / rejected / deferred |
| Redaction owner signoff required | yes / no |
| Raw logs/screenshots auto-write prohibited | yes / no |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 20. Evidence Lifecycle/Status Approval Record

Lifecycle approvals:

| Lifecycle state | Approved | Notes |
| --- | --- | --- |
| `planned` | yes / no |  |
| `generated` | yes / no |  |
| `submitted` | yes / no |  |
| `redaction_review` | yes / no |  |
| `approved` | yes / no |  |
| `rejected` | yes / no |  |
| `superseded` | yes / no |  |
| `archived` | yes / no |  |

Status model approvals:

| Status | Approved | Notes |
| --- | --- | --- |
| `draft` | yes / no |  |
| `submitted_for_review` | yes / no |  |
| `redaction_required` | yes / no |  |
| `approved` | yes / no |  |
| `rejected` | yes / no |  |
| `superseded` | yes / no |  |
| `archived` | yes / no |  |

| Field | Record |
| --- | --- |
| State transition rules approved | yes / no |
| Approval decision | approved / rejected / deferred / not_applicable |
| Approval date/time |  |
| Approval reference |  |
| Evidence/reference link |  |
| Conditions |  |
| Expiry/review date |  |
| Notes |  |
| Signature/name |  |

## 21. Reviewer/Signoff Workflow Approval Record

| Name | Role | Decision | Date/time | Evidence hash reference | Notes |
| --- | --- | --- | --- | --- | --- |
|  | UAT lead | approved / rejected / deferred / not_applicable |  |  |  |
|  | Engineering lead | approved / rejected / deferred / not_applicable |  |  |  |
|  | POS Server owner | approved / rejected / deferred / not_applicable |  |  |  |
|  | Central PMS owner | approved / rejected / deferred / not_applicable |  |  |  |
|  | Operations lead | approved / rejected / deferred / not_applicable |  |  |  |
|  | Compliance/accounting observer if fiscal number allocated | approved / rejected / deferred / not_applicable |  |  |  |

## 22. Incident/Escalation Approval Record

| Incident type | Owner | Required action | Approval status | Approval reference | Notes |
| --- | --- | --- | --- | --- | --- |
| Sensitive data detected |  |  |  |  |  |
| Evidence write failure |  |  |  |  |  |
| Hash failure |  |  |  |  |  |
| Output path error |  |  |  |  |  |
| Fiscal number allocated unexpectedly |  |  |  |  |  |
| Unknown POS Server outcome |  |  |  |  |  |
| POS Server/Central PMS mismatch |  |  |  |  |  |

## 23. Rollback/Cleanup Approval Record

| Rollback/cleanup item | Owner | Approval status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| Diagnostic config disable procedure |  |  |  |  |
| Evidence preservation rule |  |  |  |  |
| Fiscal reference preservation rule |  |  |  |  |
| POS Server fiscal document preservation rule |  |  |  |  |
| Fiscal number reuse prohibition |  |  |  |  |
| Stakeholder notification workflow |  |  |  |  |
| Evidence closure workflow |  |  |  |  |

## 24. Future Writer Test Acceptance Approval Record

| Test acceptance item | Required result | Approval status | Evidence reference | Notes |
| --- | --- | --- | --- | --- |
| Valid write to allow-listed path | pass |  |  |  |
| Missing approval rejected | pass |  |  |  |
| Missing run ID rejected | pass |  |  |  |
| Missing evidence owner rejected | pass |  |  |  |
| Path traversal rejected | pass |  |  |  |
| Source repo write rejected | pass |  |  |  |
| Existing file not overwritten | pass |  |  |  |
| SHA-256 generated | pass |  |  |  |
| Hash file written | pass |  |  |  |
| Sensitive marker rejected | pass |  |  |  |
| Redaction-required handled | pass |  |  |  |
| No payment/exit behavior change | pass |  |  |  |
| No POS Server call from writer | pass |  |  |  |
| No endpoint/CLI added unless explicitly in scope | pass |  |  |  |

## 25. Final Go/No-Go Approval Record

| Final decision item | Required for go | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| All owners approved | yes |  |  |  |
| Storage/output approved | yes |  |  |  |
| Retention approved | yes |  |  |  |
| Redaction approved | yes |  |  |  |
| Hash/signature approved | yes |  |  |  |
| Run ID ownership approved | yes |  |  |  |
| Access matrix approved | yes |  |  |  |
| Writer option selected | yes |  |  |  |
| Endpoint/tooling excluded unless separately approved | yes |  |  |  |
| No-go blockers cleared | yes |  |  |  |

| Field | Record |
| --- | --- |
| Final decision | go / no_go / conditional_go / deferred |
| Final approver |  |
| Final date/time |  |
| Conditions |  |
| Implementation allowed before all go criteria are met | no |
| Approved implementation branch, if any |  |
| Notes |  |
| Signature/name |  |

## 26. Conditions and Dependencies

| Condition ID | Condition | Owner | Due date | Status | Required before implementation | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| CND-001 |  |  |  | open / closed / deferred | yes / no |  |
| CND-002 |  |  |  | open / closed / deferred | yes / no |  |
| CND-003 |  |  |  | open / closed / deferred | yes / no |  |

## 27. Rejection/Deferment Record

| Approval area | Rejected/deferred by | Reason | Date/time | Required remediation | Follow-up owner | Follow-up due date |
| --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |
|  |  |  |  |  |  |  |
|  |  |  |  |  |  |  |

## 28. Approval Evidence References

| Approval reference | Evidence type | Location | Owner | Hash/reference if applicable | Notes |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |
|  |  |  |  |  |  |
|  |  |  |  |  |  |

## 29. Final Decision Summary

| Field | Record |
| --- | --- |
| Writer implementation allowed | yes / no |
| Selected writer approach | manual_save_only / application_level_writer / cli_writer / deferred |
| Remaining blockers |  |
| Approved branch to start implementation, if any |  |
| Implementation not approved until all required go criteria are met | acknowledged / not_acknowledged |
| Final summary owner |  |
| Final summary date/time |  |

## 30. Requirements Traceability Summary

| Requirement | Covered by |
| --- | --- |
| Fillable approval record | Sections 6-29 |
| Evidence repository ownership approval | Section 7 |
| Output location approval | Section 8 |
| Retention approval | Section 9 |
| Redaction owner approval | Section 10 |
| Hash/checksum/signature approval | Section 11 |
| Run ID sequence ownership approval | Section 12 |
| Access control approval | Section 13 |
| App-level writer vs CLI writer decision | Section 14 |
| Local dry-run folder approval | Section 15 |
| Source repository write prohibition | Section 16 |
| Path allow-list approval | Section 17 |
| Overwrite/supersession approval | Section 18 |
| Sensitive-data rejection approval | Section 19 |
| Evidence lifecycle/status approval | Section 20 |
| Reviewer/signoff approval | Section 21 |
| Incident/escalation approval | Section 22 |
| Rollback/cleanup approval | Section 23 |
| Future writer test acceptance approval | Section 24 |
| Final go/no-go approval | Section 25 |
| Conditions and dependencies | Section 26 |
| Rejection/deferment record | Section 27 |
| Approval evidence references | Section 28 |
| Authority boundaries | Section 4 |
| Non-goals | Section 5 |

## Recommended Next Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-evidence-writer-approval-record-review`

Purpose: review the fillable approval record with actual owners or placeholders and decide whether the project remains manual-save only or may proceed toward an application-level/CLI writer implementation.
