# Vendor PMS Connector Workflow and State Input Pack

Version: v1.0
Status: Specialist input pack for Lead integration
Date: 2026-07-01
Owner: Connector Workflow and State specialist

## 1. Purpose

This input pack describes Vendor PMS connector workflows and state ownership at companion technical-design level for the later Vendor PMS Connector System Design and HikCentral Connector Profile.

The pack preserves the v1.3 authority model:

- Vendor PMS / HCP remains the normal source for raw parking session lifecycle and tariff computation.
- Central PMS owns payment-linked platform control decisions, payment finality, TariffSnapshot recording, fiscal issuance reference recording, degraded resolve decision under approved policy, and ExitAuthorization.
- Site POS Server owns fiscal issuance for the resolved Site.
- The connector reports normalized vendor facts, health, freshness, and acknowledgment outcomes. It does not decide financial truth, declare payment finality, issue fiscal documents, approve degraded resolve, issue ExitAuthorization, or open gates.
- Projection is operational visibility and controlled degraded support only. It is not financial truth, fiscal truth, payment finality, or exit authority.

This pack intentionally does not define endpoint paths, DTOs, table definitions, event payload schemas, queue names, retry counts, polling implementation, SQL routines, implementation classes, or final retry algorithms.

## 2. Source Documents Reviewed

Primary sources reviewed:

| Source | Workflow relevance |
| --- | --- |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md` | Specialist scope, file ownership, connector guardrails, target companion document boundaries. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core authority model, VendorSystem and AdapterMapping concepts, normal/degraded resolve boundaries, fiscal-before-exit rule. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Architecture-level component responsibilities, connector posture, projection/freshness posture, failure behavior, continuity states. |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved decisions for VendorSystem, AdapterMapping, runtime vendor object key, connector instance, one-minute HCP polling baseline, normal/degraded mode. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Open questions for connector topology, vendor acknowledgment mechanics, HCP health/freshness modeling, degraded freshness thresholds. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Source-to-target impact for connector design, HikCentral mapping, projection, degraded mode, database/API/engineering deferrals. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Degraded resolve, continuity activation, stale/ambiguous projection behavior, manual release, reconciliation and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator visibility for connector health, projection freshness, stale warnings, fiscal exceptions, and non-payment governance boundaries. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Operational versus financial source labels, connector health dashboard inputs, projection freshness reporting, vendor acknowledgment backlog visibility. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal authority, fiscal issuance before ExitAuthorization, fiscal exception behavior, POS non-authority constraints. |

The optional HikCentral API discovery input pack was checked at `docs/v1.3/vendor-pms-connector/input-packs/02_hikcentral_api_discovery.md` and was not available at drafting time. HCP-specific workflow confirmation remains pending; this pack therefore stays generic except where approved v1.3 sources already establish HCP planning posture, such as one-minute passageway polling as the business baseline and HCP ParkingLotIndexCode as vendor-side identity only.

## 3. Connector Role in Normal Mode

In normal mode, the connector is the runtime integration boundary between Central PMS and a configured VendorSystem. It uses AdapterMapping to relate an ExitPass Site to a vendor-side parking object, and it keeps the adapter codebase separate from the deployed connector instance.

Normal-mode responsibilities:

- Resolve the correct VendorSystem and vendor-side object context supplied through approved Central PMS routing.
- Use the configured adapter codebase for that vendor family while preserving the connector instance boundary for the specific VendorSystem.
- Submit live session lookup or live fee calculation requests to the Vendor PMS / HCP where supported by confirmed vendor capability.
- Normalize vendor responses into vendor facts usable by Central PMS or other authorized consumers.
- Report whether the vendor response indicates a live session, missing vendor record, already exited state, already paid state, fee unavailable state, ambiguous mapping, timeout, unavailable dependency, or other normalized error category.
- Maintain health and freshness signals about the connector instance and vendor interaction boundary.
- Support projection ingestion through polling or other later-approved topology, while keeping projection operational only.
- Submit vendor payment acknowledgment only after Central PMS finality and fiscal prerequisites where applicable have been satisfied and Central PMS asks the connector to notify the vendor.

Normal-mode non-responsibilities:

- The connector does not record platform payment finality.
- The connector does not decide payable basis after payment or discount policy.
- The connector does not create or mutate TariffSnapshot authority records.
- The connector does not issue fiscal documents or fiscal references.
- The connector does not issue ExitAuthorization or tell gate infrastructure to release.
- The connector does not decide degraded resolve eligibility.

## 4. Connector Role in Degraded / Continuity Mode

In degraded or continuity mode, the connector's role is informational and evidentiary. It may continue to report last-known health, last successful projection freshness, vendor unavailability, timeout patterns, mapping ambiguity, and acknowledgment backlog. It does not approve degraded operation.

Degraded-mode responsibilities:

- Surface connector health, vendor availability, poll/freshness status, and stale or ambiguous projection indicators.
- Preserve distinction between live vendor facts and projection-derived facts.
- Report that normal live resolve or fee calculation is unavailable when the vendor or connector cannot provide it.
- Provide last successful projection context only as operational visibility or controlled degraded support, subject to Central PMS and Continuity policy.
- Tag or expose acknowledgment failures and unknown acknowledgment outcomes for reconciliation and post-restoration review.

Degraded-mode non-responsibilities:

- The connector does not activate continuity.
- The connector does not approve degraded resolve.
- The connector does not decide degraded tariff basis.
- The connector does not decide whether stale projection can be used.
- The connector does not turn passageway records into tariff authority.
- The connector does not convert manual release into normal ExitAuthorization.

Degraded resolve handoff belongs to Central PMS under approved continuity policy. If normal live resolve fails and projection is stale, ambiguous, or insufficient, the connector should report that condition. Central PMS then fails closed or routes to approved supervisor/manual review according to Continuity requirements.

## 5. Workflow Summary Table

| Workflow | Primary trigger | Connector contribution | Central PMS / authorized owner decision | Key state output |
| --- | --- | --- | --- | --- |
| Normal live resolve | Customer, channel, terminal, or operator lookup requires current vendor session context | Calls or otherwise queries Vendor PMS through the configured connector instance and normalizes vendor session facts | Determines platform session resolution, Site context, and next payment flow step | Live vendor facts or normalized vendor exception |
| Live fee calculation | Central PMS requires normal tariff computation | Requests vendor fee calculation where confirmed by vendor capability and normalizes response | Records payable basis / TariffSnapshot and continues payment workflow | Vendor fee fact or fee unavailable exception |
| Projection polling | Scheduled projection baseline or later-approved topology action | Polls vendor passageway/projection source where supported and reports projection/freshness | Stores/uses projection as operational visibility and possible degraded support only | Projection facts, last successful poll, stale/health indicators |
| Projection freshness classification | Poll success, poll failure, time since projection, mapping result, or dashboard request | Reports freshness inputs and ambiguity/error indicators | Applies final freshness thresholds and degraded use policy | Fresh, aging, stale, unavailable, ambiguous, or insufficient classification concepts |
| Vendor payment acknowledgment | Central PMS has payment finality and fiscal prerequisites are satisfied where required | Sends or attempts vendor-side payment notification where supported and reports outcome | Decides exit eligibility, reconciliation handling, and escalation posture | Acknowledged, already paid, failed, timeout, unavailable, unknown, duplicate response |
| Vendor unavailable / timeout | Live resolve, fee calculation, projection, or acknowledgment cannot complete | Normalizes failure without inventing business result | Keeps normal workflow pending/fail-closed or routes to degraded policy | Unavailable or timeout state with affected VendorSystem/object context |
| Mapping ambiguity | Multiple or missing AdapterMapping/vendor object matches | Reports ambiguity and refuses to choose business authority | Blocks normal resolve or routes to approved review | Ambiguous mapping or missing mapping state |
| Duplicate vendor response | Replayed, repeated, or conflicting vendor response arrives | Detects and reports duplicate/replay posture at integration boundary | Applies canonical idempotency/finality rules | Duplicate, replayed, or conflicting vendor fact |

## 6. Normal Live Resolve Workflow

Normal live resolve is the preferred session-resolution path when Vendor PMS / HCP and connector dependencies are available.

Conceptual flow:

1. A channel, terminal, or operator-initiated backend workflow requests session context through Central PMS-approved routing.
2. Central PMS resolves Site Group and candidate Site context according to approved business rules.
3. Central PMS identifies the configured VendorSystem and AdapterMapping for the resolved Site or candidate Site context.
4. The connector instance for that VendorSystem performs live vendor session lookup using the adapter codebase appropriate to the vendor family.
5. The connector normalizes the vendor response without deciding platform authority.
6. Central PMS evaluates the normalized facts, resolves platform session context, and controls the next payment workflow step.

Important workflow notes:

- AdapterMapping must be used to translate an ExitPass Site to the vendor-side parking object. A vendor object reference, such as an HCP ParkingLotIndexCode, must not become ExitPass `site_id`.
- The runtime vendor object reference should preserve the approved conceptual key posture: `vendorSystemId + vendorObjectType + vendorObjectRef`.
- A missing vendor record is not a successful resolve. It should be normalized as missing/not found and returned to Central PMS for customer/operator messaging or review.
- An already exited vendor state is not an ExitPass ExitAuthorization. It is a vendor lifecycle fact for Central PMS review and reconciliation.
- An already paid vendor state is not Central PMS payment finality. It is a vendor-side fact that may indicate prior vendor-side payment, duplicate notification, reconciliation issue, or policy-specific outcome. Central PMS decides platform treatment.
- If the vendor returns multiple candidate sessions or a response cannot be mapped unambiguously to a Site/vendor object context, the connector should report ambiguity rather than choose.

## 7. Fee Calculation Workflow

Live fee calculation belongs to Vendor PMS / HCP in normal mode where supported by confirmed vendor capability. The connector is a transport and normalization boundary, not tariff authority.

Conceptual flow:

1. Central PMS has a resolved session context and needs the normal payable basis.
2. Central PMS routes the request to the connector instance associated with the VendorSystem and AdapterMapping.
3. The connector requests live fee calculation or current payable amount from the vendor system where supported.
4. The connector normalizes the vendor fee response, including vendor-side amount, timestamp/validity context where available, and vendor-side exceptions.
5. Central PMS records the authoritative platform TariffSnapshot or payable-basis record according to approved Central PMS workflow.
6. Payment proceeds through Payment Orchestrator or approved channel workflow. The connector is not involved in declaring payment finality.

Fee exception concepts:

- Vendor fee calculation unavailable: report a normalized fee-unavailable result. Central PMS decides whether to retry, fail closed, or route to degraded policy.
- Vendor timeout during fee calculation: report timeout and preserve enough context for audit/observability without treating the amount as zero or final.
- Vendor says session already paid: report the vendor-side state; Central PMS decides whether payment can continue, whether reconciliation is needed, or whether the user should be routed to support.
- Vendor says session exited: report the vendor-side lifecycle state; Central PMS decides platform messaging, reconciliation posture, or exception handling.
- Duplicate fee response: report duplicate/replay posture and allow Central PMS to apply canonical idempotency and TariffSnapshot rules.

The connector must not invent tariff amounts from projection or passageway records. In degraded mode, tariff basis belongs to Central PMS under approved continuity policy using approved tariff configuration or approved continuity basis.

## 8. Projection Polling Workflow

Projection polling is an operational visibility workflow. For HCP, approved planning sources establish one-minute passageway polling as the business baseline. This pack does not define the polling implementation, scheduler, endpoint, event schema, persistence model, or retry algorithm.

Conceptual flow:

1. A connector instance associated with a VendorSystem collects vendor-side passageway/projection facts according to later-approved topology.
2. The connector uses AdapterMapping to attach vendor-side parking object context to the appropriate ExitPass Site context.
3. The connector reports normalized projection facts, mapping status, and poll health/freshness inputs to Central PMS or an authorized consumer.
4. Central PMS treats projection as operational visibility and possible degraded support only.
5. Operator Console and Management Dashboard may show projection-based active sessions, active vehicles, occupancy approximation, last successful poll, stale warnings, vendor availability, and mapping health where authorized.

Projection boundaries:

- Projection does not replace live vendor fee calculation in normal mode.
- Projection does not become payment finality.
- Projection does not become fiscal truth.
- Projection does not authorize exit.
- Passageway records alone must not be used to invent tariffs.
- Projection may support degraded resolve only when Central PMS and Continuity policy explicitly allow it and freshness/ambiguity checks pass.

## 9. Projection Freshness Classification Workflow

Freshness classification should be treated as a conceptual workflow for the later connector design, Operator Console, Continuity, and Management Dashboard inputs. Exact thresholds, labels, alert rules, and persistence fields remain open.

Candidate conceptual classifications:

| Classification concept | Meaning | Connector role | Owner of policy consequence |
| --- | --- | --- | --- |
| Fresh | Last projection input is within approved freshness threshold and mapping is unambiguous | Report latest success/freshness inputs | Central PMS / Continuity policy decides allowed use |
| Aging | Projection is still visible but approaching policy limit | Report elapsed age and health signals | Central PMS / dashboard policy decides warning behavior |
| Stale | Projection is outside approved threshold or last successful poll is too old | Report stale inputs and avoid presenting as current live fact | Central PMS blocks degraded use or routes to review |
| Unavailable | Vendor, connector, network, or poll source is unavailable | Report unavailable dependency and last-known state | Central PMS / operations decides fail-closed, degraded-watch, or escalation |
| Ambiguous | Projection cannot be mapped safely to one Site/vendor object/session context | Report ambiguity and conflicting context | Central PMS fails closed or routes to approved review |
| Insufficient | Projection lacks required facts for safe operational use | Report insufficiency | Central PMS fails closed or routes to approved review |

The one-minute polling baseline is a business planning baseline for HCP passageway data, not an approved freshness threshold. Exact stale threshold and warning rules remain open.

## 10. Vendor Payment Acknowledgment Workflow

Vendor payment acknowledgment is the workflow where ExitPass notifies the Vendor PMS that the platform-side payment and required fiscal prerequisites have been satisfied. It is downstream of Central PMS finality, not a prerequisite for declaring platform payment finality.

Conceptual flow:

1. Payment Orchestrator or approved payment channel reports a verified payment outcome to Central PMS.
2. Central PMS declares and records platform payment finality when its rules are satisfied.
3. Central PMS routes fiscal issuance to the resolved Site POS Server where required.
4. Site POS Server issues the Sales Invoice and returns fiscal document identity/status.
5. Central PMS records the fiscal issuance reference.
6. Central PMS determines whether vendor payment acknowledgment should be sent now, retried, or delayed according to later design and Site/vendor policy.
7. The connector attempts vendor acknowledgment through the configured connector instance where vendor capability is confirmed.
8. The connector reports normalized acknowledgment outcome to Central PMS and operational/reconciliation consumers.

Acknowledgment outcome concepts:

- Acknowledged: vendor accepted the payment acknowledgment.
- Already paid: vendor indicates the session/payment is already marked paid. This should be normalized for Central PMS reconciliation; it does not itself create platform payment finality.
- Already exited: vendor indicates the session has already exited. This is a vendor lifecycle fact requiring Central PMS review or reconciliation.
- Failed: vendor rejected or could not process the acknowledgment.
- Timeout: outcome is unknown due to timeout.
- Vendor unavailable: dependency was unavailable before or during acknowledgment.
- Unknown: connector cannot determine whether acknowledgment succeeded.
- Duplicate/replayed response: repeated vendor response or repeated Central PMS request requires idempotency posture without duplicate side effects.

Vendor acknowledgment failure must be auditable and reconciliation-tagged. Whether exit is blocked by acknowledgment failure remains an open policy/design question; approved sources only establish that Central PMS owns ExitAuthorization and that vendor acknowledgment failure must be queued, retried, or escalated according to later design.

## 11. Vendor Unavailable / Timeout Workflow

Vendor unavailable and timeout states may occur during live resolve, fee calculation, projection polling, or vendor payment acknowledgment. The connector should normalize the technical failure into a state that Central PMS and operations can understand without converting it into a business decision.

Conceptual flow:

1. Connector attempts vendor interaction for the requested workflow.
2. Vendor dependency is unavailable, network access fails, authentication/permission prevents access, response exceeds allowed waiting posture, or the connector cannot safely classify the vendor outcome.
3. Connector reports a normalized unavailable, timeout, or unknown result with affected VendorSystem and vendor object context where known.
4. Central PMS determines workflow consequence:
   - Normal live resolve or fee calculation may remain pending, fail closed, or route to controlled degraded policy.
   - Projection may be marked stale or unavailable.
   - Vendor acknowledgment may enter retry/escalation/reconciliation posture.
5. Operator Console and Management Dashboard may show health, degraded-watch/degraded-active state, stale warnings, and incident/backlog visibility where authorized.

Unavailable and timeout rules:

- Timeout does not mean success.
- Timeout does not mean vendor rejected the request.
- Unknown acknowledgment status should not be retried blindly without idempotency posture in later design.
- Vendor unavailable does not automatically permit degraded payment or exit.
- Vendor unavailable does not automatically allow projection use; Central PMS must check continuity policy and freshness.

## 12. Mapping Ambiguity Workflow

Mapping ambiguity occurs when the connector cannot safely associate an ExitPass Site/session context with one and only one vendor-side object/session context.

Potential ambiguity sources:

- Missing AdapterMapping for a Site and VendorSystem.
- Multiple AdapterMappings that match the same context.
- Vendor-side object reference conflicts.
- HCP ParkingLotIndexCode or other vendor object identity being incorrectly treated as platform Site identity.
- Multiple vendor sessions matching a lookup key such as plate, ticket, card, or vendor-side reference.
- Projection facts that conflict with live resolve facts.

Conceptual flow:

1. Central PMS or the connector identifies that mapping context is missing, duplicated, or conflicting.
2. The connector reports missing or ambiguous mapping and avoids choosing a Site, vendor object, fee, or session by heuristic.
3. Central PMS fails closed for payment/exit purposes or routes to approved operator/supervisor review where policy allows.
4. Operational views may show mapping health issues for support resolution.

Mapping ambiguity must be treated as a control issue, not an implementation inconvenience. It can affect Site attribution, vendor routing, POS Server routing, fiscal attribution, reporting, and reconciliation.

## 13. State Ownership Notes

| State or fact | Owner / authority | Connector posture |
| --- | --- | --- |
| VendorSystem configuration concept | Central PMS / approved configuration governance | Connector instance binds to a configured VendorSystem but does not own platform configuration authority. |
| AdapterMapping concept | Central PMS / approved configuration governance | Connector uses mapping to interact with vendor-side objects and reports mapping ambiguity. |
| Adapter codebase | Engineering/runtime ownership, vendor-specific boundary | Reusable implementation separate from any deployed connector instance. |
| Connector instance health | Connector/runtime plus Central PMS health aggregation | Connector reports health and dependency status; Central PMS/ops consume and act. |
| Vendor object references | Vendor PMS / HCP source identity, mapped by AdapterMapping | Connector preserves vendor reference and must not turn it into ExitPass Site identity. |
| Raw session lifecycle in normal mode | Vendor PMS / HCP | Connector normalizes vendor facts only. |
| Live fee calculation in normal mode | Vendor PMS / HCP | Connector requests and normalizes vendor tariff response where supported. |
| TariffSnapshot / payable basis record | Central PMS | Connector does not own or persist platform payable authority. |
| Session projection and control state | Central PMS | Connector supplies projection inputs and freshness facts; projection remains operational. |
| Degraded resolve decision | Central PMS under approved Continuity policy | Connector reports unavailability, freshness, and ambiguity; it does not approve degraded use. |
| Payment finality | Central PMS | Connector does not declare finality even if vendor says paid. |
| Payment provider outcome evidence | Payment Orchestrator / approved payment channel | Connector does not verify payment provider truth. |
| Fiscal issuance | Resolved Site POS Server | Connector does not issue fiscal documents. |
| Fiscal issuance reference recording | Central PMS | Connector does not create fiscal reference authority. |
| Vendor payment acknowledgment outcome | Vendor PMS/HCP response normalized by connector, consumed by Central PMS/reconciliation | Connector reports acknowledgment facts and unknowns. |
| ExitAuthorization | Central PMS | Connector does not issue or consume ExitAuthorization. |
| Gate/exit execution | Gate/exit system consuming Central PMS authorization | Connector does not open gates. |
| Operator visibility | Operator Console | Connector provides health/freshness facts through approved backend flow. |
| Management reporting | Management Dashboard and Reporting | Connector facts may feed operational visibility, not financial truth. |
| Reconciliation/post-restoration review | Operations / reconciliation workflow | Connector outcomes and failures are inputs, not closure authority. |

## 14. Retry / Idempotency / Duplicate Handling Concepts

This section states design concepts only. It does not define retry counts, algorithms, queue names, storage fields, event schemas, or implementation classes.

Retry posture:

- Live resolve and fee calculation retries should avoid extending customer wait indefinitely. Central PMS should remain responsible for deciding pending, fail-closed, or degraded-policy handoff.
- Projection polling retry posture should preserve last-known success, failure reason, and freshness inputs without presenting stale data as current.
- Vendor acknowledgment retry posture should account for Central PMS finality and fiscal prerequisites, vendor idempotency behavior where known, and reconciliation visibility.
- Retry attempts should be observable and auditable enough to support support review and post-restoration reconciliation.

Idempotency posture:

- Repeated Central PMS requests should not produce duplicate vendor-side payment effects.
- Repeated vendor responses should not duplicate Central PMS payment finality, TariffSnapshot, fiscal issuance, or ExitAuthorization.
- Unknown acknowledgment outcomes require a later design for safe retry or status confirmation, especially if the vendor may have accepted the acknowledgment before timeout.
- Vendor "already paid" should be treated as a normalized vendor fact requiring Central PMS reconciliation, not as proof of platform finality.
- Vendor "already exited" should be treated as a normalized lifecycle fact requiring Central PMS review, not as retrospective ExitAuthorization.

Duplicate handling posture:

- Duplicate live session or fee responses should be correlated conceptually to the same session/request context where possible, but final correlation design is deferred.
- Conflicting duplicate responses should be escalated as uncertainty rather than silently choosing one.
- Duplicate projection facts should not inflate operational counts or occupancy estimates.
- Duplicate acknowledgment responses should be normalized into acknowledged/already-paid/unknown/conflict concepts without duplicate side effects.

## 15. Open Workflow Questions

These questions should be carried into Lead integration and later companion design; they do not reopen approved authority decisions.

| ID | Question | Source basis / reason |
| --- | --- | --- |
| WF-OQ-001 | Does each deployment topology use connector push to Central PMS, Central PMS pull from connector, or both? | v1.3 open question for HCP connector topology. |
| WF-OQ-002 | What exact connector health states, freshness labels, stale thresholds, and alert rules should be approved? | Open across core, Operator Console, Continuity, and Dashboard sources. |
| WF-OQ-003 | What exact degraded tariff freshness threshold applies before projection can support degraded resolve? | Continuity and core open question. |
| WF-OQ-004 | Who owns exact degraded tariff configuration, rounding, grace, and exception policy? | Continuity open questions; connector must not decide. |
| WF-OQ-005 | Is vendor payment acknowledgment synchronous, asynchronous, queued/retried, or policy-variable by Site/vendor? | Core open question; this pack does not choose mechanics. |
| WF-OQ-006 | Does vendor acknowledgment failure block ExitAuthorization, only create reconciliation backlog, or vary by Site/vendor policy? | Approved sources identify failure handling but leave exit-block policy open. |
| WF-OQ-007 | How should unknown vendor acknowledgment outcome be confirmed without duplicate vendor-side payment marking? | Needs vendor capability and idempotency design. |
| WF-OQ-008 | What exact normalized vendor error categories should be exposed to Central PMS, Operator Console, Dashboard, and reconciliation? | Companion design must classify without defining DTOs in this pack. |
| WF-OQ-009 | What HCP APIs confirm live session lookup, fee calculation, passageway polling, and payment acknowledgment capability? | `02_hikcentral_api_discovery.md` was not available; HCP-specific confirmation remains pending. |
| WF-OQ-010 | What should happen when vendor says already paid but Central PMS has no platform payment finality? | Requires reconciliation and policy decision. |
| WF-OQ-011 | What should happen when vendor says exited before Central PMS-issued ExitAuthorization or approved manual release is found? | Requires exception, audit, and reconciliation policy. |
| WF-OQ-012 | What exact mapping governance workflow resolves missing or ambiguous AdapterMapping issues? | Mapping affects Site, vendor routing, POS Server routing, reporting, and reconciliation. |
| WF-OQ-013 | What are the exact post-restoration reconciliation SLA and closure states for connector-origin failures? | Continuity and Dashboard sources leave reconciliation SLA open. |

## 16. Summary for Lead

The Vendor PMS connector workflow design should be written as a bounded integration design, not as a new authority layer.

Key integration conclusions:

- Keep VendorSystem, AdapterMapping, adapter codebase, and connector instance distinct.
- Treat vendor-side object references as vendor identity only. For HCP, ParkingLotIndexCode must not be used as ExitPass `site_id`.
- Use live Vendor PMS / HCP lookup and fee calculation in normal mode where vendor capability is confirmed.
- Keep projection and passageway polling as operational visibility and controlled degraded support only.
- Carry the one-minute HCP passageway polling business baseline into the HCP profile, while leaving exact implementation, freshness thresholds, and alert rules to later design.
- Make stale, ambiguous, unavailable, insufficient, missing, already-paid, already-exited, fee-unavailable, timeout, duplicate, and unknown outcomes explicit normalized workflow states.
- Route degraded resolve decisions to Central PMS under approved Continuity policy. The connector only reports facts and freshness.
- Send vendor payment acknowledgment only after Central PMS payment finality and fiscal prerequisites where applicable, and treat failed/unknown acknowledgment as audit and reconciliation input.
- Preserve fiscal sequencing: payment finality, Site POS Server fiscal issuance, Central PMS fiscal reference recording, then Central PMS ExitAuthorization if eligible.
- Do not let connector health, projection, dashboard visibility, operator review, vendor acknowledgment, or vendor-side status override Central PMS control decisions.

HCP-specific workflow confirmation remains pending because the HikCentral API discovery input pack was not available at drafting time.
