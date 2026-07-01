# ExitPass Continuity System Design Input Pack 02: Degraded Workflow and State

## 1. Purpose

This input pack provides companion technical-design input for ExitPass Continuity degraded workflows and conceptual state ownership. It is intended for later Lead synthesis into the Continuity System Design and must not be treated as the final design.

The pack describes normal, degraded-watch, degraded-active, Continuity Terminal active, restoration-in-progress, post-restoration review, and closed/reconciled concepts. It preserves the approved authority model and avoids endpoint paths, DTOs, database objects, database enum values, event payloads, queue names, retry counts, timer values, alert thresholds, implementation classes, UI wireframes, final screen names, and runbook steps.

## 2. Source Documents Reviewed

- `docs/v1.3/continuity/system-design/ExitPass_Continuity_System_Design_Orchestration_Plan.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_System_Design_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_Open_Questions.md`

## 3. Continuity Role in Normal Mode

In normal mode, Continuity is inactive but observable readiness exists. Normal Vendor PMS/HCP, Central PMS, POS Server, payment, and gate authority boundaries apply.

Normal-mode posture:

- Vendor PMS/HCP remains authority for raw parking session lifecycle and normal tariff computation.
- Central PMS coordinates session projection/control state, records accepted payable basis, owns payment finality, records fiscal issuance references, decides degraded resolve only when approved policy is active, and issues ExitAuthorization.
- Vendor PMS Connector / HikCentral Connector reports vendor facts, availability, health, mapping status, projection freshness, live fee results, and acknowledgment outcomes where approved.
- POS Server remains the resolved Site fiscal issuance authority.
- Assisted Payment Terminal and WebPay remain channels; they do not own finality, fiscal issuance, or ExitAuthorization.
- Operator Console remains non-payment governance and may show readiness, connector health, projection freshness, and exception context where authorized.
- Management Dashboard and Reporting remains visibility/reporting only, with source and freshness labels.

Continuity in normal mode must not silently activate when a dependency fails. A degraded signal may move the operating posture to degraded-watch, but allowed degraded workflows require explicit policy evaluation, scope, authority, audit, incident/BCP reference where required, and reconciliation tagging.

## 4. Continuity Role in Degraded Mode

In degraded mode, Continuity provides controlled, explicit, scoped operation for approved degraded/BCP conditions. It does not replace the normal authority model.

Degraded-mode posture:

- Continuity activation is explicit, controlled, audited, reconciliation-tagged, time-bound, and scoped to affected Site/Site Group/dependency/workflow.
- Vendor PMS/HCP outage does not automatically permit degraded payment, fiscal issuance, manual release, or exit.
- Central PMS owns degraded resolve decisioning under approved Continuity policy.
- Projection is operational visibility and controlled degraded support only.
- Projection freshness is evaluated by Central PMS or approved policy using connector-provided freshness and health facts; the terminal or connector must not decide eligibility alone.
- Stale, ambiguous, insufficient, unavailable, or unsafe projection fails closed or routes to approved governance.
- Degraded tariff/payable basis must come from approved tariff configuration or approved continuity basis; it must not be invented from projection or passageway records.
- Continuity Terminal is disabled by default and can become active only within approved activation scope.
- Payment uncertainty remains pending or exception until Central PMS confirms payment finality and fiscal prerequisites.
- Vendor payment acknowledgment failure does not erase Central PMS payment finality and does not by itself authorize exit.
- Restoration does not automatically close continuity-origin records; post-restoration review and reconciliation are required.

## 5. Workflow Summary Table

| Workflow area | Trigger | Participating components | Authority owner | Expected posture |
| --- | --- | --- | --- | --- |
| Dependency degradation detection | Vendor PMS/HCP, connector, projection, payment, POS, or gate signal becomes degraded, stale, unknown, or unavailable. | Connector, Central PMS, Operator Console, Management Dashboard, audit/observability. | Central PMS / integration health workflow for platform posture; connector reports facts only. | Enter degraded-watch or raise governance visibility without silently activating Continuity. |
| Continuity activation | Approved authority recognizes a degraded/BCP condition requiring controlled operation. | Operator Console or approved operations workflow, Central PMS, audit/event, reconciliation, affected channels. | Approved continuity/governance authority, with Central PMS enforcing workflow eligibility. | Activate degraded-active scope with incident/BCP reference, affected dependency, allowed workflows, restrictions, audit tags, and reconciliation tags. |
| Degraded-watch | Dependency is at risk or degraded but continuity workflows are not active. | Connector, Central PMS, Operator Console, Management Dashboard. | Central PMS / approved policy evaluates whether activation is required. | Visibility, restriction warnings, and fail-closed handling for unsafe flows. |
| Degraded-active | Approved degraded controls are active for defined scope. | Central PMS, connector, APT/Continuity Terminal where approved, POS Server where available, Operator Console, audit/reconciliation. | Central PMS for degraded resolve/payment/fiscal reference/ExitAuthorization; governance workflow for activation. | Permit only approved workflows and fail closed outside scope. |
| Continuity Terminal active | Approved activation enables restricted terminal mode for authorized terminals/users/scope. | APT in Continuity Terminal mode, Central PMS, Operator Console, POS Server, audit/reconciliation. | Central PMS for backend decisions; approved governance for terminal activation. | Restricted lookup/payment/fiscal display/exception handoff only where policy allows. |
| Vendor PMS/HCP live resolve failure | Live lookup, live fee calculation, or vendor dependency cannot provide usable result. | Central PMS, connector, Vendor PMS/HCP, Operator Console where escalation applies. | Central PMS under approved Continuity policy. | Try normal path where possible; otherwise degraded evaluation or fail closed. |
| Projection freshness evaluation | Degraded evaluation needs last-known vendor/session context. | Connector, Central PMS, Operator Console/MDR for visibility. | Central PMS / approved policy. | Fresh, unambiguous, sufficient, mapped projection may support controlled degraded decision; otherwise fail closed or governance route. |
| Degraded tariff/payable basis | Live vendor tariff unavailable and approved degraded operation is in scope. | Central PMS, approved tariff configuration/continuity basis, connector facts, APT/WebPay if allowed. | Central PMS. | Use only approved basis; do not derive tariff from projection. |
| Payment uncertainty | Provider, channel, or orchestrator outcome is unknown, pending, conflicting, or duplicate-risk. | Payment channel, Payment Orchestrator, Central PMS, POS Server only after finality, Operator Console/MDR for visibility. | Central PMS for platform payment finality. | Keep pending/exception; do not issue normal ExitAuthorization until finality and fiscal prerequisites are satisfied. |
| Vendor acknowledgment failure | Vendor acknowledgment after platform payment/fiscal progression fails or is unknown. | Central PMS, connector, Vendor PMS/HCP, reconciliation, dashboards. | Central PMS retains payment finality; connector reports acknowledgment outcome. | Retry/escalate/reconcile per later design; acknowledgment failure alone does not authorize exit. |
| Restoration/deactivation | Affected dependency returns or approved authority ends continuity event. | Central PMS, connector, Operator Console/governance, APT, Management Dashboard, reconciliation. | Approved governance for deactivation; Central PMS for disabling restricted workflow eligibility. | Move to restoration-in-progress, disable continuity-only workflows, then post-restoration review. |

## 6. Dependency Degradation Detection Workflow

- Trigger: Vendor PMS/HCP outage, connector stale/unavailable, live fee unavailable, mapping ambiguity, projection stale/ambiguous/insufficient, payment uncertainty, POS fiscal exception, gate issue, or vendor acknowledgment failure.
- Participating components: Vendor PMS/HCP, Vendor PMS Connector / HikCentral Connector, Central PMS, Operator Console, Management Dashboard and Reporting, audit/event capability, reconciliation consumers.
- Authority owner: The connector reports facts and normalized outcomes; Central PMS and approved integration health/continuity policy own platform interpretation and workflow eligibility.
- Normal path or degraded path: In normal mode, Central PMS uses live vendor interaction where available and treats projection as visibility. A degradation signal may create degraded-watch visibility and operational warnings without enabling degraded workflows.
- Fail-closed path: Missing mapping, ambiguous mapping, stale projection, insufficient projection, unknown vendor outcome, unsafe payment state, or unavailable fiscal authority blocks normal payment/exit or routes to approved governance. The connector must not choose a Site, session, tariff, payment state, fiscal state, discount approval, or exit eligibility by heuristic.
- Audit/reconciliation requirement: Degradation recognition should preserve affected dependency, Site/Site Group, connector/vendor context, freshness/failure indicators, actor or system origin where relevant, audit tag, and later reconciliation correlation.
- Open design questions: Exact connector health states, freshness labels, stale thresholds, alert rules, topology ownership, and normalized vendor error categories remain open.

## 7. Continuity Activation Workflow

- Trigger: Approved operations/governance authority determines that a degraded/BCP condition requires controlled continuity operation for a defined scope.
- Participating components: Operator Console or approved operations workflow, Central PMS, affected channels, APT/Continuity Terminal where approved, connector, POS Server where available, audit/event capability, Management Dashboard, reconciliation workflow.
- Authority owner: Exact activation authority remains open; approved governance owns activation approval. Central PMS enforces downstream eligibility for degraded resolve, payment-linked state, fiscal reference recording, and ExitAuthorization.
- Normal path or degraded path: Activation records the affected Site/Site Group, affected dependency, incident/BCP reference, reason, allowed workflows, restricted workflows, activation scope, approval context, audit tags, and reconciliation tags. Degraded-active posture applies only inside the approved scope.
- Fail-closed path: If activation authority, scope, incident/BCP reference, allowed workflow scope, dependency identity, or required governance control is missing or unsafe, Continuity must not activate. The system remains in degraded-watch or blocks affected workflows.
- Audit/reconciliation requirement: Activation and every continuity-origin activity must be incident-tagged where required, audit-tagged, reconciliation-tagged, and traceable through post-restoration review.
- Open design questions: Exact activation authority, approval workflow, permission matrix, activation/deactivation roles, and continuity scope model remain open.

## 8. Degraded-Watch Workflow

- Trigger: A dependency is degraded, stale, at risk, or under observation, but approved degraded controls are not active.
- Participating components: Connector, Central PMS, Operator Console, Management Dashboard, audit/observability.
- Authority owner: Central PMS / approved policy owns whether degraded-watch remains observational or escalates to activation review.
- Normal path or degraded path: Normal workflows continue only where authority requirements are satisfied. Operator Console and dashboards may show dependency health, projection freshness, degraded-watch status, restriction warnings, and incident context where authorized.
- Fail-closed path: Any workflow requiring fresh live vendor facts, approved degraded basis, fiscal issuance, payment finality, or ExitAuthorization must block if those prerequisites are not met. Degraded-watch is not permission to pay, issue fiscal documents, manually release, or exit.
- Audit/reconciliation requirement: Watch-state recognition should be observable and audit-correlatable when it affects workflow eligibility or customer/operator messaging. Projection data shown in dashboards must carry source/freshness labels.
- Open design questions: Exact criteria for watch-state recognition, escalation to activation, dashboard indicators, alert thresholds, and retention of watch-only records remain open.

## 9. Degraded-Active Workflow

- Trigger: Continuity activation is approved for a defined Site/Site Group/dependency/workflow scope.
- Participating components: Central PMS, connector, Vendor PMS/HCP where partially available, APT/Continuity Terminal where approved, POS Server where available, Operator Console/governance, Management Dashboard, audit/event, reconciliation.
- Authority owner: Central PMS owns degraded resolve decisioning, payment finality, fiscal reference recording, and ExitAuthorization. Governance owns activation/deactivation approval. POS Server remains fiscal authority.
- Normal path or degraded path: Central PMS evaluates each requested workflow against activation scope, dependency condition, projection freshness/ambiguity/sufficiency, approved degraded tariff basis, payment/fiscal status, and allowed workflow controls. Only approved degraded workflows proceed.
- Fail-closed path: Requests outside scope, unsupported dependency conditions, stale/ambiguous/insufficient projection, missing approved tariff basis, payment uncertainty, unresolved fiscal issuance, unsafe discount evidence, or unavailable required authority block or route to governance review.
- Audit/reconciliation requirement: Every degraded-active action must carry continuity, incident/BCP, audit, and reconciliation context sufficient for later reconstruction. Dashboard visibility must distinguish operational continuity context from financial truth.
- Open design questions: Exact allowed workflow bundles, degraded eligibility rules, activation scope dimensions, exception classifications, and closure authority remain open.

## 10. Vendor PMS / HCP Live Resolve Failure Workflow

- Trigger: Live Vendor PMS/HCP lookup, session resolve, or fee calculation is unavailable, times out, returns unknown/conflicting outcome, lacks required permission, or cannot safely identify the session or fee.
- Participating components: Central PMS, Vendor PMS Connector / HikCentral Connector, Vendor PMS/HCP, Operator Console where escalation applies, audit/reconciliation.
- Authority owner: Vendor PMS/HCP remains normal raw session and tariff authority when available. Central PMS owns the decision to fail closed, continue normal retry/status confirmation, or evaluate approved degraded resolve.
- Normal path or degraded path: Central PMS first prefers live vendor facts in normal mode. If live resolve fails and continuity policy is active, Central PMS evaluates whether projection and approved degraded tariff basis can support controlled degraded resolve.
- Fail-closed path: Vendor outage or live resolve failure does not automatically permit payment, fiscal issuance, manual release, or exit. Missing mapping, ambiguous mapping, uncertain vendor object identity, unconfirmed ticket/card lookup behavior, live fee unavailable without approved degraded basis, or unsafe vendor outcome fails closed or routes to approved governance.
- Audit/reconciliation requirement: Failure context must be audit-correlatable with vendor system, connector instance, resolved or attempted Site/Site Group, mapping status, projection freshness where used, degraded decision basis, and reconciliation tags.
- Open design questions: Exact HCP ticket/card identifier policy, vendor error-code normalization, connector topology, and vendor capability confirmation remain open.

## 11. Projection Freshness Evaluation Workflow

- Trigger: Central PMS needs projection context for operational visibility or controlled degraded support because live vendor session or fee facts are unavailable or degraded.
- Participating components: Connector, Central PMS, Operator Console, Management Dashboard, audit/reconciliation.
- Authority owner: Central PMS / approved Continuity policy evaluates freshness, ambiguity, sufficiency, and mapping status. The connector provides inputs; the terminal and dashboard display results only.
- Normal path or degraded path: Connector reports last-known projection facts, mapping context, availability, and freshness inputs. Central PMS determines whether projection is fresh enough, unambiguous enough, and sufficient enough for the requested degraded workflow under approved policy.
- Fail-closed path: Stale, ambiguous, insufficient, unavailable, conflicting, or unmapped projection does not support degraded tariff, payment, fiscal issuance, discount approval, ExitAuthorization, or manual release. It fails closed or routes to approved supervisor/manual review.
- Audit/reconciliation requirement: Any degraded use or rejection based on projection must capture projection source category, freshness/staleness context, ambiguity/sufficiency decision basis, affected dependency, audit tag, and reconciliation tag.
- Open design questions: Exact freshness threshold, freshness labels, stale warning rules, sufficiency criteria, ambiguity criteria, and dashboard/operator presentation language remain open.

## 12. Degraded Tariff / Payable-Basis Workflow

- Trigger: Live Vendor PMS/HCP fee calculation is unavailable, but approved continuity policy may allow degraded payable-basis determination.
- Participating components: Central PMS, approved tariff configuration or approved continuity basis, connector projection/health inputs, APT/Continuity Terminal or WebPay where allowed, POS Server after payment finality where fiscal issuance is available, audit/reconciliation.
- Authority owner: Central PMS owns accepted payable-basis recording and degraded tariff decisioning under approved Continuity policy. Vendor PMS/HCP remains normal tariff authority when live computation is available.
- Normal path or degraded path: In normal mode, Central PMS records payable basis from live vendor calculation. In degraded-active mode, Central PMS may use approved tariff configuration or approved continuity basis only when policy, projection, mapping, Site, and workflow scope requirements are satisfied.
- Fail-closed path: Degraded tariff must not be invented from projection, passageway records, terminal history, dashboard values, cashier judgment, or connector heuristics. If approved tariff basis, rounding/grace treatment, Site mapping, projection support, or discount/payable-basis recalculation cannot be safely determined, the workflow blocks or routes to approved governance.
- Audit/reconciliation requirement: Degraded payable-basis use must be incident-tagged, audit-tagged, reconciliation-tagged, distinguishable from normal vendor tariff calculation, and included in post-restoration review.
- Open design questions: Exact degraded tariff owner, approved tariff configuration source, rounding/grace rules, stale threshold, discount interaction, and reconciliation labels remain open.

## 13. Continuity Terminal Restricted Operation Workflow

- Trigger: Approved continuity activation enables Continuity Terminal mode for authorized terminals, users, Sites/Site Groups, shifts, and workflows.
- Participating components: Assisted Payment Terminal in Continuity Terminal mode, Central PMS, Operator Console or approved governance workflow, connector, POS Server where available and allowed, Payment Orchestrator where payment is allowed, audit/reconciliation, Management Dashboard visibility.
- Authority owner: Approved governance owns activation. Central PMS owns backend decisions, payment finality, degraded resolve, fiscal reference recording, and ExitAuthorization. POS Server owns fiscal issuance. The terminal owns only user interaction and status display within policy.
- Normal path or degraded path: Continuity Terminal is disabled by default. Once active, it displays backend-provided degraded context, projection freshness, payment restrictions, fiscal restrictions, and escalation status. It may support restricted lookup, payment, fiscal display, exception handoff, and controlled release context only where policy allows.
- Fail-closed path: Vendor PMS/HCP outage, WebPay/APM outage, connector stale state, network degradation, or terminal availability does not by itself authorize payment, fiscal issuance, degraded tariff, manual release, or exit. Unsafe entitlement, evidence, projection freshness, payable basis, payment, fiscal, device trust, shift, Site/Site Group, or exit state blocks or routes to approved governance.
- Audit/reconciliation requirement: Terminal activity must preserve actor, device, shift, Site/Site Group, session/payment/fiscal/continuity context, incident/BCP reference where applicable, audit tag, reconciliation tag, and post-restoration review handoff.
- Open design questions: Exact terminal activation authority, device trust requirements, allowed offline posture, fiscal presentation rules, manual release handoff boundary, and Continuity Terminal workflow set remain open.

## 14. Payment Uncertainty Workflow

- Trigger: Payment provider, Payment Orchestrator, channel, callback, status check, or duplicate/replay condition leaves platform payment finality unknown, pending, conflicting, or unsafe.
- Participating components: Payment channel, Payment Orchestrator, Central PMS, POS Server only after Central PMS finality, Operator Console/MDR for visibility, audit/reconciliation.
- Authority owner: Central PMS owns platform payment finality. Payment Orchestrator reports verified provider outcomes but does not declare platform finality. Terminals, dashboards, Operator Console, POS Server, and gates do not declare payment finality.
- Normal path or degraded path: Central PMS waits for verified outcome or routes to pending/exception handling according to approved policy. Customer/operator messaging must distinguish payment received, payment pending verification, fiscal issuance pending, and exit authorization pending.
- Fail-closed path: Payment uncertainty does not authorize fiscal issuance, normal ExitAuthorization, manual release, or vendor acknowledgment as if paid. Duplicate-risk conditions must avoid duplicating payment finality, fiscal issuance, ExitAuthorization, or vendor acknowledgment effects.
- Audit/reconciliation requirement: Payment uncertainty must be audit-correlatable with channel, provider outcome context, payment attempt, Central PMS finality decision, fiscal handoff if any, customer/operator messaging category, and reconciliation status.
- Open design questions: Exact provider status-confirmation rules, duplicate handling policy, user messaging categories, exception ownership, and reconciliation SLA remain open.

## 15. Vendor Payment Acknowledgment Failure Workflow

- Trigger: Vendor PMS/HCP acknowledgment after Central PMS payment finality and required fiscal prerequisites fails, times out, is unavailable, returns unknown, conflicts, or is not approved for the deployment.
- Participating components: Central PMS, Vendor PMS Connector / HikCentral Connector, Vendor PMS/HCP, POS Server context where fiscal prerequisites apply, reconciliation workflow, Operator Console/MDR visibility, audit/event.
- Authority owner: Central PMS retains platform payment finality and exit eligibility authority. The connector reports acknowledgment outcome. Vendor acknowledgment is not ExitPass payment finality.
- Normal path or degraded path: Central PMS determines whether acknowledgment should be attempted, held, retried, escalated, or reconciled according to later approved Site/vendor policy. The connector reports normalized outcome without changing platform finality.
- Fail-closed path: Acknowledgment failure does not erase Central PMS payment finality, does not imply payment failed, and does not by itself authorize exit. Unknown or mutating acknowledgment outcomes must not be retried blindly without approved idempotency and duplicate-handling posture.
- Audit/reconciliation requirement: Acknowledgment failure must be audit-tagged and reconciliation-tagged, visible as backlog where authorized, and included in post-restoration or connector-origin reconciliation review.
- Open design questions: Exact acknowledgment synchronicity, retry/escalation posture, idempotency behavior, exit-block policy, vendor-state meaning, and safe confirmation method remain open.

## 16. Restoration Detection and Deactivation Workflow

- Trigger: Affected dependency appears restored, approved authority ends continuity, dependency health returns, connector projection resumes safely, Vendor PMS/HCP live resolve is available, or operational scope is no longer degraded.
- Participating components: Connector, Central PMS, Operator Console/governance, APT/Continuity Terminal, POS Server if relevant, Management Dashboard, audit/event, reconciliation.
- Authority owner: Approved governance owns deactivation approval/review. Central PMS owns disabling degraded eligibility and continuity-only workflow access. Connector reports restored health/freshness facts only.
- Normal path or degraded path: Restoration-in-progress begins when dependency recovery is detected or approved deactivation starts. Continuity-only workflows are disabled or limited, Continuity Terminal availability is removed for the scope, normal live vendor/POS/payment/fiscal authority paths are restored where safe, and continuity-origin records are prepared for review.
- Fail-closed path: Apparent restoration does not automatically close records, discard tags, assume vendor acknowledgments succeeded, infer fiscal issuance, or reconcile payment/fiscal/vendor/gate mismatches. If dependency health is conflicting or incomplete, remain restricted or route to governance review.
- Audit/reconciliation requirement: Deactivation, restoration evidence, remaining exceptions, pending acknowledgments, payment uncertainty, fiscal exceptions, manual release context, terminal activity, and customer/operator exception messaging must remain reviewable.
- Open design questions: Exact restoration criteria, deactivation approval workflow, health confirmation rules, review SLA, closure authority, and status labels remain open.

## 17. Conceptual State Ownership Notes

| Conceptual state | Meaning | Primary owner posture | Entry concept | Exit concept | Guardrail |
| --- | --- | --- | --- | --- | --- |
| Normal | No approved degraded operation is active for the scope. | Central PMS and normal authority chain; Vendor PMS/HCP and POS Server retain their normal authorities. | Default operating posture. | Degraded signal creates degraded-watch or activation review. | No silent fallback. |
| Degraded-watch | Dependency is degraded, stale, at risk, or under observation; continuity workflows are not active. | Central PMS / approved policy for eligibility; connector reports facts. | Health/freshness/dependency concern. | Return to normal or approved activation. | Watch does not permit degraded payment, fiscal issuance, manual release, or exit. |
| Degraded-active | Approved degraded controls are active for defined scope. | Governance owns activation; Central PMS owns degraded decisions and platform authority. | Approved activation. | Deactivation or restoration-in-progress. | Scope, incident/BCP, audit, and reconciliation controls are mandatory. |
| Continuity Terminal active | Restricted terminal mode is enabled for approved users/devices/sites/workflows. | Governance owns terminal activation; Central PMS owns backend decisions. | Approved activation includes terminal scope. | Deactivation disables continuity-only terminal workflows. | Disabled by default; terminal never becomes finality, fiscal, tariff, or exit authority. |
| Restoration-in-progress | Affected dependency is returning and continuity workflows are being wound down. | Governance and Central PMS coordinate disabling degraded eligibility. | Restoration signal or deactivation decision. | Post-restoration review. | Restoration does not auto-close continuity-origin records. |
| Post-restoration review | Continuity-origin activity is under reconciliation, audit, exception review, and operational closure checks. | Operations/reconciliation workflow with governance visibility. | Deactivation/restoration produces review backlog. | Closed/reconciled after required checks. | Projection cannot close financial or fiscal reconciliation. |
| Closed / reconciled | Continuity event is closed and required review/reconciliation is complete. | Reconciliation/governance closure authority, exact owner open. | Required matches, exceptions, approvals, and reviews complete. | Later audit access remains possible. | Closure cannot silently mutate authority records. |

These states are architecture-level concepts only. Exact names, state transitions, persistence fields, API statuses, event payloads, timers, alert thresholds, and runbook procedures remain deferred.

## 18. Retry / Idempotency / Duplicate Handling Concepts

- Live resolve and fee calculation: Re-attempts or status confirmations must not extend customer/operator waits indefinitely and must not produce multiple accepted payable bases for the same authoritative decision without Central PMS control.
- Projection polling: Polling failures may preserve last-known success and failure context, but stale projection must not be presented as current or used for degraded decisions outside approved policy.
- Payment uncertainty: Provider or channel duplicates, replay, unknown outcomes, or late outcomes must not duplicate Central PMS payment finality, fiscal issuance, vendor acknowledgment, or ExitAuthorization.
- Fiscal sequencing: Payment finality precedes fiscal issuance request; fiscal issuance success and Central PMS fiscal reference recording precede normal ExitAuthorization unless a separately approved exception/manual-release policy applies.
- Vendor acknowledgment: Unknown or failed acknowledgment requires later-approved safe retry/status-confirmation behavior. It does not modify Central PMS payment finality by itself.
- Manual release: Manual release does not close payment, fiscal, vendor acknowledgment, or reconciliation state by itself.
- Restoration: Recovered dependencies must not silently overwrite continuity-origin audit, incident, or reconciliation tags.

Open retry/idempotency questions include exact duplicate detection posture, vendor acknowledgment idempotency, unknown outcome confirmation, provider retry/status confirmation boundaries, fiscal exception duplicate prevention, and reconciliation closure rules.

## 19. Open Workflow Questions

- What is the exact BCP/continuity activation authority and approval workflow?
- What are the exact Continuity Terminal activation and deactivation rules by role, device, shift, Site/Site Group, and workflow?
- What exact connector health states, projection freshness labels, stale thresholds, stale warning rules, and alert rules are approved?
- What exact degraded tariff configuration source, owner, rounding/grace treatment, and payable-basis policy apply?
- What exact criteria define projection freshness, ambiguity, sufficiency, mapping ambiguity, and controlled continuation?
- What exact vendor/HCP ticket/card/plate lookup behavior is confirmed for live fee calculation and degraded support?
- What exact vendor payment acknowledgment policy applies: synchronous/asynchronous posture, retry/escalation behavior, idempotency, exit-block policy, and reconciliation treatment?
- What exact payment uncertainty handling and duplicate provider outcome posture apply?
- What exact manual release policy and emergency override boundary apply?
- What exact fiscal exception release policy applies when payment finality exists but fiscal issuance is pending, failed, or unknown?
- What exact restoration criteria, deactivation workflow, post-restoration review SLA, closure authority, and closure labels apply?
- What exact dashboard/reporting labels are required to keep operational projection visibility separate from financial truth?

## 20. Summary for Lead

This input pack recommends that the Continuity System Design model degraded operation as explicit, scoped, audited, reconciliation-tagged posture rather than fallback behavior.

Key synthesis points:

- Preserve the normal authority model in all states: Vendor PMS/HCP for normal raw session/tariff facts, Central PMS for platform payment-linked control and ExitAuthorization, POS Server for fiscal issuance, Operator Console for non-payment governance, Management Dashboard for visibility, and connectors for vendor facts/freshness only.
- Treat degraded-watch as visibility and restriction posture, not permission to run degraded workflows.
- Treat degraded-active as approved continuity scope with allowed workflows, restricted workflows, affected dependency, incident/BCP reference, audit tags, and reconciliation tags.
- Ensure projection freshness is evaluated by Central PMS / approved policy using connector inputs; terminals and dashboards display context only.
- Fail closed for stale, ambiguous, insufficient, unavailable, unmapped, unknown, payment-uncertain, fiscal-uncertain, or unapproved conditions.
- Keep degraded tariff/payable basis tied to approved tariff configuration or approved continuity basis, never projection-derived tariff invention.
- Keep Continuity Terminal disabled by default and restricted to approved degraded/BCP scope.
- Keep payment uncertainty pending/exception until Central PMS finality and fiscal prerequisites are satisfied.
- Treat vendor acknowledgment failure as audit/reconciliation backlog, not as a reversal of Central PMS payment finality and not as exit authorization.
- Require restoration-in-progress and post-restoration review before closed/reconciled state.

Lead synthesis should carry forward the open workflow questions rather than resolving them through invented APIs, database values, event payloads, thresholds, retry counts, timers, UI designs, or runbook steps.
