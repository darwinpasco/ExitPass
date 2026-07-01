# ExitPass Continuity System Design v1.0 Review

## 1. Review Summary

Reviewed `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md` against the orchestration plan, specialist input packs, approved v1.3 baseline documents, companion BRDs/designs, and the generated continuity system-design diagrams.

No blocking issues were found. The draft preserves the required continuity authority model, keeps Continuity as explicit controlled degraded operation rather than silent fallback, maintains Central PMS/POS Server/vendor/connector/channel/governance/reporting boundaries, and defers API, database, event, runbook, UAT, timer, threshold, and implementation details.

## 2. Files Reviewed

- `docs/v1.3/continuity/ExitPass_Continuity_System_Design_v1.0.md`
- `docs/v1.3/continuity/system-design/ExitPass_Continuity_System_Design_Orchestration_Plan.md`
- `docs/v1.3/continuity/system-design/input-packs/01_continuity_authority_scope_guard.md`
- `docs/v1.3/continuity/system-design/input-packs/02_degraded_workflow_and_state.md`
- `docs/v1.3/continuity/system-design/input-packs/03_reconciliation_manual_release_fiscal_exception.md`
- `docs/v1.3/continuity/system-design/input-packs/04_diagram_planning.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/continuity/system-design/diagrams/`

## 3. Continuity Architecture Review

The architecture overview is aligned with the orchestration plan and input packs. It treats Continuity as a platform-level controlled degraded-operation capability spanning Central PMS, Vendor PMS/HCP, connectors, projection, Payment Orchestrator, Site POS Server, Assisted Payment Terminal / Continuity Terminal, Operator Console, Management Dashboard, gate/exit execution, audit, and reconciliation.

The draft does not introduce a separate alternate authority chain. It explicitly states that Continuity is not an alternate normal mode and that silent fallback and automatic fallback are prohibited.

## 4. Operating-State Review

The draft preserves the conceptual operating states required by the plan: Normal, Degraded-watch, Degraded-active, Continuity Terminal active, Restoration-in-progress, Post-restoration review, and Closed / reconciled.

The states are correctly described as design concepts only and are not converted into database enum values, API statuses, event payloads, timer rules, alert thresholds, UI screen names, or runbook procedures.

## 5. Authority Boundary Review

The authority model is preserved:

- Central PMS remains authority for payment-linked state, TariffSnapshot / payable-basis recording, payment finality, fiscal issuance reference recording, degraded resolve decisioning under approved policy, and ExitAuthorization.
- Vendor PMS/HCP remains normal raw session lifecycle and normal tariff computation authority.
- POS Server remains resolved Site fiscal issuance authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- Operator Console remains non-payment governance.
- Management Dashboard remains visibility/reporting only.

No authority drift was found.

## 6. Central PMS / Vendor PMS / Connector Boundary Review

The draft keeps Vendor PMS/HCP as normal session and tariff authority and keeps the Vendor PMS Connector / HikCentral Connector as a fact, health, projection freshness, availability, and normalized-outcome reporter.

The connector is not allowed to approve degraded resolve, invent tariff, treat stale projection as usable by itself, declare payment finality, issue fiscal documents, issue ExitAuthorization, or operate gates. Central PMS owns degraded resolve decisioning under approved Continuity policy.

## 7. POS Server / Fiscal Issuance Boundary Review

The draft preserves the resolved Site POS Server as fiscal issuance authority and uses Sales Invoice terminology consistent with POS/Invoicing.

Fiscal issuance failure or timeout does not automatically authorize exit. Payment finality is not automatically reversed by fiscal failure. Normal ExitAuthorization remains blocked until fiscal prerequisites succeed unless a separately approved exception/manual-release policy applies.

## 8. Assisted Payment Terminal / Continuity Terminal Boundary Review

The draft correctly positions Continuity Terminal as a restricted degraded/BCP mode of the Assisted Payment Terminal app family. It is disabled by default and enabled only under approved continuity activation scope for authorized terminals, users, Sites/Site Groups, and workflows.

The terminal displays backend-returned degraded context and status. It does not declare payment finality, issue fiscal documents, approve discounts, issue ExitAuthorization, or open gates.

## 9. Operator Console and Management Dashboard Boundary Review

Operator Console is consistently treated as separate non-payment governance. It may support activation approval, fiscal exception review, manual release review, evidence review, audit, and post-restoration review, but it must not collect payment, declare payment finality, issue Sales Invoices, issue ExitAuthorization, directly open gates, or treat projection as approval.

Management Dashboard is consistently treated as visibility/reporting only. It may show source-labeled continuity, connector, projection, fiscal exception, payment uncertainty, manual release, vendor acknowledgment, and reconciliation visibility. It does not become payment, fiscal, discount, exit, or reconciliation authority.

## 10. Degraded Workflow and Projection Freshness Review

The degraded workflow posture is aligned with the specialist input. Degraded-watch is visibility and restriction posture, not permission for degraded payment, fiscal bypass, manual release, or exit.

Projection is used only for operational visibility and controlled degraded support. It is explicitly not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority. Stale, ambiguous, insufficient, unavailable, conflicting, or unmapped projection fails closed or routes to approved governance.

## 11. Payment Uncertainty / Vendor Acknowledgment Review

Payment uncertainty is correctly modeled as pending/exception until verified provider outcome is accepted by Central PMS. Payment Orchestrator reports verified outcomes but does not declare platform finality.

Vendor payment acknowledgment failure is downstream of Central PMS payment finality and fiscal prerequisites. Vendor paid state and HCP `parkingfee/confirm` are not treated as ExitPass payment finality. Unknown acknowledgment outcomes are not blindly retried; idempotency and safe confirmation remain deferred.

## 12. Fiscal Exception / Pending Exit / Manual Release Review

The draft preserves the two independent facts required by the input pack: Central PMS payment finality may be true while fiscal issuance remains pending, failed, timed out, unknown, or missing fiscal reference.

Manual release is treated as last-resort governed exception, not normal ExitAuthorization. It requires governance, reason/incident/audit/reconciliation tagging, attribution, and post-review where policy applies. It does not close payment, fiscal, vendor acknowledgment, gate, settlement, or reconciliation gaps by itself.

## 13. Post-Restoration Reconciliation Review

The draft requires continuity-origin activity to enter post-restoration review after restoration or deactivation. Returning to normal operation does not close continuity-origin records.

The reconciliation categories cover activation/deactivation, affected Site/Site Group, affected dependency, projection freshness, degraded basis, payment attempts, provider outcomes, Central PMS payment finality, POS fiscal issuance, Central PMS fiscal references, vendor acknowledgment, gate outcomes, manual releases, statutory discount activity, Operator Console actions, dashboard/export access, and settlement/revenue assurance context.

## 14. Observability / Audit / Reporting Review

The draft requires observability across vendor availability, connector health, projection freshness, mapping ambiguity, live resolve/fee availability, degraded states, Continuity Terminal activation, payment uncertainty, fiscal exceptions, vendor acknowledgment backlog, gate issues, manual release, and reconciliation backlog.

Audit requirements support reconstruction of activation/deactivation authority, affected scope, projection context, degraded payable basis, payment/fiscal/vendor/gate/terminal/governance facts, manual release approval, customer/operator message state, and unresolved reconciliation items.

## 15. Diagram Coverage Review

The diagram folder `docs/v1.3/continuity/system-design/diagrams/` contains:

- 13 `.puml` files.
- 13 `.jpg` files.
- 0 `.png` files.

The diagrams are conceptual and aligned to the diagram-planning input pack. The reviewed PUML files do not include secrets, DTOs, database tables, enum values, endpoint maps, event payloads, implementation classes, timer values, thresholds, or runbook steps. They label authority boundaries and non-authority surfaces clearly.

## 16. Open Questions and Deferrals Review

The draft carries forward open questions for activation authority, activation/deactivation workflow, projection freshness thresholds, connector health/freshness labels, degraded tariff owner and rules, offline payment/fiscal policy, fiscal exception release policy, manual release policy, vendor acknowledgment policy, HCP `cardNum`, HCP `parkingfee/confirm`, POS Server deployment/service boundary, exact APIs/DTOs, database changes, event payloads, engineering implementation, UAT scripts, and runbook procedures.

No endpoint paths, DTOs, database tables/columns, database enum values, event payloads, implementation classes, thresholds, timers, runbook steps, or UAT scripts are finalized in the draft.

## 17. Risky Terminology Scan

Risky terminology was searched in the draft and system-design diagrams.

Safe contextual / explicit prohibition:

- `automatic fallback` and `silent fallback` appear as prohibited concepts.
- `projection as source of truth` appears as an explicit prohibition.
- `vendor paid state` and `parkingfee/confirm` appear only to reject ExitPass payment finality or to preserve HCP open questions.
- `Operator Console collect payment / open gates` appears only in prohibition language.
- `offline payment` and `offline fiscal issuance` appear only as not approved/open/deferred.
- `secret` appears only in a statement that the document does not approve secrets.
- `key` appears only for unresolved HCP lookup-key context.

No unsafe occurrences found:

- `fallback payment without activation`
- `projection as tariff truth`
- `projection as payment/fiscal/exit truth`
- `connector approves degraded resolve`
- `connector authorizes exit`
- `Continuity Terminal normal mode`
- `Continuity Terminal always available`
- `manual release equals ExitAuthorization`
- `fiscal failure authorizes exit`
- `vendor paid state means ExitPass payment finality`
- `parkingfee/confirm means ExitPass payment finality`
- `offline fiscal issuance approved`
- `offline payment approved`
- `Official Receipt`
- standalone `OR`
- credential-bearing `token`, `certificate`, or secret material

## 18. Issues Found

No blocking issues found.

## 19. Required Fixes, if any

None.

## 20. Nice-to-Have Fixes, if any

- Consider tightening the Management Dashboard boundary wording so the phrase "unless a later approved policy explicitly assigns a limited workflow action" cannot be read as approval within this v1.0 Continuity System Design. The phrase is source-aligned, but a short qualifier such as "outside this design's approval scope" would make the deferral more explicit.

## 21. Recommendation

Recommended to proceed with the draft as a review-passed Continuity System Design v1.0 companion document, subject only to the non-blocking nice-to-have wording clarification above.
