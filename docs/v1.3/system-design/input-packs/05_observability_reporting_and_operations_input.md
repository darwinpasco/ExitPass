# ExitPass System Design v1.3 Input Pack 05: Observability, Reporting, and Operations

## 1. Purpose

This input pack summarizes the observability, reporting, health, operations, continuity visibility, reconciliation, and runbook implications that the ExitPass System Design v1.3 lead should incorporate at architecture level.

The pack is limited to system-design inputs. It does not define dashboard wireframes, BI/data mart design, database/reporting schema, event payload schemas, alert rules, monitoring tool configuration, runbook procedures, implementation classes, API contracts, or source code.

The key design posture is:

- Operational visibility may use connector, projection, health, and continuity state.
- Financial, fiscal, settlement, and reconciliation reporting must use canonical payment, provider, POS fiscal, and reconciliation records.
- Observability must preserve ExitPass authority boundaries rather than creating alternate truth through dashboards, logs, projections, or reports.
- Continuity, manual release, fiscal exceptions, payment uncertainty, and degraded operation must be visible, auditable, tagged, and reconciled.

## 2. Source Documents Reviewed

The following approved source documents were reviewed for this input pack:

| Source | Relevant coverage reviewed |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Sections 3.4, 3.6, 4.3 to 4.5, 5.1, 6.2 to 6.4, 9, 12 to 17, 18, and open questions. Core authority model, projection visibility, stale connector alerts, fiscal issuance exceptions, audit/logging/reporting, source-of-truth separation, traceability, degraded operation, and dependencies. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Sections 9 to 14, 18 to 23, 25, 29, and acceptance criteria. Terminal health, terminal identity, payment/fiscal/exit status display, Continuity Terminal activation, exception messaging, audit metadata, and terminal reporting boundaries. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Sections 9 to 12, 17 to 25, 27, 31 to 34, and acceptance criteria. Continuity states, activation/deactivation, connector health, projection freshness, degraded state visibility, fiscal/payment/gate exceptions, manual release, reconciliation, post-restoration review, and monitoring expectations. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Sections 12 to 14, 21 to 24, 27 to 29, 33, and acceptance criteria. Operator Console operational visibility, connector/projection visibility, stale warnings, fiscal exception review, manual release governance, audit logging, scoped reporting/export, and non-payment boundary. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Sections 10 to 13, 15 to 18, 20 to 31, 33 to 37. Dashboard/reporting domains, source labeling, operational visibility versus financial truth, continuity reporting, connector health, projection freshness, export audit, RBAC/scope controls, and open reporting questions. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Sections 10 to 18, 21 to 24, 27 to 30, 34 to 36, and acceptance criteria. Site POS Server authority, fiscal issuance health and exceptions, Sales Invoice reporting, X-read/Z-read/BIR Sales Summary/EJ/POSLog/export audit, fiscal recovery, and reconciliation boundaries. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Sections 2 to 6. Approved BRD baseline, preserved authority model, downstream open items, and Operations Runbook Pack as a later deliverable. |
| `docs/v1.3/system-design/ExitPass_System_Design_v1.3_Orchestration_Plan.md` | Sections 2 to 9. Input-pack boundary, v1.2 style/outline rule, file ownership rules, and validation rules. |
| `D:\Docs\ExitPass\v1.2\ExitPass System Design v1.2.docx` | Style and outline baseline, especially v1.2 sections 12.9, 14, 15, and 16 covering failure monitoring/alerting, observability, business continuity, and operational runbook posture. |

## 3. Observability Domains

The System Design v1.3 should preserve the v1.2 control-aware observability posture while expanding the monitored domains for v1.3 scope.

| Domain | Architecture-level expectation | Primary source support |
| --- | --- | --- |
| Connector health | Connector status, last successful poll, poll latency, failed poll count, vendor acknowledgment backlog, mapping health where available, and degraded/stale state should be visible to authorized users. Connector observations are health signals, not tariff/payment truth. | Core BRD 5.1.5, 13.3, 14.1, 17.1; Continuity 12.2, 18, 20, 27; Operator Console 22; MDR 21. |
| Projection freshness | Projection freshness, stale sessions, sessions not seen in latest poll, freshness threshold status, and stale/ambiguous/insufficient warnings should be visible. Projection remains operational visibility and degraded support only. | Core BRD 5.1.5, 13.4, 14.4; Continuity 20; Operator Console 22; MDR 10.1, 12, 21, 31. |
| HCP / Vendor PMS availability | Normal resolve depends on live Vendor PMS/HCP for session lifecycle and tariff computation. Vendor unavailability should be observable by Site/Site Group, connector instance, affected VendorSystem, and incident scope. | Core BRD 3.3, 5.1.4, 13.2, 17.1; Continuity 12.1, 19; MDR 21. |
| Vendor PMS degraded state | Degraded-watch and degraded-active states should be visible and scoped. Degraded use must show approved policy status, projection freshness, allowed workflow scope, and activation/deactivation status. | Continuity 10, 11, 12, 18, 20, 27; MDR 22; Operator Console 21, 22. |
| Site and Site Group operational visibility | Dashboards/reports should support Site Group lookup/payment-scope views and Site reporting/contract/vendor/POS/operations views. Financial and fiscal attribution should use resolved Site. | Core BRD 3.4, 5.1.2, 5.1.3; MDR 11, 29; Operator Console 10, 17. |
| POS Server health and fiscal issuance | POS Server fiscal issuance status, pending/failed/timed-out issuance, fiscal reference missing, report/export availability, fiscal state integrity, and recovery status should be visible without making dashboards or Operator Console fiscal authorities. | Core BRD 12.4, 13.6, 13.7; POS 10, 18, 22, 28, 30, 34; Operator Console 23; MDR 24. |
| Fiscal issuance exceptions | Payment received but fiscal issuance pending, fiscal issuance failed/timed out, fiscal reference missing, and payment received but exit authorization unavailable must be visible as controlled exceptions. | Core BRD 13.6, 13.7; POS 18.2, 30; Operator Console 23; Continuity 12.5; MDR 24, 27. |
| Payment Orchestrator health | Provider interaction, callback/retrieval status, provider outcome evidence, payment uncertainty, provider outcome backlog, and payment rail performance should be observable. Payment Orchestrator does not declare platform finality. | Core BRD 5.1.6, 12.3, 15.1, 17.2; Continuity 12.6, 23; MDR 23. |
| Provider outcome uncertainty | Unknown, pending, failed, cancelled, or uncertain provider outcomes must be visible and must not imply payment finality or exit eligibility. | Assisted Payment Terminal 18; Continuity 12.6, 23; MDR 22, 23; Core BRD 13.1. |
| Gate/exit health | Gate/exit device issue visibility should distinguish hardware execution failure from payment, fiscal, and authorization truth. Gate execution must consume Central PMS authorization and not infer eligibility. | Core BRD 13.11, 17.4; Continuity 12.8, 23, 24; MDR 27; Operator Console 24. |
| Continuity/degraded state visibility | Normal, degraded-watch, degraded-active, Continuity-terminal-active, restoration-in-progress, post-restoration-review, and closed/reconciled states should be visible to authorized users. | Continuity 10, 11, 18, 25, 27; MDR 22; Operator Console 21. |
| Continuity Terminal activation state | Continuity Terminal is disabled by default. Activation state should show affected Site/Site Group, dependency, incident/BCP reference, approving authority where required, activation reason, allowed workflow scope, and reconciliation tags. | Assisted Payment Terminal 9.2, 14.2, 20; Continuity 11, 13, 21; MDR 22. |
| Management dashboards and reporting | Management Dashboard provides visibility only. It aggregates operational, financial, fiscal, audit, continuity, and reconciliation views while labeling each metric by source and freshness. | MDR 4 to 6, 10 to 13, 20 to 31. |
| Operational visibility versus financial truth | Operational dashboards may use projections and health feeds. Financial/revenue dashboards must use canonical payment, provider, fiscal, and reconciliation records. | Core BRD 4.5, 14.4; Continuity 27; MDR 10, 12, 13, 31. |
| Audit, eventing, correlation, and traceability | The architecture should support reconstruction from URL/channel and Site Group through resolved Site, connector, TariffSnapshot, PaymentConfirmation, fiscal issuance reference, ExitAuthorization, gate consumption, exceptions, continuity tags, and reporting/export access. | Core BRD 14.1 to 14.3; Operator Console 27; MDR 28, 29; POS 28, 34; v1.2 SDD 14.7. |
| Reconciliation and post-restoration review | Continuity-origin activity, degraded resolves, fiscal exceptions, manual release, vendor acknowledgment failures, gate/exit events, and payment uncertainty must feed reconciliation and post-restoration review visibility. | Continuity 25, 27; Core BRD 12.5, 13.8, 13.10, 13.11; MDR 25; POS 34. |
| Alerting and incident visibility | Alerts should cover connector stale/unavailable, projection stale, Vendor PMS unavailable, payment uncertainty, fiscal issuance exceptions, POS Server fiscal recovery risk, gate/exit issue, continuity activation, manual release, and reconciliation backlog. | Core BRD 13, 14; Continuity 12, 27, 31; MDR 21, 22; v1.2 SDD 12.9, 14.6, 15.11. |
| Export/report access audit | Report access and exports should be audited, scoped, and labeled with source, generation time, filters, and freshness where applicable. Sensitive evidence and PII require elevated permission and privacy controls. | Core BRD 14.1; Operator Console 27; MDR 28 to 31; POS 28. |
| Manual release and exception review visibility | Manual release must be visible as last-resort exception activity with supervisor approval where required, incident/audit/reconciliation tags, reason/context, and post-incident or post-restoration review status. | Core BRD 13.11, 14.1; Continuity 24, 25, 27; Operator Console 24; MDR 27. |

## 4. Required Health Signals

The System Design should define architecture-level health signal categories without locking exact thresholds or monitoring tool rules.

| Health signal category | Required signal expectation |
| --- | --- |
| Connector instance health | Connector status, availability, last successful poll, failed poll count, poll latency, connector error state, and affected VendorSystem/Site/Site Group scope. |
| Projection freshness | Projection last update, freshness state, stale warning state, stale sessions, sessions not seen in latest poll, ambiguity/insufficiency indicators, and freshness threshold result once threshold is defined. |
| Vendor PMS/HCP availability | Live resolve availability, tariff calculation availability, HCP/Vendor PMS reachable/unreachable/degraded state, and vendor acknowledgment backlog where relevant. |
| Adapter mapping health | Site to VendorSystem and vendor parking object mapping health where available, including warning when mapping ambiguity affects lookup, reporting, or degraded use. |
| Central PMS control health | Ability to coordinate session projection/control state, TariffSnapshot capture, PaymentAttempt/PaymentConfirmation recording, fiscal reference recording, ExitAuthorization issuance, audit/event persistence, and continuity decisions. |
| Payment Orchestrator health | Provider connectivity, callback/retrieval verification status, provider outcome backlog, uncertain outcome count, failed initiation/callback verification count, and payment rail availability/performance. |
| POS Server health | Site POS Server availability, fiscal issuance status, fiscal queue/backlog if applicable, failed/timed-out issuance, fiscal reference missing, fiscal export/report generation availability, and fiscal recovery integrity risk. |
| Fiscal counter and recoverability health | Fiscal state continuity, Sales Invoice sequence continuity, Z-counter/reset counter/Grand Total preservation status where applicable, and supervised recovery required indicator. |
| Gate/exit health | Gate/controller availability, authorization consumption/report outcome status, failed gate execution, delayed gate report, and manual/emergency release visibility where approved. |
| Continuity state health | Normal/degraded-watch/degraded-active/Continuity-terminal-active/restoration-in-progress/post-restoration-review/closed state and affected Site/Site Group/dependency scope. |
| Continuity Terminal activation | Activation enabled/disabled state, active workflow scope, activation authority/approval where required, incident/BCP reference, device/terminal identity, and audit/reconciliation tags. |
| Assisted Payment Terminal health | Terminal authentication/trust status, assigned Site/Site Group context, cashier/shift context, terminal availability during operating hours, and pending payment/fiscal/exit status display capability. |
| Operator Console health | Authorized operational users can view payment/fiscal/exit status, connector health, projection freshness, stale warnings, continuity status, fiscal exceptions, manual release governance, and post-restoration review context. |
| Dashboard/reporting freshness | Data refresh status, generation time, filter/scope context, source category label, operational/fiscal/financial/reconciliation freshness, export status, and stale warning state. |
| Audit/event persistence | Audit/event write success, correlation continuity, privileged action audit state, evidence/report/export access audit status, and audit backlog/error state. |
| Reconciliation health | Reconciliation run status, item status, unmatched payment/fiscal/settlement/vendor acknowledgment/manual release/continuity-origin items, post-restoration review state, and reconciliation backlog. |

## 5. Required Operational Metrics

The System Design should describe required metric categories at architecture level. Exact SLOs, thresholds, sampling intervals, dashboards, and alert rules remain open for later design.

| Metric category | Metrics expected by source requirements |
| --- | --- |
| Lookup and projection metrics | Session lookup success/failure by Site Group/payment scope, projection age, projection freshness state, stale session count, sessions not seen in latest poll, ambiguous/insufficient projection count, degraded resolve blocked count. |
| Connector and Vendor PMS metrics | Connector availability, failed polls, poll latency, last successful poll age, Vendor PMS/HCP live resolve availability, vendor acknowledgment backlog, mapping health exceptions. |
| Site and Site Group operations | Active sessions/vehicles where projection supports it, occupancy approximation with freshness label, long-stay/session-age indicators, Site/Site Group operational status, incident backlog by scope. |
| Payment metrics | Payment attempts by status, payment confirmations, verified provider outcomes by status, uncertain/pending provider outcomes, payment channel mix, payment rail performance, provider callback/retrieval verification issues. |
| Fiscal metrics | Fiscal issuance pending/failed/succeeded, fiscal issuance timeout count, fiscal reference missing count, Sales Invoice count/totals, fiscal exception exposure, reprint/void/refund/cancel/return counts where authorized, X-read/Z-read/BIR Sales Summary/EJ/POSLog/export generation status where applicable. |
| Exit metrics | ExitAuthorization issued, pending, consumed, failed, expired, gate outcome report status, gate/device issue count, delayed or missing gate outcome, manual/emergency release count where policy allows. |
| Continuity metrics | Continuity activation/deactivation count, current continuity state, affected Site/Site Group/dependency, Continuity Terminal activation count, degraded-active duration, restoration-in-progress count, post-restoration-review backlog. |
| Exception metrics | Fiscal exception backlog, payment uncertainty backlog, vendor acknowledgment failure backlog, manual release cases, stale projection restrictions, supervisor/manual review routing, statutory discount exception states where applicable. |
| Reconciliation metrics | Reconciliation run status, unmatched payment items, unmatched fiscal items, settlement comparison status, continuity-origin items, manual release items, fiscal exception reconciliation status, post-restoration review closure state. |
| Audit/access metrics | Privileged action count, evidence access count, report access count, export count, failed/denied access count, operator/supervisor activity, device/terminal/shift-linked actions, sensitive data access audit status. |
| Reliability and availability metrics | Component health by service boundary, POS Server availability by Site/channel, Payment Orchestrator/provider availability, Central PMS core availability, Operator Console/Dashboard availability, terminal availability during approved operating hours. |

## 6. Required Reporting Categories

The System Design should identify reporting categories and their source categories without defining report layouts or BI storage.

| Reporting category | Required scope |
| --- | --- |
| Operational visibility reports | Active sessions, active vehicles, occupancy approximation, entry time aging, long-stay sessions, stale sessions, sessions not seen in latest poll, connector health, projection freshness, Vendor PMS/HCP availability, Site/Site Group operational status, incident backlog, manual release counts, fiscal exception backlog. |
| Connector health and projection freshness reports | Connector status, last successful poll, poll latency, failed poll count, projection age/freshness, sessions projected, stale sessions, vendor acknowledgment backlog, mapping health where available, stale warnings. |
| Site Group reporting | Customer lookup/payment-scope activity, shared payment-scope flow, channel usage by Site Group, and customer-facing route/scope visibility. |
| Site reporting | Revenue/fiscal attribution, Vendor PMS mapping, POS Server routing, Site exceptions/outages, operational ownership, fiscal exception/reconciliation backlog. |
| Payment and revenue reports | Gross/net amounts by Site, PaymentAttempt status, PaymentConfirmation records, ProviderOutcome records, payment rail performance, payment uncertainty, payment channel mix, provider outcome evidence. |
| Fiscal and POS reports | Sales Invoice count/totals, fiscal issuance pending/failed/succeeded, fiscal reference visibility, BIR/fiscal report reference visibility where authorized, X-read/Z-read/BIR Sales Summary/EJ/POSLog/export alignment where applicable. |
| Reconciliation and settlement reports | Reconciliation run/item status, unmatched payment items, unmatched fiscal items, settlement comparison, continuity-origin reconciliation items, manual release reconciliation items, fiscal exception reconciliation status. |
| Continuity and degraded-state reports | Normal/degraded-watch/degraded-active/Continuity-terminal-active/restoration/post-restoration states, affected dependency, incident/BCP reference where authorized, payment uncertainty, vendor acknowledgment backlog, manual release counts, post-restoration review state. |
| Manual release and exception reports | Manual release count/reason/category, incident/audit/reconciliation tags, fiscal exceptions, payment uncertainty, gate/exit issue reports, supervisor approval where required, post-incident review status. |
| Compliance and audit reports | Statutory discount validation report, evidence access report, operator activity, supervisor override, manual release, continuity activation, post-restoration review, fiscal exception audit, connector health incident, reprint/access/export where applicable. |
| Export/access audit reports | Report access, export generation, export filters, export user/scope, generation time, source/freshness labels, sensitive evidence/PII access, denied access attempts. |
| Executive/management summaries | Site performance, Site Group performance, portfolio summary, cashierless usage, payment channel mix, exception backlog, continuity incident summary, revenue assurance summary, SLA/health summary, with each metric source-labeled. |

## 7. Source-of-Truth Labeling Rules

The System Design should require source labeling across dashboards, reports, alerts, logs, and operational status views.

| Source category | Labeling rule |
| --- | --- |
| Projection-based visibility | Must be labeled as operational visibility or operational estimate. It may support lookup acceleration, dashboard visibility, connector health, occupancy/session monitoring, and approved degraded support. It must not be labeled or interpreted as financial truth, fiscal truth, payment finality, tariff truth in normal mode, or exit authorization truth. |
| Live Vendor PMS/HCP response | In normal mode, Vendor PMS/HCP remains authority for raw session lifecycle and tariff computation. Live resolve outputs captured by Central PMS become immutable TariffSnapshot basis for payment/reconciliation, not dashboard-owned truth. |
| Central PMS canonical records | TariffSnapshot, PaymentAttempt, PaymentConfirmation/payment finality, fiscal issuance reference recording, ExitAuthorization, session projection/control state, and continuity decision state should be labeled as Central PMS authority records where applicable. |
| Payment Orchestrator/provider records | ProviderOutcome and provider evidence should be labeled as verified provider outcome evidence. They do not establish platform payment finality until Central PMS records finality. |
| POS Server fiscal records | Sales Invoice, fiscal reports, X-read, Z-read, BIR Sales Summary, EJ, POSLog, fiscal exports, fiscal counters, reprints, adjustments, and fiscal audit records must be labeled as POS Server fiscal authority records. |
| Reconciliation records | Reconciliation status, unmatched items, continuity-origin reconciliation, manual release reconciliation, fiscal exception reconciliation, and settlement comparison should be labeled as reconciliation results, not source transaction facts. |
| Audit/event records | Operator actions, supervisor approvals, evidence access, continuity activation, manual release, fiscal exceptions, export/report access, and privileged actions should be labeled as audit or workflow records and tied to correlation context. |
| Management summary metrics | Each metric should identify whether it is an operational estimate, canonical financial record, fiscal record, reconciliation result, or audit record. Mixed-source summaries must preserve source labels per metric. |
| Exported reports | Exports must include source, generation time, filter criteria, scope, and data freshness labels where applicable. |
| Stale data | Stale, ambiguous, insufficient, or unavailable data must show warning labels and must not be used as approval for payment, tariff, discount, fiscal issuance, or exit. |

## 8. Alerting and Stale-Warning Expectations

The System Design should define alert categories and stale-warning expectations, not alert implementation rules.

Required alert/stale-warning categories:

- Connector stale, connector unavailable, or repeated poll failure.
- Projection stale, ambiguous, insufficient, or outside the approved degraded-use threshold once defined.
- Vendor PMS/HCP unavailable or degraded for live session/tariff resolve.
- Vendor payment acknowledgment failed, queued, retried, or escalated.
- POS Server unavailable, fiscal issuance failed, fiscal issuance timed out, fiscal reference missing, or fiscal recovery requires supervised audit.
- Payment provider timeout, unknown provider outcome, callback/retrieval verification failure, or provider outcome uncertainty.
- Payment received but fiscal issuance pending.
- Payment received but ExitAuthorization not yet available.
- ExitAuthorization pending, failed, expired, or not consumed.
- Gate/exit device unavailable, failed execution, missing gate outcome, or manual/emergency release initiated.
- Continuity degraded-watch, degraded-active, Continuity Terminal activated, restoration-in-progress, post-restoration-review backlog, or continuity event not closed/reconciled.
- Manual release requested, approved, rejected, or pending review.
- Evidence/report/export access by privileged role, denied access, or sensitive evidence exposure risk.
- Reconciliation backlog, unmatched payment/fiscal/settlement item, continuity-origin item, or fiscal exception reconciliation gap.

Alert content should preserve operational context:

- Affected Site and/or Site Group.
- Affected Vendor PMS/HCP, connector instance, POS Server, payment rail/provider, gate/exit device, terminal, or dashboard/reporting scope where applicable.
- Current authority state where relevant, such as provider outcome uncertain, Central PMS finality pending, fiscal issuance pending, ExitAuthorization pending, or manual release under approved exception.
- Incident/BCP reference and continuity state where applicable.
- Freshness/staleness indicator and data source category.
- Correlation identifiers sufficient for audit reconstruction, without defining payload schema in this input pack.

Open threshold items should remain explicit:

- Exact projection freshness thresholds.
- Exact connector health alert thresholds.
- Exact dashboard refresh intervals.
- Exact reconciliation SLA and status labels.
- Exact fiscal exception release policy.
- Exact manual release policy and emergency override boundary.

## 9. Runbook Implications

This pack does not create actual runbooks. It identifies runbook topics that the System Design should reserve or reference at architecture level, consistent with the v1.2 SDD posture where observability, business continuity, and operational runbooks are separate top-level sections.

Recommended runbook implication categories:

| Runbook area | Architecture-level implication |
| --- | --- |
| Connector stale/unavailable | Operators need visibility into connector health, projection freshness, affected scope, stale warnings, and degraded-use restrictions. |
| Vendor PMS/HCP outage | Runbook should preserve normal authority boundary: Vendor PMS/HCP unavailable does not automatically permit payment or exit. |
| Projection stale/ambiguous/insufficient | Runbook should route to fail-closed or supervisor/manual review; projection must not become financial or exit truth. |
| Payment provider uncertainty | Runbook should require verified outcome before Central PMS finality and must keep exit blocked until payment and fiscal prerequisites are satisfied. |
| Payment Orchestrator/provider failure | Runbook should distinguish provider interaction failure from Central PMS payment finality and reconciliation. |
| POS Server/fiscal issuance failure | Runbook should handle paid-but-not-fiscally-issued states, retries/escalation, customer/operator messaging, and no normal ExitAuthorization before fiscal completion unless approved exception policy applies. |
| Fiscal recovery/failover | Runbook should protect fiscal counters, Sales Invoice sequence, Grand Total, Z-counter/reset counter, EJ/POSLog/export continuity, and supervised recovery audit. |
| Gate/exit device issue | Runbook should distinguish hardware execution issue from authorization truth and preserve Central PMS authorization boundary. |
| Continuity activation/deactivation | Runbook should require explicit activation, affected scope, approval/authority where required, incident/BCP reference, allowed workflow scope, deactivation criteria, and post-restoration review. |
| Continuity Terminal activation | Runbook should preserve disabled-by-default posture and require activation context, device/terminal identity, Site/Site Group scope, and audit/reconciliation tags. |
| Manual release | Runbook should treat manual release as last resort, supervisor-approved where policy requires, incident-tagged, audit-tagged, reconciliation-tagged, and subject to review. |
| Reporting/export access | Runbook should preserve RBAC/scope rules, export audit, evidence/PII restrictions, and source/freshness labels. |
| Reconciliation/post-restoration review | Runbook should ensure continuity-origin activity, fiscal exceptions, payment uncertainty, vendor acknowledgment failures, manual release, and gate events remain open until reconciled or formally closed. |

## 10. Reconciliation and Post-Restoration Review Expectations

The System Design should treat reconciliation and post-restoration review as architecture concerns, not optional reporting cleanup.

Required expectations:

- Continuity-origin activity must move into post-restoration review after restoration or deactivation.
- Reconciliation should include continuity activations/deactivations, affected Site/Site Group/dependency, projection-based resolves, degraded tariff basis, payments, uncertain payment outcomes, fiscal issuance successes/failures/pending cases, manual releases, vendor payment acknowledgments, gate/exit events, continuity-mode statutory discount activity, and material customer/operator exception messages.
- Financial reconciliation must use canonical payment, provider, fiscal, and reconciliation records. Projection data is excluded from financial truth except as separately labeled operational context.
- POS/fiscal reconciliation must align Sales Invoice records, fiscal lines, fiscal counters, X-read, Z-read, BIR Sales Summary, EJ, POSLog, exports, audit records, and Central PMS fiscal issuance references where applicable.
- Manual release records must remain tagged and reviewable; they must not be converted into normal payment finality, fiscal truth, or normal ExitAuthorization.
- Vendor acknowledgment failures after Central PMS payment finality must be queued/retried/escalated according to later design and reconciliation-tagged.
- Fiscal issuance failure after payment finality must not automatically reverse payment and must not automatically authorize exit. Pending/failure state remains visible until resolved or formally handled through approved exception/manual-release policy.
- Payment outcome uncertainty must remain pending until Payment Orchestrator or approved payment workflow verifies the outcome and Central PMS records finality.
- Reconciliation dashboards/reports should show run status, item status, unmatched payment items, unmatched fiscal items, settlement comparison, continuity-origin items, manual release items, fiscal exception reconciliation status, and post-restoration review state.

## 11. Open Observability / Reporting / Operations Questions

The following open questions should be carried into the System Design drafting notes or unresolved-items section as applicable.

| ID | Open question |
| --- | --- |
| OBS-OQ-001 | What exact projection freshness thresholds apply by Site, Site Group, Vendor PMS/HCP, connector instance, payment channel, and degraded-use scenario? |
| OBS-OQ-002 | What exact connector health thresholds and alert severity rules apply for failed polls, poll latency, last successful poll age, and Vendor PMS/HCP unavailability? |
| OBS-OQ-003 | How should HCP connector health and projection freshness be modeled architecturally without creating database schema in the SDD? |
| OBS-OQ-004 | What is the exact BCP/continuity activation authority by role, Site/Site Group, dependency, and incident type? |
| OBS-OQ-005 | What is the exact Continuity Terminal activation/deactivation workflow and what statuses must be exposed to Operator Console and Management Dashboard? |
| OBS-OQ-006 | What is the exact manual release policy, including emergency override boundary, approving role, allowed execution path, and reconciliation SLA? |
| OBS-OQ-007 | What is the exact fiscal issuance exception release policy when payment finality exists but fiscal issuance is failed, timed out, or unknown? |
| OBS-OQ-008 | What is the exact POS Server fiscal health model, including fiscal counter continuity, recovery/failover status, fiscal export/report availability, and supervised recovery audit status? |
| OBS-OQ-009 | What is the exact dashboard refresh interval, data freshness display, and stale warning rule set per dashboard/report category? |
| OBS-OQ-010 | What are the exact reconciliation SLA, status labels, owner roles, and closure criteria after restoration? |
| OBS-OQ-011 | What exact report export formats, export approval controls, export retention periods, and exported-report labeling requirements apply? |
| OBS-OQ-012 | What exact evidence access redaction, masking, privacy, and audit controls apply in compliance reports and exports? |
| OBS-OQ-013 | What exact dashboard/report delivery scope is included in v1.3 versus deferred to a later Management Dashboard System Design or BI implementation design? |
| OBS-OQ-014 | What exact relationship should hold between Operator Console scoped reporting and broader Management Dashboard/Reporting views? |
| OBS-OQ-015 | What exact Site Group versus Site default view behavior applies for operational, financial, fiscal, and executive summaries? |
| OBS-OQ-016 | Is vendor payment acknowledgment synchronous or queued/retried per Site, and what health/reconciliation signals should surface? |
| OBS-OQ-017 | What BIR/fiscal report visibility is allowed in Management Dashboard without making it fiscal authority or a BIR-authoritative reporting system? |
| OBS-OQ-018 | What monitoring stack, tracing implementation, dashboard technology, BI/reporting technology, and data store changes are required? These are deferred to later technical design and should not be specified in the SDD beyond architecture posture. |

## 12. Recommended ExitPass System Design v1.3 Sections Affected

The System Design Lead should incorporate this input into the v1.2-style outline as controlled v1.3 updates.

| SDD section | Recommended treatment |
| --- | --- |
| System Overview | Note observability/reporting as visibility and control support, not authority. |
| System Context | Include Management Dashboard/Reporting, Operator Console, Assisted Payment Terminal/Continuity Terminal, POS Server, Payment Orchestrator, Vendor PMS/HCP, connector instance, and gate/exit boundaries. |
| System Architecture | Preserve service/component lifecycle, logs, metrics, health checks, and operational boundaries; add v1.3 domains for POS Server, Continuity, Management Dashboard, connector projection, and Site/Site Group scope. |
| Trust Boundaries | Make dashboard/report/export access, evidence access, terminal/device trust, POS Server fiscal authority, Payment Orchestrator provider boundary, Vendor PMS/HCP boundary, and gate/exit boundary explicit. |
| Core Workflows | Reflect payment-to-exit with fiscal issuance, degraded resolve, Continuity Terminal activation, manual release governance, fiscal exception review, and post-restoration review at architecture level. |
| Event Architecture | Require correlation across channel entry, Site Group, resolved Site, connector, projection, TariffSnapshot, PaymentAttempt, ProviderOutcome, PaymentConfirmation, fiscal reference, ExitAuthorization, gate outcome, audit/export, continuity, and reconciliation events without defining payload schemas. |
| State Machines | Include or reference high-level continuity states, payment uncertainty state, fiscal issuance exception state, ExitAuthorization/gate consumption state, manual release review state, and reconciliation/post-restoration review state. |
| Data Architecture | Keep source-of-truth classification explicit; avoid reporting schema or data mart design; label projection, canonical payment, fiscal, reconciliation, and audit records by authority category. |
| API Architecture | Mention health/status/reporting/audit access expectations only at system boundary level; do not define endpoint paths or DTOs. |
| Security Architecture | Include RBAC/scope controls for dashboards, exports, evidence access, privileged actions, terminal identity, device trust, continuity activation, manual release, and fiscal exception review. |
| Failure Mode Architecture | Add v1.3 failure visibility for connector stale, Vendor PMS/HCP unavailable, projection stale, payment uncertainty, fiscal issuance failure, POS Server recovery risk, gate/exit issue, continuity activation, manual release, and reconciliation backlog. |
| Deployment Architecture | Preserve observability per deployable service and Site-level POS Server operational boundary; do not define monitoring tool configuration. |
| Observability | Primary target section. Add control-aware observability domains, health signals, metrics, alert categories, event/audit correlation, data freshness/source labels, operational dashboards, and export/access audit expectations. |
| Business Continuity | Add continuity/degraded state visibility, Continuity Terminal activation state, incident/BCP reference, restoration/post-restoration review, and reconciliation expectations. |
| Operational Runbooks | Reference required runbook categories and operational implications only; do not draft actual runbook procedures in the SDD unless the System Design Lead is explicitly tasked to do so. |
| Appendix | Carry open observability/reporting/operations questions and source traceability as needed. |

## 13. Summary for System Design Lead

ExitPass v1.3 observability must be authority-aware. The System Design should let operations see connector health, projection freshness, Vendor PMS/HCP availability, Site/Site Group operational status, POS Server fiscal health, Payment Orchestrator/provider outcome uncertainty, gate/exit health, continuity state, Continuity Terminal activation, fiscal exceptions, manual release, and reconciliation backlog without allowing those views to become payment, fiscal, tariff, discount, or exit authority.

The most important v1.3 reporting rule is source labeling. Operational dashboards may use projection and health data, but financial and revenue reports must use canonical payment, provider, POS fiscal, and reconciliation records. Fiscal dashboards must reconcile to POS Server fiscal documents and Central PMS fiscal issuance references. Management summaries may combine operational and financial facts only when each metric is labeled by source category and freshness.

The v1.3 System Design should carry forward the v1.2 structure for Observability, Business Continuity, and Operational Runbooks, but update the content for v1.3 domains: Site/Site Group scope, connector projection, POS Server fiscal issuance, Assisted Payment Terminal, Continuity Terminal, Operator Console governance, Management Dashboard/Reporting, export/report access audit, and post-restoration reconciliation.

Open decisions remain around thresholds, refresh intervals, activation authority, manual release policy, fiscal exception release policy, export controls, reconciliation SLA/status labels, and exact implementation technologies. These should be captured as open design items and not silently resolved in the SDD.
