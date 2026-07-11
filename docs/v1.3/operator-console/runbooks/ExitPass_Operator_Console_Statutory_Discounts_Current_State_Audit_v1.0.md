# ExitPass Operator Console Statutory Discounts Current State Audit v1.0

## Result

Status: `AUDIT_COMPLETE_SOURCE_INSPECTION_ONLY`

This audit summarizes the current statutory discount implementation state from source inspection. No fixes, refactors, schema changes, runtime endpoint calls, UAT scenarios, or full test suite runs were performed.

## Executive summary

ExitPass has a substantial statutory discount foundation across Central PMS and Operator Console:

- Operator Console can create privacy-minimized statutory discount drafts, capture metadata-only evidence, review/approve/reject drafts, apply an approved discount to a payable/tariff basis, and review a statutory discount audit report.
- Central PMS has policy resolution support, policy import dry-run/review support, action/access evaluation, persistence, and focused tests around the statutory discount workflow.
- Payment integration is partially implemented: payment attempt creation can consume the effective applied tariff snapshot and reject stale original payable bases.
- POS Server/Sales Invoice request mapping has discount-reference support, but a full end-to-end proof that an approved statutory discount produces the correct Sales Invoice fiscal payload was not confirmed from current source inspection.
- The overall statutory discount track is partially ready for controlled UAT, but not ready for broad production/UAT execution until business-rule correctness, fiscal handoff, RBAC, payable-basis persistence alignment, and manual smoke evidence are tightened.

## Current implemented capabilities

| Capability | Current state | Notes |
| --- | --- | --- |
| Ticket lookup | Implemented, read-only | Operator Console can inspect ticket/session context. Sales Invoice number is not currently available from this read model. |
| Policy resolution | Implemented | Resolves statutory policy context and readiness using configured/local policy data or national fallback references. |
| Discount draft request | Implemented | Creates privacy-minimized draft metadata; does not apply discount or mutate payment/gate state. |
| Evidence capture | Implemented, metadata-only | Stores evidence references/metadata only; no raw ID images or raw evidence bytes. |
| Approval/rejection | Implemented | Supports approve/reject decisions with validation state transitions and conflict handling. |
| Payable basis application | Implemented | Applies an approved validation to a payable/tariff basis and creates an applied tariff snapshot. |
| Discount audit report | Implemented | Read-only report over statutory discount validation/audit metadata. |
| Policy import dry-run/review | Implemented | Supports dry-run and DB-backed review queue. Approval does not activate/import production policy rows. |
| Vendor acknowledgment visibility | Implemented separately | Useful surrounding ops visibility, but not a statutory discount-specific workflow. |
| UAT/smoke evidence | Not confirmed | Focused automated tests exist; current-source manual statutory discount UAT evidence was not confirmed. |

## UI routes/pages

| Route | Purpose | Actions | Permission/access posture | Operator readiness |
| --- | --- | --- | --- | --- |
| `/operator-console` | Operator Console landing page | Navigation only | Operator Console access | Partially ready; statutory discount entry points are visible. |
| `/operator-console/ticket-lookup` | Read-only ticket/session lookup | Lookup only | Operator Console read/access context | Partially ready; clarifies Ticket number vs Sales Invoice number, but Sales Invoice number is not available in this read model. |
| `/operator-console/statutory-discounts` | Statutory discount draft queue | View draft | `VIEW_STATUTORY_DISCOUNT_DRAFT` access evaluation | Partially ready; queue fields are operational but still draft/id heavy. |
| `/operator-console/statutory-discounts/{draftId}` | Draft detail/review | Capture evidence metadata, approve, reject, apply payable basis | Action-specific Operator Console access evaluation | Partially ready; mutating workflow exists with guardrails. |
| `/operator-console/audit` | Statutory discount audit report | Read-only report | `VIEW_AUDIT_REPORT` access evaluation | Partially ready; useful but still technical fields remain. |
| `/operator-console/production-policy-import-review` | Production policy import dry-run/review | Dry-run, submit/review decisions | Explicit policy import review RBAC policies | Partially ready for review workflow only; no activation/import. |
| `/operator-console/vendor-acknowledgments` | Vendor payment acknowledgment visibility | Read-only | Vendor acknowledgment ops access | Adjacent visibility; not discount-specific. |

Known UI gaps:

- The statutory audit report still exposes technical identifiers such as correlation/session IDs in places where more operator-friendly business values would be preferable.
- Ticket Lookup does not currently surface a Sales Invoice number from the inspected read model.
- Policy import approval stops at review/alignment and is not a production policy activation path.

## Backend endpoints/services

| Endpoint/service | Method/route | Purpose | Mutates state | Access posture |
| --- | --- | --- | --- | --- |
| Ticket session summary | `POST /v1/ops/operator-console/ticket-session-summary` | Read-only ticket/session/payable visibility | No | Operator Console access pattern. |
| Draft queue | `GET /v1/ops/operator-console/statutory-discounts/drafts` | List statutory discount drafts | No | `VIEW_STATUTORY_DISCOUNT_DRAFT`. |
| Draft detail | `GET /v1/ops/operator-console/statutory-discounts/drafts/{draftId}` | Read draft detail | No | `VIEW_STATUTORY_DISCOUNT_DRAFT`. |
| Draft creation | `POST /v1/ops/operator-console/statutory-discounts/draft` | Create statutory discount validation draft | Yes, draft/evidence metadata only | Action/access evaluation; no payment/gate mutation. |
| Decision | `POST /v1/ops/operator-console/statutory-discounts/{draftId}/decision` | Approve/reject draft | Yes, validation state | Action/access evaluation. |
| Evidence capture | `POST /v1/ops/operator-console/statutory-discounts/{draftId}/evidence` | Capture evidence metadata | Yes, metadata only | Action/access evaluation. |
| Evidence list | `GET /v1/ops/operator-console/statutory-discounts/{draftId}/evidence` | Read evidence metadata | No | Action/access evaluation. |
| Apply payable basis | `POST /v1/ops/operator-console/statutory-discounts/{validationId}/apply-payable-basis` | Apply approved discount to payable/tariff basis | Yes, tariff/payable snapshot state | Action/access evaluation; no payment/provider/gate mutation. |
| Policy resolution | `POST /v1/ops/operator-console/statutory-discounts/resolve-policy` | Resolve policy/readiness for operator review | No business mutation; access evaluation may be recorded | `VIEW_POLICY_RESOLUTION`. |
| Statutory audit report | `GET /v1/ops/operator-console/audit/statutory-discounts` | Read audit/report rows | No | `VIEW_AUDIT_REPORT`. |
| Policy import dry-run | `/v1/ops/operator-console/statutory-discounts/policies/import/dry-run` | Validate policy import payload | No | Policy import review RBAC. |
| Policy import reviews | `/v1/ops/operator-console/statutory-discounts/policies/import/reviews` | Submit/list/review dry-run results | Yes, review queue only | Explicit import review policies. |

Notable services inspected:

- `OperatorConsoleStatutoryDiscountDraftService`
- `OperatorConsoleStatutoryDiscountDecisionService`
- `OperatorConsoleStatutoryDiscountEvidenceService`
- `OperatorConsoleStatutoryDiscountApplyPayableBasisService`
- `OperatorConsoleStatutoryDiscountPolicyResolutionService`
- `OperatorConsoleStatutoryDiscountReadService`
- `OperatorConsoleAccessEvaluationService`
- `CreateOrReusePaymentAttemptHandler`
- POS Server fiscal document request mapper models/tests for discount references.

## Business rules implemented

Implemented or partially implemented:

- Entitlement types include Senior Citizen and PWD; an `OTHER_STATUTORY` enum/value exists but full operator workflow support was not confirmed.
- Policy resolution can return configured local policy context or national fallback references for RA 9994 / RA 10754 style cases.
- Policy readiness classifications include verified, manual review, unverified, missing policy/site/evidence rule, expired/inactive, sandbox-only, and not-ready states.
- Evidence requirements are policy-driven in the read model; draft/evidence services use metadata-only references.
- Evidence type defaults inspected: `PWD` maps to `PWD_ID`; other supported statutory flow defaults to `SENIOR_CITIZEN_ID`.
- Approval blocks until required evidence is satisfied.
- Duplicate active drafts for the same parking session and entitlement are prevented/reused/fail-closed depending on state.
- Apply payable basis requires:
  - approved validation
  - required evidence captured when required
  - active parking session
  - active, unexpired original tariff snapshot
  - no existing statutory/coupon discount on the original snapshot
  - no payment attempt already present for the parking session
  - policy snapshot with supported benefit posture.
- Computation path observed uses a fixed VAT-exclusive statutory discount style formula:
  - VAT rate: 12%
  - statutory discount rate: 20%
  - VAT-exclusive amount = gross / 1.12, rounded half-away-from-zero
  - VAT = gross - VAT-exclusive
  - discount = VAT-exclusive * 20%, rounded half-away-from-zero
  - final payable = VAT-exclusive - discount

Not confirmed or incomplete:

- Full legal/business-rule coverage for all local ordinances, caps, free duration rules, overnight/valet exclusions, stacking, residency, passenger/driver distinctions, and date-effective policy variations.
- Solo parent or other special categories beyond stored generic enum/policy metadata.
- Final statutory wording for receipts/reports.
- End-to-end correctness of POS Server Sales Invoice line/tax/discount totals for statutory discount cases.

## Computation and payment/fiscal integration status

| Area | Status | Finding |
| --- | --- | --- |
| Payable/tariff computation | Partially ready | Approved validations can create an applied tariff snapshot using a fixed VAT/discount formula. Broader legal-rule variations need verification. |
| Payment attempt amount | Partially ready | Payment creation can consume the effective applied tariff snapshot and reject stale original snapshots. Focused tests exist. |
| Payment confirmation/finality | Not a statutory discount owner | No evidence that discount review creates payment confirmation or finality directly; this is correct. |
| Sales Invoice/POS Server request model | Partially ready | Mapper supports discount references and privilege details with tests, but end-to-end approved-discount-to-fiscal-payload proof was not confirmed. |
| POS Server fiscal lines/totals | Not confirmed | No current-source proof found that statutory discount application produces legally correct POS Server line/tax/tender totals end to end. |
| Audit/reconciliation linkage | Partially ready | Validation/audit metadata and applied tariff snapshot data exist. Full payment/fiscal reconciliation visibility remains incomplete/not confirmed. |

Important current-state interpretation:

The statutory discount workflow is more than a review-only prototype because it can affect payable basis and payment attempt amount. However, it is not yet fully proven as an end-to-end payment plus Sales Invoice statutory discount flow.

## Data model / persistence status

Observed statutory discount persistence/state objects include:

| Object | Purpose |
| --- | --- |
| `discounts.discount_policy_references` | Policy reference metadata. |
| `discounts.statutory_discount_validations` | Core validation/draft/decision state. |
| `discounts.discount_evidence_references` | Evidence metadata references only. |
| `discounts.statutory_discount_policy_registry` | Dedicated policy registry/state for policy resolution. |
| `discounts.statutory_discount_payable_basis_applications` | Schema exists for payable basis application tracking. |
| `core.tariff_snapshots` | Original and applied payable basis snapshots. |
| `operations.operator_action_logs` and access-evaluation objects | Operator action/audit context. |

Relevant enums/fields exist for:

- entitlement type
- validation channel/status
- evidence type
- policy resolution basis
- evidence redaction/verification/capture metadata
- statutory discount applied payable basis status/channel.

Persistence gaps/risks:

- Current apply-writer code path inspected appears to create/activate applied tariff snapshots and return applied payable data, but the observed return path can leave `PayableBasisApplicationId` null. The relationship between the schema-level payable-basis application table/routine and the current application writer should be verified before UAT.
- No schema change was made in this audit.
- No broad constraint/index review was performed beyond source-level object discovery.

## Auditability and controls

Implemented audit/control posture:

- Operator Console access evaluation records action context for statutory discount workflows.
- Drafts capture requester, site/site group, device/shift-style context where available, idempotency/correlation metadata, and policy context.
- Evidence capture records metadata, operator confirmation, capture method, masked/reference data, captured by, and captured at.
- Approval/rejection records reviewer/validated metadata, decision/failure reasons, and status transitions.
- Statutory discount audit report exposes validation, evidence, payable basis, policy, amount, correlation, and access-evaluation summaries.
- Policy import review has a separate review queue and role-based decision trail.

Auditability gaps:

- Full linkage from discount validation to payment confirmation and Sales Invoice/fiscal document is not fully proven from current source inspection.
- Side-effect flags comparable to the newer fiscal void audit posture are not consistently exposed for statutory discount rows.
- Some audit surfaces still show technical identifiers rather than business-friendly values.
- Raw evidence is intentionally not stored/displayed; this is safe but means human evidence review depends on external/reference workflows.

## RBAC/security

Observed security controls:

- Operator Console action catalog includes statutory discount actions:
  - `SESSION_LOOKUP`
  - `CREATE_STATUTORY_DISCOUNT_DRAFT`
  - `VIEW_STATUTORY_DISCOUNT_DRAFT`
  - `DECIDE_STATUTORY_DISCOUNT`
  - `CAPTURE_EVIDENCE`
  - `VIEW_EVIDENCE`
  - `APPLY_STATUTORY_DISCOUNT_PAYABLE_BASIS`
  - `VIEW_POLICY_RESOLUTION`
  - `VIEW_AUDIT_REPORT`
  - supervisor review/override actions.
- Access evaluation checks HR identity, device trust/binding, device assignment, shift context, site context, and action support.
- Policy import review uses explicit Central PMS RBAC policy mappings:
  - `operator-console.policy-import-review.submit`
  - `operator-console.policy-import-review.view-own`
  - `operator-console.policy-import-review.review`
  - `operator-console.policy-import-review.manage`
  - role-specific approval permissions.

RBAC/security gaps:

- Core statutory discount workflow permissions are primarily action/access-evaluation driven in the inspected code. A clean explicit RBAC policy catalog equivalent for view/request/review/apply/report statutory discount actions was not confirmed.
- Before production or formal UAT, verify that view-only, reviewer, approver, evidence-capture, apply-payable-basis, and audit-report duties are separated as intended.
- No authorization bypass was observed in this audit, but permission posture should be explicitly contract-tested for UAT users.

## Tests and evidence

Existing focused test areas found:

| Test area | Coverage observed |
| --- | --- |
| Draft service/API | Draft creation, duplicate/validation behavior, queue/detail reads. |
| Evidence service/API | Metadata-only evidence capture/listing and required evidence status. |
| Decision service/API | Approve/reject transitions and blocked/conflict behavior. |
| Payable basis application | Approval/evidence/tariff/payment preconditions and applied amount behavior. |
| Policy resolution | Policy readiness and fallback behavior. |
| Policy import review | Dry-run, review queue, decision permissions. |
| Operator Console UI | Queue/detail/evidence/decision/apply/audit/policy-import workflows in `App.test.tsx`. |
| Payment integration | Applied tariff snapshot used by payment attempts; stale original basis rejected. |
| Fiscal mapper | Discount references/privilege details mapped safely, sensitive payload terms rejected. |
| E2E-style statutory flow | Source tests show policy resolution -> draft -> evidence -> approve -> apply payable basis. |

Validation run for this audit:

- No focused statutory tests were run because this task is source audit/doc-only and requested no implementation changes.
- Requested repository validation was run after document creation; results are recorded in the Validation section.

Evidence gaps:

- No current merged manual statutory discount UAT/smoke result was confirmed during this audit.
- No runtime proof was confirmed for approved statutory discount -> payment -> POS Server Sales Invoice -> Operator Console fiscal visibility.
- No complete legal-rule correctness suite was confirmed for local ordinances/caps/exclusions/stacking variations.

## UAT readiness assessment

| Area | Readiness | Rationale |
| --- | --- | --- |
| Operator workflow | Partially ready | Queue/detail/evidence/decision/apply pages exist and are tested, but UX and role separation need UAT validation. |
| Business rule correctness | Partially ready | Core Senior/PWD style policy metadata exists; applied formula is fixed and broader rule variations are not proven. |
| Computation correctness | Partially ready | Focused computation/payment tests exist; statutory legal variants and fiscal totals need proof. |
| Payment integration | Partially ready | Payment attempts can use applied payable basis and reject stale original basis. |
| Sales Invoice/fiscal integration | Not ready / needs verification | Mapper support exists, but end-to-end fiscal payload correctness is not confirmed. |
| Audit/reconciliation | Partially ready | Audit report exists; payment/fiscal linkage and side-effect reporting need strengthening. |
| RBAC | Partially ready | Access evaluation exists; explicit permission separation for UAT users needs confirmation. |
| Policy import/versioning | Partially ready | Dry-run/review exists; activation/import/version promotion is not implemented. |
| Manual smoke/UAT evidence | Unknown / needs verification | No current manual statutory discount smoke evidence was confirmed from source inspection. |

Overall classification: `PARTIALLY_READY`

The statutory discount capability is ready for targeted engineering validation slices, but not ready for broad controlled UAT execution until computation, RBAC, payment/Sales Invoice handoff, and evidence records are proven.

## Known gaps and risks

1. Legal/business-rule coverage is not complete from source inspection. The apply path uses a fixed VAT-exclusive statutory discount formula and does not prove all configured policy metadata is enforced in computation.
2. End-to-end Sales Invoice/POS Server fiscal payload correctness for statutory discounts is not confirmed.
3. Payable-basis application persistence alignment should be verified because schema objects exist but the inspected writer path can return no payable-basis application ID.
4. Core statutory discount RBAC permissions are less explicit than newer fiscal void/reporting policies.
5. Policy import review approval does not activate production policy rows; that is a deliberate boundary but a blocker for production policy lifecycle.
6. Operator/audit UX still contains technical identifiers and could be improved before real user UAT.
7. Raw evidence is not stored, by design; the external evidence/reference review process must be operationally defined before UAT.
8. Manual controlled UAT evidence for statutory discounts was not confirmed.

## Recommended next high-value slices

1. Define and test the statutory discount computation contract for Senior Citizen and PWD parking cases, including VAT, rounding, caps, free-period/local-ordinance behavior, and unsupported-policy fail-closed behavior.
2. Align payable-basis application persistence with the schema-level application table/routine and ensure every applied discount has a durable application ID and audit trail.
3. Prove the end-to-end flow: approved statutory discount -> applied payable basis -> payment attempt amount -> payment confirmation -> POS Server Sales Invoice payload/lines/taxes/discount privilege details.
4. Harden statutory discount RBAC contracts for view, request, evidence capture, approve/reject, apply payable basis, audit report, and policy import review.
5. Create a controlled non-production UAT fixture and smoke result for one Senior Citizen or PWD statutory discount case.
6. Add targeted audit/reconciliation enrichment linking statutory validation, applied payable basis, payment, and Sales Invoice/fiscal document where safely derivable.
7. Defer broader UI polish until the computation/payment/fiscal proof is complete, except for any UX issue that blocks operator UAT execution.

## Files changed

Created:

- `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Statutory_Discounts_Current_State_Audit_v1.0.md`

No source code, schema, tests, runtime configuration, or POS Server files were modified.

## Validation

Commands:

```powershell
git diff --check
git status --short --untracked-files=all
```

Focused tests:

- Not run. This was a codebase/current-state audit only and no runtime/source behavior was changed.

Validation result:

- `git diff --check` passed.
- `git status --short --untracked-files=all` showed only this new audit report.
