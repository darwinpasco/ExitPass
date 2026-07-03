# ExitPass Central PMS POS Server Controlled UAT Approved Test Data Plan v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | Central PMS POS Server Controlled UAT Approved Test Data Plan |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-approved-test-data-plan |
| Scope | Approved test data planning for first controlled POS Server fiscal issuance diagnostic execution |
| Status | Planning only; no UAT execution |

## 2. Purpose and Scope

This plan defines the approved test data, Site/Site POS Server values, upstream finality references, and safe fiscal request facts required for the first controlled Central PMS to POS Server fiscal issuance diagnostic execution.

This plan closes the gap between:

- controlled UAT harness implementation;
- evidence export;
- manual-save procedure;
- evidence governance;
- first actual controlled UAT diagnostic run.

The plan defines what data may be used, what data must not be used, how upstream finality references must be formed, which Site/Site POS Server mapping must be confirmed, and which fiscal request facts are safe for controlled diagnostic execution.

This plan does not execute UAT and does not approve live execution by itself.

## 3. Current Implementation Baseline

Current Central PMS implementation and documentation baseline includes:

- controlled UAT operator runbook;
- controlled UAT evidence template;
- controlled UAT harness planning;
- controlled UAT manual-save procedure;
- application-level controlled UAT harness;
- safe evidence JSON exporter;
- disabled/default-safe POS Server live-call seam;
- controlled diagnostic seam;
- no endpoint;
- no CLI/tooling;
- no automatic file-writing;
- no payment confirmation wiring;
- no ExitAuthorization wiring;
- no fiscal gating enforcement;
- no retry scheduler;
- no GET readback worker.

## 4. Authority Boundaries

This test data plan preserves these authority boundaries:

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- UAT evidence and test data are audit artifacts only and do not create operational authority.

Approved test data must not be interpreted as authority to mutate real payment state, issue ExitAuthorization, open a gate, or bypass fiscal exception handling.

## 5. Non-Goals

This task does not:

- execute UAT;
- execute live POS Server calls;
- create real fiscal documents;
- add endpoint/tooling;
- implement file-writing;
- enable payment/exit production flow;
- issue ExitAuthorization;
- enforce fiscal gating;
- implement retry;
- implement GET readback worker;
- implement Operator Console queue;
- implement Dashboard projection;
- modify source code;
- modify SQL;
- modify POS Server runtime.

## 6. Approved Test Data Posture

Approved posture:

- no uncontrolled production customer data;
- no raw provider callback payloads;
- no PAN/CVV/tokens/secrets;
- no unmanaged PII;
- no raw entitlement evidence;
- no uncontrolled images/files;
- no arbitrary free-text sensitive notes;
- test data must be approved by UAT lead and engineering lead;
- production fiscal sequence use requires explicit POS Server owner and compliance/accounting approval;
- non-production fiscal sequence/test policy is preferred.

If any required data is not approved, the first controlled UAT diagnostic execution must not proceed.

## 7. Test Environment Requirements

| Requirement | Planned value | Approval / evidence |
| --- | --- | --- |
| Environment name | TBD | TBD |
| Central PMS base environment | TBD | TBD |
| POS Server base environment | TBD | TBD |
| Database/environment reference | TBD | TBD |
| Production or non-production | TBD; non-production preferred | TBD |
| POS Server Base URL reference | TBD; reference only, no secrets | TBD |
| Diagnostic config enabled window | TBD | TBD |
| Evidence save mode | Mode A official / Mode B temporary | TBD |
| Rollback/support owner | TBD | TBD |
| Run approval reference | TBD | TBD |

Rules:

- Do not include secrets or credentials in this plan.
- Do not run against production unless pilot Site, fiscal-number allocation consequence, and compliance/accounting approval are explicitly recorded.
- Diagnostic config must be enabled only for the approved execution window.

## 8. Site / Site POS Server Approval Requirements

| Requirement | Planned value | Approval / evidence |
| --- | --- | --- |
| Site id/ref | TBD | TBD |
| Site name | TBD | TBD |
| Site group, if applicable | TBD | reporting only |
| Site POS Server id/ref | TBD | TBD |
| Site POS Server environment | TBD | TBD |
| Site POS Server base URL reference | TBD; reference only, no secrets | TBD |
| Fiscal identity expected | TBD | TBD |
| Fiscal sequence policy expected | TBD | TBD |
| Fiscal sequence state expected | TBD | TBD |
| Approved by Site owner | TBD | TBD |
| Approved by POS Server owner | TBD | TBD |
| Approved by engineering lead | TBD | TBD |

Rules:

- Site Group is not fiscal authority.
- Site/Site POS Server mapping must be explicit.
- The selected Site POS Server must match the POS Server fiscal identity/policy/sequence configuration used by the request.

## 9. POS Server Fiscal Configuration Requirements

Confirm before execution:

| Requirement | Status | Evidence/reference |
| --- | --- | --- |
| Fiscal identity active/effective | TBD | TBD |
| Fiscal sequence policy active/effective | TBD | TBD |
| Fiscal sequence state configured | TBD | TBD |
| Fiscal document type supported | TBD | TBD |
| Fiscal numbering consequence understood | TBD | TBD |
| Idempotency behavior understood | TBD | TBD |
| Replay behavior understood | TBD | TBD |
| Conflict behavior understood | TBD | TBD |
| GET readback available for manual verification if needed | TBD | TBD |
| Test/non-production sequence preferred | TBD | TBD |
| Production sequence explicit approval, if applicable | TBD | TBD |

Production fiscal sequence use is not acceptable without explicit POS Server owner and compliance/accounting approval.

## 10. Central PMS Fiscal Reference Requirements

Confirm before execution:

| Requirement | Status | Evidence/reference |
| --- | --- | --- |
| Fiscal reference persistence patch applied | TBD | TBD |
| Repository/harness tests passed | TBD | TBD |
| Controlled UAT harness available | TBD | TBD |
| Evidence exporter available | TBD | TBD |
| Manual-save procedure available | yes | This document set |
| Payment/exit flow guard remains false | TBD | TBD |
| Fiscal gating enforcement remains false | TBD | TBD |
| No retry/readback worker present | TBD | TBD |
| No endpoint/CLI/tooling present | TBD | TBD |

Central PMS must not use payment confirmation or ExitAuthorization production flows to invoke POS Server for this UAT diagnostic.

## 11. Test Transaction Data Requirements

Required safe data:

| Field | Requirement | Planned value |
| --- | --- | --- |
| Run id | assigned and unique | TBD |
| Correlation id | assigned and unique | TBD |
| Environment name | matches approved environment | TBD |
| Evidence owner | approved actor | TBD |
| Approval reference | UAT approval reference | TBD |
| Site ref | approved Site | TBD |
| Site POS Server ref | approved Site POS Server | TBD |
| Parking session ref | approved test/synthetic ref | TBD |
| Payment attempt ref | approved test/synthetic ref | TBD |
| Payment confirmation ref | approved test/synthetic ref | TBD |
| Payable basis ref | approved test/synthetic ref | TBD |
| Upstream finality ref | stable idempotency source | TBD |
| Business day date | approved UAT business date | TBD |
| Currency code | `PHP` unless explicitly approved otherwise | PHP |
| Amount minor units | low-risk approved test amount | TBD |
| Line summary | synthetic/approved line facts | TBD |
| Tender summary | safe test tender facts | TBD |
| Tax/totals summary | matches payable basis | TBD |
| Expected run type | newly_created / idempotent_replay / conflict / failure / unknown | TBD |

## 12. Parking Session Reference Requirements

Requirements:

- test parking session ref is approved;
- no uncontrolled real customer data;
- ticket/plate data masked or synthetic;
- session is suitable for fiscal issuance diagnostic;
- session will not trigger real gate behavior;
- session will not be used for production ExitAuthorization.

| Field | Planned value | Approval/reference |
| --- | --- | --- |
| Parking session ref | TBD | TBD |
| Synthetic or masked | TBD | TBD |
| Gate behavior disabled/irrelevant | TBD | TBD |
| Production ExitAuthorization excluded | yes | TBD |

## 13. Payment Attempt Reference Requirements

Requirements:

- payment attempt ref is approved;
- data does not mutate real payment finality;
- diagnostic does not create provider transaction;
- no payment reversal/refund side effects;
- no ExitAuthorization side effects.

| Field | Planned value | Approval/reference |
| --- | --- | --- |
| Payment attempt ref | TBD | TBD |
| Synthetic/test source | TBD | TBD |
| No provider transaction mutation | TBD | TBD |
| No reversal/refund side effect | TBD | TBD |

## 14. Payment Confirmation Reference Requirements

Requirements:

- payment confirmation ref is approved;
- reference is safe for fiscal diagnostic evidence;
- no production payment confirmation status is mutated;
- no ExitAuthorization is issued by the diagnostic path.

| Field | Planned value | Approval/reference |
| --- | --- | --- |
| Payment confirmation ref | TBD | TBD |
| Payment finality remains unchanged | TBD | TBD |
| No ExitAuthorization side effect | yes | TBD |

## 15. Payable Basis Reference Requirements

Requirements:

- payable basis ref is approved;
- payable facts are stable for the run;
- amount, lines, tenders, taxes, and totals are internally consistent;
- payable basis must not contain raw provider payloads or unmanaged customer PII.

| Field | Planned value | Approval/reference |
| --- | --- | --- |
| Payable basis ref | TBD | TBD |
| Stable facts approved | TBD | TBD |
| Amount/tax/totals consistent | TBD | TBD |
| Sensitive payload excluded | TBD | TBD |

## 16. Upstream Finality Reference Rules

Strict rules:

- upstream finality ref must be stable;
- one semantic request per upstream finality ref;
- replay uses same upstream finality ref and same semantic facts;
- conflict test uses same upstream finality ref with intentionally different semantic facts only if separately approved;
- do not create new upstream finality ref to bypass a conflict;
- do not reuse upstream finality ref across unrelated runs;
- include run id in upstream finality ref pattern only if approved.

Suggested pattern:

```text
CPS-POS-UAT:<run-id>:<scenario>:<sequence>
```

| Scenario | Upstream finality rule | Approval required |
| --- | --- | --- |
| newly_created | new stable upstream finality ref for new semantic request | UAT lead / engineering lead |
| idempotent_replay | same upstream finality ref and same semantic facts as original | UAT lead / engineering lead |
| conflict | same upstream finality ref with changed semantic facts | UAT lead / engineering lead / POS Server owner |
| failure | stable upstream finality ref matching the attempted request | UAT lead / engineering lead |
| unknown | preserve upstream finality ref for reconciliation | UAT lead / engineering lead / POS Server owner |

## 17. Fiscal Document Request Facts

Safe facts needed:

| Request fact | Requirement | Planned value |
| --- | --- | --- |
| Fiscal document type | supported by POS Server fiscal configuration | TBD |
| Business day date | approved UAT date | TBD |
| Site ref | approved Site | TBD |
| Site POS Server ref | approved Site POS Server | TBD |
| Parking session ref | approved test/synthetic ref | TBD |
| Payment attempt ref | approved test/synthetic ref | TBD |
| Payment confirmation ref | approved test/synthetic ref | TBD |
| Payable basis ref | approved test/synthetic ref | TBD |
| Upstream finality ref | stable idempotency source | TBD |
| Currency | PHP unless explicitly approved otherwise | PHP |
| Amount minor units | positive low-risk test amount | TBD |
| Line count | at least one line | TBD |
| Tender count | at least one tender | TBD |
| Tax detail present | yes/no, must match payable basis | TBD |
| Totals present | yes | TBD |
| Correlation id | assigned | TBD |

## 18. Line / Tender / Tax / Totals Requirements

Line facts:

- synthetic or approved test facts only;
- no arbitrary sensitive free text;
- amount must be positive where required by harness validation;
- currency must be present and should be `PHP`.

Tender facts:

- safe test tender only;
- no card PAN;
- no wallet token;
- no provider secret;
- no customer account data;
- no raw payment provider payload.

Tax/totals facts:

- must match payable basis;
- must be internally consistent with line and tender totals;
- use low-risk test amount where possible.

| Fact group | Requirement | Planned value |
| --- | --- | --- |
| Lines | synthetic/approved, positive amount, currency present | TBD |
| Tenders | safe test tender, positive amount, currency present | TBD |
| Tax details | match payable basis | TBD |
| Totals | match line/tender/tax facts | TBD |

## 19. Idempotency and Replay Test Data Rules

Newly-created scenario:

- use a new stable upstream finality ref;
- use approved semantic request facts;
- expect a new POS Server fiscal document if live diagnostic execution is approved.

Replay scenario:

- use the same upstream finality ref as original;
- use the same semantic request facts;
- expect same fiscal document id/number;
- expect no duplicate Central PMS fiscal reference;
- expect no sequence advancement on replay if POS Server supports idempotent replay.

Do not change request facts under the same upstream finality ref unless executing an approved conflict scenario.

## 20. Conflict Test Data Rules

Conflict tests are higher risk and must be separately approved.

Rules:

- use same upstream finality ref with intentionally different semantic facts;
- expected outcome is POS Server conflict and Central PMS conflict mapping;
- do not resolve by creating a new upstream finality ref;
- do not proceed to ExitAuthorization;
- capture conflict evidence and route to review.

Required approvals:

- UAT lead;
- engineering lead;
- POS Server owner;
- compliance/accounting observer if fiscal sequence risk exists.

## 21. Failure and Unknown Outcome Test Data Rules

Failure simulations:

- prefer mocked/non-production environment;
- use stable upstream finality ref;
- expect fail-closed mapping;
- do not treat failure as payment failure;
- do not issue ExitAuthorization.

Unknown outcome live tests:

- require readback/reconciliation plan before execution;
- preserve upstream finality ref;
- do not assume success;
- do not issue ExitAuthorization based on unknown;
- capture evidence and abort/reconcile.

Unknown/failure live scenarios should not be first-run scenarios unless explicitly approved by engineering lead and POS Server owner.

## 22. Sensitive Data Exclusion Rules

Prohibited:

- PAN;
- CVV;
- tokens;
- credentials;
- secrets;
- raw provider callback payloads;
- raw entitlement evidence;
- uncontrolled images/files;
- unmanaged customer PII;
- free-form sensitive blobs;
- unmasked plate/ticket if not explicitly approved.

If prohibited data appears, abort the run preparation and route to evidence/redaction owner before any diagnostic execution.

## 23. Approved Data Table Template

| Scenario id | Run id | Expected run type | Environment | Site ref | Site POS Server ref | Parking session ref | Payment attempt ref | Payment confirmation ref | Payable basis ref | Upstream finality ref | Amount minor units | Currency | Line/tender summary | Approved by | Approval reference | Sensitive-data check | Status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TBD | TBD | newly_created / idempotent_replay / conflict / failure / unknown | TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | PHP | TBD | TBD | TBD | pending | pending_data_assignment | TBD |

Status values:

- `pending_data_assignment`
- `pending_approval`
- `approved_for_controlled_uat`
- `rejected`
- `deferred`
- `not_ready_for_execution`

## 24. First UAT Run Candidate Data Record

The first UAT run candidate data is intentionally placeholder-only. Actual IDs must be assigned by approved owners.

| Field | Candidate value | Status |
| --- | --- | --- |
| Scenario id | TBD | pending_data_assignment |
| Run id | TBD | pending_data_assignment |
| Expected run type | newly_created | pending_approval |
| Environment | TBD | pending_data_assignment |
| Site ref | TBD | pending_data_assignment |
| Site POS Server ref | TBD | pending_data_assignment |
| Parking session ref | TBD | pending_data_assignment |
| Payment attempt ref | TBD | pending_data_assignment |
| Payment confirmation ref | TBD | pending_data_assignment |
| Payable basis ref | TBD | pending_data_assignment |
| Upstream finality ref | TBD | pending_data_assignment |
| Business day date | TBD | pending_data_assignment |
| Currency | PHP | pending_approval |
| Amount minor units | TBD | pending_data_assignment |
| Line summary | TBD | pending_data_assignment |
| Tender summary | TBD | pending_data_assignment |
| Tax/totals summary | TBD | pending_data_assignment |
| Approval reference | TBD | pending_data_assignment |
| Sensitive-data check | TBD | pending_data_assignment |
| Execution status | not_ready_for_execution | not_ready_for_execution |

No actual IDs are approved by this document.

## 25. Pre-Run Validation Checklist

| Check | Required result | Status | Evidence/reference |
| --- | --- | --- | --- |
| Test data approved | yes | TBD | TBD |
| Site/Site POS Server mapping approved | yes | TBD | TBD |
| POS Server fiscal config confirmed | yes | TBD | TBD |
| Central PMS config confirmed | yes | TBD | TBD |
| Evidence save path ready | yes | TBD | TBD |
| Run id assigned | yes | TBD | TBD |
| Upstream finality ref assigned | yes | TBD | TBD |
| No sensitive data | confirmed | TBD | TBD |
| No payment/exit wiring | confirmed | TBD | TBD |
| No fiscal gating enforcement | confirmed | TBD | TBD |
| No retry/readback worker | confirmed | TBD | TBD |
| Rollback owner online | yes | TBD | TBD |

All checks must pass before diagnostic execution.

## 26. Abort Criteria

Abort preparation or execution if:

- real customer data appears;
- sensitive data appears;
- wrong Site/Site POS Server is selected;
- fiscal config is missing;
- upstream finality ref is unstable;
- amount/tax/totals mismatch;
- output evidence location is unavailable;
- payment/exit flow mutation is observed;
- ExitAuthorization is issued;
- gate behavior is triggered;
- POS Server response is unknown without readback/reconciliation plan.

Abort evidence must be recorded in the UAT evidence template.

## 27. Post-Run Evidence Expectations

Expected evidence package:

- evidence JSON;
- manual-save package;
- hash/checksum if used;
- POS Server response facts;
- Central PMS fiscal reference result;
- no payment finality mutation confirmation;
- no ExitAuthorization confirmation;
- no gate behavior confirmation;
- replay/conflict/failure outcome as applicable;
- reviewer signoff.

Post-run evidence must follow the manual-save procedure until an evidence writer is approved.

## 28. Risks and Open Questions

Risks:

- real customer data could be selected accidentally;
- production fiscal sequence could allocate a fiscal number without sufficient approval;
- upstream finality references could be reused incorrectly;
- conflict tests could be run without approval;
- unknown outcomes may lack a readback/reconciliation plan;
- evidence location may remain temporary/manual rather than official.

Open questions:

- first approved environment;
- first approved Site and Site POS Server;
- first approved fiscal identity/policy/sequence values;
- first approved test payment/session/payable references;
- final evidence save Mode A location;
- whether first run should include replay immediately after newly-created success;
- whether conflict/failure/unknown scenarios are deferred until after first successful run.

## 29. Requirements Traceability Summary

| Requirement | Covered by |
| --- | --- |
| Approved test data posture | Section 6 |
| Test environment requirements | Section 7 |
| Site/Site POS Server requirements | Section 8 |
| POS Server fiscal configuration | Section 9 |
| Central PMS fiscal reference requirements | Section 10 |
| Test transaction data requirements | Section 11 |
| Parking session requirements | Section 12 |
| Payment attempt requirements | Section 13 |
| Payment confirmation requirements | Section 14 |
| Payable basis requirements | Section 15 |
| Upstream finality rules | Section 16 |
| Fiscal document request facts | Section 17 |
| Line/tender/tax/totals requirements | Section 18 |
| Idempotency/replay test data rules | Section 19 |
| Conflict test data rules | Section 20 |
| Failure/unknown test data rules | Section 21 |
| Sensitive data exclusion | Section 22 |
| Approved data table template | Section 23 |
| First UAT candidate placeholder | Section 24 |
| Pre-run validation | Section 25 |
| Abort criteria | Section 26 |
| Post-run evidence expectations | Section 27 |
| Risks/open questions | Section 28 |
| Authority boundaries | Section 4 |
| Non-goals | Section 5 |

## Recommended Next Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-first-run-readiness-review`

Purpose: review whether approved test data, Site/Site POS Server mapping, POS Server fiscal config, Central PMS config, and evidence manual-save path are ready for the first controlled UAT diagnostic execution.
