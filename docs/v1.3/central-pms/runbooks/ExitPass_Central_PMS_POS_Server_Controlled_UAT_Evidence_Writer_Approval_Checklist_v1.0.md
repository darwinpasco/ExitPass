# ExitPass Central PMS POS Server Controlled UAT Evidence Writer Approval Checklist v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS POS Server Controlled UAT Evidence Writer Approval Checklist |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-evidence-writer-approval-checklist |
| Scope | Approval checklist before any controlled UAT evidence writer implementation |
| Status | Checklist only; no implementation |

## 2. Purpose and Scope

This checklist defines the approvals required before implementing any controlled Central PMS POS Server UAT evidence file writer.

The checklist closes the gap between:

- controlled UAT evidence exporter;
- controlled UAT evidence retention/governance plan;
- controlled UAT evidence file writer planning document;
- any future implementation of an application-level writer or CLI writer.

The goal is to prevent file-writing code from being built before the required owners, storage location, redaction workflow, retention period, hash/signature rules, access controls, run ID governance, and writer approach are approved.

This document does not approve implementation by itself. It defines the approvals that must be recorded before implementation can begin.

## 3. Current Implementation Baseline

Current Central PMS implementation and documentation baseline includes:

- controlled UAT operator runbook;
- controlled UAT evidence template;
- controlled UAT harness planning;
- controlled UAT evidence retention/governance plan;
- evidence file writer planning;
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

The current harness and exporter can validate controlled UAT metadata and produce safe structured JSON. They do not save files, call POS Server merely to export evidence, mutate payment finality, issue ExitAuthorization, or trigger gate behavior.

## 4. Authority Boundaries

The evidence writer approval process must preserve these authority boundaries:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- Evidence files are audit artifacts only and do not create operational authority.

Approving an evidence writer must not approve fiscal issuance execution, payment finality mutation, ExitAuthorization issuance, gate behavior, retry scheduling, readback automation, or fiscal gating enforcement.

## 5. Non-Goals

This checklist task does not:

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

## 6. Approval Checklist Overview

Every approval area must be completed before any evidence writer implementation starts.

Approval statuses:

- `not_started`
- `pending`
- `approved`
- `rejected`
- `deferred`
- `not_applicable`

| Approval area | Owner | Required evidence | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- | --- |
| Evidence repository ownership |  | Owner and deputy named | `not_started` |  |  |
| Output location |  | Approved root/location and folder pattern | `not_started` |  |  |
| Retention |  | Retention period and archival workflow | `not_started` |  |  |
| Redaction ownership |  | Redaction owner and SLA | `not_started` |  |  |
| Hash/checksum/signature |  | Hash/signature approach and verification workflow | `not_started` |  |  |
| Run ID sequence ownership |  | Run ID format and sequence owner | `not_started` |  |  |
| Access control |  | Role matrix and permission decision | `not_started` |  |  |
| Writer approach |  | App-level vs CLI decision | `not_started` |  |  |
| Local dry-run evidence folder |  | Allow/deny decision and location if allowed | `not_started` |  |  |
| Source repository write prohibition |  | Prohibition approved; exception documented if any | `not_started` |  |  |
| Path allow-list |  | Root allow-list and traversal rejection rule | `not_started` |  |  |
| Overwrite/supersession |  | No-overwrite and revision policy | `not_started` |  |  |
| Sensitive-data rejection |  | Rejection markers and status model | `not_started` |  |  |
| Evidence lifecycle/status |  | Lifecycle and status model | `not_started` |  |  |
| Reviewer/signoff workflow |  | Required reviewer roles | `not_started` |  |  |
| Incident/escalation |  | Escalation path and owners | `not_started` |  |  |
| Rollback/cleanup |  | Cleanup and preservation workflow | `not_started` |  |  |
| Future writer test acceptance |  | Test acceptance checklist | `not_started` |  |  |

## 7. Evidence Repository Ownership Approval

Approval must identify:

- official evidence repository owner;
- backup/deputy owner;
- owner responsibilities;
- escalation contact;
- repository maintenance cadence.

Owner responsibilities must include:

- maintaining folder structure;
- approving evidence storage requests;
- managing access review;
- coordinating retention and archival;
- coordinating evidence rejection or supersession;
- escalating sensitive-data incidents.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Official evidence repository owner named | named owner | `not_started` |  |
| Backup/deputy owner named | named deputy | `not_started` |  |
| Owner responsibilities documented | approved | `not_started` |  |
| Escalation contact documented | approved | `not_started` |  |
| Maintenance cadence documented | approved | `not_started` |  |

No writer implementation may begin if the evidence repository owner is missing.

## 8. Output Location Approval

Approval must define:

- approved root path or repository location;
- allowed environments;
- allowed Site/Site POS Server folders;
- evidence folder pattern;
- ticket/change linkage;
- whether local dry-run storage is allowed.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Approved root path or repository location selected | approved location | `not_started` |  |
| Allowed environments listed | approved environment list | `not_started` |  |
| Allowed Site/Site POS Server folder rules defined | approved folder rules | `not_started` |  |
| Evidence folder pattern approved | approved pattern | `not_started` |  |
| Ticket/change linkage required | approved | `not_started` |  |
| Local dry-run storage decision recorded | approved / rejected | `not_started` |  |

The writer must not support unspecified output locations.

## 9. Retention Approval

Approval must define:

- minimum retention period;
- extended retention when fiscal numbers are allocated;
- archival owner;
- deletion approval workflow;
- superseded evidence retention;
- rejected evidence retention.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Minimum retention period approved | approved period | `not_started` |  |
| Fiscal-number allocated retention approved | approved period/process | `not_started` |  |
| Archival owner named | named owner | `not_started` |  |
| Deletion approval workflow approved | approved workflow | `not_started` |  |
| Superseded evidence retention approved | approved | `not_started` |  |
| Rejected evidence retention approved | approved | `not_started` |  |

No implementation may include automatic deletion or purge behavior.

## 10. Redaction Owner Approval

Approval must define:

- redaction owner role;
- who can access unredacted evidence;
- who can approve redacted evidence;
- redaction review SLA;
- redaction evidence/signoff record;
- handling of rejected sensitive evidence.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Redaction owner role approved | approved role | `not_started` |  |
| Unredacted evidence access list approved | approved role list | `not_started` |  |
| Redacted evidence approver approved | approved role | `not_started` |  |
| Redaction review SLA approved | approved SLA | `not_started` |  |
| Redaction signoff record approved | approved format | `not_started` |  |
| Rejected sensitive evidence workflow approved | approved workflow | `not_started` |  |

No writer may mark evidence as approved merely because automated sensitive marker checks pass.

## 11. Hash/Checksum/Signature Approval

Approval must define:

- SHA-256 as the minimum checksum;
- whether digital signature/attestation is required;
- hash file format;
- hash storage location;
- hash verification workflow;
- who verifies hashes during review.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| SHA-256 minimum approved | approved | `not_started` |  |
| Digital signature/attestation decision recorded | required / deferred / not required | `not_started` |  |
| Hash file format approved | approved format | `not_started` |  |
| Hash storage location approved | approved location | `not_started` |  |
| Hash verification workflow approved | approved workflow | `not_started` |  |
| Hash reviewer role approved | approved role | `not_started` |  |

Any future writer must fail closed if hash computation fails.

## 12. Run ID Sequence Ownership Approval

Approval must define:

- run ID format;
- sequence owner;
- duplicate prevention process;
- Site/environment encoding;
- correction/supersession suffix;
- who may allocate run IDs.

Approved run ID pattern:

```text
CPS-POS-UAT-YYYYMMDD-<site>-<sequence>
```

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Run ID format approved | approved format | `not_started` |  |
| Sequence owner named | named owner | `not_started` |  |
| Duplicate prevention process approved | approved process | `not_started` |  |
| Site/environment encoding approved | approved | `not_started` |  |
| Correction/supersession suffix approved | approved suffix rule | `not_started` |  |
| Run ID allocator roles approved | approved role list | `not_started` |  |

No writer may auto-generate official run IDs unless the sequence owner and allocation process are approved.

## 13. Access Control Approval

Approval must define access for:

- engineering lead;
- Central PMS developer/operator;
- POS Server owner;
- UAT lead;
- operations lead;
- compliance/accounting observer;
- support/helpdesk;
- ordinary parking operator.

Ordinary parking operators must have no raw UAT evidence access.

Checklist:

| Role | Raw evidence access | Redacted evidence access | Writer execute access | Approval status | Approval reference |
| --- | --- | --- | --- | --- | --- |
| Engineering lead |  |  |  | `not_started` |  |
| Central PMS developer/operator |  |  |  | `not_started` |  |
| POS Server owner |  |  |  | `not_started` |  |
| UAT lead |  |  |  | `not_started` |  |
| Operations lead |  |  |  | `not_started` |  |
| Compliance/accounting observer |  |  |  | `not_started` |  |
| Support/helpdesk |  |  |  | `not_started` |  |
| Ordinary parking operator | No | No | No | `not_started` |  |

Access must be least privilege, environment scoped, and reviewed before writer implementation.

## 14. App-Level Writer vs CLI Writer Decision Gate

A writer approach must be selected before implementation begins.

Allowed decision outcomes:

- no writer/manual save remains in force;
- application-level writer approved;
- CLI writer approved;
- endpoint/tooling writer rejected/deferred;
- evidence registry rejected/deferred.

Decision checklist:

| Decision item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Manual save remains in force until writer implementation starts | confirmed | `not_started` |  |
| Application-level writer decision | approved / rejected / deferred | `not_started` |  |
| CLI writer decision | approved / rejected / deferred | `not_started` |  |
| Endpoint/tooling writer decision | rejected / deferred unless auth approved | `not_started` |  |
| Evidence registry decision | rejected / deferred unless registry approved | `not_started` |  |
| Selected writer approach documented | selected approach | `not_started` |  |

No writer implementation may begin until the selected option is approved.

## 15. Local Dry-Run Evidence Folder Approval

Approval must decide whether a local dry-run evidence folder is allowed.

If allowed, approval must define:

- exact allowed root;
- gitignore requirement;
- local-only scope;
- retention expectations;
- prohibition on official evidence claims from local-only files.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Local dry-run folder allowed | yes / no | `not_started` |  |
| Exact local root approved if allowed | approved root | `not_started` |  |
| Gitignore rule approved if allowed | approved | `not_started` |  |
| Local-only status documented | approved | `not_started` |  |
| Official evidence prohibition documented | approved | `not_started` |  |

Local dry-run storage, if approved, is not official UAT evidence storage.

## 16. Source Repository Write Prohibition Approval

Approval must confirm:

- writer must not write official evidence inside the source repository;
- source repository writes are rejected by default;
- any local dry-run exception must be explicit, gitignored, and non-official;
- source-controlled docs, SQL, generated files, and runtime files must not be used as evidence output targets.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Official evidence in source repo prohibited | approved | `not_started` |  |
| Default source repo write rejection approved | approved | `not_started` |  |
| Local dry-run exception rule approved | approved / not applicable | `not_started` |  |
| Generated/DOCX/SQL/runtime output prohibition approved | approved | `not_started` |  |

## 17. Path Allow-List Approval

Approval must define:

- output root allow-list;
- path traversal rejection rule;
- no source repo writing unless approved gitignored local dry-run mode;
- no system/temp/default folders unless explicit local-test mode;
- per-run subdirectory requirement.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Output root allow-list approved | approved root list | `not_started` |  |
| Path traversal rejection approved | approved | `not_started` |  |
| Source repo write rejection approved | approved | `not_started` |  |
| System/temp/default folder rule approved | approved | `not_started` |  |
| Per-run subdirectory required | approved | `not_started` |  |

The future writer must normalize paths before evaluating allow-list membership.

## 18. Overwrite/Supersession Policy Approval

Approval must define:

- fail if target exists;
- no overwrite by default;
- revision suffix requirements;
- supersession reason/approver requirements;
- retention of original and superseded evidence.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Fail-if-target-exists rule approved | approved | `not_started` |  |
| No overwrite by default approved | approved | `not_started` |  |
| Revision suffix format approved | approved format | `not_started` |  |
| Supersession reason required | approved | `not_started` |  |
| Supersession approver required | approved | `not_started` |  |
| Original/superseded retention approved | approved | `not_started` |  |

Approved evidence must never be edited in place.

## 19. Sensitive-Data Rejection Approval

Approval must define:

- prohibited markers;
- rejection status;
- no JSON/file write if rejected;
- redaction required status;
- redaction owner signoff before approval;
- no raw logs/screenshots auto-write.

Minimum prohibited sensitive categories:

- PAN;
- CVV;
- tokens;
- credentials;
- secrets;
- raw provider callback payloads;
- raw entitlement evidence;
- uncontrolled images/files;
- unmanaged customer PII;
- free-form sensitive blobs.

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Prohibited marker list approved | approved list | `not_started` |  |
| Rejection status approved | approved status | `not_started` |  |
| No-write-on-rejection rule approved | approved | `not_started` |  |
| Redaction-required status approved | approved status | `not_started` |  |
| Redaction owner signoff required | approved | `not_started` |  |
| Raw logs/screenshots auto-write prohibited | approved | `not_started` |  |

## 20. Evidence Lifecycle/Status Approval

Evidence lifecycle to approve:

- `planned`
- `generated`
- `submitted`
- `redaction_review`
- `approved`
- `rejected`
- `superseded`
- `archived`

Evidence status model to approve:

- `draft`
- `submitted_for_review`
- `redaction_required`
- `approved`
- `rejected`
- `superseded`
- `archived`

Checklist:

| Item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Lifecycle states approved | approved | `not_started` |  |
| Status model approved | approved | `not_started` |  |
| State transition rules approved | approved | `not_started` |  |
| Approved status requires reviewer signoff | approved | `not_started` |  |
| Rejected status requires reason/owner | approved | `not_started` |  |
| Superseded status retains original | approved | `not_started` |  |

## 21. Reviewer/Signoff Workflow Approval

Required signoff roles:

- UAT lead;
- engineering lead;
- POS Server owner;
- Central PMS owner;
- operations lead;
- compliance/accounting observer if fiscal number allocated.

Checklist:

| Reviewer role | Required when | Status | Approval reference |
| --- | --- | --- | --- |
| UAT lead | all official UAT evidence | `not_started` |  |
| Engineering lead | all writer implementation approval | `not_started` |  |
| POS Server owner | POS Server fiscal evidence involved | `not_started` |  |
| Central PMS owner | Central PMS fiscal reference evidence involved | `not_started` |  |
| Operations lead | operational UAT evidence involved | `not_started` |  |
| Compliance/accounting observer | fiscal number allocated or compliance review required | `not_started` |  |

Reviewer signoff must include reviewer identity, timestamp, decision, notes, and evidence hash reference.

## 22. Incident/Escalation Approval

Approval must define handling for:

- sensitive data detected;
- evidence write failure;
- hash failure;
- output path error;
- fiscal number allocated unexpectedly;
- unknown POS Server outcome;
- mismatch between POS Server and Central PMS evidence.

Checklist:

| Incident type | Required owner | Required action | Status | Approval reference |
| --- | --- | --- | --- | --- |
| Sensitive data detected | redaction owner | restrict access, reject or redaction review | `not_started` |  |
| Evidence write failure | evidence repository owner | preserve diagnostics, retry only after approval | `not_started` |  |
| Hash failure | engineering lead | reject evidence package until corrected | `not_started` |  |
| Output path error | evidence repository owner | abort write and correct configuration | `not_started` |  |
| Unexpected fiscal number allocation | POS Server owner / compliance | incident-tag and reconcile | `not_started` |  |
| Unknown POS Server outcome | Central PMS owner / POS Server owner | preserve upstream finality reference and reconcile | `not_started` |  |
| POS Server/Central PMS mismatch | engineering lead / POS Server owner | escalate and reconcile before approval | `not_started` |  |

## 23. Rollback/Cleanup Approval

Approval must define:

- disabling diagnostic config;
- preserving evidence;
- preserving fiscal references;
- preserving POS Server fiscal documents;
- no fiscal number reuse;
- stakeholder notification;
- evidence closure.

Checklist:

| Rollback/cleanup item | Required result | Status | Approval reference |
| --- | --- | --- | --- |
| Diagnostic config disable procedure approved | approved | `not_started` |  |
| Evidence preservation rule approved | approved | `not_started` |  |
| Fiscal reference preservation rule approved | approved | `not_started` |  |
| POS Server fiscal document preservation rule approved | approved | `not_started` |  |
| Fiscal number reuse prohibition approved | approved | `not_started` |  |
| Stakeholder notification workflow approved | approved | `not_started` |  |
| Evidence closure workflow approved | approved | `not_started` |  |

Cleanup must not delete fiscal records or POS Server fiscal documents without a separately approved data governance process.

## 24. Test Acceptance Checklist for Future Writer

Before any writer implementation is accepted, tests must cover:

- valid write to allow-listed path;
- missing approval rejected;
- missing run ID rejected;
- missing evidence owner rejected;
- path traversal rejected;
- source repo write rejected unless local dry-run mode approved;
- existing file not overwritten;
- SHA-256 generated;
- hash file written;
- sensitive marker rejected;
- redaction-required state handled;
- no payment/exit behavior change;
- no POS Server call from writer;
- no endpoint/CLI added unless explicitly in scope.

Acceptance table:

| Test area | Required result | Status | Evidence reference |
| --- | --- | --- | --- |
| Allow-listed write | pass | `not_started` |  |
| Missing approval rejection | pass | `not_started` |  |
| Missing run ID rejection | pass | `not_started` |  |
| Missing evidence owner rejection | pass | `not_started` |  |
| Path traversal rejection | pass | `not_started` |  |
| Source repo write rejection | pass | `not_started` |  |
| Existing file not overwritten | pass | `not_started` |  |
| SHA-256 generated | pass | `not_started` |  |
| Hash file written | pass | `not_started` |  |
| Sensitive marker rejected | pass | `not_started` |  |
| Redaction-required handled | pass | `not_started` |  |
| No payment/exit behavior change | pass | `not_started` |  |
| No POS Server call from writer | pass | `not_started` |  |
| No endpoint/CLI added unless in scope | pass | `not_started` |  |

## 25. Final Go/No-Go Checklist Before Writer Implementation

Go criteria:

- repository owner approved;
- output location approved;
- retention approved;
- redaction owner approved;
- hash/signature approach approved;
- run ID owner approved;
- access matrix approved;
- app-level vs CLI writer decision approved;
- local dry-run policy approved;
- path allow-list approved;
- overwrite/supersession policy approved;
- sensitive-data rejection policy approved;
- lifecycle/status approved;
- reviewer workflow approved.

No-go criteria:

- any required owner missing;
- output path not approved;
- retention unresolved;
- redaction workflow unresolved;
- hash/signature unresolved;
- access control unresolved;
- no decision between app-level or CLI writer;
- endpoint/tooling writer proposed without auth/role approval.

Final decision table:

| Decision item | Go required? | Status | Approval reference | Notes |
| --- | --- | --- | --- | --- |
| All owners approved | yes | `not_started` |  |  |
| Storage/output approved | yes | `not_started` |  |  |
| Retention approved | yes | `not_started` |  |  |
| Redaction approved | yes | `not_started` |  |  |
| Hash/signature approved | yes | `not_started` |  |  |
| Run ID ownership approved | yes | `not_started` |  |  |
| Access matrix approved | yes | `not_started` |  |  |
| Writer option selected | yes | `not_started` |  |  |
| Endpoint/tooling excluded unless separately approved | yes | `not_started` |  |  |

## 26. Risks and Open Questions

Risks:

- File writer implementation may start before owners are recorded.
- Output location may be technically valid but operationally unapproved.
- Redaction workflow may be incomplete when evidence contains sensitive context.
- Hash requirements may change after implementation.
- App-level and CLI approaches may require different access controls.
- Local dry-run folders may be mistaken for official evidence storage.
- Endpoint/tooling writer pressure may appear before auth/role controls are approved.

Open questions:

- Who is the official evidence repository owner?
- Which evidence storage location is approved first?
- What exact retention period applies when fiscal numbers are allocated?
- Is SHA-256 sufficient, or is a digital signature/attestation required?
- Who owns run ID sequence allocation?
- Should the first writer be application-level or CLI?
- Is local dry-run storage allowed?
- What role approves superseded evidence?

## 27. Requirements Traceability Summary

| Requirement | Covered by |
| --- | --- |
| Approval checklist before writer implementation | Sections 6-25 |
| Evidence repository ownership | Section 7 |
| Output location approval | Section 8 |
| Retention approval | Section 9 |
| Redaction owner approval | Section 10 |
| Hash/checksum/signature approval | Section 11 |
| Run ID sequence ownership | Section 12 |
| Access control approval | Section 13 |
| App-level vs CLI decision gate | Section 14 |
| Local dry-run policy | Section 15 |
| Source repository write prohibition | Section 16 |
| Path allow-list approval | Section 17 |
| Overwrite/supersession policy | Section 18 |
| Sensitive-data rejection policy | Section 19 |
| Evidence lifecycle/status | Section 20 |
| Reviewer/signoff workflow | Section 21 |
| Incident/escalation handling | Section 22 |
| Rollback/cleanup handling | Section 23 |
| Future writer test acceptance | Section 24 |
| Final go/no-go before implementation | Section 25 |
| Authority boundaries | Section 4 |
| Non-goals | Section 5 |

## Recommended Next Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-evidence-writer-approval-record`

Purpose: create a fillable approval record template for the evidence writer approval checklist, so actual owner approvals can be captured before implementation.
