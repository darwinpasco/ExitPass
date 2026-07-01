# ExitPass Continuity System Design v1.0

Status: Draft companion technical design for v1.3

## 1. Document Control

### Version History

| Version | Date | Description |
| --- | --- | --- |
| v1.0 | 2026-07-02 | Initial Continuity companion System Design covering controlled degraded operation, conceptual operating states, activation, projection eligibility, Continuity Terminal restricted operation, payment/fiscal/exit exceptions, manual release governance, reconciliation, observability, and authority guardrails. |

### Document Ownership

| Role | Owner |
| --- | --- |
| Documentation stream | ExitPass v1.3 documentation |
| Lead design owner | Lead Continuity Design agent |
| Downstream consumers | API Contract Pack, Database Delta, Engineering Pack, Test/UAT Pack, Operations Runbook Pack, and continuity implementation planning |

### Approval Posture

This document is a companion System Design. It does not approve endpoint paths, DTOs, database tables, database enum values, event payloads, queue names, retry counts, timer values, alert thresholds, implementation classes, UI wireframes, final screen names, POS Server recovery internals, fiscal counter mechanics, runbook procedures, UAT scripts, or secrets.

## 2. Executive Summary

ExitPass Continuity is the controlled degraded-operation capability for situations where normal dependencies are unavailable, stale, ambiguous, unknown, or unsafe. It protects the normal authority model while allowing explicitly approved, scoped, audited, incident-tagged, reconciliation-tagged, and reviewable operation.

Continuity is not an alternate normal mode. Silent fallback and automatic fallback are prohibited concepts. Continuity activation must be explicit and controlled.

The design preserves:

- Vendor PMS/HCP as normal raw session and tariff authority.
- Central PMS as degraded resolve, payment finality, fiscal reference, and ExitAuthorization authority.
- POS Server as resolved Site fiscal issuance authority.
- Connectors as vendor fact, health, projection freshness, availability, and normalized outcome reporters.
- Operator Console as non-payment governance.
- Management Dashboard and Reporting as visibility only.

## 3. Design Purpose and Scope

This design defines the system-level continuity posture and workflows for ExitPass v1.3.

In scope:

- Continuity architecture and component boundaries.
- Conceptual operating states.
- Dependency degradation detection.
- Continuity activation, deactivation, and scope control.
- Vendor PMS/HCP live resolve failure handling.
- Projection freshness and degraded eligibility.
- Degraded tariff/payable-basis posture.
- Continuity Terminal restricted operation.
- Payment uncertainty handling.
- Vendor payment acknowledgment failure handling.
- Fiscal issuance exception and payment-finality-but-fiscal-pending workflows.
- ExitAuthorization under continuity.
- Gate/exit issue handling.
- Manual release governance.
- Continuity-origin tagging, audit, reconciliation, and post-restoration review.
- Operator Console governance boundary.
- Management Dashboard visibility boundary.
- Observability, alert posture, fail-closed rules, deployment posture, open questions, and deferrals.

Out of scope:

- Source code, database schema, API contract, engineering implementation, runbook procedures, and UAT scripts.
- POS Server recovery internals, fiscal counter mechanics, offline fiscal approval, offline payment approval, endpoint design, DTOs, database enum values, event payloads, retry counts, timer values, alert thresholds, implementation classes, and UI wireframes.

## 4. Approved Baseline Inputs

| Source | Use |
| --- | --- |
| `docs/v1.3/continuity/system-design/ExitPass_Continuity_System_Design_Orchestration_Plan.md` | Scope, authority guardrails, operating-state guardrails, specialist ownership, and review gates. |
| `docs/v1.3/continuity/system-design/input-packs/01_continuity_authority_scope_guard.md` | Authority boundaries, non-authority matrix, risky terminology, and open questions. |
| `docs/v1.3/continuity/system-design/input-packs/02_degraded_workflow_and_state.md` | Conceptual state and degraded workflow guidance. |
| `docs/v1.3/continuity/system-design/input-packs/03_reconciliation_manual_release_fiscal_exception.md` | Fiscal exception, manual release, vendor acknowledgment, reconciliation, and audit guidance. |
| `docs/v1.3/continuity/system-design/input-packs/04_diagram_planning.md` | Diagram set, authority labels, component lists, and diagram risk controls. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 authority model and degraded operation boundaries. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Platform architecture, continuity section, failure modes, audit, observability, and deferrals. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approved BRD baseline posture and downstream open-question discipline. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Primary business source for Continuity. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Continuity Terminal business requirements. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md` | Continuity Terminal technical boundary and handoff posture. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Governance, supervisor review, fiscal exception review, manual release, and post-restoration review boundaries. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Visibility/reporting and source-of-truth labels. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Fiscal authority, Sales Invoice, fiscal exception, and offline fiscal restrictions. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md` | Connector health, projection, degraded handoff, and acknowledgment posture. |
| `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md` | HCP projection, `cardNum` uncertainty, and conditional `parkingfee/confirm` posture. |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved authority, degraded, fiscal, and continuity decisions. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Open questions preserved by this design. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Impact map for degraded resolve, Continuity, API/database/engineering, and Test/UAT work. |

Existing Continuity BRD diagrams under `docs/v1.3/continuity/diagrams/` were used as business input only.

## 5. Continuity Architecture Overview

Continuity spans platform control, integration health, terminal/channel behavior, fiscal exception posture, governance, visibility, audit, and reconciliation.

Primary components:

- Central PMS.
- Vendor PMS/HCP.
- Vendor PMS Connector / HikCentral Connector.
- Parking Session Projection.
- Payment Orchestrator.
- Resolved Site POS Server.
- Assisted Payment Terminal / Continuity Terminal.
- WebPay, APM, and other affected channels where policy allows.
- Operator Console.
- Management Dashboard and Reporting.
- Gate/exit execution.
- Audit/event capability.
- Reconciliation workflow.

Continuity uses connector facts, projection freshness, payment status, fiscal status, gate status, and governance context as inputs. Central PMS and approved Continuity policy decide what can proceed. Operator Console governs and reviews. Management Dashboard displays status and backlog with source labels.

## 6. Authority Model

| Function | Authority | Continuity posture |
| --- | --- | --- |
| Raw session lifecycle in normal mode | Vendor PMS/HCP | Preserved where vendor is available. |
| Normal tariff computation | Vendor PMS/HCP | Preserved in normal mode; degraded basis uses Central PMS-approved policy only. |
| Vendor facts, health, projection freshness, normalized outcomes | Vendor PMS Connector / HikCentral Connector | Report inputs only; no degraded approval authority. |
| Projection and payment-linked control state | Central PMS | Projection supports visibility and controlled degraded evaluation only. |
| Degraded resolve decision | Central PMS under approved Continuity policy | Central PMS decides whether degraded use is allowed. |
| TariffSnapshot / payable-basis recording | Central PMS | Records live vendor basis or approved degraded basis. |
| Statutory discount policy and validation persistence | Central PMS / Discount workflow | Remains backend authority. |
| Payment provider interaction | Payment Orchestrator or approved payment integration | Reports verified provider outcome only. |
| Platform payment finality | Central PMS | Not owned by provider, channel, connector, terminal, or dashboard. |
| Sales Invoice / fiscal issuance | Resolved Site POS Server | Fiscal authority remains POS Server. |
| Fiscal issuance reference recording | Central PMS | Links fiscal result to platform state. |
| ExitAuthorization | Central PMS | Issued only by Central PMS when eligible. |
| Manual release governance | Operator Console / approved operations workflow | Last-resort exception governance. |
| Gate/exit execution | Gate/exit system consuming Central PMS authorization | Must not bypass Central PMS except separately approved emergency process. |
| Reporting visibility | Management Dashboard and Reporting | Visibility only. |

## 7. Non-Authority Scope

Continuity shall not:

- Operate as automatic fallback.
- Operate as silent fallback.
- Treat projection as source of truth for payment, fiscal, tariff, discount, or exit.
- Let connectors approve degraded resolve.
- Let vendor paid state or HCP `parkingfee/confirm` create ExitPass payment finality.
- Let fiscal failure authorize exit automatically.
- Let manual release equal normal ExitAuthorization.
- Let Operator Console collect payment or directly open gates.
- Let Management Dashboard close reconciliation or become an authority.
- Approve unmanaged offline payment or unmanaged offline fiscal issuance.

## 8. Continuity Operating States

The operating states are conceptual design states only. They are not final database enum values, API statuses, event payloads, timers, alert thresholds, UI screen names, or runbook procedures.

| State | Meaning |
| --- | --- |
| Normal | No approved degraded operation is active for the scope. Normal authority model applies. |
| Degraded-watch | A dependency is degraded, stale, at risk, or under observation; continuity workflows are not active. |
| Degraded-active | Approved degraded controls are active for defined Site/Site Group/dependency/workflow scope. |
| Continuity Terminal active | Continuity Terminal mode is enabled only for authorized terminals, users, Sites/Site Groups, and workflows within activation scope. |
| Restoration-in-progress | Dependency recovery or deactivation is underway; continuity-only workflows are being wound down. |
| Post-restoration review | Continuity-origin activity is under reconciliation, audit review, exception review, and closure checks. |
| Closed / reconciled | Continuity event is closed and required review/reconciliation is complete. |

## 9. Normal Mode Posture

In Normal state:

- Vendor PMS/HCP provides raw session lifecycle and normal tariff computation.
- Central PMS records payment-linked state, TariffSnapshot, payment finality, fiscal reference, and ExitAuthorization.
- POS Server issues Sales Invoices and fiscal records.
- Connectors report health, availability, projection freshness, live fee results, and acknowledgment outcomes where enabled.
- Operator Console may show health and governance context without collecting payment.
- Management Dashboard may show source-labeled visibility.

Continuity readiness may be observable, but Continuity workflows are inactive.

## 10. Degraded-Watch State

Degraded-watch is visibility and restriction posture. It does not enable degraded payment, degraded tariff, fiscal bypass, manual release, or exit.

Entry examples:

- Vendor PMS/HCP degraded or unreachable.
- Connector stale/unavailable.
- Projection stale or ambiguous.
- POS Server fiscal exception.
- Payment provider uncertainty.
- Gate/exit issue.
- Vendor acknowledgment backlog.

Central PMS and approved health/continuity policy evaluate whether the condition remains watch-only or requires activation review.

## 11. Degraded-Active State

Degraded-active requires approved continuity activation for a defined scope.

The active scope should capture:

- Site and Site Group.
- Affected dependency.
- Incident/BCP reference.
- Activation reason.
- Approval context where required.
- Allowed workflows.
- Restricted workflows.
- Audit and reconciliation tags.

Central PMS evaluates every requested degraded workflow against activation scope, projection freshness, mapping status, approved tariff basis, payment state, fiscal state, and safety controls.

## 12. Continuity Terminal Active State

Continuity Terminal active means restricted Continuity Terminal mode is available within approved scope.

Rules:

- Disabled by default.
- Enabled only under approved continuity activation.
- Bound to authorized terminal, cashier/user, Site, Site Group, shift/session, and allowed workflow set.
- Displays backend-returned degraded context, freshness labels, payment restrictions, fiscal restrictions, and escalation guidance.
- Does not declare payment finality, issue fiscal documents, approve discounts, issue ExitAuthorization, or open gates.

## 13. Restoration-in-Progress State

Restoration-in-progress begins when affected dependencies appear restored or approved deactivation starts.

Posture:

- Disable or restrict continuity-only workflows.
- Restore normal live vendor/POS/payment/fiscal authority paths where safe.
- Preserve continuity-origin tags.
- Preserve pending exceptions, vendor acknowledgment backlog, payment uncertainty, fiscal exceptions, gate issues, and manual release records for review.
- Do not automatically close continuity-origin records.

## 14. Post-Restoration Review State

Post-restoration review reconciles and reviews continuity-origin activity before closure.

Review should cover:

- Activation/deactivation scope and authority.
- Affected dependency and incident/BCP reference.
- Projection-based resolves and degraded payable basis.
- Payment attempts, provider outcomes, payment finality, and payment uncertainty.
- POS fiscal outcomes and Central PMS fiscal references.
- Vendor acknowledgment outcomes.
- Gate outcomes and manual releases.
- Continuity Terminal activity.
- Statutory discount activity under continuity.
- Operator Console actions and dashboard/export access.

## 15. Closed / Reconciled State

Closed / reconciled means required continuity review and reconciliation are complete under approved authority.

Closure must not:

- Mutate payment finality.
- Invent fiscal records.
- Treat projection as financial truth.
- Hide unresolved vendor acknowledgment.
- Convert manual release into normal ExitAuthorization.
- Erase incident, audit, or reconciliation tags.

Exact closure authority, SLA, and labels remain open.

## 16. Dependency Degradation Detection

Dependency degradation may originate from:

- Vendor PMS/HCP availability or live fee failure.
- Connector health, authentication, permission, mapping, timeout, or unknown outcome.
- Projection freshness, ambiguity, insufficiency, or unavailable state.
- Payment provider timeout or uncertain outcome.
- POS Server fiscal issuance failure, timeout, or pending state.
- Gate/exit device failure or uncertain gate outcome.
- Vendor payment acknowledgment failure, unknown outcome, or backlog.

Connectors report facts and normalized outcomes. Central PMS and approved continuity policy interpret workflow eligibility. Operator Console and Management Dashboard may display warnings and context.

## 17. Continuity Activation and Scope Control

Continuity activation is an explicit controlled event.

Activation should record:

- Affected Site/Site Group.
- Affected dependency.
- Incident or BCP reference.
- Reason.
- Approval actor or policy trigger where required.
- Activation time.
- Allowed and restricted workflows.
- Continuity Terminal eligibility if applicable.
- Audit tag.
- Reconciliation tag.

If activation authority, scope, dependency identity, incident context, or allowed workflow scope is missing or unsafe, continuity must not activate. The system remains in Normal or Degraded-watch posture and affected workflows fail closed or route to governance.

## 18. Vendor PMS / HCP Live Resolve Failure Handling

When live Vendor PMS/HCP resolve or fee calculation fails:

1. Connector reports unavailable, timeout, unknown, ambiguous, permission failure, mapping failure, or fee unavailable state.
2. Central PMS evaluates normal retry/status confirmation where appropriate.
3. If approved continuity scope exists, Central PMS evaluates degraded resolve eligibility.
4. Central PMS uses projection and approved degraded basis only when policy, freshness, mapping, and sufficiency requirements pass.
5. Otherwise the flow fails closed or routes to governance.

HCP `cardNum`, ticket-only lookup key, and `parkingfee/confirm` behavior remain open vendor/deployment questions.

## 19. Projection Freshness and Degraded Eligibility

Projection is operational visibility and controlled degraded support only.

Eligibility evaluation should consider:

- Projection age/freshness.
- Mapping status.
- Ambiguity.
- Sufficiency for requested workflow.
- Affected Site/Site Group and vendor object context.
- Dependency state.
- Approved activation scope.
- Approved degraded tariff basis.

Stale, ambiguous, insufficient, unavailable, conflicting, or unmapped projection fails closed or routes to approved supervisor/manual review.

## 20. Degraded Tariff / Payable-Basis Handling

Normal tariff computation remains Vendor PMS/HCP authority.

If live tariff is unavailable and continuity policy allows degraded operation:

- Central PMS owns degraded payable-basis decisioning.
- Degraded tariff/payable basis must use approved tariff configuration or approved continuity basis.
- Degraded basis must not be invented from projection, passageway records, terminal history, dashboard values, connector heuristics, or cashier judgment.
- Degraded payable-basis use must be incident-tagged, audit-tagged, reconciliation-tagged, and distinguishable from normal vendor tariff calculation.

Exact configuration owner, rounding, grace rules, freshness threshold, and discount interaction remain open.

## 21. Continuity Terminal Restricted Operation

Continuity Terminal is a restricted degraded/BCP mode of Assisted Payment Terminal.

Allowed behavior depends on active continuity policy and backend authority:

- Display degraded context and projection freshness.
- Support restricted lookup only where allowed.
- Display approved degraded payable basis.
- Support restricted statutory discount handling only under approved degraded-mode policy.
- Initiate payment only where policy and backend state allow.
- Display POS Server fiscal status where available and allowed.
- Display Central PMS ExitAuthorization or blocked/pending status.
- Route exceptions to Operator Console or approved operations workflow.

Continuity Terminal remains disabled by default and cannot silently replace normal Vendor PMS/Central PMS authority.

## 22. Payment Uncertainty Handling

Payment uncertainty occurs when provider outcome is timeout, unknown, duplicate, conflicting, or not yet verified.

Rules:

- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- Central PMS does not record payment finality until outcome is verified and accepted.
- ExitAuthorization must not be issued based on uncertain payment.
- Customer/operator messaging must distinguish pending verification from payment received.
- Payment uncertainty remains audit- and reconciliation-visible.

## 23. Vendor Payment Acknowledgment Failure Handling

Vendor acknowledgment is downstream of Central PMS payment finality and required fiscal prerequisites where applicable.

Failure states may include failed, timeout, unavailable, unknown, duplicate, already-paid, conflicting, or not approved for deployment.

Rules:

- Connector reports acknowledgment outcome only.
- Vendor state does not create ExitPass payment finality.
- HCP `parkingfee/confirm` is conditional vendor acknowledgment only and remains disabled unless explicitly approved.
- Unknown acknowledgment must not be retried blindly without later-approved idempotency and safe confirmation posture.
- Acknowledgment failures are audit-tagged and reconciliation-tagged.

## 24. Fiscal Issuance Exception Handling

Fiscal exceptions occur when POS Server fiscal issuance fails, times out, is unknown, remains pending, or lacks Central PMS fiscal reference recording.

Rules:

- POS Server remains fiscal authority.
- Central PMS records fiscal reference but does not become fiscal issuer.
- Fiscal failure does not automatically reverse payment finality.
- Fiscal failure does not automatically authorize exit.
- Normal ExitAuthorization remains blocked until fiscal prerequisites succeed unless a separately approved exception/manual-release policy applies.
- Exact POS Server recovery internals and fiscal counter mechanics remain deferred.

## 25. Payment Finality but Fiscal Pending / Failed Workflow

The design preserves two independent facts:

- Central PMS payment finality may be true.
- Fiscal issuance may still be pending, failed, timed out, unknown, or missing fiscal reference.

Workflow:

1. Central PMS records payment finality after verified outcome.
2. Central PMS requests fiscal issuance from the resolved Site POS Server.
3. If POS Server issuance succeeds, Central PMS records fiscal reference and evaluates normal ExitAuthorization.
4. If fiscal issuance fails or remains pending/unknown, Central PMS blocks normal ExitAuthorization and starts controlled fiscal exception review.
5. APT/Continuity Terminal may display backend status only.
6. Operator Console or approved operations workflow may support review/escalation where policy allows.

## 26. ExitAuthorization Under Continuity

Central PMS remains the only ExitAuthorization issuer.

Continuity does not alter the base rule:

- Payment finality must be recorded by Central PMS.
- Fiscal prerequisites must be satisfied unless approved exception/manual-release policy applies.
- Session/payable-basis state must be safe under normal or approved degraded rules.
- ExitAuthorization must be issued by Central PMS before gate/exit execution consumes it.

Manual release, where allowed, is not normal ExitAuthorization.

## 27. Gate / Exit Issue Handling

Gate or exit issues do not alter payment truth, fiscal truth, vendor truth, or reconciliation requirements.

If Central PMS ExitAuthorization exists but gate execution fails, the issue is operational/gate-side and should be visible, auditable, and reconciled.

If Central PMS ExitAuthorization does not exist, a gate issue path must not bypass payment or fiscal prerequisites except under a formally approved manual emergency process.

Physical release outside normal authorization consumption must be treated as manual or emergency release exception with incident, audit, reconciliation, attribution, and post-review controls.

## 28. Manual Release Governance

Manual release is last-resort governed exception.

Manual release requires, where policy applies:

- Supervisor approval.
- Reason capture.
- Incident tag.
- Audit tag.
- Reconciliation tag.
- Operator/supervisor/device/Site/session attribution.
- Preservation of unresolved payment, fiscal, vendor acknowledgment, gate, and settlement items.
- Post-review.

Manual release must not silently convert stale projection, missing fiscal issuance, unresolved vendor acknowledgment, payment uncertainty, or gate issue into normal authority records.

## 29. Continuity-Origin Record Tagging

Continuity-origin activity must be identifiable from activation through closure.

Tagging concepts should include:

- Continuity event identity or reference.
- Affected Site/Site Group.
- Affected dependency.
- Incident/BCP reference.
- Operating state concept.
- Activation/deactivation context.
- Projection/degraded basis context where used.
- Payment, fiscal, vendor acknowledgment, gate, manual release, statutory discount, terminal, Operator Console, dashboard/export, and settlement/revenue assurance correlation.

Exact database fields, event names, enum values, table relationships, and payload schemas remain deferred.

## 30. Post-Restoration Reconciliation

After restoration or deactivation, continuity-origin activity enters post-restoration review.

Review lifecycle:

1. Confirm activation/deactivation scope and authority.
2. Review affected dependencies and incident/BCP reference.
3. Review degraded resolves and payable basis.
4. Review payments, provider outcomes, payment finality, and payment uncertainty.
5. Review fiscal outcomes and Central PMS fiscal references.
6. Review vendor acknowledgment outcomes.
7. Review gate outcomes and manual releases.
8. Review statutory discounts under continuity.
9. Review Operator Console governance actions and dashboard/export access.
10. Resolve or escalate exceptions.
11. Close/reconcile only under approved closure authority.

Returning to normal operation does not close continuity-origin records by itself.

## 31. Reconciliation Data Categories

Reconciliation should compare:

- Continuity activation/deactivation.
- Affected Site/Site Group.
- Affected dependency.
- Projection freshness and degraded basis.
- PaymentAttempt, provider outcome, and Central PMS payment finality.
- POS Server fiscal issuance.
- Central PMS fiscal reference.
- Vendor payment acknowledgment.
- Gate outcome.
- Manual release or emergency release.
- Statutory discount/entitlement activity.
- Operator Console governance actions.
- Management Dashboard/report/export access.
- Settlement/revenue assurance context.

Projection may explain an item but cannot close financial, fiscal, settlement, vendor acknowledgment, discount, or exit reconciliation by itself.

## 32. Audit and Non-Repudiation

Audit must support reconstruction of:

- Who activated/deactivated continuity and under what authority.
- What dependency, Site/Site Group, and workflow scope was affected.
- What projection/freshness/mapping context was used.
- What payable basis was accepted and why.
- What payment, fiscal, vendor acknowledgment, gate, terminal, and governance facts existed.
- Who approved manual release or exception handling.
- What customer/operator message state was displayed.
- What reconciliation items remained unresolved.

High-risk actions must be attributable, permissioned, tamper-evident at audit level, privacy-aware, and reviewable.

## 33. Operator Console Governance Boundary

Operator Console may support:

- Continuity activation approval where policy requires.
- Deactivation/post-restoration review.
- Incident tagging and reason capture.
- Fiscal exception review.
- Manual release review.
- Evidence review.
- Connector health/projection freshness visibility.
- Reconciliation support.

Operator Console must not collect payment, declare payment finality, issue Sales Invoices, mutate fiscal records, issue ExitAuthorization, directly open gates, or treat projection as approval.

## 34. Management Dashboard Visibility Boundary

Management Dashboard may show:

- Continuity state.
- Degraded-watch/degraded-active indicators.
- Continuity Terminal activation.
- Connector health and projection freshness.
- Fiscal exception backlog.
- Manual release counts.
- Payment uncertainty.
- Vendor acknowledgment backlog.
- Post-restoration review status.
- Reconciliation backlog.

Management Dashboard remains visibility/reporting only. It must not activate continuity, approve manual release, close reconciliation, declare finality, issue fiscal documents, approve discounts, mutate payable basis, issue ExitAuthorization, or open gates unless a later approved policy explicitly assigns a limited workflow action.

## 35. Observability, Health, and Alerts

Continuity observability should cover signal categories without defining final metric names or thresholds:

- Vendor PMS/HCP availability.
- Connector health.
- Projection freshness/staleness.
- Mapping ambiguity.
- Live resolve and fee calculation availability.
- Degraded-watch and degraded-active state.
- Continuity Terminal activation.
- Payment uncertainty.
- Fiscal issuance pending/failed/timed out.
- Vendor acknowledgment backlog.
- Gate/exit issue.
- Manual release activity.
- Post-restoration review and reconciliation backlog.

Alerts and dashboards must carry source, freshness, and authority labels.

## 36. Failure Modes and Fail-Closed Rules

Fail closed or route to approved governance when:

- Activation authority or scope is missing.
- Vendor PMS/HCP live resolve is unavailable and no approved degraded policy applies.
- Projection is stale, ambiguous, insufficient, unmapped, unavailable, or conflicting.
- Degraded tariff basis is missing or unsafe.
- Statutory discount entitlement, policy, evidence, or payable-basis recalculation is unsafe.
- Payment outcome is unknown.
- Fiscal issuance fails, times out, is unknown, or lacks fiscal reference.
- Vendor acknowledgment outcome is unknown or conflicting.
- Gate outcome is uncertain or gate is unavailable.
- Manual release is not approved.
- Site/Site Group/vendor/POS mapping is missing or ambiguous.

Fail-closed behavior prevents unmanaged degraded tariff, unmanaged offline fiscal issuance, unmanaged offline payment, unmanaged manual release, and continuity closure without review.

## 37. Deployment Posture

Deployment posture should support:

- Continuity disabled by default.
- Environment-specific continuity configuration.
- Site/Site Group/dependency scope.
- Connector health and freshness visibility.
- Operator Console governance.
- APT/Continuity Terminal restricted activation.
- POS Server fiscal authority preservation.
- Audit and reconciliation tagging.
- Management Dashboard visibility.
- Post-restoration review.

Exact infrastructure, topology, permissions, service boundaries, retry mechanics, timers, thresholds, runbook procedures, and UAT scripts remain deferred.

## 38. Open Questions and Deferred Decisions

| ID | Open question / deferred decision |
| --- | --- |
| CON-SD-OQ-001 | Exact BCP / continuity activation authority. |
| CON-SD-OQ-002 | Exact activation/deactivation workflow. |
| CON-SD-OQ-003 | Exact projection freshness threshold. |
| CON-SD-OQ-004 | Exact connector health states, freshness labels, stale thresholds, and alert rules. |
| CON-SD-OQ-005 | Degraded tariff configuration owner. |
| CON-SD-OQ-006 | Degraded tariff rounding and grace rules. |
| CON-SD-OQ-007 | Offline payment policy. |
| CON-SD-OQ-008 | Offline fiscal issuance policy. |
| CON-SD-OQ-009 | Fiscal issuance exception release policy. |
| CON-SD-OQ-010 | Manual release policy and emergency override boundary. |
| CON-SD-OQ-011 | Vendor payment acknowledgment sync/async/queue/retry/exit-block policy. |
| CON-SD-OQ-012 | Unknown vendor acknowledgment safe confirmation method. |
| CON-SD-OQ-013 | Reconciliation SLA, closure authority, and closure labels. |
| CON-SD-OQ-014 | HCP `cardNum` meaning and ticket-only lookup key. |
| CON-SD-OQ-015 | HCP `parkingfee/confirm` requirement and vendor state behavior. |
| CON-SD-OQ-016 | POS Server deployment, registration, and service boundary. |
| CON-SD-OQ-017 | Exact API endpoints and DTOs. |
| CON-SD-OQ-018 | Exact database changes. |
| CON-SD-OQ-019 | Exact event payloads. |
| CON-SD-OQ-020 | Exact engineering implementation. |
| CON-SD-OQ-021 | Exact UAT scripts. |
| CON-SD-OQ-022 | Exact runbook procedures. |

## 39. Requirements Traceability Summary

| Requirement area | Source | Design sections |
| --- | --- | --- |
| Authority model | ExitPass BRD, System Design, input pack 01 | 6, 7, 36 |
| Operating states | Continuity BRD, orchestration plan, input pack 02 | 8-15 |
| Activation and scope | Continuity BRD, input pack 02 | 17 |
| Vendor/connector degraded handling | Vendor PMS Connector design, HikCentral profile, input packs 01 and 02 | 16, 18, 19 |
| Degraded tariff/payable basis | Continuity BRD, System Design, input pack 02 | 20 |
| Continuity Terminal | APT BRD, APT System Design, input pack 02 | 12, 21 |
| Payment/fiscal/exit exceptions | POS/Invoicing BRD, System Design, input pack 03 | 22, 24, 25, 26 |
| Manual release | Continuity BRD, Operator Console BRD, input pack 03 | 27, 28 |
| Reconciliation and review | Management Dashboard BRD, input pack 03 | 29-32 |
| Governance and visibility | Operator Console BRD, Management Dashboard BRD | 33, 34 |
| Observability and alerts | System Design, connector design, input packs 02 and 04 | 35 |
| Diagrams | Input pack 04 | Appendix C |

## 40. Appendix A: Glossary

| Term | Definition |
| --- | --- |
| Continuity | Explicit controlled degraded-operation capability. |
| Degraded-active | Approved degraded controls active for a defined scope. |
| Degraded-watch | Dependency is degraded or at risk, but continuity workflows are not active. |
| ExitAuthorization | Central PMS-issued authorization consumed by gate/exit execution. |
| Manual release | Last-resort governed exception, not normal ExitAuthorization. |
| Projection | Operational visibility and controlled degraded support data only. |
| Site | Reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary. |
| Site Group | Customer lookup/payment scope. |
| TariffSnapshot | Central PMS-owned accepted payable-basis record. |

## 41. Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| APM | AutoPay Machine |
| APT | Assisted Payment Terminal |
| BCP | Business Continuity Plan |
| BRD | Business Requirements Document |
| HCP | HikCentral Professional |
| PMS | Parking Management System |
| POS | Point of Sale |
| UAT | User Acceptance Testing |

## 42. Appendix C: Diagram Index

| Diagram | File |
| --- | --- |
| CON-SD-D01 Continuity Logical Architecture | [CON-SD-D01_Continuity_Logical_Architecture.jpg](system-design/diagrams/CON-SD-D01_Continuity_Logical_Architecture.jpg) / [PUML](system-design/diagrams/CON-SD-D01_Continuity_Logical_Architecture.puml) |
| CON-SD-D02 Continuity Operating-State Model | [CON-SD-D02_Continuity_Operating_State_Model.jpg](system-design/diagrams/CON-SD-D02_Continuity_Operating_State_Model.jpg) / [PUML](system-design/diagrams/CON-SD-D02_Continuity_Operating_State_Model.puml) |
| CON-SD-D03 Dependency Degradation Detection and Degraded-Watch Flow | [CON-SD-D03_Dependency_Degradation_Detection_and_Degraded_Watch_Flow.jpg](system-design/diagrams/CON-SD-D03_Dependency_Degradation_Detection_and_Degraded_Watch_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D03_Dependency_Degradation_Detection_and_Degraded_Watch_Flow.puml) |
| CON-SD-D04 Continuity Activation and Scope-Control Flow | [CON-SD-D04_Continuity_Activation_and_Scope_Control_Flow.jpg](system-design/diagrams/CON-SD-D04_Continuity_Activation_and_Scope_Control_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D04_Continuity_Activation_and_Scope_Control_Flow.puml) |
| CON-SD-D05 Vendor PMS / HCP Degraded Resolve Decision Flow | [CON-SD-D05_Vendor_PMS_HCP_Degraded_Resolve_Decision_Flow.jpg](system-design/diagrams/CON-SD-D05_Vendor_PMS_HCP_Degraded_Resolve_Decision_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D05_Vendor_PMS_HCP_Degraded_Resolve_Decision_Flow.puml) |
| CON-SD-D06 Projection Freshness and Degraded Eligibility Flow | [CON-SD-D06_Projection_Freshness_and_Degraded_Eligibility_Flow.jpg](system-design/diagrams/CON-SD-D06_Projection_Freshness_and_Degraded_Eligibility_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D06_Projection_Freshness_and_Degraded_Eligibility_Flow.puml) |
| CON-SD-D07 Continuity Terminal Restricted Operation Flow | [CON-SD-D07_Continuity_Terminal_Restricted_Operation_Flow.jpg](system-design/diagrams/CON-SD-D07_Continuity_Terminal_Restricted_Operation_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D07_Continuity_Terminal_Restricted_Operation_Flow.puml) |
| CON-SD-D08 Payment, Fiscal Issuance, and ExitAuthorization Under Continuity | [CON-SD-D08_Payment_Fiscal_Issuance_and_ExitAuthorization_Under_Continuity.jpg](system-design/diagrams/CON-SD-D08_Payment_Fiscal_Issuance_and_ExitAuthorization_Under_Continuity.jpg) / [PUML](system-design/diagrams/CON-SD-D08_Payment_Fiscal_Issuance_and_ExitAuthorization_Under_Continuity.puml) |
| CON-SD-D09 Fiscal Issuance Failure / Pending Exit Exception Flow | [CON-SD-D09_Fiscal_Issuance_Failure_Pending_Exit_Exception_Flow.jpg](system-design/diagrams/CON-SD-D09_Fiscal_Issuance_Failure_Pending_Exit_Exception_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D09_Fiscal_Issuance_Failure_Pending_Exit_Exception_Flow.puml) |
| CON-SD-D10 Manual Release Governance Flow | [CON-SD-D10_Manual_Release_Governance_Flow.jpg](system-design/diagrams/CON-SD-D10_Manual_Release_Governance_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D10_Manual_Release_Governance_Flow.puml) |
| CON-SD-D11 Vendor Acknowledgment Failure and Reconciliation Flow | [CON-SD-D11_Vendor_Acknowledgment_Failure_and_Reconciliation_Flow.jpg](system-design/diagrams/CON-SD-D11_Vendor_Acknowledgment_Failure_and_Reconciliation_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D11_Vendor_Acknowledgment_Failure_and_Reconciliation_Flow.puml) |
| CON-SD-D12 Post-Restoration Reconciliation Lifecycle | [CON-SD-D12_Post_Restoration_Reconciliation_Lifecycle.jpg](system-design/diagrams/CON-SD-D12_Post_Restoration_Reconciliation_Lifecycle.jpg) / [PUML](system-design/diagrams/CON-SD-D12_Post_Restoration_Reconciliation_Lifecycle.puml) |
| CON-SD-D13 Continuity Observability and Audit Event Flow | [CON-SD-D13_Continuity_Observability_and_Audit_Event_Flow.jpg](system-design/diagrams/CON-SD-D13_Continuity_Observability_and_Audit_Event_Flow.jpg) / [PUML](system-design/diagrams/CON-SD-D13_Continuity_Observability_and_Audit_Event_Flow.puml) |

