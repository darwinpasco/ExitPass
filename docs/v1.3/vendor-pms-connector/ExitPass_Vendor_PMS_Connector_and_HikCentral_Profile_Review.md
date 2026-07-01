# ExitPass Vendor PMS Connector and HikCentral Profile Review

Date: 2026-07-01
Reviewer: Codex v1.3
Scope: Review only

## 1. Review Summary

The generic Vendor PMS Connector System Design v1.0 and HikCentral Connector Profile v1.0 are aligned with the v1.3 BRD baseline, ExitPass System Design v1.3, the connector orchestration plan, and the six specialist input packs.

No required fixes were found. The documents preserve the intended authority boundaries: Vendor PMS/HCP remains raw parking session lifecycle and normal tariff authority; Central PMS remains payment finality, platform control state, degraded decision, fiscal-reference recording, and ExitAuthorization authority; POS Server remains fiscal issuance authority; Payment Orchestrator reports verified provider outcomes without declaring platform finality.

The HikCentral profile stays subordinate to the generic connector design, preserves HCP ParkingLotIndexCode as vendor-side identity, keeps `cardNum` and ticket-only support unresolved, and keeps `parkingfee/confirm` disabled until explicitly approved.

## 2. Files Reviewed

- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_System_Design_v1.0.md`
- `docs/v1.3/hikcentral-connector/ExitPass_HikCentral_Connector_Profile_v1.0.md`
- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md`
- `docs/v1.3/vendor-pms-connector/input-packs/01_authority_scope_guard.md`
- `docs/v1.3/vendor-pms-connector/input-packs/02_hikcentral_api_discovery.md`
- `docs/v1.3/vendor-pms-connector/input-packs/03_connector_workflow_and_state.md`
- `docs/v1.3/vendor-pms-connector/input-packs/04_security_credentials_trust.md`
- `docs/v1.3/vendor-pms-connector/input-packs/05_observability_projection_operations.md`
- `docs/v1.3/vendor-pms-connector/input-packs/06_diagram_planning.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`
- `docs/v1.3/vendor-pms-connector/diagrams/`
- `docs/v1.3/hikcentral-connector/diagrams/`

## 3. Generic Vendor PMS Connector Design Review

The generic design is reusable and vendor-neutral. It clearly separates VendorSystem, AdapterMapping, adapter codebase, and connector instance. It keeps normal live resolve, fee calculation, projection polling, vendor acknowledgment, error normalization, security, observability, audit, reconciliation, and deployment posture at companion technical-design level.

The document does not define final endpoint paths, DTOs, database tables or columns, event payloads, retry algorithms, queue names, implementation classes, deployment scripts, or runbook procedures.

## 4. HikCentral Connector Profile Review

The HikCentral profile correctly specializes the generic design without overriding it. It covers confirmed HCP API areas, source gaps, HCP authentication posture, ParkingLotIndexCode identity, passageway projection, fee calculation, ticket/card/plate uncertainty, conditional vendor payment acknowledgment, security, health, and deployment questions.

Ticket-only support remains unconfirmed. `cardNum` remains an unresolved vendor/deployment question. `parkingfee/confirm` is treated as mutating and disabled until explicitly approved.

## 5. Generic-to-HikCentral Alignment Review

The HikCentral profile is explicitly subordinate to the generic Vendor PMS Connector System Design. No profile section attempts to override Central PMS authority, Site POS Server fiscal authority, or the approved Site Group/Site model.

The generic runtime identity model `vendorSystemId + vendorObjectType + vendorObjectRef` is preserved conceptually in the HikCentral profile, with final API/database representation deferred.

## 6. Authority Boundary Review

Confirmed:

- Vendor PMS/HCP remains raw parking session lifecycle and normal tariff computation authority.
- Central PMS remains platform payment finality, TariffSnapshot recording, fiscal-reference recording, degraded resolve, and ExitAuthorization authority.
- Payment Orchestrator reports verified provider outcomes but does not declare platform finality.
- POS Server remains fiscal issuance authority and does not issue ExitAuthorization.
- The connector does not declare payment finality.
- The connector does not issue fiscal documents.
- The connector does not issue ExitAuthorization.
- The connector does not directly open gates.

The generic design includes a future-gate-profile caveat, but frames it as a later approved controlled boundary. That is consistent with the current deferral posture.

## 7. Site Group / Site / VendorSystem / AdapterMapping Review

The documents preserve Site Group as customer lookup/payment scope and Site as reporting, contract, Vendor PMS mapping, Site POS Server routing, fiscal attribution, and operational boundary.

VendorSystem, AdapterMapping, adapter codebase, and connector instance remain distinct. HCP ParkingLotIndexCode is consistently treated as vendor-side identity and must map through AdapterMapping. No reviewed text treats ParkingLotIndexCode as ExitPass `site_id`.

## 8. HikCentral API Source and Source-Gap Review

The profile identifies the local HikCentral OpenAPI Developer Guide PDF as the primary vendor source and carries forward source gaps from the discovery pack:

- Full field tables were not extractable locally.
- No local OpenAPI YAML, Swagger, Postman collection, Bruno collection for parking-fee APIs, or full parking API collection was found.
- Ticket-only support is not confirmed.
- `cardNum` requires vendor clarification.

Confirmed endpoint areas are listed only as HikCentral-specific source/capability references, not final ExitPass API contracts.

## 9. Projection / Passageway / Freshness Review

Projection is consistently described as operational visibility and controlled degraded support only. It is not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority.

Passageway records are not used as payable session truth and must not invent tariffs. One-minute HCP passageway polling is preserved as a planning baseline, not a final freshness threshold, scheduler implementation, or guarantee of current state.

## 10. Fee Calculation / Ticket / cardNum Review

The documents preserve Vendor PMS/HCP as normal tariff authority. The connector requests live fee calculation where capability and identifier policy are confirmed, and Central PMS records the accepted payable basis as TariffSnapshot.

Ticket-only support remains unconfirmed. `cardNum` is explicitly unresolved and requires vendor/deployment clarification before it can be treated as a physical ticket lookup key.

## 11. Vendor Payment Acknowledgment / parkingfee/confirm Review

Vendor payment acknowledgment is downstream of Central PMS payment finality and required fiscal prerequisites where applicable. It is not ExitPass payment finality.

HikCentral `parkingfee/confirm` is correctly identified as mutating, disabled by default, prohibited in read-only diagnostics, and enabled only after deployment requirement, safety behavior, idempotency posture, retry policy, reconciliation handling, and ExitPass design approval are complete.

## 12. Security / Credentials / Secrets Review

No real secrets, AppKey, AppSecret, AK, SK, signatures, auth headers, or credential examples were found.

Credential terminology appears only as contextual/prohibited language, glossary entries, or security posture. The documents require secret redaction, credential segregation, connector-owned signing, and no credential exposure in repository files, documentation examples, logs, screenshots, prompts, test fixtures, dashboards, or support notes.

## 13. Observability / Operations / Reconciliation Review

The documents cover connector health, HCP availability, projection freshness, stale warnings, mapping ambiguity, poll status, fee availability, acknowledgment backlog, authentication/authorization failure, audit, and reconciliation.

Operational views are constrained to authorized visibility. Financial and revenue reporting remains tied to canonical payment, fiscal, settlement, and reconciliation records rather than projection.

## 14. Diagram Coverage Review

Diagram inventory confirmed:

- Generic connector diagrams: 8 `.puml` files and 8 `.jpg` files.
- HikCentral diagrams: 7 `.puml` files and 7 `.jpg` files.
- No `.png` files remain in the reviewed diagram folders.

The diagrams are conceptual. They do not include secrets, real credential values, database tables, route maps, implementation classes, or device SDK details. HCP-D03 includes capability-area labels only and explicitly defers endpoint contracts, request bodies, response DTOs, and implementation details.

## 15. Open Questions and Deferrals Review

The reviewed documents preserve open questions rather than deciding them silently. Deferred areas include:

- Connector push/pull topology and scheduler ownership.
- Exact health states, freshness labels, stale thresholds, and alert rules.
- Degraded tariff freshness threshold.
- Vendor acknowledgment sync/async/queue/retry/exit-blocking behavior.
- Safe retry or confirmation handling for unknown mutating acknowledgment outcomes.
- Mapping governance workflow.
- Secret store, mTLS/certificate model, service identity, rotation, and break-glass process.
- HCP `cardNum`, ticket-only lookup key, `parkingfee/confirm` behavior, HCP error codes, pagination/date/rate/timezone limits, and license/permission requirements.

## 16. Risky Terminology Scan

Reviewed terms:

- `HCP site`: not found as unsafe terminology.
- `ParkingLotIndexCode as site_id`: found only as a prohibition or safe mapping rule.
- `projection source of truth`: not found as an approved concept.
- `connector payment confirmation`: not found as an approved connector authority concept.
- `connector finality`: not found as an approved connector authority concept.
- `connector exit authorization`: not found as an approved connector authority concept.
- `connector gate open`: not found as an approved connector authority concept.
- `automatic fallback`: not found in the primary documents or diagrams.
- `silent fallback`: not found in the primary documents or diagrams.
- `vendor payment means ExitPass payment finality`: found only as a prohibited misuse case in input pack 01.
- `vendor paid state means Sales Invoice issued`: found only as a prohibited misuse case in input pack 01.
- `passageway record means payable session truth`: found only as a prohibited misuse case in input pack 01.
- `parking lot when ExitPass Site is intended`: HikCentral profile uses "parking lot" for HCP/vendor API capability and object discovery, not as an ExitPass Site synonym.
- `Official Receipt` / uppercase `OR`: not found.
- `AppKey`, `AppSecret`, `AK`, `SK`, `secret`, `signature`: safe contextual use only; no values or reusable examples included.

## 17. Issues Found

No required issues found.

Non-blocking observation: input pack 06 states that input pack 05 was not available at its own review time. The final documents nevertheless cite and incorporate the observability/projection/operations posture, and the current file set includes input pack 05. This is a stale planning-artifact note, not a defect in the two reviewed target documents.

## 18. Required Fixes, if any

None.

## 19. Nice-to-Have Fixes, if any

None required for approval readiness of these two documents. If the input packs are ever republished, the stale note in input pack 06 about input pack 05 availability could be cleaned up as a documentation hygiene item, but this review task explicitly does not modify specialist input packs.

## 20. Recommendation

Recommend accepting the generic Vendor PMS Connector System Design v1.0 and HikCentral Connector Profile v1.0 for their current companion technical-design purpose.

They are ready to serve as inputs to later API Contract Pack, Database Delta, Engineering Pack, Test/UAT Pack, and Runbook Pack work, provided all listed open questions and deferrals remain unresolved until explicitly approved in the appropriate downstream artifact.
