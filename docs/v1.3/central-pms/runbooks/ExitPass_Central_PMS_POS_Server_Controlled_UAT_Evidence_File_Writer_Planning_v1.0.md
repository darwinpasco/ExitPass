# ExitPass Central PMS POS Server Controlled UAT Evidence File Writer Planning v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS POS Server Controlled UAT Evidence File Writer Planning |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-harness-evidence-file-writer-planning |
| Scope | Planning for future controlled UAT evidence file-writing strategy |
| Status | Planning only; no implementation |

## 2. Purpose and Scope

This plan decides whether the Central PMS controlled POS Server UAT harness should later include an explicit evidence file writer, or whether evidence saving should remain external/manual until CLI, endpoint, or operator tooling is approved.

The plan closes the gap between:

- controlled POS Server fiscal issuance UAT harness execution;
- safe evidence JSON export;
- UAT evidence template requirements;
- evidence retention and governance requirements.

This document defines the safety rules for any future file-writing implementation, including output directory controls, naming conventions, overwrite prevention, hash/checksum behavior, redaction gates, approval/run-id requirements, and audit traceability.

This document does not implement file writing and does not authorize file writing by itself.

## 3. Current Implementation Baseline

Current Central PMS implementation and documentation baseline includes:

- controlled UAT operator runbook;
- controlled UAT evidence template;
- controlled UAT harness planning;
- evidence retention/governance plan;
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

The application-level exporter currently produces a safe structured evidence model and serialized JSON. It does not write evidence to disk, call POS Server, mutate payment finality, issue ExitAuthorization, or trigger gate behavior.

## 4. Authority Boundaries

The file-writing strategy must preserve these authority boundaries:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.

Evidence storage and file writing must not create a new operational authority. A saved evidence file is an audit artifact only. It is not payment finality, fiscal issuance execution, ExitAuthorization, gate permission, or manual release approval.

## 5. Non-Goals

This planning task does not:

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

## 6. File-Writing Problem Statement

The controlled UAT harness can produce structured evidence JSON, but evidence saving is still manual. Before any code writes files automatically, the project must decide:

- whether application code should write evidence at all;
- where evidence can be written;
- who owns the target evidence location;
- how the writer prevents accidental overwrite or path traversal;
- how hashes are calculated and recorded;
- how redaction is handled before evidence becomes official;
- how approval references, run IDs, and reviewer signoff are linked;
- how evidence remains separate from production payment, fiscal, exit, and gate behavior.

The primary risk is that file-writing automation may accidentally store sensitive data, write into the source repository, overwrite approved evidence, weaken approval traceability, or create a false impression that diagnostic evidence is a production operational action.

## 7. Options Considered

The following evidence-save options were considered:

- Option A: no writer / manual external save.
- Option B: application-level explicit file writer.
- Option C: CLI writer.
- Option D: endpoint/tooling-managed writer.
- Option E: future evidence registry writer.

Each option is evaluated for safety, governance readiness, operational usability, implementation complexity, access control, and suitability before production flow integration.

## 8. Option A: No Writer / Manual External Save

Description:

- The exporter continues returning a safe evidence object and JSON string.
- An approved actor manually saves the JSON to the approved evidence repository.
- The evidence package is linked to the run ID, approval reference, and change/ticket record.
- No Central PMS source code writes files.

Advantages:

- Safest immediate posture.
- No file system risk in Central PMS application code.
- No path traversal, overwrite, or repository pollution risk from application behavior.
- Easiest to govern before automation.
- Keeps storage responsibility with the approved UAT/evidence owner.

Limitations:

- Slower and more manual.
- Requires disciplined evidence naming and placement.
- Manual copy/save can introduce human error.
- Hash calculation and review package assembly remain external until a future controlled process exists.

Planning posture:

- Recommended immediate posture until writer controls are approved.

## 9. Option B: Application-Level Explicit File Writer

Description:

- Add a future application-level helper that writes evidence JSON to an approved folder.
- The writer is invoked explicitly by controlled UAT harness code, not by payment or ExitAuthorization flows.
- The writer remains disabled unless approved configuration and request metadata are present.

Required controls:

- Strict configured output root allow-list.
- Per-run subdirectory requirement.
- Path traversal rejection.
- Overwrite prevention.
- SHA-256 hash generation.
- Redaction gate.
- Required run ID, approval reference, evidence owner, and output location.
- No default output directory.
- No writing to production operational paths.

Advantages:

- Reduces manual evidence handling mistakes.
- Supports repeatable UAT evidence package generation.
- Can enforce naming and hash rules consistently.
- Can integrate cleanly with existing application-level harness and exporter.

Risks:

- Adds file system behavior to application code.
- Requires precise output-path governance.
- Could be misused if enabled in an uncontrolled environment.
- Must not write inside the source repository except an explicitly gitignored local dry-run folder.

Planning posture:

- Viable later, only after approval checklist completion and storage governance finalization.

## 10. Option C: CLI Writer

Description:

- Add a future internal command-line invocation that runs evidence save behavior outside the normal API surface.
- The CLI accepts approved UAT input, calls the application-level harness/exporter, and writes evidence to an approved location.

Required controls:

- Explicit operator invocation.
- Required run ID and approval reference.
- Required output root configuration.
- Environment and Site scope controls.
- Sensitive marker rejection.
- No secrets in command arguments or source-controlled config.
- Hash/checksum output.

Advantages:

- Good fit for controlled UAT workflows.
- Easier to isolate from production API traffic.
- Easier to bind to an approved operator workstation or UAT environment.
- Supports evidence folder creation and hash calculation with clear operator intent.

Risks:

- Risk if distributed broadly.
- Requires CLI packaging, access controls, and operational instructions.
- Still needs output path, redaction, and approval governance.

Planning posture:

- Candidate after application-level file writer controls are approved or instead of Option B if UAT prefers command-driven evidence handling.

## 11. Option D: Endpoint/Tooling-Managed Writer

Description:

- Add a future internal API endpoint or operator tooling surface that runs the diagnostic seam and writes evidence.

Required controls:

- Strong authentication and authorization.
- Role-based access.
- Approval/run ID validation.
- Endpoint disabled by default.
- Diagnostic guard enabled only for approved windows.
- Audit event for invocation and evidence write.
- Public exposure prevention.

Advantages:

- Easier remote UAT invocation.
- Can integrate with future operations workflow.
- Can centralize audit and approvals if tooling is mature.

Risks:

- Higher security risk than application-level or CLI-only approaches.
- Requires settled endpoint authorization conventions.
- Risk of accidental fiscal number allocation if exposed incorrectly.
- Not appropriate before the endpoint/tooling strategy is approved.

Planning posture:

- Not recommended now.

## 12. Option E: Future Evidence Registry Writer

Description:

- Add a future registry-backed writer that stores evidence metadata, file references, hashes, approvals, and review status in a governed evidence registry.

Advantages:

- Strongest traceability later.
- Can support evidence lifecycle, review status, and audit reporting.
- Can integrate with future Operator Console or Management Dashboard visibility if approved.

Risks:

- Requires DB, API, UI, permissions, retention, and backup decisions.
- Requires governance for sensitive or unredacted evidence.
- Not appropriate before Operator Console/Dashboard evidence workflow exists.

Planning posture:

- Future candidate only.

## 13. Comparison Matrix

| Option | Safety now | Complexity | Access control need | Overwrite/path risk | Evidence consistency | Recommended timing |
| --- | --- | --- | --- | --- | --- | --- |
| A: Manual external save | Highest | Low | Existing repository/ticket permissions | Low from application; manual process risk remains | Medium | Immediate |
| B: Application-level writer | Medium/high with controls | Medium | Config and approved UAT actor controls | Medium unless allow-list and no-overwrite rules exist | High | After approval checklist |
| C: CLI writer | Medium/high with controls | Medium | Operator/workstation and config controls | Medium unless allow-list and no-overwrite rules exist | High | After Option A governance is stable |
| D: Endpoint/tooling writer | Medium/low before auth is settled | High | Strong role/auth/audit required | Medium | High | Later, after endpoint/tooling approval |
| E: Evidence registry writer | High when mature | High | Full role, retention, and registry governance | Low if designed correctly | Highest | Future platform capability |

## 14. Recommended Approach

Immediate recommendation:

- Keep evidence saving manual/external using an approved shared evidence repository or document management location.
- Continue using the application-level evidence exporter to generate safe JSON.
- Require approved actor review before evidence is saved as official UAT evidence.
- Link saved evidence to a ticket/change reference and approval record.
- Do not implement a file writer yet.

Next implementation, only after approval:

- Consider an application-level explicit file writer or CLI writer.
- Require strict path, overwrite, hash, redaction, approval, and access controls.
- Keep any writer explicit and disabled by default.
- Keep any writer unreachable from payment confirmation, ExitAuthorization, fiscal gating enforcement, retry, readback, Operator Console, or Dashboard paths.

Not recommended yet:

- Endpoint/tooling-managed writer.
- Evidence registry writer.

A file writer should not be built until all of the following are complete:

- evidence repository owner assigned;
- approved output location selected;
- retention period approved;
- redaction owner assigned;
- hash/signature requirements finalized;
- run ID naming convention approved;
- access control owners identified.

## 15. Output Directory Rules

Any future file writer must follow these output directory rules:

- Use only a configured allow-listed root directory.
- Reject relative path traversal, including `..`, mixed separators, encoded traversal, and symlink-like redirection where detectable.
- Reject writing inside the source repository unless explicitly configured for a gitignored local evidence folder used only for local dry-run validation.
- Reject system, temp, user-profile, desktop, downloads, or default directories unless explicitly configured for local test mode.
- Require a per-run subdirectory under the allow-listed root.
- Separate redacted and unredacted evidence if both exist.
- Never write secrets, tokens, credentials, PAN/CVV, raw provider callback payloads, raw entitlement evidence, uncontrolled images/files, unmanaged customer PII, or free-form sensitive blobs.
- Never infer output location from request text fields.
- Never write to POS Server runtime folders.

Recommended folder pattern:

```text
ExitPass-CentralPMS-PosServer-UAT/
  <environment>/
    <site-ref>/
      <yyyy-mm-dd>/
        <run-id>/
          controlled-posserver-fiscal-uat-<run-id>-evidence.json
          controlled-posserver-fiscal-uat-<run-id>-review.md
          controlled-posserver-fiscal-uat-<run-id>-redaction.md
          controlled-posserver-fiscal-uat-<run-id>-hash.txt
```

## 16. File Naming Convention

Run ID format:

```text
CPS-POS-UAT-YYYYMMDD-<site>-<sequence>
```

File names:

- `controlled-posserver-fiscal-uat-<run-id>-evidence.json`
- `controlled-posserver-fiscal-uat-<run-id>-review.md`
- `controlled-posserver-fiscal-uat-<run-id>-redaction.md`
- `controlled-posserver-fiscal-uat-<run-id>-hash.txt`

Revision naming for corrections:

- `controlled-posserver-fiscal-uat-<run-id>-evidence-r02.json`
- `controlled-posserver-fiscal-uat-<run-id>-review-r02.md`
- `controlled-posserver-fiscal-uat-<run-id>-redaction-r02.md`
- `controlled-posserver-fiscal-uat-<run-id>-hash-r02.txt`

Rules:

- File names must be derived from normalized run ID, not arbitrary user text.
- Environment and Site should be represented in the folder path, not duplicated through uncontrolled free text in file names.
- File names must be ASCII-safe.
- File extensions must be fixed and expected.

## 17. Overwrite Prevention Rules

Any future file writer must:

- fail if the target evidence file already exists;
- fail if the target review file already exists;
- fail if the target hash file already exists;
- never overwrite by default;
- never append to approved evidence in place;
- require corrections to use a superseding file with a revision suffix;
- retain superseded evidence alongside replacement evidence;
- record supersession reason and approver;
- preserve the original hash and replacement hash separately.

Approved evidence must not be edited in place. If a correction is needed, create a new revision and retain the original as superseded evidence.

## 18. Hash/Checksum Rules

Any future file writer must:

- compute a SHA-256 hash of the evidence JSON after writing;
- calculate the hash over the exact saved bytes;
- record hash algorithm, hash value, file name, run ID, timestamp, and writer version in the hash file;
- record the hash in the review/signoff document;
- never reuse a hash after evidence content changes;
- hash original and redacted evidence separately if both exist;
- fail if hash computation fails;
- treat missing hash as incomplete evidence.

Recommended hash file content:

```text
runId: CPS-POS-UAT-YYYYMMDD-<site>-<sequence>
file: controlled-posserver-fiscal-uat-<run-id>-evidence.json
algorithm: SHA-256
hash: <hex>
generatedAtUtc: <timestamp>
writer: <component/version>
```

## 19. Redaction Gate Rules

Any future file writer must:

- refuse export if the evidence exporter reports sensitive markers;
- refuse export when evidence status is `rejected_sensitive_metadata`;
- mark redaction required when notes or metadata require human review;
- never write unredacted logs/screenshots automatically;
- never write raw provider callback payloads, raw entitlement evidence, secrets, tokens, credentials, PAN/CVV, unmanaged customer PII, or uncontrolled images/files;
- require redaction owner signoff before evidence can be marked approved;
- keep unredacted evidence, if any, in restricted storage only;
- allow redacted evidence to be attached to broader review packages only after redaction signoff.

Redaction status must be explicit. Absence of detected sensitive markers is not the same as final redaction approval for attachments outside the core JSON evidence file.

## 20. Approval/Run-ID Requirements

Any future file writer must require:

- run ID;
- approval reference;
- evidence owner;
- UAT lead;
- environment;
- Site;
- Site POS Server;
- output location;
- correlation ID;
- final status;
- reviewer signoff later.

The writer must reject execution if required approval or run identity metadata is missing.

The writer must not allow arbitrary generated run IDs for real UAT evidence unless the run ID format and sequence owner are approved.

## 21. Access Control Expectations

Future writer access must be limited to approved engineering/UAT actors. It must not be available to ordinary parking operators, terminal users, public API clients, or unsupported support roles.

Expected role posture:

| Role | Future writer access | Notes |
| --- | --- | --- |
| Engineering lead | Approve / limited execute | Owns technical readiness |
| Central PMS developer/operator | Limited execute | Requires approval and run ID |
| POS Server owner | Review / approve | Confirms POS Server evidence posture |
| UAT lead | Approve / review | Owns UAT evidence completeness |
| Operations lead | Review / approve | Confirms operational safety |
| Compliance/accounting observer | Review | Required when fiscal numbers may be allocated |
| Support/helpdesk | No direct writer access | May receive redacted summary only |
| Ordinary parking operator | No access | Must not invoke or save controlled UAT evidence |

## 22. Retention/Governance Alignment

Future file writing must align with the controlled UAT evidence retention and governance plan:

- evidence must be stored only in approved locations;
- evidence must be linked to run ID and approval reference;
- retention period must be approved by compliance/accounting/legal where applicable;
- fiscal-number allocated evidence may require longer retention;
- superseded evidence must remain retained;
- approved evidence must include hash/signoff;
- rejected evidence must record rejection reason and owner;
- archived evidence must remain traceable to the run and approval record.

The writer must not delete, modify, or purge evidence as part of normal operation. Cleanup and archival are governance actions, not automatic writer side effects.

## 23. Error/Abort Behavior

Any future file writer should abort without writing if:

- output root is missing;
- output root is not allow-listed;
- target file exists;
- target path is outside the approved root after normalization;
- run ID is missing or invalid;
- approval reference is missing;
- evidence owner is missing;
- sensitive marker is detected;
- evidence JSON is invalid;
- hash computation fails;
- disk write fails;
- redaction status rejects export;
- payment/exit flow mutation is detected in evidence;
- diagnostic result indicates unmanaged sensitive evidence risk;
- writer cannot confirm no endpoint/CLI/payment/exit/fiscal gating side effect for the current mode.

Abort results must be explicit and audit-friendly. A failed write must not trigger POS Server calls, payment mutations, ExitAuthorization, retry, readback, or gate behavior.

## 24. Test Planning for Future Writer

Before any writer implementation is accepted, tests should cover:

- valid evidence writes to allowed directory;
- disallowed path rejected;
- relative path traversal rejected;
- encoded or mixed-separator traversal rejected where applicable;
- source repository write rejected unless local ignored evidence folder mode is explicitly enabled;
- existing file not overwritten;
- revision file accepted only with approved supersession metadata;
- SHA-256 hash generated over exact saved bytes;
- hash file includes algorithm, file name, run ID, and timestamp;
- sensitive evidence rejected;
- redaction-required evidence does not become approved automatically;
- redacted and unredacted naming separated;
- missing run ID rejected;
- missing approval reference rejected;
- missing evidence owner rejected;
- no endpoint required;
- no CLI required unless implementing Option C;
- no payment confirmation behavior changed;
- no ExitAuthorization behavior changed;
- no POS Server call made by file writing itself;
- no retry scheduler/readback worker introduced.

## 25. Risks and Open Questions

Risks:

- Manual saving may remain inconsistent until a controlled writer exists.
- A premature writer may write to the wrong location.
- File automation may accidentally weaken redaction governance.
- Writer distribution may expand beyond approved UAT actors.
- Hash/signoff may become disconnected if review files are maintained manually.
- Evidence retention requirements may differ when real fiscal numbers are allocated.

Open questions:

- Who is the official evidence repository owner?
- Which storage location is approved for first controlled UAT evidence?
- What is the exact retention period for fiscal-number allocated UAT evidence?
- Is SHA-256 sufficient, or is a signature/attestation required?
- Who is the redaction owner?
- Are unredacted evidence files allowed outside engineering-controlled storage?
- Should the first writer be application-level or CLI-based?
- Should local dry-run evidence folders be allowed, and if so, where are they gitignored?

## 26. Requirements Traceability Summary

| Requirement | Covered by |
| --- | --- |
| Plan writer vs manual evidence saving | Sections 6-14 |
| Preserve authority boundaries | Section 4 |
| Preserve non-goals | Section 5 |
| Compare writer options | Sections 7-13 |
| Recommend immediate posture | Section 14 |
| Define output directory controls | Section 15 |
| Define naming convention | Section 16 |
| Prevent overwrites | Section 17 |
| Define hash/checksum behavior | Section 18 |
| Define redaction gates | Section 19 |
| Require approval/run ID | Section 20 |
| Define access expectations | Section 21 |
| Align retention/governance | Section 22 |
| Define abort behavior | Section 23 |
| Plan future tests | Section 24 |
| Track risks/open questions | Section 25 |

## Recommended Next Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-evidence-writer-approval-checklist`

Purpose: define the approval checklist that must be completed before implementing any evidence file writer.
