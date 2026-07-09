# ExitPass Central PMS POS Server Controlled UAT Data Assignment Blocker Closure Plan v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Data Assignment Blocker Closure Plan |
| Version | v1.0 |
| Date | 2026-07-09 |
| Branch | `docs/controlled-uat-data-assignment-blocker-closure-plan` |
| Scope | Documentation-only closure plan for controlled UAT data assignment blockers |
| Current readiness decision | `not_ready_for_execution` until blockers are closed |

This plan is documentation-only. It does not modify source code, schema, tests, configuration, runtime state, POS Server state, Central PMS state, HikCentral state, payment provider state, fiscal state, ExitAuthorization state, gate state, refund/reversal state, rendering behavior, evidence files, or UAT runbooks.

No UAT scenarios were run while preparing this plan. No Central PMS, POS Server, HikCentral, or payment provider runtime endpoints were called.

## 2. Purpose

This plan defines the practical closure path for the Controlled UAT Data Assignment blockers that kept the prior review decision at:

```text
not_ready_for_execution
```

The goal is to identify what must be filled, who must own it, what source of truth and evidence must exist, what value format is acceptable, and what gate must pass before controlled Central PMS to POS Server UAT execution can start.

This plan does not approve execution. It creates a checklist for closing assignment blockers and producing a new readiness review.

## 3. Current Readiness Decision

Current decision:

```text
not_ready_for_execution until blockers are closed
```

The assignment record and review showed required fields still as `TBD`, blank, `not_started`, `incomplete`, unapproved, or untraceable. Until all required fields are assigned, approved, and evidenced, the controlled UAT run must not proceed to execution.

## 4. Inputs And References

Reference documents:

| Reference | Path |
| --- | --- |
| Controlled UAT Data Assignment Record | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Record_v1.0.md` |
| Controlled UAT Data Assignment Review | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_Data_Assignment_Review_v1.0.md` |
| First Run Readiness Refresh | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Controlled_UAT_First_Run_Readiness_Refresh_v1.0.md` |
| Fiscal Issuance Controlled UAT Runbook | `docs/v1.3/central-pms/runbooks/ExitPass_Central_PMS_POS_Server_Fiscal_Issuance_Controlled_UAT_Runbook_v1.0.md` |
| Local Runtime Smoke Record | `docs/v1.3/operator-console/checkpoints/ExitPass_Central_PMS_Operator_Console_Local_Runtime_Smoke_Record_v1.0.md` |

The Controlled UAT Data Assignment Record remains the assignment source of truth. This closure plan defines what must be updated in that record and what evidence must be attached before a new readiness review can change the execution posture.

## 5. Closure Roles

Default role expectations:

| Role | Responsibility |
| --- | --- |
| UAT lead | Owns overall assignment completeness, sequencing, and final gate request. |
| Engineering lead | Owns Central PMS readiness, harness readiness, code/test evidence, and no-side-effect controls. |
| POS Server owner | Owns POS Server environment, fiscal identity, sequence policy/state, fiscal numbering risk, and POS Server availability evidence. |
| Site owner | Owns selected Site and Site POS Server mapping approval. |
| Operations lead | Owns approved run window, support coverage, stop criteria, and rollback readiness. |
| Evidence owner | Owns evidence folder/path, manual-save procedure, checksums, and evidence package traceability. |
| Privacy/compliance reviewer | Owns sensitive-data exclusion and any fiscal-numbering consequence acknowledgement. |
| Rollback/support owner | Must be reachable during any approved execution window and empowered to stop the run. |

Small-organization consolidated ownership is acceptable only when the assignment record explicitly names the same person in each required role and records the approval reference.

## 6. Blocker Closure Matrix

Status values for closure work:

- `open`
- `assigned_pending_evidence`
- `evidence_ready`
- `approved`
- `not_applicable_with_reason`
- `rejected`

No required blocker may remain `open`, `TBD`, blank, `not_started`, `incomplete`, or unapproved at the execution gate.

| Blocker category | Required field or artifact | Owner | Source of truth | Evidence required | Acceptable value format | Closure condition | Cannot-proceed impact |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Owner and approval blockers | UAT lead, engineering lead, POS Server owner, Central PMS owner, Site owner, operations lead, rollback/support owner, evidence owner, privacy/compliance observer, run approval reference, evidence save approval reference, fiscal number allocation approval if applicable | UAT lead | Assignment record owner/approval section | Named owners, dated approvals, approval reference, fiscal-numbering risk decision | Person or group name plus approval reference such as `DEV-UAT-CPS-POS-###` or project ticket/change id | Every required owner is named; approval references exist; fiscal-numbering consequence is accepted or marked not applicable with reason | No accountable owner exists for execution, stop decision, evidence, or fiscal-numbering risk |
| Environment blockers | Environment name, Central PMS environment, POS Server environment, database references, production/non-production decision, POS Server base URL reference, diagnostic window, rollback owner | UAT lead with engineering lead and POS Server owner | Assignment record environment section | Environment inventory, local/runtime smoke record, config screenshot or config file reference without secrets | Non-production environment label; URLs as `http://host:port`; date/time window with timezone; database names or environment aliases | Environment is explicitly non-production or production approval exists; Central PMS/POS Server/database references are filled; approved window exists | Wrong environment could allocate fiscal numbers or mutate non-UAT state |
| Site / Site POS Server assignment blockers | Site id/ref, Site name, Site group applicability, Site POS Server id/ref, Site POS Server environment/base URL, expected fiscal identity, fiscal sequence policy, fiscal sequence state, Site/POS/engineering approvals | Site owner and POS Server owner | Assignment record Site section | Site mapping evidence, POS Server mapping evidence, approval reference | Stable symbolic ref or GUID; site name; POS Server ref; URL reference; fiscal identity/policy/state refs | Site and Site POS Server are mapped to the approved POS Server fiscal configuration and owner approvals are attached | Fiscal request could target the wrong site or wrong fiscal numbering authority |
| POS Server fiscal configuration blockers | Fiscal identity id/ref, active/effective confirmation, fiscal sequence policy id/ref, policy active/effective confirmation, fiscal sequence state id/ref, state configured confirmation, document type, numbering consequence acceptance, idempotency/replay/conflict acknowledgements, GET readback posture, test/non-production sequence decision, POS Server signoff | POS Server owner | POS Server configuration record and assignment record | POS Server config snapshot or controlled seed reference, row existence evidence, owner signoff, fiscal numbering risk approval | GUIDs or approved symbolic refs; `sales_invoice` or approved fiscal document type; yes/no confirmations; not-applicable reason where needed | Fiscal identity, sequence policy, and sequence state are proven active/effective for the selected non-production site; numbering consequence accepted | UAT could fail at fiscal issuance or allocate from unapproved sequence |
| Central PMS configuration blockers | Fiscal reference persistence confirmation, harness availability, evidence exporter availability, manual-save procedure, `EnablePosServerFiscalIssuanceLiveCall`, `EnableControlledUatDiagnosticPath`, diagnostic window, payment/exit/fiscal-gating guards false, no retry/readback worker, no endpoint/CLI/tooling, engineering signoff | Engineering lead | Assignment record Central PMS config section | Build/test evidence, config reference without secrets, feature-flag snapshot, local runtime smoke record | Boolean values as `true`/`false`; run window with timezone; test command result references | Controlled diagnostic flags are assigned only for approved window; payment/exit/gating guards are explicitly false; harness and exporter evidence exists | Central PMS may not call the intended seam or may accidentally enable forbidden payment/exit/fiscal-gating behavior |
| HikCentral / Vendor PMS session source blockers | Vendor PMS/HikCentral source applicability, parking session source, approved test parking session reference, no-write posture, owner approval | Operations lead with Site owner | Assignment record transaction reference section and approved test data plan | Approved test parking/session fixture, source-system owner confirmation, no-write acknowledgement | Vendor/session refs as stable symbolic refs or GUIDs; applicability value `not_applicable` or named source | Session source is assigned and approved, or explicitly not applicable with reason; no HikCentral write behavior is confirmed | UAT cannot prove the fiscal request is tied to approved parking/session context |
| Payment/payable/reference blockers | Run id, correlation id, parking session ref, payment attempt ref, payment confirmation ref, payable basis ref, business day date, currency, amount minor units, expected run type, approval reference | UAT lead with engineering lead | Assignment record test transaction section | Approved test data plan, payable basis fixture, owner approval | Run id `CPS-POS-UAT-YYYYMMDD-...`; correlation GUID; refs as symbolic refs or GUIDs; currency ISO code; amount as integer minor units | All payment/payable references are filled with approved non-production values and match fiscal request totals | Fiscal request lacks traceable Central PMS payment/payable context |
| Upstream finality reference blockers | Upstream finality ref, pattern used, one semantic request confirmation, replay reuse posture if applicable, conflict bypass prohibition acknowledgement, assigned by, approved by, approval reference | Engineering lead | Assignment record upstream finality section | Idempotency key assignment record, semantic request summary, approval reference | Pattern `CPS-POS-UAT:<run-id>:<scenario>:<sequence>` or approved alternative; yes/no confirmations | Finality ref is unique/stable for first issuance, replay/conflict posture is explicitly approved or deferred, and conflict bypass is prohibited | Idempotency behavior cannot be interpreted and replay/conflict risk is uncontrolled |
| Fiscal request fact blockers | Fiscal document type, business day date, Site ref, Site POS Server ref, parking session ref, payment refs, payable basis ref, upstream finality ref, currency, amount minor units, line count, tender count, tax detail presence, totals presence, correlation id | Engineering lead | Assignment record fiscal request facts section | Fiscal request fact sheet, semantic hash/fact summary if available, approval reference | Structured field table; dates as `YYYY-MM-DD`; currency ISO code; counts and amount as integers; correlation GUID | Every required fiscal request fact is filled and matches the approved transaction references | POS Server request cannot be reconstructed, reviewed, or safely compared |
| Line/tender/tax/totals fact blockers | Line summary, line amount total, tender summary, tender amount total, tax detail summary, tax amount total, grand total, totals match payable basis, sensitive data excluded, approval reference | Engineering lead with privacy/compliance reviewer | Assignment record line/tender/tax/totals section | Totals reconciliation sheet, payable basis comparison, sensitive-data exclusion attestation | Amounts as integer minor units; counts as integers; yes/no totals match confirmation | Line, tender, tax, and grand totals reconcile exactly to payable basis and contain no sensitive data | Fiscal request may be rejected or produce unsafe/unreviewable evidence |
| Evidence folder/path blockers | Save mode, target location reference, evidence owner, checksum requirement, checksum command/reference, ticket/change linkage, reviewer signoff path, temporary local handling owner, approval reference | Evidence owner | Assignment record evidence section and evidence governance plan | Created folder/path, access check, checksum command, ticket/change id, reviewer path | Mode A or Mode B; path without secrets; checksum algorithm such as SHA-256; ticket/change id | Evidence path exists, owner can write/read, checksum procedure is known, and reviewer path is assigned | Evidence cannot be saved, reviewed, or traced after execution |
| Sensitive-data and privacy blockers | No PAN, CVV, tokens, credentials, secrets, raw callbacks, raw entitlement evidence, uncontrolled files/images, unmanaged PII, free-form sensitive blobs, unmasked plate/ticket unless approved | Privacy/compliance reviewer | Assignment record sensitive-data section | Sensitive-data checklist, sample evidence review, redaction/masking confirmation | Yes/no checklist; any exception must include explicit approval reference and masking rule | All exclusions pass or approved exceptions are documented with masking and owner signoff | Evidence could leak secrets, payment data, customer PII, or unmanaged statutory evidence |
| Rollback/stop criteria blockers | Stop criteria, rollback/support owner, owner availability window, forbidden side-effect checks, local DB safety, no production sequence, abort procedure, escalation contact | Operations lead and rollback/support owner | Assignment record pre-run validation and runbook stop criteria | Stop/abort checklist, contact reference, DB/environment safety evidence, side-effect query plan | Named owner; approved window; yes/no safety confirmations; side-effect check list | Stop criteria are written, owner is online for window, side-effect checks are defined, and production sequence is excluded or separately approved | Run cannot be safely stopped or recovered if unexpected fiscal/payment/exit behavior appears |

## 7. Required Closure Evidence Package

Before a new readiness review, the evidence package must contain or reference:

1. Updated Controlled UAT Data Assignment Record with no `TBD` placeholders in required execution fields.
2. Owner and approval table with named owners and approval references.
3. Environment and database assignment evidence, including non-production confirmation.
4. Site and Site POS Server mapping evidence.
5. POS Server fiscal identity, fiscal sequence policy, and fiscal sequence state evidence.
6. Central PMS configuration evidence showing controlled diagnostic flags and forbidden guards.
7. Approved parking/payment/payable/upstream finality references.
8. Fiscal request fact sheet with line, tender, tax, and totals reconciliation.
9. Evidence path creation proof and checksum procedure.
10. Sensitive-data exclusion checklist.
11. Stop/rollback criteria and available owner confirmation.
12. Updated readiness review decision.

Evidence may be a document path, ticket/change reference, screenshot reference, test output reference, or controlled local file path. Do not include secrets, credentials, raw provider payloads, raw POS Server request/response bodies, PAN, CVV, unmanaged customer PII, or raw statutory evidence.

## 8. Execution Gate

Controlled UAT execution must not start until all gate checks pass:

| Gate check | Required outcome |
| --- | --- |
| Required fields filled | Every required assignment field is filled with an approved value or explicit `not_applicable_with_reason`. |
| Owners assigned | UAT, engineering, POS Server, Site, operations, evidence, privacy/compliance, and rollback/support owners are named. |
| Approvals captured | Run approval, evidence save approval, fiscal numbering acceptance, and owner signoffs are recorded. |
| Evidence paths created | Evidence folder/path exists, is writable by the evidence owner, and has a checksum procedure. |
| Sensitive-data checks complete | Privacy checklist passes and any exception has explicit approval and masking instructions. |
| No TBD placeholders | No required field contains `TBD`, blank, `not_started`, `incomplete`, or unapproved values. |
| Environment safe | Environment is confirmed non-production or has explicit production approval, with no production fiscal sequence unless separately approved. |
| Runtime controls assigned | Central PMS controlled diagnostic flags and forbidden guards are assigned for the approved window. |
| Stop criteria ready | Stop/abort owner is available and forbidden side-effect checks are defined. |
| Readiness review updated | A new readiness review changes posture from `not_ready_for_execution` only if all closure evidence is accepted. |

Execution gate decision values:

- `blocked`: any required item is missing or unapproved.
- `ready_for_readiness_review`: assignment record is filled and evidence exists, but review has not completed.
- `ready_for_dry_run_checklist`: readiness review accepts the assignment package for dry-run checklist preparation.
- `ready_for_execution`: only a later execution-specific gate may assign this decision.

## 9. Explicit Non-Goals

This closure plan does not:

- execute UAT;
- call runtime endpoints;
- call Central PMS runtime endpoints;
- call POS Server runtime endpoints;
- call HikCentral runtime endpoints;
- call payment provider runtime endpoints;
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

## 10. Recommended Next Step

Recommended next step:

1. Fill the Controlled UAT Data Assignment Record using real project values, named owners, approval references, and evidence paths.
2. Attach the evidence package defined in this plan.
3. Run a new data assignment review.
4. Run a refreshed first-run readiness review.
5. Proceed only if the new review confirms no required placeholders remain and the gate advances to the next allowed readiness state.

Do not execute controlled UAT until the assignment record and refreshed readiness review explicitly clear the blockers.
