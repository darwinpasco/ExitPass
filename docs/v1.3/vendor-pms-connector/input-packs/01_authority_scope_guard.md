# Vendor PMS Connector Authority and Scope Guard Input Pack

Version: v1.0  
Status: Specialist input pack for Lead synthesis  
Date: 2026-07-01  
Assigned focus: Authority boundaries, approved baseline references, source contradictions, non-authority guardrails

## 1. Purpose

This input pack provides the authority and scope guard for the later:

- Vendor PMS Connector System Design.
- HikCentral Connector Profile.

It exists to prevent authority drift, terminology drift, and premature API, database, or engineering detail during Lead drafting. It does not draft either final document and does not decide endpoint paths, DTOs, database tables, event payloads, retry algorithms, queue names, implementation classes, connector deployment scripts, credential storage implementation, service identity implementation, or exact HCP connector topology.

## 2. Source Documents Reviewed

| Source document | Relevant authority or scope input |
| --- | --- |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md` | Direct task scope, input-pack ownership, connector design boundaries, HikCentral profile boundaries, and authority guardrails. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Core v1.3 authority model, Site/Site Group semantics, VendorSystem and AdapterMapping business concepts, projection rules, degraded resolve boundaries, fiscal-before-exit sequence, and open questions. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System-level component responsibilities, trust boundaries, connector posture, projection/polling boundaries, adapter containment, failure behavior, observability posture, and deferred companion-design scope. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approval posture and confirmation that BRDs are business baseline inputs only, with downstream API/database/engineering and BIR/accounting questions still open. |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved decisions for authority, Site/Site Group, VendorSystem, AdapterMapping, HCP ParkingLotIndexCode, runtime vendor object identity, one-minute polling baseline, and projection limits. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Preserved unresolved questions for connector topology, vendor acknowledgment behavior, HCP health/freshness modeling, degraded freshness threshold, and physical lot modeling. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Source-to-target impact map for connector design, HikCentral mapping, projection, degraded mode, API/database deferrals, and test/UAT/runbook downstream impacts. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Controlled degraded operation, explicit activation, fail-closed posture, projection freshness, manual release, vendor acknowledgment failure, and post-restoration reconciliation guardrails. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Connector health and projection visibility boundaries; Operator Console remains non-payment, non-fiscal, non-exit, and non-gate-control. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Reporting distinction between operational projection visibility and financial truth, connector health visibility, freshness labels, and dashboard non-authority scope. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal issuance authority, Sales Invoice terminology, fiscal issuance before ExitAuthorization, and channel/terminal non-fiscal-authority rules. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Terminal/channel authority boundaries, Central PMS payment finality, resolved Site POS Server fiscal routing, continuity-mode restrictions, and terminal non-authority rules. |

No source contradiction was found that shifts payment finality, fiscal issuance, tariff authority, degraded resolve authority, or exit authorization away from the approved v1.3 model.

## 3. Approved Terminology

| Approved term | Required meaning |
| --- | --- |
| Vendor PMS / HCP | Vendor-side parking system authority for raw parking session lifecycle and normal tariff computation. |
| Central PMS | ExitPass platform control authority for payment-linked control state, TariffSnapshot recording, payment finality, fiscal issuance reference recording, degraded resolve decisions under approved policy, and ExitAuthorization. |
| VendorSystem | Configured Vendor PMS/HCP instance. It is not an ExitPass Site, adapter codebase, AdapterMapping, or connector process by itself. |
| AdapterMapping | Mapping between an ExitPass Site and a vendor-side parking object. It is the approved bridge between platform Site and vendor object identity. |
| Adapter codebase | Reusable vendor integration implementation, such as a HikCentral adapter. It is not the deployed connector instance. |
| Connector instance | Deployed and configured runtime connector for a specific VendorSystem, unless a later approved design explicitly assigns another topology. |
| Runtime vendor object key | Conceptual runtime identity of `vendorSystemId + vendorObjectType + vendorObjectRef`. This is not a final database key, API field set, or DTO definition. |
| HCP ParkingLotIndexCode | Vendor-side HCP parking object identity. It must not be treated as ExitPass `site_id`. |
| Site Group | Customer lookup/payment scope. |
| Site | Reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary. |
| Physical parking lot | Physical or vendor-side lot concept. It is not automatically an ExitPass Site. |
| Parking Session Projection / projection | Operational visibility and controlled degraded support data. It is not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority. |
| TariffSnapshot | Central PMS record of payable basis from live vendor calculation or approved degraded computation. |
| PaymentConfirmation / platform payment finality | Central PMS-owned platform payment finality concept. |
| Sales Invoice | Primary parking fiscal output term under POS/Invoicing v1.3. |
| Site POS Server | Resolved Site fiscal issuance authority for Sales Invoice and fiscal records. |
| ExitAuthorization | Central PMS-issued authorization consumed by gate/exit execution. |
| Gate/exit execution | Physical or site infrastructure that consumes Central PMS authorization and must not bypass it. |

## 4. Authority Matrix

| Function or decision | Approved authority | Connector/HCP design implication |
| --- | --- | --- |
| Raw parking session lifecycle in normal mode | Vendor PMS / HCP | Connector may read/report vendor facts and normalized outcomes; it does not become canonical lifecycle authority inside ExitPass. |
| Normal tariff computation | Vendor PMS / HCP | Normal resolve must use live vendor fee calculation where available. Projection and passageway records must not invent or replace normal tariff computation. |
| ExitPass Site resolution and platform scope | Central PMS using approved Site/Site Group and AdapterMapping model | HCP ParkingLotIndexCode maps through AdapterMapping and must not be used directly as `site_id`. |
| TariffSnapshot recording | Central PMS | Connector reports resolved vendor outcome; Central PMS records the immutable TariffSnapshot. |
| PaymentAttempt and platform payment state | Central PMS | Connector cannot mark payment attempts final or paid. |
| Verified provider outcome reporting | Payment Orchestrator or approved payment workflow | Provider success is evidence; Central PMS determines platform finality. |
| Payment finality | Central PMS | Vendor paid state, vendor acknowledgment, connector response, and projection cannot create ExitPass payment finality. |
| Fiscal treatment and Sales Invoice issuance | Resolved Site POS Server | Connector does not issue fiscal documents and must not imply Sales Invoice issuance. |
| Fiscal issuance reference recording | Central PMS | Connector does not own fiscal reference recording. |
| Degraded resolve decision | Central PMS under approved continuity policy | Connector projection can support controlled degraded evaluation, but cannot silently activate degraded mode or approve degraded basis. |
| Projection freshness/health facts | Connector reports; Central PMS/integration health workflow classifies and exposes | Freshness thresholds and alert rules remain open; do not finalize exact values. |
| Vendor payment acknowledgment | Later connector design, under Central PMS payment/fiscal authority | Acknowledgment is downstream of Central PMS finality and fiscal handling; exact sync/queue/retry/exit-block policy remains open. |
| ExitAuthorization | Central PMS | Connector and HCP profile must not issue, simulate, or replace ExitAuthorization. |
| Gate/exit execution | Gate/exit system consuming Central PMS authorization | Connector must not open gates directly unless a later approved gate profile assigns a controlled integration boundary. |
| Manual release | Approved operations/continuity governance policy | Manual release is not normal ExitAuthorization and must remain incident/audit/reconciliation tagged where allowed. |
| Reporting and dashboard visibility | Management Dashboard and Reporting using labeled sources | Projection and connector health can appear as operational visibility only. |

## 5. Non-Authority Matrix

| Actor, component, or data source | Must not be treated as authority for |
| --- | --- |
| Vendor connector | Platform payment finality, fiscal issuance, fiscal reference recording, ExitAuthorization, gate opening, discount approval, or normal ExitPass Site identity. |
| HikCentral Connector Profile | Generic connector design override, Central PMS authority override, POS Server fiscal override, or replacement for AdapterMapping. |
| HCP ParkingLotIndexCode | ExitPass `site_id`, Site Group, fiscal Site, POS Server routing authority, or canonical platform object identity. |
| Passageway records | Payable session truth, normal tariff truth, payment finality, fiscal truth, discount approval, or exit authority. |
| Projection/polling feed | Financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority. |
| Vendor paid state or vendor acknowledgment | ExitPass payment finality or proof that a Sales Invoice was issued. |
| Payment provider success | Platform payment finality until Central PMS records it. |
| Payment Orchestrator | Platform payment finality, fiscal issuance, or ExitAuthorization. |
| WebPay, APM, Assisted Payment Terminal, Cashier-Assisted Terminal, Continuity Terminal | Platform payment finality, independent fiscal issuance, Sales Invoice authority, or ExitAuthorization. |
| Operator Console | Payment collection, payment finality, fiscal issuance, Sales Invoice mutation, ExitAuthorization, or direct gate opening. |
| Management Dashboard and Reporting | Payment, fiscal, tariff, discount, continuity activation, reconciliation closure, ExitAuthorization, or gate-control authority. |
| POS Server | Payment finality, normal tariff authority, degraded resolve decision, ExitAuthorization, or gate opening. |
| Gate/exit system | Payment, fiscal, tariff, or platform authorization decisioning. |

## 6. Vendor PMS Connector Scope

The generic Vendor PMS Connector System Design may cover, at companion technical-design level:

- VendorSystem as a configured Vendor PMS/HCP instance.
- AdapterMapping between ExitPass Site and vendor-side parking object.
- Adapter codebase versus deployed connector instance separation.
- Connector instance per Vendor PMS/HCP instance, unless a later approved design explicitly changes this topology.
- Runtime vendor object key as the conceptual `vendorSystemId + vendorObjectType + vendorObjectRef`.
- Normal live vendor session and fee resolve behavior.
- Vendor payment acknowledgment as a design question requiring explicit later decision on synchronous, queued, retry, reconciliation, and exit-block policy.
- Projection and polling ingestion boundaries.
- Connector health, availability, freshness, stale/unavailable/ambiguous states, and degraded-state signaling.
- Security, credentials, network trust, and deployment topology options at design level only.
- Observability and operational controls for Operator Console, Management Dashboard, Continuity workflows, and support.
- Failure handling and reconciliation tagging where connector state affects controlled degraded operation.

The generic design must not be HikCentral-specific except where using HCP as an example. HikCentral-specific identity, polling, passageway, and API capability details belong in the HikCentral Connector Profile.

## 7. HikCentral Connector Profile Scope

The HikCentral Connector Profile may cover, at vendor-profile level:

- HCP-specific API source references and source availability.
- HCP ParkingLotIndexCode handling as vendor-side identity only.
- Mapping from HCP parking object identity through AdapterMapping to ExitPass Site.
- One-minute HCP passageway polling as the approved planning baseline.
- HCP passageway records as operational projection input, not financial truth or payable-session truth.
- HCP live fee calculation / parking fee resolve behavior only where confirmed by source capability.
- HCP vendor payment acknowledgment behavior only where confirmed by source capability.
- HCP connector health, availability, and projection freshness signals.
- HCP deployment topology options and source constraints.
- Known gaps in local vendor documentation or API collection availability.

The HikCentral Connector Profile must not override the generic Vendor PMS Connector System Design. If an HCP-specific behavior appears to require an exception to the generic connector model, the profile should record it as an unresolved design issue for Lead review, not silently redefine the generic design.

## 8. Scope Boundaries and Deferrals

The final connector documents must preserve these boundaries:

- Do not finalize API endpoint paths.
- Do not finalize DTOs or request/response schemas.
- Do not finalize database tables, columns, constraints, indexes, or migrations.
- Do not finalize event payloads.
- Do not finalize retry algorithms.
- Do not finalize queue names.
- Do not finalize implementation classes, SDK call sequences, or adapter internals.
- Do not finalize connector deployment scripts.
- Do not finalize exact service identity implementation.
- Do not finalize exact credential storage implementation.
- Do not finalize exact HCP connector topology.
- Do not finalize exact projection freshness thresholds.
- Do not finalize exact vendor acknowledgment retry, reconciliation, or exit-block policy.
- Do not create diagrams, DOCX files, Test/UAT Pack, Runbook Pack, Database/API/Engineering Pack, or source code.

Allowed language should remain conceptual, authority-preserving, and explicitly deferred where the approved sources left matters open.

## 9. Risky Terminology and Misuse Cases

| Risky wording or misuse case | Required guardrail |
| --- | --- |
| HCP site | Prefer HCP parking object, HCP parking lot object, or HCP vendor-side object unless the source explicitly means ExitPass Site. |
| ParkingLotIndexCode as `site_id` | Prohibited. HCP ParkingLotIndexCode is vendor-side identity and maps through AdapterMapping. |
| Projection source of truth | Prohibited. Use operational visibility or controlled degraded support. |
| Connector payment confirmation | Avoid unless it clearly means vendor acknowledgment status. PaymentConfirmation/platform finality is Central PMS-owned. |
| Connector finality | Prohibited for payment/fiscal/exit. Connector reports facts, health, availability, and normalized vendor outcomes. |
| Connector exit authorization | Prohibited. ExitAuthorization is Central PMS-issued. |
| Connector gate open | Prohibited unless a later approved gate profile assigns a controlled integration boundary. |
| Automatic fallback | Prohibited. Use controlled degraded mode under approved policy, activation, freshness, audit, and reconciliation controls. |
| Silent fallback | Prohibited by approved continuity posture. |
| Vendor payment means ExitPass payment finality | Prohibited. Central PMS creates platform payment finality. |
| Vendor paid state means Sales Invoice issued | Prohibited. Sales Invoice issuance is by resolved Site POS Server and fiscal reference is recorded by Central PMS. |
| Passageway record means payable session truth | Prohibited. Passageway records are projection input and must not invent tariff or payable basis. |
| Parking lot when ExitPass Site is intended | Must be corrected. Physical/vendor lots are not automatically ExitPass Sites. |

Source review found risky concepts addressed consistently as warnings or prohibitions, not as approved authority transfers. Two wording areas need Lead caution:

- The Management Dashboard BRD uses "parking lot mapping health" as an operational visibility example. The final connector documents should clarify whether this means vendor-side parking object mapping health, not ExitPass Site identity.
- The Assisted Payment Terminal BRD mentions "fallback payment operations during degradation" in business context. Final connector documents should use controlled degraded operation language and avoid implying automatic fallback.

## 10. Required Statements for Final Documents

The Lead should carry these statements, in substance, into the final Vendor PMS Connector System Design and HikCentral Connector Profile:

- Vendor PMS / HCP remains authority for raw parking session lifecycle and normal tariff computation.
- Central PMS remains authority for payment-linked platform state, TariffSnapshot recording, payment finality, fiscal issuance reference recording, degraded resolve decision under approved policy, and ExitAuthorization.
- The vendor connector reports vendor facts, health, availability, and normalized outcomes, but does not create platform payment finality.
- The vendor connector does not issue fiscal documents.
- The vendor connector does not issue ExitAuthorization.
- The vendor connector does not open gates directly unless a later approved gate profile explicitly assigns a controlled integration boundary.
- Projection/polling data is operational visibility and controlled degraded support only.
- Projection is not financial truth, fiscal truth, tariff truth in normal mode, payment finality, discount approval, or exit authority.
- HCP ParkingLotIndexCode is vendor-side identity and must not be treated as ExitPass `site_id`.
- VendorSystem, AdapterMapping, adapter codebase, and connector instance must remain distinct.
- HikCentral Connector Profile must not override the generic Vendor PMS Connector System Design.
- Fiscal issuance remains under resolved Site POS Server authority.
- Gate/exit execution consumes Central PMS authorization and must not bypass Central PMS authorization.
- Fiscal issuance must succeed before normal ExitAuthorization unless a separately approved exception/manual-release policy applies.
- Vendor payment acknowledgment failure must remain auditable and reconciliation-tagged, with exact sync/queue/retry/exit-block policy deferred.
- Controlled degraded operation must be explicit, fail-closed by default, audit-tagged, reconciliation-tagged, and never silent fallback.

## 11. Open Questions to Preserve

The Lead must preserve, not silently decide, these connector-relevant open questions:

| Source ID | Open question to preserve |
| --- | --- |
| V13-Q003 | Do physical parking lots or clusters need first-class tables in v1.3, or do they remain operational metadata? |
| V13-Q004 | What is the exact degraded tariff freshness threshold? |
| V13-Q009 | In each deployment topology, does the HCP connector push to Central PMS or does Central PMS pull from a connector endpoint? |
| V13-Q010 | Is vendor payment acknowledgment synchronous or queued/retried per Site? |
| V13-Q011 | How should HCP connector health and projection freshness be modeled? |
| CON-OQ-003 | What is the exact projection freshness threshold? |
| CON-OQ-004 | Who owns exact degraded tariff configuration? |
| CON-OQ-011 | What is the exact vendor acknowledgment retry policy? |
| OC-OQ-006 | What are exact connector health/projection freshness thresholds and alerting rules? |
| MDR-OQ-005 | What is the exact projection freshness threshold and stale warning rule set? |
| MDR-OQ-006 | What are the exact connector health alert thresholds? |
| POS-OQ-015 | What are the final endpoint names? Deferred to API Contract. |
| POS-OQ-016 | What are the final DTO boundaries? Deferred to API Contract. |
| POS-OQ-017 | What are the final database tables/columns? Deferred to Database Design / Database Delta. |
| APT-OQ-008 | What is the exact degraded payable-basis freshness threshold? |
| Orchestration source issue | If specialists require vendor API collections beyond the local HikCentral OpenAPI Developer Guide, missing collection availability must be reported rather than invented. |
| Orchestration deferral | Exact HCP connector topology, service identity implementation, credential storage implementation, projection thresholds, and vendor acknowledgment retry/exit-block policy remain deferred. |

## 12. Summary for Lead

The final Vendor PMS Connector System Design should define a reusable connector boundary that preserves the approved ExitPass v1.3 authority model: Vendor PMS/HCP owns normal raw session lifecycle and tariff computation; Central PMS owns payment-linked control state, TariffSnapshot recording, platform payment finality, degraded resolve decision under policy, fiscal reference recording, and ExitAuthorization; the resolved Site POS Server owns Sales Invoice issuance.

The final HikCentral Connector Profile should stay subordinate to the generic connector design and cover only HCP-specific identity, source constraints, confirmed capabilities, ParkingLotIndexCode handling, passageway polling, projection freshness, live resolve, acknowledgment, and deployment considerations. It must not convert HCP ParkingLotIndexCode into ExitPass `site_id`, must not make projection financial or tariff truth, and must not bypass Central PMS or Site POS Server authority.

The Lead should avoid final API/database/engineering detail, carry all open questions forward, and treat risky terms as explicit misuse cases. No approved source reviewed supports connector payment finality, connector fiscal issuance, connector ExitAuthorization, connector direct gate opening, automatic fallback, or projection as source of truth.
