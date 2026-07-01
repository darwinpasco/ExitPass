# ExitPass System Design v1.3 Input Pack 03: Workflow and State

## 1. Purpose

This input pack provides architecture-level workflow and state input for ExitPass System Design v1.3. It is intended to help the System Design Lead draft the Core Workflows, Event Architecture, State Machines, Failure Mode Architecture, Observability, Business Continuity, Operational Runbooks, and Appendix sections while preserving the approved v1.3 BRD authority model.

This pack describes business-to-system choreography, ownership boundaries, failure handling, audit/reconciliation expectations, and diagram recommendations. It does not define endpoint paths, DTOs, database tables, event payload schemas, queue names, SQL routines, or implementation classes.

## 2. Source Documents Reviewed

| Source document | Reviewed use in this pack |
| --- | --- |
| `docs/v1.3/system-design/ExitPass_System_Design_v1.3_Orchestration_Plan.md` | Input-pack ownership, required structure, v1.2 style/outline baseline, and System Design integration rules. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core authority model, centralized WebPay, Site Group/Site semantics, projection posture, normal/degraded resolve, fiscal issuance before ExitAuthorization, audit, acceptance criteria, and open questions. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Cashier-Assisted Terminal flow, Continuity Terminal mode, statutory discount capture, terminal authority exclusions, terminal audit, fiscal routing, and open terminal questions. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Continuity states, degraded resolve, activation/deactivation, manual release, fiscal exception handling, vendor acknowledgment failure, reconciliation, and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Non-payment governance boundary, statutory discount review, continuity governance, connector/projection visibility, fiscal exception review, manual release governance, and audit controls. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Operational visibility versus financial truth, reporting source ownership, connector/projection freshness dashboards, financial/fiscal/reconciliation reporting, and export/access audit. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site-level POS Server fiscal authority, Sales Invoice issuance sequence, fiscal issuance failure/timeout, POS reporting, fiscal audit, continuity fiscal constraints, and POS open questions. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approved baseline list, approval meaning, preserved authority model, and open downstream confirmation items. |
| `D:\Docs\ExitPass\v1.2\ExitPass System Design v1.2.docx` | Style and workflow baseline: owner-per-step workflow posture, authority-first causal chain, eventing/outbox posture, state ownership, failure breakpoints, and diagram index shape. |

## 3. Workflow Summary Table

| # | Workflow | Primary authority chain | Main failure concern | SDD diagram recommendation |
| --- | --- | --- | --- | --- |
| 1 | Centralized WebPay normal payment-to-exit flow | WebPay channel -> Central PMS -> Vendor PMS/HCP -> Payment Orchestrator -> Site POS Server -> Central PMS -> Gate | Wrong Site Group/Site resolution, payment uncertainty, fiscal block before exit | Sequence diagram showing WebPay, Site Group/Site resolution, tariff snapshot, payment finality, fiscal issuance, ExitAuthorization, gate consumption. |
| 2 | Assisted Payment Terminal cashier-assisted flow | Assisted Payment Terminal -> Central PMS -> Vendor PMS/HCP -> Payment Orchestrator/payment channel -> Site POS Server -> Central PMS | Terminal treated as payment/fiscal/exit authority | Sequence or swimlane diagram separating cashier UI actions from Central PMS, POS Server, and ExitAuthorization ownership. |
| 3 | Statutory discount validation and payable-basis refresh flow | Assisted Terminal or Operator Console capture/review -> Central PMS / Discount workflow -> Vendor PMS or approved degraded basis -> Central PMS | Discount applied without approved validation or stale payable basis reused | State/workflow diagram for validation status, payable-basis refresh, and payment gate. |
| 4 | Payment finality to fiscal issuance to ExitAuthorization flow | Payment Orchestrator verifies provider outcome -> Central PMS declares finality -> Site POS Server issues Sales Invoice -> Central PMS records fiscal reference and issues ExitAuthorization | ExitAuthorization issued before fiscal success | Causal chain diagram with blocking conditions and authority owner per transition. |
| 5 | POS fiscal issuance failure / timeout flow | Central PMS and Site POS Server, with Operator Console review | Paid session blocked because fiscal outcome is failed, pending, or unknown | Failure-mode sequence diagram showing retry/escalation/manual release decision points. |
| 6 | Vendor PMS / HCP degraded resolve flow | Central PMS continuity decision -> projection source only under approved controls -> approved tariff basis | Projection treated as session/tariff/financial truth | Degraded resolve decision tree with fail-closed and supervisor/manual review branch. |
| 7 | Continuity Terminal activation and restricted operation flow | Operator Console/approved operations workflow -> Continuity policy -> Assisted Payment Terminal in Continuity mode -> Central PMS | Continuity silently replacing normal authority | Activation/deactivation state diagram and restricted terminal swimlane. |
| 8 | Manual release governance flow | Supervisor/Operator Console governance -> approved manual emergency process -> reconciliation workflow | Manual release becoming normal exit authorization or payment finality | Governance diagram showing approval, reason, incident/audit/reconciliation tags, and post-review. |
| 9 | Vendor payment acknowledgment failure flow | Central PMS owns payment finality; connector/Vendor PMS acknowledgment is downstream | Vendor acknowledgment failure confused with payment failure | Retry/escalation diagram downstream of confirmed payment and fiscal handling. |
| 10 | Connector polling / projection freshness flow | Connector/integration health workflow -> Central PMS projection/read model -> dashboards/console | Stale projection used for tariff/payment/exit decisions | Polling/projection freshness diagram with operational-only labeling and stale warnings. |
| 11 | Management Dashboard reporting source flow | Dashboard consumes labeled operational, canonical financial, fiscal, audit, and reconciliation sources | Projection or occupancy estimate treated as revenue/fiscal truth | Source-of-truth boundary diagram for operational versus financial/fiscal/reconciliation views. |
| 12 | Post-restoration reconciliation flow | Reconciliation workflow with Central PMS, POS Server, provider outcomes, Vendor PMS acknowledgments, continuity/manual release records | Continuity-origin gaps remain unresolved after restoration | Reconciliation lifecycle diagram from restoration to closure. |

## 4. Architecture-Level Workflow Details

### 4.1 Centralized WebPay Normal Payment-to-Exit Flow

**Participating components:** Parker, centralized WebPay, Central PMS, Vendor PMS/HCP connector, Vendor PMS/HCP, Payment Orchestrator, payment provider, resolved Site POS Server, gate/exit system, audit/event capability, Management Dashboard as downstream visibility.

**Authority owner per major step:**

- WebPay owns customer interaction and submitted Site Group/payment-scope context only.
- Central PMS owns URL/scope interpretation into platform control context, payment-linked control state, TariffSnapshot recording, payment finality, fiscal reference recording, and ExitAuthorization.
- Vendor PMS/HCP owns raw session lifecycle and normal tariff computation.
- Payment Orchestrator owns provider interaction and verified provider outcome reporting, but not platform finality.
- Resolved Site POS Server owns Sales Invoice issuance and fiscal records.
- Gate/exit system consumes Central PMS authorization and reports gate outcome.

**Normal path:** Customer enters through centralized WebPay using a site-specific or payment-scope URL. The URL resolves to allowed lookup/payment scope, Central PMS resolves the session through the appropriate Vendor PMS/HCP connector, records the vendor-authoritative payable basis as an immutable payment basis, initiates payment through Payment Orchestrator, receives verified provider outcome, records Central PMS payment finality, requests fiscal issuance from the resolved Site POS Server, records the fiscal issuance reference, issues ExitAuthorization, and the gate consumes the authorization.

**Failure path:** Ambiguous/no session fails deterministically. Vendor lookup/tariff failure fails closed unless approved degraded controls apply. Unknown payment outcome remains pending and must not create finality. Fiscal failure or timeout blocks normal ExitAuthorization and enters exception handling. Gate consumption failure does not change payment or fiscal truth and may route to controlled manual release governance.

**Audit/reconciliation requirement:** Trace from public URL/scope, Site Group, resolved Site, connector, session resolve, tariff basis, payment attempt, provider outcome, payment finality, fiscal issuance, ExitAuthorization, and gate outcome.

**Open design questions:** Exact WebPay slug registry; whether slugs resolve to Site Group, Site, or both; exact connector topology; exact vendor acknowledgment timing; exact customer messaging for pending fiscal/exit states.

**Recommended diagram content for SDD:** Sequence diagram with explicit authority labels above each lane and a visible break before ExitAuthorization when fiscal issuance is pending or failed.

### 4.2 Assisted Payment Terminal Cashier-Assisted Flow

**Participating components:** Cashier, Assisted Payment Terminal in Cashier-Assisted mode, Central PMS, Central PMS / Discount workflow, Vendor PMS/HCP connector, Vendor PMS/HCP, Payment Orchestrator or approved payment channel, Site POS Server, Operator Console for escalation/review, gate/exit system, audit/event capability.

**Authority owner per major step:**

- Assisted Payment Terminal owns terminal UI, cashier/device/channel/shift context, scan/manual entry workflow, and customer/cashier messaging.
- Central PMS owns session resolve, control state, payment finality, payable-basis refresh, fiscal reference recording, and ExitAuthorization.
- Central PMS / Discount workflow owns statutory discount policy resolution and validation persistence.
- Vendor PMS/HCP owns normal raw session and tariff computation.
- Site POS Server owns Sales Invoice issuance.
- Operator Console or approved operations workflow owns supervisor review and governance, not payment collection.

**Normal path:** Cashier authenticates on an approved terminal with assigned Site/Site Group and shift context, scans or enters ticket/card identifier, terminal requests backend session resolve, Central PMS resolves session and payable basis, terminal displays payable amount, optional statutory discount capture is sent to Central PMS / Discount workflow, payable basis is refreshed after approval, cashier collects payment through approved flow, Central PMS records payment finality after verified outcome, fiscal issuance is routed through resolved Site POS Server, and Central PMS issues ExitAuthorization when eligible.

**Failure path:** Invalid cashier, untrusted terminal, wrong Site/Site Group context, no active shift, session ambiguity, stale projection, discount pending/rejected/failed, unknown payment outcome, fiscal failure/timeout, or pending ExitAuthorization must block the terminal from implying paid/fiscally issued/exit-authorized status. Supervisor escalation is governance, not local authority takeover.

**Audit/reconciliation requirement:** Cashier identity, terminal/device identity, Site/Site Group, shift/session context, ticket/card lookup, payable-basis display, discount capture, evidence reference, payment result display, fiscal status display, ExitAuthorization status display, and escalation/manual release messaging.

**Open design questions:** Cash support; hosted checkout versus terminal-integrated payments; exact hardware integrations; terminal certificate/key storage; exact fixed-station variant eligibility; fiscal reprint/display behavior.

**Recommended diagram content for SDD:** Swimlane showing terminal actions as UI/channel actions and Central PMS/POS Server as authority transitions.

### 4.3 Statutory Discount Validation and Payable-Basis Refresh Flow

**Participating components:** Assisted Payment Terminal or Operator Console, Central PMS / Discount workflow, Central PMS, Vendor PMS/HCP connector, Vendor PMS/HCP or approved degraded tariff basis, Site POS Server, audit/evidence capability, Operator Console supervisor review where required.

**Authority owner per major step:**

- Assisted Payment Terminal may capture cashier-facing validation inputs.
- Operator Console may review/elevate validation cases within non-payment governance scope.
- Central PMS / Discount workflow owns policy resolution, validation result, evidence reference, and validation persistence.
- Central PMS owns payable-basis linkage and TariffSnapshot refresh.
- Vendor PMS/HCP owns normal discount-aware tariff computation where integration supports it; under continuity, Central PMS may use only approved degraded basis.
- POS Server owns fiscal treatment on Sales Invoice after approved payment basis.

**Normal path:** After valid session resolution, cashier/operator initiates Senior Citizen/PWD or other supported entitlement validation where policy allows. Required structured details, evidence reference, and attestation are submitted to Central PMS / Discount workflow. If approved, Central PMS refreshes or recalculates payable basis through the approved backend path and makes the updated basis available before payment. Payment cannot proceed with an unapproved discount basis.

**Failure path:** Unresolved policy basis, unauthorized actor, missing required evidence, pending review, rejected/expired/failed validation, stale projection, unavailable Vendor PMS when required, or unsafe continuity conditions fail closed or route to supervisor/manual review. The terminal or console must not mutate payable basis directly.

**Audit/reconciliation requirement:** Validation request, entitlement type, policy basis, evidence reference, cashier attestation, actor/device/Site context, validation result, payable-basis refresh, fiscal treatment, and reconciliation tags for continuity-mode discount activity.

**Open design questions:** Exact policy registry ownership; maximum pending duration; evidence retention periods; government/cooperative verification integration; degraded-mode discount handling threshold.

**Recommended diagram content for SDD:** Workflow/state hybrid showing validation statuses, payable-basis refresh gate, and prohibition on discounted payment before approval.

### 4.4 Payment Finality to Fiscal Issuance to ExitAuthorization Flow

**Participating components:** Payment provider, Payment Orchestrator, Central PMS, Site POS Server, gate/exit system, audit/event capability, Management Dashboard/Reconciliation as consumers.

**Authority owner per major step:**

- Payment provider owns external provider outcome.
- Payment Orchestrator verifies provider outcome and reports verified outcome.
- Central PMS declares platform payment finality and records payment evidence.
- Site POS Server issues Sales Invoice and owns fiscal records.
- Central PMS records fiscal reference and issues ExitAuthorization.
- Gate/exit system consumes Central PMS authorization.

**Normal path:** Payment Orchestrator receives and verifies provider outcome, reports it to Central PMS, Central PMS records payment finality, Central PMS requests fiscal issuance from resolved Site POS Server, POS Server issues Sales Invoice and returns fiscal status/identity, Central PMS records fiscal reference, Central PMS checks eligibility, issues ExitAuthorization, and gate consumes it.

**Failure path:** Duplicate provider callbacks must be idempotent. Unknown or unverifiable provider outcomes remain pending. Fiscal failure/timeout stops the causal chain before ExitAuthorization. Gate failure after authorization is operational and must not rewrite payment/fiscal truth.

**Audit/reconciliation requirement:** Verified provider outcome, Central PMS finality transition, fiscal issuance request/result, fiscal reference, ExitAuthorization issue/consume, and gate outcome must support reconstruction.

**Open design questions:** Exact customer-facing message timing; fiscal exception release policy; whether any approved manual emergency process may release without normal fiscal success; vendor acknowledgment ordering relative to fiscal/exit per Site.

**Recommended diagram content for SDD:** Causal chain diagram with hard ordering: verified outcome -> Central PMS finality -> POS Server fiscal success -> Central PMS ExitAuthorization.

### 4.5 POS Fiscal Issuance Failure / Timeout Flow

**Participating components:** Central PMS, resolved Site POS Server, Operator Console, Assisted Payment Terminal or WebPay/APM as messaging surfaces, supervisor/operations workflow, reconciliation workflow, Management Dashboard.

**Authority owner per major step:**

- Central PMS owns payment finality state, fiscal reference recording, and ExitAuthorization blocking decision.
- Site POS Server owns fiscal issuance status and recovery of fiscal state.
- Operator Console owns review/escalation governance.
- Supervisor/approved operations workflow owns any allowed manual release approval.
- Reconciliation workflow owns closure of exception items.

**Normal path:** If fiscal issuance succeeds, Central PMS records the fiscal reference and continues to ExitAuthorization.

**Failure path:** If fiscal issuance fails, times out, or returns unknown status, Central PMS does not issue normal ExitAuthorization. The case enters controlled fiscal exception handling. Messaging states payment received but fiscal issuance or exit authorization is pending. Retry, escalation, or manual release is governed by later approved policy. Fiscal recovery must avoid duplicate fiscal documents and silent rollback.

**Audit/reconciliation requirement:** Payment finality, fiscal request attempt, timeout/failure/unknown status, retry/escalation decisions, supervisor approval where applicable, manual release tags, and final fiscal/reconciliation closure.

**Open design questions:** Exact retry policy; duplicate fiscal document detection posture; sequence gap/reserved number handling; fiscal exception release policy; POS Server recovery/anchoring model.

**Recommended diagram content for SDD:** Failure breakpoint sequence showing payment already final, fiscal pending/failed, no normal ExitAuthorization, exception workflow, and reconciliation closure.

### 4.6 Vendor PMS / HCP Degraded Resolve Flow

**Participating components:** Requesting channel, Central PMS, Vendor PMS/HCP connector, projection/read model, continuity policy, Operator Console, Assisted Payment Terminal in Continuity mode where activated, audit/reconciliation workflow.

**Authority owner per major step:**

- Vendor PMS/HCP remains normal authority for raw session lifecycle and normal tariff computation.
- Central PMS owns degraded resolve decision under approved continuity policy.
- Projection/read model provides operational visibility and possible degraded support, not financial truth.
- Operator Console or approved operations workflow owns governance/approval where policy requires.
- Reconciliation workflow owns post-restoration review.

**Normal path:** Central PMS first attempts normal live Vendor PMS/HCP resolve. If unavailable or degraded, Central PMS checks whether continuity/degraded controls are active and whether projection is fresh, unambiguous, and policy-allowed. If allowed, degraded resolve may proceed using approved continuity tariff basis, with clear incident/audit/reconciliation tags.

**Failure path:** Stale, ambiguous, insufficient, or unauthorized projection use fails closed or routes to supervisor/manual review. Passageway records alone must not invent tariffs. Projection must not establish payment finality, fiscal truth, or ExitAuthorization.

**Audit/reconciliation requirement:** Dependency health, projection freshness, degraded decision, affected Site/Site Group, incident/BCP reference, degraded tariff basis, operator/supervisor approval where applicable, and post-restoration comparison.

**Open design questions:** Exact projection freshness threshold; exact degraded tariff configuration owner; exact rounding/grace rules; exact connector health model.

**Recommended diagram content for SDD:** Decision tree from live resolve to degraded-active eligibility to fail-closed/supervisor review.

### 4.7 Continuity Terminal Activation and Restricted Operation Flow

**Participating components:** Operator Console or approved operations workflow, supervisor/authorized activator, Continuity policy, Assisted Payment Terminal in Continuity mode, Central PMS, projection/read model, Payment Orchestrator or approved channel, Site POS Server where available/allowed, reconciliation workflow.

**Authority owner per major step:**

- Approved operations workflow/Operator Console owns activation governance and supervisor approval where policy requires.
- Central PMS owns continuity control state, degraded resolve decision, payment finality, fiscal reference recording, and ExitAuthorization.
- Assisted Payment Terminal owns restricted continuity terminal UI only.
- POS Server remains fiscal authority where fiscal issuance is available and allowed.
- Reconciliation workflow owns post-restoration review.

**Normal path:** A degraded/BCP condition is recognized, authorized activation records affected scope, dependency, incident/BCP reference, allowed workflows, restricted workflows, review interval, audit tag, and reconciliation tag. Continuity Terminal mode becomes available only for the approved scope. Terminal workflows use projection or approved continuity source only where policy allows, and payment/fiscal/exit steps remain under Central PMS/POS authority.

**Failure path:** Missing activation authority, missing incident reference, stale projection, unsafe discount validation, unknown payment outcome, unavailable fiscal issuance without approved exception policy, or expired activation scope blocks or escalates. Continuity Terminal must not silently replace normal WebPay/Vendor PMS/Central PMS authority.

**Audit/reconciliation requirement:** Activation/deactivation records, actor and approval, affected scope/dependency, allowed workflows, terminal use, degraded resolves, payments, fiscal exceptions, manual releases, and post-restoration review.

**Open design questions:** Exact BCP activation authority; exact activation/deactivation workflow; exact offline payment and offline fiscal issuance policy; exact continuity terminal permission matrix.

**Recommended diagram content for SDD:** Continuity state diagram: Normal, Degraded-watch, Degraded-active, Continuity-terminal-active, Restoration-in-progress, Post-restoration-review, Closed/reconciled.

### 4.8 Manual Release Governance Flow

**Participating components:** Operator Console, supervisor/authorized operator, Central PMS, gate/exit operational process, audit/event capability, reconciliation workflow, Management Dashboard.

**Authority owner per major step:**

- Operator Console may support request, review, approval/rejection, reason capture, and tagging.
- Supervisor or approved authority owns approval where policy allows.
- Central PMS remains normal ExitAuthorization authority and records control state where applicable.
- Gate/exit execution remains governed by approved manual emergency process, not dashboard or console payment authority.
- Reconciliation workflow owns closure and review.

**Normal path:** Manual release is requested only as last resort under fiscal, continuity, gate, or emergency exception. Operator Console captures reason, Site/session/device/actor context, incident tag, audit tag, reconciliation tag, and supervisor decision. If approved under policy, the separately approved manual release process executes and records outcome for review.

**Failure path:** Missing supervisor authority, missing reason, missing incident/reconciliation tags, unresolved payment uncertainty, or attempt to treat release as normal payment finality/ExitAuthorization must block or escalate. Operator Console must not directly open gates unless a future approved design explicitly assigns that boundary.

**Audit/reconciliation requirement:** Human approver, requester, device/site/session context, reason, policy basis, incident/audit/reconciliation tags, gate outcome, and post-review status.

**Open design questions:** Exact manual release policy; exact emergency override boundary; exact role matrix; exact relationship between Operator Console approval and physical gate execution.

**Recommended diagram content for SDD:** Governance swimlane separating approval, Central PMS state, physical release execution, and reconciliation closure.

### 4.9 Vendor Payment Acknowledgment Failure Flow

**Participating components:** Central PMS, Vendor PMS/HCP connector, Vendor PMS/HCP, Operator Console, Management Dashboard, reconciliation workflow, audit/event capability.

**Authority owner per major step:**

- Central PMS owns confirmed payment finality regardless of downstream acknowledgment status.
- Connector owns vendor-specific acknowledgment transmission and normalized result reporting.
- Vendor PMS/HCP owns vendor-side paid-state acceptance where integration requires it.
- Operator Console/operations workflow owns escalation review.
- Reconciliation workflow owns backlog closure.

**Normal path:** After Central PMS confirms payment and required downstream prerequisites are met according to Site policy, Central PMS sends vendor paid-state acknowledgment through the connector. Connector reports success, and Central PMS records acknowledgment result for audit/reconciliation.

**Failure path:** Vendor acknowledgment failure, timeout, or unknown result must not erase or reverse Central PMS payment finality. It must be retryable, escalated, or reconciliation-tagged according to later design. Exit behavior may depend on Site policy and integration profile but must not silently bypass audit.

**Audit/reconciliation requirement:** Payment finality, acknowledgment attempt/result, retry/escalation status, affected connector/Site, and reconciliation outcome.

**Open design questions:** Synchronous versus queued/retried acknowledgment per Site; retry limits; whether exit is blocked by acknowledgment failure for each vendor profile; dashboard backlog thresholds.

**Recommended diagram content for SDD:** Downstream acknowledgment flow after payment finality, with failure branch into retry/escalation and reconciliation backlog.

### 4.10 Connector Polling / Projection Freshness Flow

**Participating components:** Vendor PMS/HCP, connector instance, Central PMS projection/read model, Operator Console, Management Dashboard, continuity policy, audit/observability capability.

**Authority owner per major step:**

- Vendor PMS/HCP owns raw session lifecycle.
- Connector owns polling/adapter interaction and normalized operational feed.
- Central PMS owns projection/read-model state and integration health classification.
- Operator Console and Management Dashboard consume projection visibility with freshness labeling.
- Continuity policy controls whether projection may support degraded resolve.

**Normal path:** Connector polls or otherwise receives vendor operational data, normalizes it into Central PMS projection/read-model state, records last successful update/freshness, and exposes health/freshness indicators to operations and dashboards. Projection accelerates lookup/visibility and informs degraded decisions only under approved controls.

**Failure path:** Poll failure, stale projection, mapping ambiguity, connector outage, or vendor unavailability raises operational warnings. Stale, ambiguous, or insufficient projection must not be used as payment, tariff, discount, fiscal, or exit authority.

**Audit/reconciliation requirement:** Last successful poll/update, stale status, connector health, affected Site/Site Group/vendor mapping, degraded-use decision, and post-restoration review where projection supported continuity.

**Open design questions:** HCP connector push versus Central PMS pull topology; exact projection freshness threshold; exact connector health and alert modeling; dashboard refresh interval.

**Recommended diagram content for SDD:** Component/data-flow diagram with projection clearly labeled operational visibility, not financial truth.

### 4.11 Management Dashboard Reporting Source Flow

**Participating components:** Management Dashboard and Reporting, Central PMS, projection/read model, Payment Orchestrator/payments domain, Site POS Server, audit/evidence records, reconciliation workflow, Operator Console/Continuity workflow as governance sources.

**Authority owner per major step:**

- Management Dashboard owns presentation, filtering, export controls, and source/freshness labels.
- Central PMS owns canonical payment, tariff, fiscal-reference, ExitAuthorization, projection, and control-state records.
- POS Server owns fiscal documents and fiscal reports.
- Payment Orchestrator/payments domain owns provider outcome evidence for reporting consumption.
- Reconciliation workflow owns reconciliation status/results.
- Operator Console/Continuity workflow owns governance state for reporting visibility.

**Normal path:** Dashboard user selects authorized Site Group, Site, or portfolio scope. Operational dashboards consume projection and health data with freshness labels. Financial dashboards consume canonical payment/provider/fiscal/reconciliation records. Fiscal dashboards reconcile to POS Server fiscal documents and Central PMS fiscal references. Exports are scope-controlled and audited.

**Failure path:** Projection-only data must not appear as confirmed revenue, fiscal truth, payment finality, or exit authorization truth. Stale projection must display warning labels. Missing fiscal/provider/reconciliation data must be shown as delayed/unknown, not inferred.

**Audit/reconciliation requirement:** Report access, export, filter criteria, source labels, freshness labels, sensitive evidence/report access, and reconciliation status.

**Open design questions:** Exact dashboard role matrix; default Site Group versus Site view behavior; exact source labels; exact refresh intervals; exact fiscal dashboard integration with POS reports; export formats and controls.

**Recommended diagram content for SDD:** Source boundary diagram separating operational estimate, projection-based visibility, canonical financial record, fiscal record, reconciliation result, and audit record.

### 4.12 Post-Restoration Reconciliation Flow

**Participating components:** Reconciliation workflow/team, Central PMS, Vendor PMS/HCP connector, Vendor PMS/HCP, Payment Orchestrator/provider outcome source, Site POS Server, Operator Console, Management Dashboard, Continuity records, audit/event capability.

**Authority owner per major step:**

- Reconciliation workflow owns review status and closure.
- Central PMS owns platform payment/fiscal-reference/ExitAuthorization/control facts.
- POS Server owns fiscal documents and fiscal recovery facts.
- Payment Orchestrator/provider source owns verified provider outcome evidence.
- Vendor PMS/HCP/connector provides vendor session and acknowledgment comparison.
- Operator Console/Continuity records provide governance context.

**Normal path:** After dependency restoration, continuity-only workflows are deactivated and continuity-origin items enter post-restoration review. Reconciliation compares degraded resolves, payment outcomes, fiscal issuance, vendor acknowledgments, manual releases, gate outcomes, and discount activity. Items are closed only after discrepancies are resolved, escalated, or accepted under approved policy.

**Failure path:** Missing vendor data, unmatched payment/fiscal records, unresolved fiscal exceptions, pending vendor acknowledgments, missing manual release approval, or unresolved discount evidence keeps items open. Dashboard may show backlog but must not close reconciliation unless later policy assigns that workflow action.

**Audit/reconciliation requirement:** Continuity activation/deactivation, affected scope, degraded basis, payments, fiscal documents, manual releases, vendor acknowledgment, gate outcomes, statutory discount activity, review decision, and closure evidence.

**Open design questions:** Exact reconciliation SLA; exact reconciliation status labels; exact closure authority; exact evidence retention for continuity exceptions; settlement comparison scope.

**Recommended diagram content for SDD:** Lifecycle/state diagram from Restoration-in-progress to Post-restoration-review to Closed/reconciled, with unresolved-item branches.

## 5. State Ownership Notes

| State category | Architecture-level owner | Notes for SDD |
| --- | --- | --- |
| Vendor PMS raw session state | Vendor PMS/HCP | Raw entry/exit/session lifecycle and normal tariff computation remain vendor authority. ExitPass must not treat projection as the raw session source of truth. |
| Central PMS payment-linked control state | Central PMS | Owns platform session/control facts used by payment, fiscal-reference recording, ExitAuthorization, continuity decisions, and state transitions after vendor/session facts are resolved. |
| Projection/read-model state | Central PMS/integration health workflow, sourced through connector | Operational visibility, lookup acceleration, stale alerts, and degraded support only. Not payment finality, financial truth, tariff authority, fiscal truth, or exit authority. |
| Tariff/payable-basis state | Vendor PMS/HCP in normal mode; Central PMS records immutable TariffSnapshot/payable basis | Normal tariff computation remains vendor-owned. Central PMS records immutable payable basis used for payment. Under continuity, degraded payable basis must use approved policy/configuration only. |
| Payment finality state | Central PMS | Payment Orchestrator verifies and reports provider outcomes but does not declare platform finality. Channels and terminals do not declare finality. |
| Fiscal issuance state | Resolved Site POS Server | POS Server issues Sales Invoice and owns fiscal records/counters/reports. Central PMS records fiscal reference and blocks/continues ExitAuthorization based on fiscal status. |
| ExitAuthorization state | Central PMS | Only Central PMS issues and controls ExitAuthorization. Gate/exit systems consume authorization and report outcomes. POS Server, dashboards, terminals, Operator Console, and payment providers do not issue it. |
| Continuity/degraded-mode state | Central PMS with Operator Console/approved operations workflow for activation governance | Continuity is disabled by default, explicit, scoped, approved where required, audit-tagged, reconciliation-tagged, and time-bound. |
| Review/reconciliation state | Operator Console/approved operations workflow for governance; reconciliation workflow for closure | Includes fiscal exceptions, manual release, continuity-origin items, vendor acknowledgment backlog, evidence review, and post-restoration closure. Dashboards provide visibility, not authority, unless later approved policy assigns workflow action. |

## 6. Failure and Retry Notes

- Fail closed is the default for unknown session, ambiguous session, stale projection, unresolved tariff basis, unresolved discount policy, unknown payment outcome, fiscal issuance failure/timeout, and unsafe continuity conditions.
- Payment finality must not be inferred from WebPay, Assisted Payment Terminal, provider redirect display, dashboard status, or payment provider raw payload alone. Central PMS finality requires verified outcome reporting from Payment Orchestrator or approved payment workflow.
- Fiscal issuance failure after confirmed payment does not automatically reverse payment and does not automatically authorize exit. It creates a controlled fiscal exception.
- Retry design should be described conceptually in the SDD as controlled retry/escalation/reconciliation, without naming queues, payloads, or retry algorithms in this input pack.
- Vendor acknowledgment failure is downstream of Central PMS payment finality and must be auditable, retryable or escalated, and reconciliation-tagged.
- Continuity mode must be explicitly activated or recognized under policy. No silent fallback from normal flow to degraded flow.
- Manual release is a last resort and must be supervisor-approved where policy requires, incident-tagged, audit-tagged, reconciliation-tagged, reason-coded, and post-reviewed.
- Duplicate external callbacks, duplicate fiscal status checks, repeated terminal submissions, and repeated gate authorization attempts must be treated as idempotency/replay-safety concerns for SDD discussion, without defining implementation mechanics here.

## 7. Audit and Reconciliation Notes

The SDD should preserve end-to-end reconstruction from channel entry through exit or exception closure. Minimum reconstruction path should include:

- URL/channel entry, Site Group, resolved Site, and actor/device context where applicable.
- Vendor connector, raw session reference, projection freshness where used, and TariffSnapshot/payable basis.
- Discount validation request, evidence reference, policy basis, validation result, and payable-basis refresh.
- Payment attempt, verified provider outcome, Central PMS payment finality, and provider uncertainty handling.
- Fiscal issuance request/result, Sales Invoice/fiscal reference, fiscal failure/timeout exception, and fiscal retry/escalation status.
- ExitAuthorization issue/consume, gate outcome, manual release governance where applicable.
- Continuity activation/deactivation, affected dependency, incident/BCP reference, allowed workflows, and post-restoration review status.
- Dashboard/report access, export activity, source labels, freshness labels, and sensitive evidence/report access.

Reconciliation should separately compare payment/provider facts, POS fiscal facts, vendor acknowledgment/session facts, gate/manual release facts, and continuity-origin facts. Projection-only records may support investigation but must not close financial or fiscal reconciliation by themselves.

## 8. Eventing / Outbox Considerations at Conceptual Level

The v1.3 SDD should retain the v1.2 posture that material authoritative transitions emit durable events through an outbox-style mechanism, while avoiding hidden authority transfer through events.

Conceptual event families to reflect in SDD:

- Session and scope resolution facts.
- Tariff/payable-basis snapshot facts, including refresh after approved discount validation.
- Statutory discount validation facts.
- Payment attempt and payment finality facts.
- Fiscal issuance request/result/exception facts.
- ExitAuthorization issue/consume and gate outcome facts.
- Connector health, projection freshness, stale projection, and degraded-mode facts.
- Continuity activation/deactivation and Continuity Terminal use facts.
- Manual release governance facts.
- Vendor payment acknowledgment attempt/result facts.
- Reconciliation item opened/updated/closed facts.
- Dashboard/report access/export facts where audit-relevant.

Conceptual rules:

- Events communicate completed facts or auditable operational state changes; they do not make consumers authority owners.
- Each event family should have exactly one authoritative producer in the SDD.
- Consumers must tolerate at-least-once delivery and stale read-models conceptually.
- External provider/vendor/POS payloads should be normalized by the boundary owner before entering canonical platform eventing.
- Eventing should support audit, observability, recovery, and reconciliation without introducing API/database/payload detail in the System Design narrative.

## 9. Recommended SDD Diagrams

The System Design Lead should consider these SDD diagrams or updates. This pack does not create final diagrams.

| Diagram candidate | Recommended content |
| --- | --- |
| v1.3 Context Workflow Overlay | Centralized WebPay, Assisted Payment Terminal, Operator Console, Management Dashboard, Central PMS, Vendor PMS/HCP, Payment Orchestrator, Site POS Server, gate/exit, reconciliation. |
| Site Group/Site Workflow Routing | URL/payment-scope resolution, Site Group lookup scope, resolved Site for vendor mapping/POS/reporting. |
| Normal Payment-to-Exit Fiscal Sequence | Session resolve, tariff basis, payment finality, Sales Invoice issuance, fiscal reference recording, ExitAuthorization, gate consumption. |
| Cashier-Assisted Payment with Discount | Terminal capture, Central PMS/Discount validation, payable-basis refresh, payment, fiscal issuance, exit status display. |
| Payment-Fiscal-Exit Causal Chain Failure Model | Breakpoints for payment unknown, fiscal failed/timeout, vendor acknowledgment failed, gate issue, manual release. |
| Degraded Resolve Decision Tree | Live Vendor PMS/HCP resolve, projection freshness, approved continuity policy, degraded tariff basis, fail-closed/supervisor review. |
| Continuity Activation State Model | Normal, Degraded-watch, Degraded-active, Continuity-terminal-active, Manual-release-controlled, Restoration-in-progress, Post-restoration-review, Closed/reconciled. |
| Manual Release Governance Flow | Request, supervisor approval, reason/incident/audit/reconciliation tagging, execution boundary, post-review. |
| Projection and Connector Freshness Flow | Connector polling/update, projection/read model, freshness labels, dashboard/console visibility, degraded-use guardrails. |
| Reporting Source-of-Truth Boundary | Operational visibility versus canonical financial, fiscal, audit, and reconciliation records. |
| Post-Restoration Reconciliation Flow | Continuity-origin item intake, comparison against payment/fiscal/vendor/gate facts, exception closure. |
| Conceptual Event Ownership Matrix | Event families mapped to single authoritative producers and expected consumers, without payload/queue detail. |

## 10. Open Workflow and State Questions

| Area | Open question for System Design Lead |
| --- | --- |
| WebPay scope resolution | What is the exact public URL slug registry structure, and do slugs resolve to Site Group, Site, or both? |
| Site/Site Group attribution | What is the default view and rule when a customer enters through a shared Site Group but financial/fiscal attribution belongs to a resolved Site? |
| Connector topology | Does each HCP connector push to Central PMS, does Central PMS pull from connector endpoints, or can both patterns exist by deployment? |
| Projection freshness | What exact freshness threshold, stale warning rules, and degraded eligibility thresholds apply? |
| Degraded tariff basis | Who owns approved degraded tariff configuration, rounding, grace periods, and versioning? |
| Continuity activation | Who has exact BCP/continuity activation authority, and what approval workflow is required by Site/dependency type? |
| Continuity offline policy | Is any offline payment or offline fiscal issuance allowed, and under what BIR/accounting/POS Server constraints? |
| Fiscal exception release | Can manual release occur when fiscal issuance is failed/pending/unknown, and what approval/policy boundary applies? |
| POS recovery | How are duplicate fiscal documents, reserved numbers, sequence gaps, fiscal counter continuity, and recovery anchoring handled? |
| Vendor acknowledgment | Is vendor paid-state acknowledgment synchronous, queued/retried, or Site/vendor-profile dependent, and does failure block exit? |
| Manual release execution | What exact boundary exists between Operator Console approval and physical/manual gate execution? |
| Discount policy | What is the exact policy registry, evidence retention, pending duration, and government/cooperative verification integration for statutory discounts? |
| Dashboard freshness | What exact dashboard refresh intervals, source labels, and alert thresholds apply by operational, financial, fiscal, and reconciliation view? |
| Reconciliation closure | What are the exact reconciliation SLA, status labels, closure authority, and settlement comparison scope? |
| Role matrix | What exact role matrix governs cashier, supervisor, operator, auditor, finance, support, administrator, and read-only client actions across workflows? |

## 11. Recommended ExitPass System Design v1.3 Sections Affected

| SDD section | Recommended impact |
| --- | --- |
| System Overview | Add v1.3 workflow scope: centralized WebPay, Assisted Payment Terminal, Continuity, Operator Console, POS/Invoicing, Management Dashboard. |
| System Context | Update external actors/systems for Site Group/Site, Vendor PMS/HCP connectors, Site POS Server, terminal app family, dashboard/reporting, and reconciliation. |
| System Architecture | Preserve authority separation and add Site POS Server, projection/read model, continuity state, reporting source boundaries, and terminal module separation. |
| Trust Boundaries | Reflect public WebPay, assisted terminals, Operator Console, dashboard users, Vendor PMS/HCP, payment providers, POS Server, gate systems, and evidence/reporting access. |
| Core Workflows | Add or revise the twelve workflows covered in this input pack, explicitly naming authority owner per major step. |
| Event Architecture | Extend event families for fiscal issuance, projection freshness, continuity, manual release, vendor acknowledgment, dashboard/export audit, and reconciliation. |
| State Machines | Add/update conceptual state models for fiscal issuance, continuity/degraded mode, projection freshness, review/reconciliation, and vendor acknowledgment while preserving Central PMS payment/exit ownership. |
| Data Architecture | Mention source-of-truth categories at conceptual level only; defer tables/columns to database design. |
| API Architecture | Preserve workflow boundaries without endpoint paths or DTO definitions in SDD narrative. |
| Security Architecture | Reflect role/device/shift controls for Assisted Payment Terminal, Operator Console, dashboard/export, continuity activation, manual release, and evidence access. |
| Failure Mode Architecture | Add failure breakpoints for fiscal issuance, vendor acknowledgment, projection stale, continuity activation, manual release, payment uncertainty, and post-restoration reconciliation. |
| Observability | Include connector health, projection freshness, fiscal exception backlog, vendor acknowledgment backlog, continuity state, dashboard access/export audit, and reconciliation backlog. |
| Business Continuity | Expand from v1.2 continuity posture to v1.3 Continuity Terminal, activation/deactivation, restricted operation, degraded resolve, manual release, and post-restoration review. |
| Operational Runbooks | Add runbook hooks for fiscal exception, connector stale, continuity activation/deactivation, manual release, vendor acknowledgment backlog, and post-restoration reconciliation. |
| Appendix | Update diagram index, glossary, open questions, and authority/event/state ownership matrices. |

## 12. Summary for System Design Lead

ExitPass v1.3 preserves the core authority model from v1.2 while expanding the workflow surface around centralized WebPay, assisted cashier operation, statutory discount capture, Site POS Server fiscal issuance, controlled degraded operation, manual release governance, connector projection visibility, dashboard/reporting, and reconciliation.

The key SDD drafting rule is to keep the causal chain explicit:

1. Vendor PMS/HCP owns normal raw session and tariff facts.
2. Central PMS owns platform control state, payment finality, fiscal reference recording, degraded decisions, and ExitAuthorization.
3. Payment Orchestrator verifies and reports provider outcomes but does not declare finality.
4. Resolved Site POS Server issues fiscal documents and does not authorize exit.
5. Projection supports visibility and controlled degraded decisions only.
6. Operator Console and Management Dashboard provide governance and visibility, not payment/fiscal/exit authority.
7. Assisted Payment Terminal is a payment-capable workflow surface but not payment finality, discount policy, fiscal, or exit authority.
8. Continuity and manual release are explicit, approved, audited, reconciliation-tagged, and post-reviewed.

The System Design should carry open questions forward rather than silently resolving them, especially projection freshness, continuity activation authority, fiscal exception release policy, POS recovery mechanics, vendor acknowledgment retry behavior, and reconciliation closure rules.
