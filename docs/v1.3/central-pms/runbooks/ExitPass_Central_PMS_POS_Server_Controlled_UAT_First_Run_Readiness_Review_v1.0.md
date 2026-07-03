# ExitPass Central PMS POS Server Controlled UAT First Run Readiness Review v1.0

## 1. Document Control

| Field | Value |
| --- | --- |
| Document | ExitPass Central PMS POS Server Controlled UAT First Run Readiness Review |
| Version | v1.0 |
| Date | 2026-07-03 |
| Branch | feature/central-pms-pos-server-controlled-uat-first-run-readiness-review |
| Scope | Documentation/review only |
| Decision | not_ready_for_execution |
| Owner | Central PMS implementation/orchestration |

## 2. Purpose and Scope

This review determines whether the first controlled Central PMS to POS Server fiscal issuance diagnostic execution is ready to proceed.

The review covers:

- approved test data readiness
- environment readiness
- Site and Site POS Server mapping readiness
- POS Server fiscal identity, policy, and sequence readiness
- Central PMS configuration readiness
- upstream finality reference readiness
- evidence manual-save readiness
- owner and approval readiness

This review does not execute UAT and does not authorize a live diagnostic call.

## 3. Current Implementation Baseline

The current Central PMS baseline has:

- controlled UAT operator runbook
- controlled UAT evidence template
- controlled UAT harness planning
- controlled UAT manual-save procedure
- controlled UAT approved test data plan
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

The approved test data plan still records first-run candidate values as `TBD` and marks the candidate data record as `not_ready_for_execution`.

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

- modify source code
- modify SQL
- create migrations
- modify generated artifacts
- modify DOCX files
- modify POS Server runtime files
- add file-writing code
- add an API endpoint
- add CLI or operator tooling
- execute a live POS Server call
- create a fiscal document
- wire anything into payment confirmation
- wire anything into ExitAuthorization
- enable fiscal gating enforcement
- add retry scheduler behavior
- add GET readback worker behavior
- implement Operator Console queues
- implement Management Dashboard projections

## 6. Review Method

The controlled UAT approved test data plan is the source of truth for required first-run data.

Each readiness area is marked with one of:

- `ready`
- `not_ready`
- `partially_ready`
- `deferred`
- `not_applicable`

Any readiness area with actual values still set to `TBD`, missing owner approval, or missing traceable evidence is marked `not_ready` for execution purposes.

No IDs, URLs, fiscal identity values, fiscal sequence values, Site references, payment references, or upstream finality references are invented in this review.

## 7. Readiness Decision Summary

| Area | Status | Execution Impact |
| --- | --- | --- |
| Environment | not_ready | Required environment values remain `TBD`. |
| Site / Site POS Server | not_ready | Site and Site POS Server values remain `TBD`. |
| POS Server fiscal configuration | not_ready | Fiscal identity, policy, and sequence are not confirmed. |
| Central PMS configuration | not_ready | Intended live-call and diagnostic config values are not recorded for the run. |
| Test transaction data | not_ready | Parking, payment, payable, and amount facts remain `TBD`. |
| Upstream finality reference | not_ready | No stable approved reference is assigned. |
| Fiscal request facts | not_ready | Required request facts are not assigned. |
| Evidence manual-save | partially_ready | Procedure exists, but actual save mode/location is not finalized. |
| Sensitive-data exclusion | partially_ready | Policy exists, but actual data has not been assigned or scanned. |
| Replay scenario | deferred | Not approved for first execution yet. |
| Conflict/failure/unknown scenarios | deferred | Not approved for first execution yet. |
| Owner approvals | not_ready | Actual owner assignments and approval references are not filled. |

Decision: `not_ready_for_execution`

Primary reasons:

- first approved environment is not filled
- Site and Site POS Server values are not filled
- POS Server fiscal identity, fiscal sequence policy, and fiscal sequence state are not confirmed
- Central PMS diagnostic configuration values are not confirmed for a specific run window
- test parking, payment, confirmation, and payable references are not assigned
- upstream finality reference is not assigned
- evidence save location is not finalized
- replay, conflict, failure, and unknown scenario sequencing is not approved
- owner approvals and approval references are not recorded

## 8. Environment Readiness Review

| Check | Current Value | Status | Notes |
| --- | --- | --- | --- |
| Environment name | TBD | not_ready | Must identify the exact controlled environment. |
| Central PMS base environment | TBD | not_ready | Must identify the Central PMS environment. |
| POS Server base environment | TBD | not_ready | Must identify the POS Server environment. |
| Database/environment reference | TBD | not_ready | Must identify the data context used for the run. |
| Production or non-production decision | TBD | not_ready | Non-production is preferred. Production requires explicit approval. |
| POS Server Base URL reference | TBD | not_ready | Use a reference only; do not record secrets. |
| Diagnostic config enabled window | TBD | not_ready | Must define start/end window if invocation is later approved. |
| Evidence save mode | TBD | not_ready | Must select official location mode or temporary controlled mode. |
| Rollback/support owner | TBD | not_ready | Owner must be online during execution. |
| Run approval reference | TBD | not_ready | Must link to approval record or change ticket. |

Environment readiness: `not_ready`

## 9. Site / Site POS Server Readiness Review

| Check | Current Value | Status | Notes |
| --- | --- | --- | --- |
| Site id/ref | TBD | not_ready | Required before selecting test transaction facts. |
| Site name | TBD | not_ready | Required for evidence and reviewer context. |
| Site group, if applicable | TBD | not_ready | Reporting context only; not fiscal authority. |
| Site POS Server id/ref | TBD | not_ready | Required for fiscal issuance routing. |
| Site POS Server environment | TBD | not_ready | Must match the selected environment. |
| Site POS Server base URL reference | TBD | not_ready | Must be a safe reference, not a secret. |
| Expected fiscal identity | TBD | not_ready | Must be confirmed by POS Server owner. |
| Expected fiscal sequence policy | TBD | not_ready | Must be confirmed by POS Server owner. |
| Expected fiscal sequence state | TBD | not_ready | Must be confirmed before fiscal number allocation risk is accepted. |
| Site owner approval | TBD | not_ready | Approval reference required. |
| POS Server owner approval | TBD | not_ready | Approval reference required. |
| Engineering lead approval | TBD | not_ready | Approval reference required. |

Site / Site POS Server readiness: `not_ready`

## 10. POS Server Fiscal Configuration Readiness Review

| Check | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| Fiscal identity active/effective | not confirmed | not_ready | Required before any live diagnostic fiscal issuance call. |
| Fiscal sequence policy active/effective | not confirmed | not_ready | Required before fiscal document creation risk is accepted. |
| Fiscal sequence state configured | not confirmed | not_ready | Required to understand fiscal number allocation impact. |
| Fiscal document type supported | not confirmed | not_ready | Required to map request safely. |
| Fiscal numbering consequence understood | not confirmed | not_ready | Must be explicitly accepted. |
| Idempotency behavior understood | documented, not environment-confirmed | partially_ready | Behavior is documented; environment evidence is not recorded. |
| Replay behavior understood | documented, not environment-confirmed | partially_ready | Behavior is documented; scenario is not approved yet. |
| Conflict behavior understood | documented, not environment-confirmed | partially_ready | Behavior is documented; scenario is deferred. |
| GET readback available for manual verification | not confirmed | not_ready | No automatic readback worker exists. |
| Test/non-production sequence preferred | documented | partially_ready | Actual selected sequence is not assigned. |
| Production sequence approval, if applicable | TBD | not_ready | Required if production sequence is used. |

POS Server fiscal configuration readiness: `not_ready`

## 11. Central PMS Configuration Readiness Review

| Check | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| Fiscal reference persistence patch applied | expected baseline, not run-confirmed | partially_ready | Must be confirmed for the target environment. |
| Repository/harness tests passed | expected baseline, not run-confirmed | partially_ready | Must be captured in first-run package or pre-run checklist. |
| Controlled UAT harness available | implemented baseline | partially_ready | The harness exists, but no invocation is approved by this review. |
| Evidence exporter available | implemented baseline | partially_ready | Export capability exists, but evidence location is not finalized. |
| Manual-save procedure available | documented | ready | Procedure exists. |
| `EnablePosServerFiscalIssuanceLiveCall` intended value | TBD | not_ready | Must be true only during approved diagnostic window. |
| `EnableControlledUatDiagnosticPath` intended value | TBD | not_ready | Must be true only during approved diagnostic window. |
| Payment-flow guard remains false | not run-confirmed | not_ready | Must be explicitly confirmed for the target environment. |
| Exit-flow guard remains false | not run-confirmed | not_ready | Must be explicitly confirmed for the target environment. |
| Fiscal gating enforcement remains false | not run-confirmed | not_ready | Must be explicitly confirmed for the target environment. |
| No retry/readback worker present | implementation baseline | partially_ready | Must be reconfirmed before execution. |
| No endpoint/CLI/tooling present | implementation baseline | partially_ready | Must be reconfirmed before execution. |

Central PMS configuration readiness: `not_ready`

## 12. Test Transaction Data Readiness Review

| Required Data | Current Value | Status | Notes |
| --- | --- | --- | --- |
| Run id | TBD | not_ready | Must follow approved run id pattern. |
| Correlation id | TBD | not_ready | Required for evidence correlation. |
| Environment name | TBD | not_ready | Must match environment approval. |
| Evidence owner | TBD | not_ready | Required for manual-save accountability. |
| Approval reference | TBD | not_ready | Required before execution. |
| Site ref | TBD | not_ready | Must match approved Site. |
| Site POS Server ref | TBD | not_ready | Must match approved Site POS Server. |
| Parking session ref | TBD | not_ready | Must be approved test data. |
| Payment attempt ref | TBD | not_ready | Must be approved test data. |
| Payment confirmation ref | TBD | not_ready | Must be approved test data. |
| Payable basis ref | TBD | not_ready | Must be approved test data. |
| Upstream finality ref | TBD | not_ready | Must be stable and scenario-scoped. |
| Business day date | TBD | not_ready | Must be approved for fiscal context. |
| Currency code | TBD | not_ready | Expected `PHP`, but not assigned. |
| Amount minor units | TBD | not_ready | Must be low-risk approved amount where possible. |
| Line summary | TBD | not_ready | Must be synthetic or approved safe facts. |
| Tender summary | TBD | not_ready | Must be safe test tender facts. |
| Tax/totals summary | TBD | not_ready | Must match payable basis. |
| Expected run type | TBD | not_ready | First run should normally be `newly_created`, but not approved. |

Test transaction data readiness: `not_ready`

## 13. Upstream Finality Reference Readiness Review

| Check | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| Stable reference assigned | no | not_ready | Required before invocation. |
| Follows approved pattern | no | not_ready | Approved pattern: `CPS-POS-UAT:<run-id>:<scenario>:<sequence>`. |
| One semantic request per reference | not confirmed | not_ready | Required to preserve idempotency semantics. |
| Replay plan uses same reference and same facts | not approved | deferred | Replay should follow only after first run approval. |
| Conflict plan separately approved | no | deferred | Conflict scenario should not be bundled into first execution. |
| No bypass reference planned | not confirmed | not_ready | Must be explicitly acknowledged. |

Upstream finality reference readiness: `not_ready`

## 14. Fiscal Request Facts Readiness Review

| Required Fact | Current Value | Status | Notes |
| --- | --- | --- | --- |
| Fiscal document type | TBD | not_ready | Must be supported by POS Server environment. |
| Business day date | TBD | not_ready | Required. |
| Site ref | TBD | not_ready | Required. |
| Site POS Server ref | TBD | not_ready | Required. |
| Parking session ref | TBD | not_ready | Required. |
| Payment attempt ref | TBD | not_ready | Required. |
| Payment confirmation ref | TBD | not_ready | Required. |
| Payable basis ref | TBD | not_ready | Required. |
| Upstream finality ref | TBD | not_ready | Required. |
| Currency | TBD | not_ready | Expected safe approved value. |
| Amount minor units | TBD | not_ready | Required. |
| Line count | TBD | not_ready | Required. |
| Tender count | TBD | not_ready | Required. |
| Tax detail presence | TBD | not_ready | Required. |
| Totals | TBD | not_ready | Required. |
| Correlation id | TBD | not_ready | Required. |

Fiscal request facts readiness: `not_ready`

## 15. Evidence Manual-Save Readiness Review

| Check | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| Mode A or Mode B selected | no | not_ready | Official location or temporary controlled location must be selected. |
| Output location known | no | not_ready | Required before execution. |
| Evidence owner assigned | no | not_ready | Required before execution. |
| Run approval reference available | no | not_ready | Required before execution. |
| File/folder naming confirmed | documented | partially_ready | Naming convention exists; target path is not selected. |
| Hash procedure available | documented | partially_ready | Manual SHA-256 procedure exists; owner is not assigned. |
| Ticket/change linkage ready | no | not_ready | Required for traceability. |
| Reviewer signoff path known | documented, not assigned | partially_ready | Required reviewers are known, but actual signoffs are not assigned. |
| Temporary local handling understood | documented | partially_ready | Must be acknowledged by evidence owner. |

Evidence manual-save readiness: `partially_ready`, but not execution-ready.

## 16. Sensitive-Data Exclusion Readiness Review

| Exclusion Check | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| No PAN | policy documented, data TBD | partially_ready | Actual data must be scanned before execution. |
| No CVV | policy documented, data TBD | partially_ready | Actual data must be scanned before execution. |
| No tokens | policy documented, data TBD | partially_ready | Actual data must be scanned before execution. |
| No credentials | policy documented, data TBD | partially_ready | Actual data must be scanned before execution. |
| No secrets | policy documented, data TBD | partially_ready | Actual data must be scanned before execution. |
| No raw provider callback payloads | policy documented, data TBD | partially_ready | Actual data must be scanned before execution. |
| No raw entitlement evidence | policy documented, data TBD | partially_ready | Actual data must be scanned before execution. |
| No uncontrolled images/files | policy documented, data TBD | partially_ready | Actual attachments must be reviewed. |
| No unmanaged customer personal data | policy documented, data TBD | partially_ready | Actual data must be reviewed. |
| No free-form sensitive blobs | policy documented, data TBD | partially_ready | Actual notes/metadata must be reviewed. |
| No unmasked plate/ticket unless explicitly approved | policy documented, data TBD | partially_ready | Actual Site test data must be reviewed. |

Sensitive-data exclusion readiness: `partially_ready`, but not execution-ready because actual test data is not assigned.

## 17. Replay Scenario Readiness Review

Expected posture: the first execution should focus on a single approved `newly_created` diagnostic run unless replay is separately approved.

| Check | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| Replay immediately after newly created run approved | no | deferred | Requires separate explicit approval. |
| Same upstream finality reference rule understood | documented | partially_ready | Must be acknowledged by executor. |
| Same semantic facts available | no | not_ready | Test facts are not assigned. |
| Expected same fiscal document id/number | documented | partially_ready | Must be validated only after first run result exists. |
| No duplicate Central PMS fiscal reference expected | documented | partially_ready | Requires repository evidence after execution. |

Replay readiness: `deferred`

## 18. Conflict/Failure/Unknown Scenario Readiness Review

Expected posture: conflict, failure, and unknown outcome scenarios are deferred until after first successful newly-created and optional replay evidence unless separately approved.

| Scenario | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| Conflict scenario | not approved | deferred | Requires separate scenario data and approval. |
| Failure scenario | not approved | deferred | Prefer mocked or non-production environment. |
| Unknown scenario | not approved | deferred | Requires readback/reconciliation plan. |
| Readback/reconciliation plan for unknown | not recorded | not_ready | No automatic readback worker exists. |
| Abort criteria clear | documented | partially_ready | Must be acknowledged by owners before execution. |

Conflict/failure/unknown readiness: `deferred`

## 19. Pre-Run Checklist Review

| Checklist Item | Current Status | Readiness |
| --- | --- | --- |
| Test data approved | no | not_ready |
| Site / Site POS Server mapping approved | no | not_ready |
| POS Server fiscal config confirmed | no | not_ready |
| Central PMS config confirmed | no | not_ready |
| Evidence save path ready | no | not_ready |
| Run id assigned | no | not_ready |
| Upstream finality ref assigned | no | not_ready |
| No sensitive data confirmed | no actual data assigned | not_ready |
| No payment/exit wiring confirmed | implementation baseline only | partially_ready |
| No fiscal gating enforcement confirmed | implementation baseline only | partially_ready |
| No retry/readback worker confirmed | implementation baseline only | partially_ready |
| Rollback owner online | no | not_ready |

Pre-run checklist readiness: `not_ready`

## 20. Abort Criteria Review

The following abort criteria are documented and remain active for any future first-run execution:

- real customer data appears
- sensitive data appears
- wrong Site or Site POS Server is selected
- fiscal configuration is missing
- upstream finality reference is unstable
- amount, tax, or totals mismatch
- output evidence location is unavailable
- payment or exit flow mutation is observed
- ExitAuthorization is issued by the diagnostic path
- gate behavior is triggered
- POS Server response is unknown without an approved readback/reconciliation plan

Abort criteria readiness: `partially_ready`

Rationale: criteria are documented, but responsible owners and target evidence path are not assigned.

## 21. Post-Run Evidence Readiness Review

| Evidence Item | Current Status | Readiness | Notes |
| --- | --- | --- | --- |
| Evidence JSON | exporter exists, no run data | partially_ready | Requires execution and manual save. |
| Manual-save package | procedure exists, no target path | partially_ready | Target path and owner remain `TBD`. |
| Hash/checksum if used | procedure exists | partially_ready | Must be performed by assigned evidence owner. |
| POS Server response facts | not available | not_ready | Requires approved execution. |
| Central PMS fiscal reference result | not available | not_ready | Requires approved execution. |
| Payment finality mutation confirmation | not available | not_ready | Requires post-run evidence. |
| ExitAuthorization confirmation | not available | not_ready | Requires post-run evidence. |
| Gate behavior confirmation | not available | not_ready | Requires post-run evidence. |
| Replay/conflict/failure outcome evidence | deferred | deferred | Not part of first execution unless separately approved. |
| Reviewer signoff | not assigned | not_ready | Required after evidence package assembly. |

Post-run evidence readiness: `partially_ready`, but not execution-ready.

## 22. Owner/Approval Readiness Review

| Role / Approval | Current Status | Readiness |
| --- | --- | --- |
| UAT lead | TBD | not_ready |
| Engineering lead | TBD | not_ready |
| POS Server owner | TBD | not_ready |
| Central PMS owner | TBD | not_ready |
| Site owner | TBD | not_ready |
| Operations lead | TBD | not_ready |
| Rollback/support owner | TBD | not_ready |
| Evidence owner | TBD | not_ready |
| Compliance/accounting observer, if fiscal number may be allocated | TBD | not_ready |
| Run approval reference | TBD | not_ready |
| Evidence save approval reference | TBD | not_ready |
| Fiscal number allocation approval, if applicable | TBD | not_ready |

Owner/approval readiness: `not_ready`

## 23. Final Readiness Decision

Decision: `not_ready_for_execution`

The first controlled UAT diagnostic run must not proceed yet.

Current allowed next step:

- fill approved data values
- confirm Site and Site POS Server mapping
- confirm POS Server fiscal identity, policy, and sequence
- confirm Central PMS diagnostic configuration and guard values
- assign parking, payment, confirmation, payable, and upstream finality references
- confirm evidence save mode, target location, evidence owner, and ticket/change linkage
- record owner approvals
- rerun first-run readiness review

Live diagnostic execution is not recommended until all required values and approvals are complete and traceable.

## 24. Conditions Required Before Execution

Execution may be reconsidered only when all of the following are complete:

- environment selected and approved
- Central PMS environment recorded
- POS Server environment recorded
- Site reference assigned and approved
- Site POS Server reference assigned and approved
- fiscal identity confirmed
- fiscal sequence policy confirmed
- fiscal sequence state confirmed
- fiscal document type confirmed
- production fiscal number risk approved if production sequence is involved
- Central PMS live-call and diagnostic flags intended values recorded for the approved window
- payment-flow and exit-flow guards confirmed false
- fiscal gating enforcement confirmed false
- run id assigned
- correlation id assigned
- upstream finality reference assigned and pattern-confirmed
- parking session reference assigned
- payment attempt reference assigned
- payment confirmation reference assigned
- payable basis reference assigned
- amount, currency, line, tender, tax, and totals facts assigned and approved
- sensitive-data exclusion scan complete
- evidence save mode and location selected
- hash/checksum approach confirmed
- reviewer signoff path assigned
- rollback/support owner online
- UAT lead, engineering lead, POS Server owner, Site owner, and evidence owner approvals recorded

## 25. Risks

| Risk | Impact | Current Control |
| --- | --- | --- |
| Fiscal number allocated against wrong Site or sequence | Compliance and reconciliation risk | Do not execute until Site, Site POS Server, fiscal identity, policy, and sequence are approved. |
| Unknown POS Server outcome without readback plan | Ambiguous fiscal evidence | Defer unknown scenario; require manual reconciliation plan. |
| Sensitive data appears in evidence | Data protection incident | Use approved test data and manual sensitive-data review before save. |
| Evidence saved to unapproved location | Traceability and retention gap | Manual-save location must be assigned before execution. |
| Payment or ExitAuthorization flow accidentally affected | Production behavior risk | Do not wire diagnostic seam into payment or ExitAuthorization flows. |
| Replay/conflict scenarios bundled into first run | Harder evidence review and fiscal risk | Defer replay/conflict/failure/unknown until after first-run approval. |

## 26. Open Blockers

- Environment name is not assigned.
- Central PMS environment is not assigned.
- POS Server environment is not assigned.
- Site reference is not assigned.
- Site POS Server reference is not assigned.
- POS Server fiscal identity is not confirmed.
- POS Server fiscal sequence policy is not confirmed.
- POS Server fiscal sequence state is not confirmed.
- Central PMS diagnostic configuration window is not confirmed.
- Payment-flow and exit-flow guard confirmations are not recorded for the run.
- Fiscal gating enforcement-off confirmation is not recorded for the run.
- Run id is not assigned.
- Correlation id is not assigned.
- Parking session reference is not assigned.
- Payment attempt reference is not assigned.
- Payment confirmation reference is not assigned.
- Payable basis reference is not assigned.
- Upstream finality reference is not assigned.
- Fiscal request facts are not assigned.
- Evidence save mode and location are not selected.
- Evidence owner is not assigned.
- Owner approvals are not recorded.
- Replay, conflict, failure, and unknown scenario sequencing is not approved.

## 27. Recommended Next Branch/Task

Recommended next branch:

`feature/central-pms-pos-server-controlled-uat-data-assignment-record`

Purpose:

Create a fillable data assignment record where actual environment, Site/Site POS Server, fiscal config, parking/payment/payable refs, upstream finality ref, evidence save location, and owner approvals can be recorded.

Rationale:

The project is not ready for execution because the required data and approvals are not filled. A data assignment record is the safest next step before another readiness review or any diagnostic run.

## 28. Requirements Traceability Summary

| Requirement | Trace |
| --- | --- |
| Use approved test data plan as source of truth | Sections 6, 7, 12, 14 |
| Mark TBD values as not ready | Sections 8 through 14, 22 |
| Preserve authority boundaries | Section 4 |
| Preserve non-goals | Section 5 |
| Confirm no execution readiness without actual values | Sections 7, 23, 24 |
| Review evidence manual-save readiness | Section 15 |
| Review sensitive-data exclusion readiness | Section 16 |
| Review replay/conflict/failure/unknown readiness | Sections 17, 18 |
| Recommend data assignment record when not ready | Section 27 |

