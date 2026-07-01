# Vendor PMS Connector Diagram Planning Input Pack

Version: v1.0
Status: Specialist input pack
Date: 2026-07-01
Owner: Diagram planning specialist

## 1. Purpose

This input pack recommends the diagram set for the later Vendor PMS Connector System Design and HikCentral Connector Profile.

This pack is planning input only. It does not create final diagrams, PlantUML files, JPG files, database diagrams, endpoint maps, implementation class diagrams, device SDK diagrams, or final design content.

The recommendations preserve the approved v1.3 authority model:

- Vendor PMS / HikCentral Professional owns raw session lifecycle and normal tariff computation.
- The connector is an integration boundary, not platform finality authority.
- Central PMS owns platform payment finality, degraded resolve decisions under approved policy, fiscal reference recording, and ExitAuthorization.
- POS Server owns fiscal issuance.
- Projection is operational visibility and controlled degraded support only.
- HCP ParkingLotIndexCode is vendor-side identity only.
- Gate opening must not be shown unless it consumes Central PMS authorization or a future approved gate profile exists.

## 2. Source Documents and Diagram Folders Reviewed

Primary sources reviewed:

| Source | Diagram-planning relevance |
| --- | --- |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md` | Defines target companion documents, specialist ownership, authority guardrails, and required diagram-planning scope. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | Provides v1.3 architecture baseline, component authority, trust boundaries, conceptual workflows, open connector questions, and existing system-design diagrams. |
| `docs/v1.3/diagrams/system-design/` | Existing v1.3 System Design PlantUML and JPG references D-01 through D-11. |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Defines Site Group/Site semantics, VendorSystem, AdapterMapping, runtime vendor object identity, projection constraints, degraded resolve controls, and open connector questions. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Defines degraded operation, projection freshness, continuity activation, fail-closed behavior, manual release controls, and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Defines Operator Console as non-payment governance with connector health/projection freshness visibility only. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Defines dashboard/reporting source labels, freshness labels, operational projection visibility, vendor acknowledgment backlog visibility, and financial-truth separation. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Defines Site POS Server fiscal authority and fiscal issuance before normal ExitAuthorization. |

Historical HikCentral references reviewed as historical planning input only:

| Source | Diagram-planning relevance |
| --- | --- |
| `docs/diagrams/hikcentral-normal-resolve-flow.puml` | Historical sequence for projection sync and live resolve; useful for conceptual flow shape, but contains endpoint/database details that should not be reused in final design diagrams. |
| `docs/diagrams/hikcentral-degraded-projection-fallback-flow.puml` | Historical sequence for projection fallback; useful for fail-closed and projection non-authority notes, but contains endpoint/database/status detail that should not be reused in final design diagrams. |
| `docs/hikcentral-operator-console-projection-health-smoke.md` | Confirms read-only projection health visibility and no payment/tariff/exit controls in Operator Console smoke evidence. |
| `docs/hikcentral-projection-live-uat.md` | Historical live projection UAT reference for passageway polling and projection target operations. |
| `docs/hikcentral-projection-production-controls.md` | Historical production-control reference for scheduler ownership, disabled-by-default degraded fallback, and safe projection operations. |
| `docs/hikcentral-projection-resolve-uat-results.md` | Historical resolve/projection validation reference. |
| `docs/hikcentral-real-sync-target-deployment-handoff.md` | Historical handoff reference for ParkingLotIndexCode confirmation, passageway fields, health, stale fallback, and no PaymentAttempt/ExitAuthorization from projection validation. |
| `docs/hikcentral-sandbox-validation-evidence-20260601.md` | Historical sandbox evidence reference; gate-related content should remain out of connector diagrams unless future gate profile approval exists. |
| `docs/hikcentral-sandbox-validation-harness.md` | Historical sandbox gate validation harness reference; useful only as a risk warning because gate/door control is outside this connector diagram pack. |
| `docs/hikcentral-sandbox-validation-runbook.md` | Historical sandbox validation process reference; do not reuse as connector authority design. |
| `docs/hikcentral-ticket-only-readonly-validation.md` | Historical ticket-only/read-only validation reference for HCP identity and projection behavior. |
| `docs/vendor/hikcentral/HikCentral Professional OpenAPI_Developer Guide_V3.1.0_20260130.pdf` | Local vendor API guide present. This pack does not extract final endpoint maps; HCP payment acknowledgment diagrams should remain conditional unless confirmed by API discovery. |

Optional input packs checked:

- `docs/v1.3/vendor-pms-connector/input-packs/02_hikcentral_api_discovery.md` was not available at review time.
- `docs/v1.3/vendor-pms-connector/input-packs/03_connector_workflow_and_state.md` was not available at review time.
- `docs/v1.3/vendor-pms-connector/input-packs/05_observability_projection_operations.md` was not available at review time.

## 3. Existing Relevant v1.3 Diagrams

Existing System Design diagrams should be reused as authority anchors, not redrawn inside the connector documents unless the Lead later chooses to reference or adapt them.

| Existing diagram | Relevance to connector diagram planning |
| --- | --- |
| D-01 `ExitPass_v1.3_Logical_Architecture` | Anchor for Central PMS, Vendor PMS/HCP, connector instance, POS Server, Payment Orchestrator, Operator Console, Management Dashboard, Gate/Exit, and Audit/Event boundaries. |
| D-02 `Authority_Boundary_Model` | Anchor for authority separation and non-authority surfaces. Connector diagrams should echo this language. |
| D-03 `Site_Group_Site_VendorSystem_Connector_POS_Topology` | Best existing anchor for VendorSystem, AdapterMapping, adapter codebase, connector instance, Site, Site Group, POS Server, and HCP ParkingLotIndexCode boundary. |
| D-04 `Normal_Payment_to_Exit_Sequence` | Anchor for normal payment-to-exit chain and placement of live vendor resolve before payment. |
| D-05 `Payment_Finality_Fiscal_Issuance_ExitAuthorization_Sequence` | Anchor for vendor acknowledgment after Central PMS payment finality and fiscal handling. |
| D-06 `Vendor_PMS_Connector_Projection_Freshness_Flow` | Anchor for projection polling, freshness, health, Operator Console, Management Dashboard, Continuity, and Audit/Event flow. |
| D-07 `Degraded_Resolve_and_Continuity_Sequence` | Anchor for degraded resolve handoff, projection use under policy, and fail-closed behavior. |
| D-08 `Assisted_Payment_Terminal_Context_and_Modes` | Useful where degraded/continuity terminal context appears; avoid making terminal diagrams part of connector design unless referenced. |
| D-09 `Operator_Console_Governance_Boundary` | Anchor for read-only connector health/projection visibility and non-payment governance. |
| D-10 `Management_Dashboard_Source_of_Truth_Boundary` | Anchor for source/freshness labels and projection-versus-financial-truth separation. |
| D-11 `Audit_Event_Outbox_Conceptual_Flow` | Anchor for health, projection, acknowledgment, governance, reporting, and reconciliation facts without defining queues or payloads. |

## 4. Recommended Vendor PMS Connector Diagram Set

Use short, conceptual diagram IDs in the final design, for example `VPC-D01` through `VPC-D08`. These are recommendations only; do not create final diagram files from this pack.

| Recommended diagram | Diagram type | Planning intent |
| --- | --- | --- |
| VPC-D01 Generic connector context | Component/context | Show Vendor PMS/HCP, connector instance, Central PMS, POS Server, Operator Console, Management Dashboard, Continuity, Audit/Event, and optional payment acknowledgment as bounded relationships. |
| VPC-D02 VendorSystem / AdapterMapping / connector instance model | Component/concept model | Show Site Group, Site, VendorSystem, AdapterMapping, adapter codebase, connector instance, vendor object identity, and POS routing without database schema detail. |
| VPC-D03 Normal live resolve sequence | Sequence | Show channel request to Central PMS, Central PMS live resolve through connector, Vendor PMS/HCP session/tariff result, Central PMS TariffSnapshot recording, and no payment/finality/exit action at resolve time. |
| VPC-D04 Fee calculation sequence | Sequence | Show normal tariff computation owned by Vendor PMS/HCP, connector normalization, Central PMS payable-basis/TariffSnapshot capture, and downstream discount/payable-basis refresh boundary where applicable. |
| VPC-D05 Projection polling and freshness flow | Activity/component flow | Show polling/projection ingestion, freshness/health classification, Operator Console and Dashboard visibility, Continuity eligibility evidence, and Audit/Event facts. |
| VPC-D06 Vendor payment acknowledgment flow | Sequence | Show acknowledgment only after Central PMS payment finality and fiscal handling; include queued/retry/escalation/reconciliation alternatives as open design posture, not final implementation. |
| VPC-D07 Degraded resolve handoff to Central PMS / Continuity | Sequence | Show failed/unsafe live resolve, projection/freshness evaluation, Central PMS degraded decision under approved policy, Continuity/Operator Console governance, and fail-closed path. |
| VPC-D08 Connector error normalization and health reporting flow | Activity/component flow | Show vendor errors normalized by connector into unavailable/stale/ambiguous/terminal/retryable/health categories for Central PMS, Operator Console, Dashboard, Audit/Event, and reconciliation visibility. |

## 5. Recommended HikCentral Connector Profile Diagram Set

Use short, conceptual diagram IDs in the final profile, for example `HCP-D01` through `HCP-D07`. Keep HikCentral diagrams profile-specific and avoid leaking HCP object semantics into generic connector diagrams.

| Recommended diagram | Diagram type | Planning intent |
| --- | --- | --- |
| HCP-D01 HikCentral object identity mapping | Component/concept model | Show HCP parking object identity, including ParkingLotIndexCode as vendor-side identity, mapped through AdapterMapping to ExitPass Site. |
| HCP-D02 HikCentral authentication boundary | Boundary/context | Show HCP credentials, signed/OpenAPI boundary, connector trust boundary, secret handling boundary, and Central PMS not directly owning HCP credentials where topology assigns them to connector. |
| HCP-D03 Parking lot / passageway / fee API use map | Capability map | Show conceptual HCP API capability areas only: parking lot identity, passageway projection records, live session/fee resolve, and conditional payment acknowledgment. Do not show endpoint paths or full endpoint maps. |
| HCP-D04 Passageway projection flow | Sequence/activity | Show one-minute business baseline polling where approved, passageway records normalized into operational projection, freshness classification, and read-only visibility. |
| HCP-D05 Ticket-only fee calculation flow | Sequence | Show ticket/card reference supplied through Central PMS, HCP live fee calculation through connector, Central PMS TariffSnapshot capture, and no projection-derived tariff. |
| HCP-D06 Vendor payment acknowledgment flow, if supported | Conditional sequence | Include only if confirmed HCP API discovery supports a payment acknowledgment action. Show Central PMS-initiated acknowledgment after payment finality/fiscal handling, with retry/escalation/reconciliation status. |
| HCP-D07 HCP connector health and stale projection flow | Activity/component flow | Show HCP availability, authentication/permission failures, poll latency, last successful poll, stale projection classification, Operator Console/Dashboard visibility, and fail-closed degraded controls. |

## 6. Diagram Purpose and Intended Section

| Diagram | Intended document | Intended section | Purpose |
| --- | --- | --- | --- |
| VPC-D01 | Vendor PMS Connector System Design | Architecture / Context | Establish reusable connector boundary and authority posture. |
| VPC-D02 | Vendor PMS Connector System Design | Configuration and Identity Model | Explain VendorSystem, AdapterMapping, adapter codebase, connector instance, Site, Site Group, vendor object reference, and POS routing relationships. |
| VPC-D03 | Vendor PMS Connector System Design | Normal Resolve Workflow | Explain live resolve sequence without payment/fiscal/exit side effects. |
| VPC-D04 | Vendor PMS Connector System Design | Fee and Tariff Handling | Explain vendor-owned normal tariff computation and Central PMS TariffSnapshot capture. |
| VPC-D05 | Vendor PMS Connector System Design | Projection and Freshness | Explain projection ingestion, health, freshness, visibility, continuity evidence, and audit facts. |
| VPC-D06 | Vendor PMS Connector System Design | Vendor Payment Acknowledgment | Explain downstream acknowledgment after Central PMS finality/fiscal handling and open retry/escalation posture. |
| VPC-D07 | Vendor PMS Connector System Design | Degraded Resolve and Continuity Handoff | Explain fail-closed degraded decisioning through Central PMS and Continuity controls. |
| VPC-D08 | Vendor PMS Connector System Design | Error Normalization, Observability, and Operations | Explain normalized connector error/health reporting without defining implementation exceptions or DTOs. |
| HCP-D01 | HikCentral Connector Profile | HCP Object Mapping | Explain ParkingLotIndexCode and other HCP object references as vendor-side identity only. |
| HCP-D02 | HikCentral Connector Profile | HCP Security and Authentication Boundary | Explain HCP authentication and credential boundary at profile level. |
| HCP-D03 | HikCentral Connector Profile | HCP API Capability Use | Explain which HCP capability areas support profile behavior without endpoint maps. |
| HCP-D04 | HikCentral Connector Profile | HCP Passageway Projection | Explain passageway polling, projection normalization, freshness, and visibility. |
| HCP-D05 | HikCentral Connector Profile | HCP Ticket-only Fee Resolve | Explain ticket/card based live fee calculation flow. |
| HCP-D06 | HikCentral Connector Profile | HCP Vendor Acknowledgment | Conditional diagram only if confirmed by HCP API discovery. |
| HCP-D07 | HikCentral Connector Profile | HCP Health and Staleness | Explain HCP-specific stale/unavailable/auth/permission health classifications and visibility. |

## 7. Key Components Per Diagram

| Diagram | Key components to include |
| --- | --- |
| VPC-D01 | Vendor PMS/HCP, connector instance, Central PMS, POS Server, Operator Console, Management Dashboard, Continuity workflow, Audit/Event capability, Reconciliation, Gate/Exit consumer only if shown consuming Central PMS authorization. |
| VPC-D02 | Site Group, Site, VendorSystem, AdapterMapping, adapter codebase, connector instance, vendor object reference, HCP ParkingLotIndexCode note, Site POS Server. |
| VPC-D03 | Parker/channel or assisted terminal, Central PMS, connector instance, Vendor PMS/HCP, Audit/Event, TariffSnapshot concept. |
| VPC-D04 | Central PMS, connector instance, Vendor PMS/HCP tariff computation, Discount workflow where applicable, TariffSnapshot, Audit/Event. |
| VPC-D05 | Vendor PMS/HCP, connector polling/projection ingestion, Central PMS projection, freshness/health classifier, Operator Console, Management Dashboard, Continuity workflow, Audit/Event. |
| VPC-D06 | Central PMS, POS Server, connector instance, Vendor PMS/HCP acknowledgment target, retry/escalation/reconciliation lane, Audit/Event. |
| VPC-D07 | Channel/Continuity Terminal, Central PMS, connector instance, Vendor PMS/HCP, projection/freshness context, Operator Console/Continuity governance, POS Server if fiscal flow appears, Audit/Reconciliation. |
| VPC-D08 | Vendor PMS/HCP, connector error classifier, Central PMS health/status receiver, Operator Console, Management Dashboard, Audit/Event, Reconciliation/operations queue as conceptual consumer only. |
| HCP-D01 | HCP ParkingLotIndexCode, HCP parking lot/passageway object, AdapterMapping, ExitPass Site, VendorSystem, connector instance, runtime vendor object identity. |
| HCP-D02 | HCP OpenAPI boundary, connector runtime, credential store/secret boundary as conceptual element, Central PMS, Audit/Event, security/trust notes. |
| HCP-D03 | HCP parking lot identity capability, passageway projection capability, live fee resolve capability, conditional acknowledgment capability, connector adapter, Central PMS. |
| HCP-D04 | HCP passageway records, connector polling, projection normalization, freshness classifier, Operator Console, Management Dashboard, Continuity evidence, Audit/Event. |
| HCP-D05 | Ticket/card input, Central PMS, HCP connector adapter, HCP live fee calculation, TariffSnapshot, Audit/Event. |
| HCP-D06 | Central PMS finality/fiscal prerequisite, HCP connector adapter, HCP acknowledgment capability if confirmed, retry/escalation/reconciliation, Audit/Event. |
| HCP-D07 | HCP availability, HCP auth/permission status, connector poll health, last successful poll, stale projection, Operator Console, Management Dashboard, Continuity fail-closed controls. |

## 8. Authority Notes Per Diagram

| Diagram | Required authority notes |
| --- | --- |
| VPC-D01 | Connector is an integration boundary. Vendor PMS/HCP owns raw session and normal tariff. Central PMS owns platform finality and ExitAuthorization. POS Server owns fiscal issuance. |
| VPC-D02 | VendorSystem, AdapterMapping, adapter codebase, and connector instance must remain distinct. HCP ParkingLotIndexCode is vendor-side identity only, never ExitPass `site_id`. |
| VPC-D03 | Normal resolve does not create payment finality, fiscal issuance, or ExitAuthorization. It captures vendor result as Central PMS TariffSnapshot/payable-basis context. |
| VPC-D04 | Fee/tariff calculation is vendor-owned in normal mode. Central PMS records the accepted payable basis and applies approved platform policy; connector does not become tariff authority. |
| VPC-D05 | Projection is operational visibility and controlled degraded evidence only. It is not financial truth, fee truth, payment finality, fiscal truth, or exit authority. |
| VPC-D06 | Vendor acknowledgment is downstream of Central PMS payment finality and fiscal handling. Acknowledgment failure is auditable and reconciliation-tagged; it does not transfer finality authority. |
| VPC-D07 | Degraded resolve decisions belong to Central PMS under approved Continuity policy. Stale, ambiguous, or insufficient projection fails closed or routes to approved governance. |
| VPC-D08 | Error normalization supports health and operational response. It must not define final endpoint payloads, database fields, implementation exception classes, or authority decisions. |
| HCP-D01 | ParkingLotIndexCode and HCP object references remain vendor-side identities mapped through AdapterMapping to ExitPass Site. |
| HCP-D02 | Authentication boundary protects HCP credentials and request signing. Possession of HCP credentials does not grant payment, fiscal, or exit authority. |
| HCP-D03 | API use map is capability-level only. Do not show endpoint paths, request bodies, response DTOs, or device SDK calls. |
| HCP-D04 | Passageway projection supports operational visibility and degraded evidence only; passageway records alone must not invent tariffs. |
| HCP-D05 | Ticket-only fee calculation must use live HCP/vendor fee capability where confirmed; Central PMS records TariffSnapshot but HCP remains normal tariff authority. |
| HCP-D06 | Include only after API discovery confirms support. If unsupported or unconfirmed, document as an open capability question rather than drawing the flow. |
| HCP-D07 | Stale projection and HCP unavailable/auth failures must visibly restrict degraded use and must not permit payment, fiscal, or exit bypass. |

## 9. Diagram Risks to Avoid

- Do not create `.puml` or `.jpg` files from this pack.
- Do not use these recommendations to draft final diagrams before Lead authorization.
- Do not create database diagrams, API route diagrams, endpoint maps, implementation class diagrams, SDK diagrams, or deployment scripts.
- Do not show connector-owned payment finality, fiscal issuance, ExitAuthorization, gate opening, discount approval, or financial truth.
- Do not show POS Server issuing ExitAuthorization or declaring payment finality.
- Do not show Payment Orchestrator/provider success as platform payment finality.
- Do not show projection as normal tariff authority, payment finality, fiscal truth, settlement truth, discount approval, financial truth, or exit authority.
- Do not show passageway records being used to invent tariffs.
- Do not show HCP ParkingLotIndexCode as ExitPass `site_id`.
- Do not collapse Site Group and Site or collapse VendorSystem, AdapterMapping, adapter codebase, and connector instance.
- Do not copy historical endpoint paths, table names, database schemas, status codes, or specific DTOs from older HikCentral diagrams into final companion diagrams.
- Do not show gate opening unless it consumes Central PMS authorization or is explicitly covered by a future approved gate profile.
- Do not make Operator Console or Management Dashboard an action surface for payment collection, fiscal issuance, ExitAuthorization, gate opening, or continuity activation beyond approved governance visibility.
- Do not treat HCP payment acknowledgment as available unless confirmed by API discovery.
- Do not decide open questions such as push/pull topology, exact freshness thresholds, exact acknowledgment retry policy, degraded tariff basis, or exit-blocking behavior inside diagrams.

## 10. PlantUML Style Recommendations

If the Lead later authorizes diagram creation, use the existing v1.3 System Design style:

- Use `skinparam shadowing false`, `skinparam defaultFontName Arial`, `skinparam componentStyle rectangle`, and `skinparam wrapWidth 220`.
- Use component/context diagrams for authority and configuration models.
- Use sequence diagrams for normal resolve, fee calculation, payment acknowledgment, and degraded handoff.
- Use activity or component-flow diagrams for polling/freshness and error normalization.
- Use short, explicit notes near authority boundaries instead of visually implying authority through arrow direction.
- Label projection arrows as "projection / operational visibility" or "freshness evidence" rather than "truth", "paid", or "authorized".
- Label vendor arrows as "live session / normal tariff" where appropriate.
- Label Central PMS arrows as "record TariffSnapshot", "declare platform finality", "record fiscal reference", or "issue ExitAuthorization" only in diagrams where those steps belong.
- Use `alt` blocks in sequence diagrams for success, unavailable/stale, retry/escalation, and fail-closed paths.
- Keep HCP-specific identifiers and capability labels in HikCentral profile diagrams only.
- Keep diagrams conceptual. Avoid endpoint paths, DTO fields, database table names, queue names, class names, SQL routines, device SDK calls, concrete ports, credentials, or environment variable names.

## 11. Summary for Lead

Recommended Vendor PMS Connector System Design diagram set:

1. Generic connector context.
2. VendorSystem / AdapterMapping / connector instance model.
3. Normal live resolve sequence.
4. Fee calculation sequence.
5. Projection polling and freshness flow.
6. Vendor payment acknowledgment flow.
7. Degraded resolve handoff to Central PMS / Continuity.
8. Connector error normalization and health reporting flow.

Recommended HikCentral Connector Profile diagram set:

1. HikCentral object identity mapping.
2. HikCentral authentication boundary.
3. Parking lot / passageway / fee API use map.
4. Passageway projection flow.
5. Ticket-only fee calculation flow.
6. Vendor payment acknowledgment flow, only if supported by confirmed API discovery.
7. HCP connector health and stale projection flow.

Lead integration notes:

- Use existing v1.3 diagrams D-01 through D-11 as authority anchors, especially D-02, D-03, D-05, D-06, D-07, D-09, D-10, and D-11.
- Keep the generic connector design reusable across Vendor PMS/HCP systems.
- Keep HikCentral object identity, authentication, and passageway/fee capability details in the HikCentral Connector Profile.
- Carry open questions forward rather than resolving them in diagrams: HCP topology, exact health/freshness model, projection thresholds, acknowledgment retry/exit-block policy, and confirmed HCP acknowledgment support.
- Final diagrams should strengthen authority separation, not introduce implementation detail.
