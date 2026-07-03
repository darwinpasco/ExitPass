# ExitPass Central PMS POS Server Controlled UAT Data Assignment Review v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT Data Assignment Review |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-data-assignment-review |
| Scope | Documentation/review only |
| Source of truth | Controlled UAT Data Assignment Record v1.0 |
| Review decision | not_ready_for_execution |

## 2. Purpose and Scope

This review determines whether the controlled UAT data assignment record is complete enough to move the first Central PMS to POS Server fiscal issuance diagnostic run from `not_ready_for_execution` to execution dry-run checklist preparation.

The review covers:

- owner and approval assignment
- environment assignment
- Site and Site POS Server assignment
- POS Server fiscal configuration assignment
- Central PMS configuration assignment
- test transaction reference assignment
- upstream finality reference assignment
- fiscal request facts assignment
- evidence save assignment
- scenario assignment and deferred replay/conflict/failure/unknown scope

The review does not execute UAT and does not authorize any diagnostic call.

## 3. Current Implementation Baseline

The current baseline has:

- controlled UAT operator runbook
- controlled UAT evidence template
- controlled UAT harness planning
- controlled UAT manual-save procedure
- controlled UAT approved test data plan
- controlled UAT first-run readiness review
- controlled UAT data assignment record
- application-level controlled UAT harness
- safe evidence JSON exporter
- disabled/default-safe POS Server live-call seam
- controlled diagnostic seam
- no API endpoint for controlled UAT invocation
- no CLI or operator tooling for controlled UAT invocation
- no automatic evidence file-writing
- no payment confirmation wiring
- no ExitAuthorization wiring
- no fiscal gating enforcement
- no retry scheduler
- no GET readback worker

## 4. Authority Boundaries

- Central PMS owns payment finality.
- Central PMS owns fiscal reference recording.
- Central PMS owns normal ExitAuthorization.
- POS Server owns fiscal issuance and numbering only.
- POS Server response is fiscal issuance evidence only.
- POS Server does not approve payment, exit, gate opening, entitlement, manual release, or continuity.
- Manual release is not normal ExitAuthorization.
- UAT readiness evidence and test data are audit artifacts only and do not create operational authority.

## 5. Non-Goals

This review does not:

- execute UAT
- execute live POS Server calls
- create fiscal documents
- add endpoint or tooling
- implement file-writing
- enable payment/exit production flow
- issue ExitAuthorization
- enforce fiscal gating
- implement retry
- implement GET readback worker
- implement Operator Console queue
- implement Dashboard projection
- modify source code
- modify SQL
- modify POS Server runtime

## 6. Review Method

The controlled UAT data assignment record is the source of truth for this review.

Each area is marked with one of:

- `complete`
- `incomplete`
- `blocked`
- `deferred`
- `not_applicable`

Any area with required fields still set to `TBD`, blank, `not_started`, `incomplete`, unapproved, or untraceable is marked `incomplete` or `blocked`.

No IDs, URLs, fiscal identity values, fiscal sequence values, Site refs, payment refs, payable refs, upstream finality refs, names, or approval references are invented in this review.

## 7. Data Assignment Review Decision

Decision: `not_ready_for_execution`

The data assignment record remains incomplete. It still contains `TBD`, `not_started`, `incomplete`, and deferred values across required execution-readiness areas.

The record is not complete enough to support:

- execution dry-run checklist preparation
- refreshed first-run readiness review using actual assigned values
- any controlled UAT diagnostic invocation

The current assignment state is best classified as:

- record status: `incomplete`
- execution readiness: `not_ready_for_execution`
- recommended next step: complete the data assignment record with actual approved values and approval references

## 8. Owner and Approval Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| UAT lead | TBD / not_started | incomplete | Required owner not assigned. |
| Engineering lead | TBD / not_started | incomplete | Required owner not assigned. |
| POS Server owner | TBD / not_started | incomplete | Required owner not assigned. |
| Central PMS owner | TBD / not_started | incomplete | Required owner not assigned. |
| Site owner | TBD / not_started | incomplete | Required owner not assigned. |
| Operations lead | TBD / not_started | incomplete | Required owner not assigned. |
| Rollback/support owner | TBD / not_started | incomplete | Required owner not assigned. |
| Evidence owner | TBD / not_started | incomplete | Required owner not assigned. |
| Compliance/accounting observer, if fiscal number may be allocated | TBD / not_started | incomplete | Applicability is not decided. |
| Run approval reference | TBD / not_started | blocked | Required traceability is missing. |
| Evidence save approval reference | TBD / not_started | blocked | Required traceability is missing. |
| Fiscal number allocation approval, if applicable | TBD / not_started | incomplete | Applicability and approval are not decided. |

Owner/approval review status: `blocked`

## 9. Environment Assignment Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Environment name | TBD / not_started | incomplete | No environment selected. |
| Central PMS base environment | TBD / not_started | incomplete | Not assigned. |
| POS Server base environment | TBD / not_started | incomplete | Not assigned. |
| Database/environment reference | TBD / not_started | incomplete | Not assigned. |
| Production or non-production decision | TBD / not_started | blocked | Required risk posture is not decided. |
| POS Server Base URL reference | TBD / not_started | incomplete | Reference only is required; none assigned. |
| Diagnostic config enabled window start/end | TBD / not_started | incomplete | No controlled window assigned. |
| Evidence save mode | TBD / not_started | blocked | Mode A or Mode B is not selected. |
| Rollback/support owner | TBD / not_started | incomplete | Not assigned. |
| Run approval reference | TBD / not_started | blocked | Missing traceability. |

Environment assignment review status: `blocked`

## 10. Site / Site POS Server Assignment Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Site id/ref | TBD / not_started | incomplete | Not assigned. |
| Site name | TBD / not_started | incomplete | Not assigned. |
| Site group, if applicable | TBD / not_started | incomplete | Applicability not recorded. |
| Site POS Server id/ref | TBD / not_started | incomplete | Not assigned. |
| Site POS Server environment | TBD / not_started | incomplete | Not assigned. |
| Site POS Server base URL reference | TBD / not_started | incomplete | Not assigned. |
| Expected fiscal identity | TBD / not_started | blocked | Required for fiscal issuance readiness. |
| Expected fiscal sequence policy | TBD / not_started | blocked | Required for fiscal issuance readiness. |
| Expected fiscal sequence state | TBD / not_started | blocked | Required for fiscal number allocation risk review. |
| Site owner approval | TBD / not_started | blocked | Missing approval. |
| POS Server owner approval | TBD / not_started | blocked | Missing approval. |
| Engineering lead approval | TBD / not_started | blocked | Missing approval. |

Site / Site POS Server assignment review status: `blocked`

## 11. POS Server Fiscal Configuration Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Fiscal identity id/ref | TBD / not_started | blocked | Required before any controlled live diagnostic call. |
| Fiscal identity active/effective confirmation | TBD / not_started | blocked | Not confirmed. |
| Fiscal sequence policy id/ref | TBD / not_started | blocked | Required before sequence allocation risk is accepted. |
| Fiscal sequence policy active/effective confirmation | TBD / not_started | blocked | Not confirmed. |
| Fiscal sequence state id/ref | TBD / not_started | blocked | Required before number allocation risk is accepted. |
| Fiscal sequence state configured confirmation | TBD / not_started | blocked | Not confirmed. |
| Fiscal document type | TBD / not_started | incomplete | Not assigned. |
| Fiscal numbering consequence accepted | TBD / not_started | blocked | Risk acceptance missing. |
| Idempotency behavior understood | TBD / not_started | incomplete | Not acknowledged for target environment. |
| Replay behavior understood | TBD / not_started | incomplete | Not acknowledged for target environment. |
| Conflict behavior understood | TBD / not_started | incomplete | Not acknowledged for target environment. |
| GET readback available | TBD / not_started | incomplete | Manual verification posture not assigned. |
| Test/non-production sequence used | TBD / not_started | incomplete | Not decided. |
| Production sequence approval reference, if applicable | TBD / not_started | blocked | Applicability and approval missing. |
| POS Server owner final signoff | TBD / not_started | blocked | Missing signoff. |

POS Server fiscal configuration review status: `blocked`

## 12. Central PMS Configuration Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Fiscal reference persistence patch confirmed | TBD / not_started | incomplete | Not confirmed for target environment. |
| Repository/harness tests evidence reference | TBD / not_started | incomplete | No evidence reference provided. |
| Controlled UAT harness available | TBD / not_started | incomplete | Baseline exists, but assignment record is not filled. |
| Evidence exporter available | TBD / not_started | incomplete | Baseline exists, but assignment record is not filled. |
| Manual-save procedure available | TBD / not_started | incomplete | Baseline exists, but assignment record is not filled. |
| `EnablePosServerFiscalIssuanceLiveCall` intended value | TBD / not_started | blocked | Required value missing. |
| `EnableControlledUatDiagnosticPath` intended value | TBD / not_started | blocked | Required value missing. |
| Diagnostic config window | TBD / not_started | blocked | Required controlled window missing. |
| Payment-flow guard false confirmation | TBD / not_started | blocked | Must be explicitly false. |
| Exit-flow guard false confirmation | TBD / not_started | blocked | Must be explicitly false. |
| Fiscal gating enforcement false confirmation | TBD / not_started | blocked | Must be explicitly false. |
| No retry/readback worker confirmation | TBD / not_started | incomplete | Must be confirmed before execution. |
| No endpoint/CLI/tooling confirmation | TBD / not_started | incomplete | Must be confirmed before execution. |
| Engineering lead signoff | TBD / not_started | blocked | Missing signoff. |

Central PMS configuration review status: `blocked`

## 13. Test Transaction Reference Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Run id | TBD / not_started | blocked | Required before any evidence package. |
| Correlation id | TBD / not_started | incomplete | Required for traceability. |
| Environment name | TBD / not_started | incomplete | Must match approved environment. |
| Evidence owner | TBD / not_started | incomplete | Must match owner assignment. |
| Approval reference | TBD / not_started | blocked | Required before execution. |
| Site ref | TBD / not_started | incomplete | Must match approved Site. |
| Site POS Server ref | TBD / not_started | incomplete | Must match approved Site POS Server. |
| Parking session ref | TBD / not_started | incomplete | Approved test data missing. |
| Payment attempt ref | TBD / not_started | incomplete | Approved test data missing. |
| Payment confirmation ref | TBD / not_started | incomplete | Approved test data missing. |
| Payable basis ref | TBD / not_started | incomplete | Approved test data missing. |
| Business day date | TBD / not_started | incomplete | Not assigned. |
| Currency code | TBD / not_started | incomplete | Not assigned. |
| Amount minor units | TBD / not_started | incomplete | Not assigned. |
| Expected run type | TBD / not_started | incomplete | Not approved. |

Test transaction reference review status: `blocked`

## 14. Upstream Finality Reference Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Upstream finality ref | TBD / not_started | blocked | Required for idempotency and evidence correlation. |
| Pattern used | TBD / not_started | incomplete | Approved pattern not instantiated. |
| One semantic request confirmation | TBD / not_started | blocked | Required before execution. |
| Replay ref reuse confirmation, if applicable | TBD / deferred | deferred | Replay is not approved. |
| Conflict bypass prohibition acknowledgement | TBD / not_started | blocked | Required before execution. |
| Assigned by | TBD / not_started | incomplete | Not assigned. |
| Approved by | TBD / not_started | blocked | Missing approval. |
| Approval reference | TBD / not_started | blocked | Missing traceability. |

Upstream finality reference review status: `blocked`

## 15. Fiscal Request Facts Review

| Item | Assignment status | Review status |
| --- | --- | --- |
| Fiscal document type | TBD / not_started | incomplete |
| Business day date | TBD / not_started | incomplete |
| Site ref | TBD / not_started | incomplete |
| Site POS Server ref | TBD / not_started | incomplete |
| Parking session ref | TBD / not_started | incomplete |
| Payment attempt ref | TBD / not_started | incomplete |
| Payment confirmation ref | TBD / not_started | incomplete |
| Payable basis ref | TBD / not_started | incomplete |
| Upstream finality ref | TBD / not_started | blocked |
| Currency | TBD / not_started | incomplete |
| Amount minor units | TBD / not_started | incomplete |
| Line count | TBD / not_started | incomplete |
| Tender count | TBD / not_started | incomplete |
| Tax detail presence | TBD / not_started | incomplete |
| Totals presence | TBD / not_started | incomplete |
| Correlation id | TBD / not_started | incomplete |

Fiscal request facts review status: `blocked`

## 16. Line / Tender / Tax / Totals Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Line summary | TBD / not_started | incomplete | Not assigned. |
| Line amount total | TBD / not_started | incomplete | Not assigned. |
| Tender summary | TBD / not_started | incomplete | Not assigned. |
| Tender amount total | TBD / not_started | incomplete | Not assigned. |
| Tax detail summary | TBD / not_started | incomplete | Not assigned. |
| Tax amount total | TBD / not_started | incomplete | Not assigned. |
| Grand total | TBD / not_started | incomplete | Not assigned. |
| Totals match payable basis | TBD / not_started | blocked | Must be confirmed. |
| Sensitive data excluded | TBD / not_started | blocked | Must be confirmed. |
| Approval reference | TBD / not_started | blocked | Missing traceability. |

Line / tender / tax / totals review status: `blocked`

## 17. Evidence Save Assignment Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| Save mode | TBD / not_started | blocked | Mode A or Mode B not selected. |
| Target location reference | TBD / not_started | blocked | Evidence location missing. |
| Evidence owner | TBD / not_started | incomplete | Not assigned. |
| Hash/checksum required | TBD / not_started | incomplete | Not decided. |
| Hash/checksum command/reference | TBD / not_started | incomplete | Not assigned. |
| Ticket/change linkage | TBD / not_started | blocked | Required traceability missing. |
| Reviewer signoff path | TBD / not_started | incomplete | Not assigned. |
| Temporary local handling owner | TBD / not_started | incomplete | Required if temporary handling is used. |
| Approval reference | TBD / not_started | blocked | Missing approval. |

Evidence save assignment review status: `blocked`

## 18. Sensitive-Data Exclusion Review

| Check | Assignment status | Review status |
| --- | --- | --- |
| No PAN | not_started / TBD | incomplete |
| No CVV | not_started / TBD | incomplete |
| No tokens | not_started / TBD | incomplete |
| No credentials | not_started / TBD | incomplete |
| No secrets | not_started / TBD | incomplete |
| No raw provider callback payloads | not_started / TBD | incomplete |
| No raw entitlement evidence | not_started / TBD | incomplete |
| No uncontrolled images/files | not_started / TBD | incomplete |
| No unmanaged customer personal data | not_started / TBD | incomplete |
| No free-form sensitive blobs | not_started / TBD | incomplete |
| No unmasked plate/ticket unless explicitly approved | not_started / TBD | incomplete |

Sensitive-data exclusion review status: `blocked`

The exclusion rules are documented, but the actual test data has not been assigned or checked.

## 19. Scenario Assignment Review

| Item | Assignment status | Review status | Notes |
| --- | --- | --- | --- |
| First scenario id | TBD / not_started | incomplete | Not assigned. |
| First run expected type | newly_created / assigned_pending_approval | incomplete | Safe default is present but not approved. |
| Replay included | TBD / deferred | deferred | Not approved. |
| Conflict included | TBD / deferred | deferred | Not approved. |
| Failure included | TBD / deferred | deferred | Not approved. |
| Unknown included | TBD / deferred | deferred | Not approved. |
| Scenario sequencing decision | TBD / not_started | blocked | Required before execution planning. |
| Scenario owner | TBD / not_started | incomplete | Not assigned. |
| Approval reference | TBD | blocked | Missing approval. |

Scenario assignment review status: `blocked`

Expected safe first run remains `newly_created` only, but it is not approved in the assignment record.

## 20. Replay Assignment Review

Replay review status: `deferred`

Replay is not approved in the assignment record. If replay is later included, the record must assign:

- original run id
- replay run id
- same upstream finality ref
- same semantic facts confirmation
- expected same fiscal document id/number
- no duplicate Central PMS fiscal reference expectation
- replay approval reference

## 21. Conflict/Failure/Unknown Assignment Review

| Scenario | Included | Review status | Notes |
| --- | --- | --- | --- |
| Conflict | no | deferred | Scenario owner, approval, expected outcome, and review plan are not assigned. |
| Failure | no | deferred | Scenario owner, approval, expected outcome, and review plan are not assigned. |
| Unknown | no | deferred | Readback/reconciliation plan is not assigned. |

Conflict/failure/unknown review status: `deferred`

Expected posture remains:

- conflict deferred
- failure deferred
- unknown deferred unless separately approved

## 22. Pre-Run Validation Review

| Validation item | Assignment status | Review status |
| --- | --- | --- |
| Test data approved | not_started | blocked |
| Environment approved | not_started | blocked |
| Site/Site POS Server mapping approved | not_started | blocked |
| POS Server fiscal config confirmed | not_started | blocked |
| Central PMS config confirmed | not_started | blocked |
| Evidence save path ready | not_started | blocked |
| Run id assigned | not_started | blocked |
| Upstream finality ref assigned | not_started | blocked |
| Sensitive-data check passed | not_started | blocked |
| Payment-flow guard false | not_started | blocked |
| Exit-flow guard false | not_started | blocked |
| Fiscal gating enforcement false | not_started | blocked |
| No retry/readback worker | not_started | incomplete |
| Rollback owner online | not_started | blocked |
| Approval reference attached | not_started | blocked |

Pre-run validation review status: `blocked`

## 23. Abort Owner Review

| Abort condition | Assignment status | Review status |
| --- | --- | --- |
| Sensitive data detected | TBD / not_started | incomplete |
| Wrong Site/Site POS Server | TBD / not_started | incomplete |
| Fiscal config missing | TBD / not_started | incomplete |
| Upstream finality unstable | TBD / not_started | incomplete |
| Amount/tax/totals mismatch | TBD / not_started | incomplete |
| Evidence location unavailable | TBD / not_started | incomplete |
| Payment/exit flow mutation observed | TBD / not_started | incomplete |
| ExitAuthorization issued | TBD / not_started | incomplete |
| Gate behavior triggered | TBD / not_started | incomplete |
| POS Server unknown outcome without readback plan | TBD / not_started | incomplete |

Abort owner review status: `incomplete`

## 24. Reviewer/Signoff Review

| Reviewer | Assignment status | Review status |
| --- | --- | --- |
| UAT lead | TBD / not_started | incomplete |
| Engineering lead | TBD / not_started | incomplete |
| POS Server owner | TBD / not_started | incomplete |
| Central PMS owner | TBD / not_started | incomplete |
| Site owner | TBD / not_started | incomplete |
| Operations lead | TBD / not_started | incomplete |
| Evidence owner | TBD / not_started | incomplete |
| Compliance/accounting observer, if fiscal number allocated | TBD / not_applicable | incomplete |

Reviewer/signoff review status: `incomplete`

## 25. Final Assignment Status Review

| Final check | Assignment record value | Review status |
| --- | --- | --- |
| All required values assigned | no | blocked |
| All required owners assigned | no | blocked |
| All required approvals recorded | no | blocked |
| Sensitive-data check passed | no | blocked |
| Evidence save path assigned | no | blocked |
| Ready for readiness re-review | no | blocked |
| Ready for execution | no | blocked |
| Final assignment decision | incomplete | incomplete |

Final assignment status review: `blocked`

## 26. Final Readiness Recommendation

Decision: `not_ready_for_execution`

The assignment record is not complete enough to move to execution dry-run checklist preparation.

Recommended next step:

Complete the data assignment record with actual approved values and approval references.

The project should not proceed to:

- execution dry-run checklist preparation
- controlled diagnostic invocation
- any live POS Server fiscal issuance call
- fiscal document creation

## 27. Conditions Required Before Dry-Run Checklist

Before an execution dry-run checklist can be prepared, the data assignment record must show:

- all required owners assigned
- all required approvals recorded
- environment selected and approved
- Central PMS and POS Server environment references assigned
- production/non-production decision recorded
- Site and Site POS Server assigned and approved
- POS Server fiscal identity, sequence policy, sequence state, and document type confirmed
- fiscal numbering consequence accepted
- Central PMS diagnostic live-call and controlled UAT path flags assigned for an approved window
- payment-flow and exit-flow guards confirmed false
- fiscal gating enforcement confirmed false
- no retry/readback worker confirmed
- no endpoint/CLI/tooling confirmed
- run id and correlation id assigned
- parking, payment attempt, payment confirmation, payable basis, and business day refs assigned
- upstream finality ref assigned and approved
- fiscal request facts assigned and approved
- line/tender/tax/totals assigned and confirmed against payable basis
- evidence save mode and target location assigned
- hash/checksum and ticket/change linkage assigned
- sensitive-data exclusion checks completed
- scenario sequencing decision approved
- abort owners assigned
- reviewer/signoff path assigned

## 28. Risks

| Risk | Impact | Current control |
| --- | --- | --- |
| Missing owners or approvals | Unauthorized or untraceable UAT action | Keep decision `not_ready_for_execution`. |
| Missing Site/Site POS Server mapping | Fiscal evidence could be tied to wrong location or fiscal identity | Require assignment and approval before dry-run checklist. |
| Missing fiscal identity/policy/sequence | Fiscal numbering risk | Require POS Server owner confirmation. |
| Missing upstream finality ref | Broken idempotency and replay traceability | Require stable approved reference. |
| Missing evidence save path | Evidence loss or uncontrolled storage | Require Mode A or Mode B assignment. |
| Sensitive data not checked | Data protection incident | Require explicit exclusion checks before execution. |
| Scenario scope not approved | Accidental replay/conflict/failure/unknown behavior | Defer non-new scenarios until approved. |

## 29. Open Blockers

- Owner assignments remain `TBD`.
- Approval references remain `TBD`.
- Environment assignment remains `TBD`.
- Site and Site POS Server assignment remains `TBD`.
- POS Server fiscal identity, policy, and sequence assignment remains `TBD`.
- Central PMS diagnostic config and guard confirmations remain `TBD`.
- Test transaction refs remain `TBD`.
- Upstream finality ref remains `TBD`.
- Fiscal request facts remain `TBD`.
- Line/tender/tax/totals remain `TBD`.
- Evidence save mode and location remain `TBD`.
- Sensitive-data exclusion checks are not started.
- Scenario sequencing decision is not approved.
- Abort owners are not assigned.
- Reviewer/signoff assignments are not complete.

## 30. Recommended Next Branch/Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-data-assignment-fill`

Purpose:

Fill the data assignment record with actual approved values and approval references for the first controlled UAT diagnostic run.

Rationale:

The current assignment record is a blank template and cannot support a readiness re-review or execution dry-run checklist preparation.

## 31. Requirements Traceability Summary

| Requirement | Trace |
| --- | --- |
| Use data assignment record as source of truth | Sections 6 through 25 |
| Mark TBD/blank/incomplete/unapproved values as incomplete or blocked | Sections 8 through 25 |
| Do not invent operational values or approvals | Section 6 |
| Decide whether ready for dry-run checklist | Sections 7, 26 |
| Review owner/approval assignment | Section 8 |
| Review environment assignment | Section 9 |
| Review Site/Site POS Server assignment | Section 10 |
| Review POS Server fiscal config assignment | Section 11 |
| Review Central PMS config assignment | Section 12 |
| Review transaction refs and upstream finality | Sections 13, 14 |
| Review evidence save and sensitive-data exclusion | Sections 17, 18 |
| Review scenario/replay/conflict/failure/unknown scope | Sections 19 through 21 |
| Preserve authority boundaries | Section 4 |
| Preserve non-goals | Section 5 |

