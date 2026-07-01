# ExitPass System Design v1.3 Consistency Review

## 1. Review Summary

Review result: conditionally approved for commit preparation.

The draft `docs/v1.3/ExitPass_System_Design_v1.3.md` is consistent with the scope guard, authority model, traceability map, workflow/state input, security/RBAC input, observability/operations input, diagram inventory, and approved BRD baseline note.

No blocking System Design content issues were found. The draft preserves the v1.2 top-level outline family, reads as a controlled v1.3 successor, stays at architecture level, preserves required authority boundaries, preserves downstream deferrals, and references all 11 required system-design diagrams.

Validation caveat: before this review note was created, the working tree already contained the untracked draft SDD and diagram files from the drafting step. Therefore `git status --short --untracked-files=all` cannot show only this review note unless those pre-existing untracked files are committed, staged elsewhere, or otherwise already tracked in the reviewer environment. This review did not modify those files.

## 2. Files Reviewed

Primary draft reviewed:

- `docs/v1.3/ExitPass_System_Design_v1.3.md`

Review inputs:

- `docs/v1.3/system-design/input-packs/07_scope_guard_and_consistency_review.md`
- `docs/v1.3/system-design/input-packs/01_authority_model_review.md`
- `docs/v1.3/system-design/input-packs/02_traceability_map.md`
- `docs/v1.3/system-design/input-packs/03_workflow_and_state_input.md`
- `docs/v1.3/system-design/input-packs/04_security_trust_and_rbac_input.md`
- `docs/v1.3/system-design/input-packs/05_observability_reporting_and_operations_input.md`
- `docs/v1.3/system-design/input-packs/06_diagram_inventory_and_puml_inputs.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

Diagram files reviewed by inventory:

- `docs/v1.3/diagrams/system-design/D-01_ExitPass_v1.3_Logical_Architecture.puml`
- `docs/v1.3/diagrams/system-design/D-02_Authority_Boundary_Model.puml`
- `docs/v1.3/diagrams/system-design/D-03_Site_Group_Site_VendorSystem_Connector_POS_Topology.puml`
- `docs/v1.3/diagrams/system-design/D-04_Normal_Payment_to_Exit_Sequence.puml`
- `docs/v1.3/diagrams/system-design/D-05_Payment_Finality_Fiscal_Issuance_ExitAuthorization_Sequence.puml`
- `docs/v1.3/diagrams/system-design/D-06_Vendor_PMS_Connector_Projection_Freshness_Flow.puml`
- `docs/v1.3/diagrams/system-design/D-07_Degraded_Resolve_and_Continuity_Sequence.puml`
- `docs/v1.3/diagrams/system-design/D-08_Assisted_Payment_Terminal_Context_and_Modes.puml`
- `docs/v1.3/diagrams/system-design/D-09_Operator_Console_Governance_Boundary.puml`
- `docs/v1.3/diagrams/system-design/D-10_Management_Dashboard_Source_of_Truth_Boundary.puml`
- `docs/v1.3/diagrams/system-design/D-11_Audit_Event_Outbox_Conceptual_Flow.puml`

## 3. v1.2 Outline / Style Alignment

The draft preserves the required v1.2 top-level outline family:

1. Document Control
2. System Overview
3. System Context
4. System Architecture
5. Trust Boundaries
6. Core Workflows
7. Event Architecture
8. State Machines
9. Data Architecture
10. API Architecture
11. Security Architecture
12. Failure Mode Architecture
13. Deployment Architecture
14. Observability
15. Business Continuity
16. Operational Runbooks
17. Appendix

The draft reads as a controlled v1.3 successor. It explicitly states the controlled-successor posture, uses the approved BRD baseline as business authority, and keeps downstream API, database, engineering, test/UAT, runbook, and BIR/accreditation content out of scope.

## 4. Authority Boundary Review

Pass.

The draft preserves the required authority boundaries:

- Vendor PMS/HCP remains authority for raw parking session lifecycle and normal tariff computation.
- Central PMS remains authority for payment-linked platform control state, TariffSnapshot recording, PaymentAttempt, PaymentConfirmation/payment finality, fiscal reference recording, degraded decisions under approved policy, and ExitAuthorization.
- Payment Orchestrator reports verified provider outcomes and does not declare platform payment finality.
- POS Server remains resolved Site fiscal issuance authority and does not declare payment finality or issue ExitAuthorization.
- WebPay remains a customer payment surface.
- Assisted Payment Terminal remains payment-capable but not finality, fiscal, discount-policy, or exit authority.
- Cashier-Assisted Terminal captures statutory validation inputs only.
- Continuity Terminal is disabled by default and restricted to degraded/BCP operation.
- Operator Console remains non-payment governance.
- Management Dashboard remains visibility/reporting only.
- Projection remains operational visibility and controlled degraded support only.
- Gate/exit execution consumes Central PMS authorization.
- Fiscal issuance must succeed before normal ExitAuthorization unless an approved exception policy applies.

No authority leakage was found.

## 5. Site Group / Site / VendorSystem / Connector Review

Pass.

The draft preserves the approved terminology and boundaries:

- Site Group is customer lookup/payment scope.
- Site is reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary.
- Default one Site Group to one Site and special one Site Group to multiple Sites are covered.
- Physical parking lot is not treated as automatically equivalent to ExitPass Site.
- VendorSystem, AdapterMapping, adapter codebase, and connector instance remain distinct.
- HCP ParkingLotIndexCode is explicitly treated as vendor-side only and not ExitPass `site_id`.
- Runtime vendor object identity remains conceptual and does not become database/API design.

No Site/Site Group or connector terminology drift was found.

## 6. Payment / Fiscal / ExitAuthorization Sequence Review

Pass.

The normal sequence is correctly preserved:

1. Scope/session resolution.
2. Vendor-authoritative tariff result.
3. Central PMS TariffSnapshot/payable basis.
4. PaymentAttempt.
5. Payment Orchestrator provider interaction and verified outcome reporting.
6. Central PMS platform payment finality.
7. Resolved Site POS Server Sales Invoice issuance.
8. Central PMS fiscal reference recording.
9. Central PMS ExitAuthorization.
10. Gate/exit consumption and outcome reporting.

The draft does not state or imply that payment provider success, WebPay success, terminal success, or POS Server fiscal issuance directly authorizes exit. Fiscal issuance failure/timeout is correctly modeled as a controlled exception that blocks normal ExitAuthorization.

## 7. Projection / Connector / Degraded Mode Review

Pass.

Projection is consistently labeled as operational visibility and controlled degraded support only. The draft does not treat projection, passageway records, connector health, or freshness labels as financial truth, tariff truth, payment finality, fiscal truth, discount approval, or exit authority.

Continuity and degraded operation are explicit, scoped, audited, reconciliation-tagged, and not silent fallback. The draft preserves open questions for exact freshness thresholds, degraded tariff basis/owner, HCP connector topology, connector health/freshness modeling, and Continuity/BCP activation authority.

## 8. Assisted Payment Terminal / Operator Console / Dashboard Boundary Review

Pass.

The draft keeps the boundaries clean:

- Assisted Payment Terminal is a payment-capable terminal app family, not an independent POS or authority service.
- Cashier-Assisted Terminal captures inputs and displays backend status.
- Continuity Terminal is disabled by default and restricted to controlled degraded/BCP mode.
- Operator Console supports governance, review, fiscal exception review, manual release governance, and evidence workflows, but does not collect payment, issue Sales Invoices, declare finality, issue ExitAuthorization, or directly open gates.
- Management Dashboard and Reporting is visibility/reporting/export only and preserves source/freshness/authority labels.

No boundary drift was found.

## 9. Security / RBAC / Evidence / Privacy Review

Pass.

The draft covers public WebPay scope binding, service identities, device/terminal trust, Operator Console trusted access, POS Server trust boundary, Payment Orchestrator/provider boundary, Vendor PMS/HikCentral credential boundary, gate/device trust, RBAC domains, evidence/privacy controls, export controls, secrets posture, audit, and non-repudiation.

It properly defers the final certificate model, mTLS topology, OAuth scopes, secrets storage implementation, terminal key storage, QR/digital Sales Invoice URL token model, exact evidence retention periods, and exact role-to-action permission matrix.

## 10. Observability / Continuity / Runbook Posture Review

Pass.

The draft covers connector health, projection freshness, Vendor PMS availability, POS Server health and fiscal backlog, payment provider uncertainty, gate/exit health, continuity/degraded visibility, terminal health, Operator Console governance visibility, dashboard/reporting source labels, audit/event correlation, reconciliation backlog, and stale warnings.

Continuity is treated as controlled degraded operation with activation scope, affected dependency, incident/BCP reference, allowed workflow scope, audit tag, reconciliation tag, post-restoration review, and fail-closed defaults.

Operational Runbooks remains a posture section only. It identifies future runbook areas but does not draft step-by-step procedures.

## 11. Diagram Coverage Review

Pass.

All 11 required diagrams are referenced in the draft and exist as both `.puml` and `.jpg` files under `docs/v1.3/diagrams/system-design/`:

| ID | Required diagram | `.puml` | `.jpg` |
| --- | --- | --- | --- |
| D-01 | ExitPass v1.3 Logical Architecture | Present | Present |
| D-02 | Authority Boundary Model | Present | Present |
| D-03 | Site Group / Site / VendorSystem / Connector Instance / POS Server Topology | Present | Present |
| D-04 | Normal Payment-to-Exit Sequence | Present | Present |
| D-05 | Payment Finality to Fiscal Issuance to ExitAuthorization Sequence | Present | Present |
| D-06 | Vendor PMS Connector Projection and Freshness Flow | Present | Present |
| D-07 | Degraded Resolve and Continuity Sequence | Present | Present |
| D-08 | Assisted Payment Terminal Context and Modes | Present | Present |
| D-09 | Operator Console Governance Boundary | Present | Present |
| D-10 | Management Dashboard Source-of-Truth Boundary | Present | Present |
| D-11 | Audit, Event, and Outbox Conceptual Flow | Present | Present |

The diagrams remain at system-design level and do not become database diagrams, API route diagrams, implementation class diagrams, or device SDK diagrams.

## 12. Open Questions and Deferrals Review

Pass.

The draft preserves the required open questions and downstream deferrals, including:

- WebPay URL slug registry.
- Whether WebPay slugs resolve to Site Group, Site, or both.
- Site Group user-facing terminology.
- Physical parking lot/cluster modeling.
- HCP connector topology and health/freshness model.
- Projection freshness thresholds.
- Degraded tariff basis and owner.
- Continuity/BCP activation authority.
- Manual release policy.
- Vendor acknowledgment retry/exit-block policy.
- POS Server deployment/registration model and whether it is a module or separate service.
- Fiscal numbering, counters, sequence gaps, BIR/accreditation identity assignment, tax/VAT treatment, Diplomat VAT treatment, and digital Sales Invoice URL security model.
- Terminal final implementation architecture and device trust/key storage model.
- Operator Console trust mechanism.
- Dashboard/reporting implementation, export controls, and retention.
- Exact API endpoints, DTOs, database deltas, event payloads, engineering implementation, Test/UAT coverage, and runbook procedures.

No open item was silently closed.

## 13. Scope Creep Check

Pass.

The draft stays at architecture level. The scan found terms such as endpoint paths, DTOs, event payload schema, queue names, and runbook procedures only in explicit non-scope or deferral language.

The draft does not define:

- Final endpoint paths.
- Final DTOs.
- Final database tables or columns.
- Final event payload schemas.
- Final implementation classes.
- Final runbook procedures.
- BIR accreditation package content.
- Deployment scripts.
- Test/UAT cases.

Risky terminology review:

- `EC Device`: not found.
- `Cashier POS`: not found.
- `Official Receipt` / `OR` as primary fiscal output: not found.
- `HCP site`: not found.
- `projection as source of truth`: not found.
- `payment success equals exit authorization`: not found.
- `POS Server confirms payment`: not found.
- `Operator Console override payment`: not found.
- `automatic fallback`: not found.
- `silent fallback`: appears only in approved negative statements.
- `offline fiscal issuance approved`: not found.
- `BIR approved`: not found.

## 14. Issues Found

No blocking content issues were found in the draft SDD.

Validation issue outside SDD content: the working tree already contained untracked SDD and diagram files before this review note was created. This prevents the validation expectation "Only this review note is added" from being literally true in the current working tree. This review did not modify those pre-existing files.

## 15. Required Fixes, if any

No required SDD content fixes.

Repository state action required before commit, depending on intended workflow:

- If the SDD and diagrams are intended to be part of the same commit, include them with this review note in that commit after approval.
- If this review task was expected to add only the review note against already tracked draft artifacts, first ensure the draft SDD and diagrams are tracked or committed in the expected baseline.

## 16. Recommended Nice-to-Have Fixes, if any

No nice-to-have fixes are required before commit.

Optional later improvements:

- Add line-level cross-reference notes from the SDD appendix to the seven input packs if reviewers want tighter auditability.
- Add a short reviewer checklist table in a future approval note, not in the SDD itself.

## 17. Recommendation

Recommendation: approve the draft for commit preparation once the repository state is normalized.

The SDD content is consistent with the scope guard and specialist input packs. It preserves authority separation, terminology discipline, deferral discipline, required diagram coverage, and controlled-successor posture. The only issue is working-tree state: the current `git status` includes the pre-existing untracked draft SDD and diagram artifacts in addition to this review note.
