# ExitPass Central PMS POS Server Controlled UAT Evidence Retention and Governance Plan v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Evidence Retention and Governance Plan |
| Version | v1.0 |
| Date | 2026-07-03 |
| Scope | Central PMS controlled POS Server fiscal issuance UAT evidence governance |
| Branch | feature/central-pms-pos-server-controlled-uat-harness-evidence-retention-planning |
| Status | Planning only |

## 2. Purpose and Scope

This plan defines where and how controlled Central PMS to POS Server fiscal issuance UAT evidence JSON should be stored, reviewed, retained, redacted, protected, and linked to approvals before any endpoint, CLI, operator tool, or automatic file-writing behavior is introduced.

The plan bridges:

- the controlled UAT operator runbook;
- the controlled UAT evidence template;
- the controlled UAT harness planning document;
- the application-level controlled UAT harness;
- the safe application-level evidence JSON exporter.

This plan does not enable storage automation. It defines governance requirements for future storage automation or manual evidence handling.

## 3. Current Implementation Baseline

Current Central PMS implementation and documentation have:

- controlled UAT operator runbook;
- controlled UAT evidence template;
- controlled UAT harness planning;
- application-level controlled UAT harness;
- application-level evidence exporter;
- no endpoint;
- no CLI/tooling;
- no automatic file-writing;
- no payment confirmation wiring;
- no ExitAuthorization wiring;
- no fiscal gating enforcement;
- no retry scheduler;
- no GET readback worker.

The evidence exporter currently returns a safe structured evidence model and JSON string. It does not write files, call POS Server, mutate payment finality, issue ExitAuthorization, or trigger gate behavior.

## 4. Authority Boundaries

The following authority boundaries are preserved:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.

## 5. Non-Goals

This planning task does not:

- implement evidence storage;
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

## 6. Evidence Governance Objectives

Evidence governance must ensure that controlled UAT evidence is:

- tied to an approved run id;
- tied to a change/ticket/approval record;
- traceable to environment, Site, Site POS Server, and upstream finality reference;
- reviewable by engineering, UAT, operations, and compliance/accounting stakeholders;
- protected from unauthorized access;
- redacted before broader sharing where logs or attachments are included;
- retained long enough to support release, audit, compliance, and fiscal reconciliation decisions;
- protected from silent modification after approval;
- never used to mutate payment finality, fiscal records, ExitAuthorization, or gate behavior;
- never used as a backdoor invocation mechanism.

## 7. Evidence Storage Options Considered

### Option A: Repository-Local Ignored Evidence Folder

Summary:

- Useful only for local development or tightly controlled dry runs.
- Must be explicitly gitignored.
- Not suitable as official evidence repository.
- Low implementation effort.
- Weak governance and weak reviewer access control.

Risks:

- Accidental local loss.
- Accidental inclusion in source control if ignore rules fail.
- Poor retention and approval linkage.

Recommended use:

- Local engineering smoke only, if allowed by team policy.
- Not official UAT evidence storage.

### Option B: Secured Shared Evidence Folder

Summary:

- Suitable for near-term UAT review.
- Access can be restricted to engineering/UAT/operations/compliance roles.
- Simple operational workflow.
- Requires retention, redaction, naming, and approval discipline.

Risks:

- Manual process can drift without checklist enforcement.
- Folder permissions must be actively maintained.
- Weak query/report capability compared with a registry.

Recommended use:

- Near-term recommended storage if paired with ticket/change linkage.

### Option C: Ticketing / Change-Management Attachment

Summary:

- Strong approval linkage.
- Easy reviewer workflow.
- Evidence is naturally tied to change request, run approval, and signoff trail.
- May be harder to query across many UAT runs.

Risks:

- Attachment size limits.
- Redaction must happen before attachment.
- Long-term archive depends on change-management retention policy.

Recommended use:

- Store approved redacted package or final signed evidence package.
- Link back to full secured evidence folder location where applicable.

### Option D: Document Management Repository

Summary:

- Stronger retention and access control than a simple shared folder.
- Suitable for official evidence repository.
- Supports folders, document review, access logs, and retention policies.
- Requires clear naming conventions and permissions.

Risks:

- Requires owner and permission model.
- May require manual upload until tooling exists.
- Must prevent uncontrolled raw logs/screenshots from being broadly shared.

Recommended use:

- Preferred official repository when available.

### Option E: Database-Backed Evidence Registry

Summary:

- Strong traceability later.
- Can support structured search, run status, reviewer state, and dashboards.
- Not recommended yet because it requires schema, API, UI, retention, and access-control decisions.

Risks:

- Adds implementation and operational complexity.
- Could be confused with fiscal reference persistence if not clearly separated.
- Requires migration and backup/retention policy.

Recommended use:

- Future Operator Console/Dashboard integration only after governance is settled.

### Option F: Object Storage With Immutable Retention

Summary:

- Strongest archive posture for compliance-oriented evidence.
- Supports immutability, hashes, retention locks, and lifecycle policies.
- More operational setup than needed before first controlled UAT harness execution.

Risks:

- Requires object storage governance, key management, lifecycle configuration, and access review.
- Tooling must be carefully designed to avoid writing sensitive artifacts into broad buckets.

Recommended use:

- Future archive/compliance phase if required by compliance/accounting/legal.

## 8. Recommended Storage Approach

Use a phased approach.

Phase 1, immediate:

- Use a secured shared evidence folder or approved document management repository.
- Link every evidence package to a ticket/change approval reference.
- Do not add automatic file writing yet.
- Evidence JSON is generated by the harness/exporter and manually saved by an approved actor.
- Redacted evidence package is attached to the ticket/change record.
- Unredacted evidence, if any, remains in restricted storage only.

Phase 2, later:

- Add internal harness or CLI file writing only after explicit approval.
- Writer must require run id, approval ref, evidence location, and redaction status.
- Writer must calculate hash and refuse sensitive markers.
- Writer must not be reachable from payment confirmation or ExitAuthorization flows.

Phase 3, future:

- Consider evidence registry, Operator Console visibility, Dashboard reporting, or object storage retention if operational need justifies it.

## 9. Evidence Folder / Naming Convention

Recommended root structure:

```text
ExitPass-CentralPMS-PosServer-UAT/
  <environment>/
    <site-ref>/
      <yyyy-mm-dd>/
        <run-id>/
          controlled-posserver-fiscal-uat-<run-id>-evidence.json
          controlled-posserver-fiscal-uat-<run-id>-review.md
          controlled-posserver-fiscal-uat-<run-id>-redaction.md
          approvals.md
          logs-redacted/
          screenshots-redacted/
          reconciliation.md
```

Run id format:

```text
CPS-POS-UAT-YYYYMMDD-<site>-<sequence>
```

File names:

- `controlled-posserver-fiscal-uat-<run-id>-evidence.json`
- `controlled-posserver-fiscal-uat-<run-id>-review.md`
- `controlled-posserver-fiscal-uat-<run-id>-redaction.md`

Rules:

- Never overwrite approved evidence.
- Use a superseding run or superseding review note for corrections.
- Keep raw/non-redacted attachments out of broad review folders.
- Attach only redacted evidence packages to broadly visible tickets.

## 10. Evidence Metadata Requirements

Every evidence package must include:

- run id;
- environment;
- Site;
- Site POS Server;
- evidence owner;
- UAT lead;
- approver refs;
- change/ticket ref;
- correlation id;
- upstream finality ref;
- fiscal document id/number, if produced;
- result status;
- final UAT outcome;
- redaction status;
- reviewer signoff status.

The evidence JSON should align with the application-level evidence exporter schema where practical.

## 11. Approval Linkage Requirements

Evidence must link to:

- change ticket or approval record;
- operator runbook checklist;
- UAT evidence template instance;
- fiscal allocation approval, if fiscal number may be allocated;
- rollback owner confirmation;
- compliance/accounting approval, if applicable.

The approval linkage must be present before any controlled UAT run is accepted as valid release evidence.

## 12. Reviewer / Signoff Workflow

Evidence review states:

- `draft`
- `submitted_for_review`
- `redaction_required`
- `approved`
- `rejected`
- `superseded`
- `archived`

Required reviewers:

- UAT lead;
- engineering lead;
- POS Server owner;
- Central PMS owner;
- operations lead;
- compliance/accounting observer if fiscal number was allocated or production-like fiscal sequence was used.

Rules:

- Draft evidence is not release evidence.
- Approved evidence must have reviewer identity, timestamp, and hash.
- Rejected evidence must record reason and owner.
- Superseded evidence must retain both original and replacement references.

## 13. Redaction Workflow

Redaction workflow:

1. Evidence JSON is generated.
2. Sensitive marker scan is performed.
3. Raw logs/screenshots are reviewed.
4. Redacted copies are produced.
5. Redaction owner signs off.
6. Unredacted evidence access remains restricted.
7. Redacted evidence is attached to ticket/review package.

Redaction artifacts:

- redaction status;
- redaction owner;
- redaction timestamp;
- source artifacts reviewed;
- redacted artifacts produced;
- sensitive data found yes/no;
- unresolved redaction issues.

## 14. Sensitive Data Handling

Evidence must not include:

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

Controls:

- The evidence exporter rejects obvious sensitive markers in safe metadata and notes.
- Operators must still review attachments, screenshots, and logs.
- Raw POS Server or Central PMS logs must be redacted before broad sharing.
- If sensitive data is found, evidence moves to `redaction_required` or `rejected`.

## 15. Access Control and Role Matrix

| Role | Raw evidence | Redacted evidence | Approve | Notes |
| --- | --- | --- | --- | --- |
| Engineering lead | Yes | Yes | Yes | Owns technical acceptance |
| Central PMS developer | Limited | Yes | No | Access as needed for investigation |
| POS Server owner | Limited | Yes | Yes | Reviews fiscal issuance behavior |
| UAT lead | Limited | Yes | Yes | Owns UAT acceptance |
| Operations lead | No by default | Yes | Yes | Confirms operational readiness |
| Compliance/accounting observer | Limited if fiscal numbers allocated | Yes | Conditional | Confirms fiscal evidence posture |
| Support/helpdesk | No | Limited summary only | No | No raw evidence access |
| Ordinary parking operator | No | No | No | Must not access raw UAT evidence |

Access must be least privilege and environment scoped.

## 16. Retention and Archival Posture

Recommended posture:

- Retain UAT evidence through certification/accreditation/release decision period.
- Retain longer if fiscal numbers were allocated.
- Exact retention period must be approved by compliance/accounting/legal.
- Archive superseded evidence but do not delete without approval.
- Keep evidence linked to run id and change approval.

Minimum recommended retention categories:

- non-fiscal dry-run evidence: retain through release decision plus agreed project audit period;
- fiscal-number allocated UAT evidence: retain per compliance/accounting/legal policy;
- rejected evidence: retain until defect/incident is closed and retention owner approves archival;
- superseded evidence: retain alongside replacement evidence.

## 17. Tamper-Evidence / Immutability Posture

Plan:

- Calculate checksum/hash for approved evidence JSON.
- Record hash in review/signoff document.
- Do not edit approved evidence in place.
- Create superseding evidence if correction is needed.
- Retain original and correction record.
- Record reviewer, approval timestamp, and storage location.

Suggested hash fields:

- algorithm, for example SHA-256;
- hash value;
- file name;
- run id;
- calculated at;
- calculated by.

Future automation should calculate and write these fields consistently.

## 18. Evidence Lifecycle

Lifecycle states:

1. `planned`
2. `generated`
3. `submitted`
4. `redaction_review`
5. `approved`
6. `rejected`
7. `superseded`
8. `archived`

Lifecycle rules:

- `planned`: run approved but evidence not generated.
- `generated`: exporter produced evidence JSON.
- `submitted`: evidence package is in approved review location.
- `redaction_review`: artifacts are being checked for sensitive data.
- `approved`: reviewers signed off and hash recorded.
- `rejected`: evidence cannot be used for release decision.
- `superseded`: replacement evidence exists and original remains retained.
- `archived`: evidence retained in final storage location.

## 19. Evidence Status Model

Evidence status values:

- `draft`
- `submitted_for_review`
- `redaction_required`
- `approved`
- `rejected`
- `superseded`
- `archived`

Status metadata:

- status;
- status reason;
- owner;
- timestamp;
- reviewer ref;
- related run id;
- related ticket/change ref.

## 20. Evidence Review Checklist

Reviewers must confirm:

- run id matches approved format;
- environment is expected;
- Site and Site POS Server are correct;
- approval refs are present;
- upstream finality ref is stable and approved;
- result status matches expected run type;
- fiscal document id/number is recorded when produced;
- Central PMS fiscal state matches POS Server evidence;
- idempotency/replay/conflict/unknown behavior is documented;
- payment finality unchanged;
- no ExitAuthorization issued by diagnostic path;
- no gate behavior triggered;
- sensitive data excluded or redacted;
- evidence hash recorded;
- rollback/cleanup status recorded.

## 21. Evidence Rejection Criteria

Reject evidence if:

- run id missing or not approved;
- approval reference missing;
- evidence is stored outside approved location;
- Site/Site POS Server mismatch exists;
- sensitive data is present and unresolved;
- raw logs/screenshots are broadly accessible;
- POS Server response is inconsistent and not reconciled;
- Central PMS record does not match POS Server evidence;
- payment finality was mutated by diagnostic path;
- ExitAuthorization was issued by diagnostic path;
- gate behavior was triggered by diagnostic path;
- fiscal gating enforcement was enabled;
- retry/readback automation ran unexpectedly;
- reviewer signoff is incomplete.

## 22. Incident / Escalation Handling

Escalate immediately if:

- sensitive data is found in evidence package;
- production fiscal number allocation was not approved;
- wrong environment or wrong Site was used;
- Site/Site POS Server mapping mismatch is discovered;
- unknown outcome occurs without approved readback/reconciliation plan;
- duplicate fiscal reference or fiscal document conflict is not explained;
- evidence indicates payment, ExitAuthorization, or gate side effect;
- evidence storage location was exposed to unauthorized users.

Incident package must include:

- run id;
- environment;
- Site/Site POS Server;
- evidence package location;
- issue description;
- impact assessment;
- owner;
- containment action;
- follow-up branch/task, if needed.

## 23. Audit and Traceability Requirements

Each evidence package must be traceable to:

- run id;
- change/ticket approval;
- UAT evidence template instance;
- operator runbook checklist;
- Central PMS evidence JSON schema version;
- POS Server API contract version;
- upstream finality reference;
- fiscal document id/number when produced;
- reviewer signoff;
- evidence hash after approval;
- archival location.

Traceability must support release decision review and future fiscal gating readiness review.

## 24. Future Automation / Tooling Implications

Future tooling should:

- write only to approved location;
- require run id;
- require approval ref;
- require evidence owner;
- require change/ticket ref;
- calculate hash;
- refuse sensitive markers;
- write redacted and non-redacted evidence separately if needed;
- never write secrets;
- never expose evidence publicly;
- never mutate payment/fiscal records as part of evidence export;
- never invoke POS Server merely to export evidence;
- never wire into payment confirmation or ExitAuthorization flows.

Before any file writer is implemented, decide whether saving evidence remains external/manual or becomes a controlled application-level helper.

## 25. Risks and Open Questions

Risks:

- manual evidence handling may be inconsistent without owner discipline;
- shared folder permissions may drift;
- raw logs/screenshots may contain sensitive data;
- ticket attachments may not satisfy long-term retention needs;
- fiscal-number allocated evidence may require stricter retention;
- future automation could accidentally become an invocation surface if boundaries are not enforced.

Open questions:

- official evidence repository owner;
- exact retention period;
- approved storage platform;
- hash algorithm and signing requirements;
- redaction owner role;
- whether unredacted evidence is allowed outside engineering;
- whether future evidence registry is required.

## 26. Requirements Traceability Summary

| Requirement area | Plan coverage |
| --- | --- |
| Current baseline | Section 3 |
| Authority boundaries | Section 4 |
| Non-goals | Section 5 |
| Governance objectives | Section 6 |
| Storage options | Section 7 |
| Recommended storage approach | Section 8 |
| Folder/naming convention | Section 9 |
| Metadata requirements | Section 10 |
| Approval linkage | Section 11 |
| Reviewer/signoff workflow | Section 12 |
| Redaction and sensitive data | Sections 13, 14 |
| Access control | Section 15 |
| Retention and archival | Section 16 |
| Tamper evidence | Section 17 |
| Lifecycle and status model | Sections 18, 19 |
| Review and rejection criteria | Sections 20, 21 |
| Incident/escalation | Section 22 |
| Audit/traceability | Section 23 |
| Future automation implications | Section 24 |
| Risks/open questions | Section 25 |

Recommended next task:

`feature/central-pms-pos-server-controlled-uat-harness-evidence-file-writer-planning`

Purpose: plan whether the application-level harness should later include an explicit file writer, or whether evidence saving should remain external/manual until CLI/tooling is approved.
