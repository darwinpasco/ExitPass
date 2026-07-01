# ExitPass HikCentral Connector Profile v1.0

Status: Draft companion technical profile for v1.3

## 1. Document Control

### Version History

| Version | Date | Description |
| --- | --- | --- |
| v1.0 | 2026-07-01 | Initial HikCentral-specific connector profile subordinate to the generic Vendor PMS Connector System Design v1.0. |

### Document Ownership

| Role | Owner |
| --- | --- |
| Documentation stream | ExitPass v1.3 documentation |
| Lead design owner | Lead Connector Design agent |
| Downstream consumers | Vendor PMS Connector implementation, API Contract Pack, Database Delta, Engineering Pack, Test/UAT Pack, and Runbook Pack |

### Approval Posture

This profile documents HikCentral-specific connector constraints and confirmed API areas from local sources. It does not approve production credentials, final endpoint contracts, DTOs, database changes, request signing code, vendor mutation behavior, or BIR/fiscal behavior.

## 2. Executive Summary

The HikCentral Connector Profile defines how the generic Vendor PMS Connector System Design applies to HikCentral Professional. It covers HCP object identity, authentication boundary, parking object discovery, passageway projection, live fee calculation, conditional vendor payment acknowledgment, health, observability, source gaps, and vendor questions.

This profile is subordinate to the generic connector design. It must not override Central PMS authority, Site POS Server fiscal authority, or the approved v1.3 Site Group/Site model.

Local source review confirms relevant HikCentral API areas, but full field tables were not extractable locally and no OpenAPI YAML, Swagger, Postman, or full parking API collection was found. Ticket-only fee calculation remains unconfirmed. The meaning of `cardNum` remains a vendor/deployment question.

## 3. Profile Purpose and Scope

In scope:

- HCP authentication and credential boundary posture.
- HCP deployment prerequisites.
- HCP vendor object identity and AdapterMapping rules.
- Parking object discovery using the confirmed parking lot list API area.
- Passageway polling and projection using the confirmed passageway record API area.
- One-minute passageway polling as the planning baseline, not a final freshness threshold.
- Live fee calculation using the confirmed parking fee calculation API area.
- Ticket/card/plate identifier policy and unresolved `cardNum` meaning.
- Conditional vendor payment acknowledgment using the confirmed parking fee confirmation API area, disabled unless approved.
- HCP health, observability, failure modes, source gaps, vendor questions, and sign-off checklist.

Out of scope:

- Final endpoint contracts, DTOs, request bodies, response field contracts, table names, event payloads, queue names, implementation classes, or request signing code.
- Production enablement of mutating vendor calls.
- Gate profile design, fiscal issuance design, POS Server design, or Central PMS authority changes.

## 4. Source Authority and Source Availability

Primary local vendor source:

- `docs/vendor/hikcentral/HikCentral Professional OpenAPI_Developer Guide_V3.1.0_20260130.pdf`

Supporting local/historical references:

- `docs/hikcentral-ticket-only-readonly-validation.md`
- `docs/hikcentral-projection-resolve-uat-results.md`
- `docs/hikcentral-projection-live-uat.md`
- `docs/hikcentral-projection-production-controls.md`
- `docs/hikcentral-real-sync-target-deployment-handoff.md`
- `docs/hikcentral-operator-console-projection-health-smoke.md`
- Historical PlantUML references under `docs/diagrams/`
- Existing repository code was used only as local evidence noted by the specialist input pack, not as authority to modify code in this task.

Source availability issues:

- The local PDF confirms relevant endpoint areas, but full PDF field tables were not extractable locally.
- No local OpenAPI YAML, Swagger, Postman collection, Bruno collection for parking-fee APIs, or full parking API collection was found.
- Ticket-only support is not confirmed by local sources.
- `cardNum` appears in passageway and fee contexts, but its exact business meaning remains vendor-questioned.
- `parkingfee/confirm` is mutating and must remain disabled unless the deployment and ExitPass design explicitly approve use.

## 5. Relationship to Vendor PMS Connector System Design

The HikCentral Connector Profile is a vendor-specific profile under the generic Vendor PMS Connector System Design.

The generic design defines:

- Authority boundaries.
- VendorSystem, AdapterMapping, adapter codebase, and connector instance model.
- Normal live resolve, fee calculation, projection, acknowledgment, and failure posture.
- Security, credentials, observability, audit, and reconciliation posture.

This profile applies those rules to HikCentral-specific identity, source availability, confirmed API areas, authentication posture, and open vendor questions. If HCP behavior appears to require an exception to the generic design, the exception must be treated as an open design issue, not as an override.

## 6. HikCentral Authority and Non-Authority Model

HikCentral Professional remains Vendor PMS/HCP authority for raw parking session lifecycle and normal tariff computation where live fee calculation is confirmed and available.

The HikCentral connector shall:

- Authenticate to HCP through the approved connector boundary.
- Discover or confirm HCP parking object identity where required.
- Poll passageway records for operational projection where approved.
- Request live fee calculation where confirmed.
- Conditionally submit vendor payment acknowledgment only when explicitly approved and requested by Central PMS.
- Report HCP availability, health, freshness, and normalized outcomes.

The HikCentral connector shall not:

- Declare ExitPass payment finality.
- Issue Sales Invoices or fiscal documents.
- Record fiscal issuance reference as platform authority.
- Issue, simulate, or replace ExitAuthorization.
- Directly operate gates unless a later approved gate profile defines a controlled boundary.
- Approve statutory discounts or mutate payable basis directly.
- Treat projection as financial, fiscal, normal tariff, payment, discount, or exit authority.
- Treat HCP ParkingLotIndexCode as ExitPass `site_id`.

## 7. HikCentral Authentication and Credential Boundary

Local sources and specialist input indicate HikCentral uses AppKey/AppSecret or AK/SK-style signed OpenAPI integration.

Profile-level requirements:

- HCP credentials are vendor integration secrets owned inside the connector boundary.
- No AppKey, AppSecret, AK, SK, derived signature material, nonce, reusable sample secret, or authorization header value shall be included in repository files, docs, logs, screenshots, prompts, test fixtures, committed config, or dashboard exports.
- Central PMS-to-connector service trust shall be separate from connector-to-HCP authentication.
- Possession of HCP credentials does not grant payment, fiscal, discount, continuity activation, or exit authority.
- Failed HCP authentication or permission checks shall be surfaced as normalized connector/vendor dependency states without exposing secret material.

Exact header names, canonical signing string, timestamp handling, skew handling, credential storage product, mTLS topology, and certificate model are deferred.

## 8. HikCentral Deployment Prerequisites

Before HCP connector enablement, the deployment must confirm:

- Target HCP base URL, scheme, port, and network reachability.
- HCP OpenAPI availability for required parking API areas.
- Parking-related module/license enablement.
- AppKey/AppSecret or AK/SK permission scope for parking object discovery, passageway records, fee calculation, and payment acknowledgment if approved.
- Environment segregation for production, UAT, and test credentials.
- Time synchronization posture for signed requests.
- Known HCP parking object references and corresponding ExitPass AdapterMapping.
- Vendor limits for page size, date range, request rate, and retention.
- Whether `parkingfee/confirm` is required, safe, idempotent, and approved for the target deployment.

## 9. HikCentral Vendor Object Identity Model

HCP object identity is vendor-side identity. ExitPass Site remains the platform reporting, contract, Vendor PMS mapping, Site POS Server routing, fiscal attribution, and operational boundary.

Profile identity model:

- VendorSystem represents the configured HCP instance.
- Connector instance represents the configured runtime connector for that VendorSystem.
- Adapter codebase represents reusable HikCentral integration behavior.
- AdapterMapping maps an ExitPass Site to an HCP parking object.
- Runtime vendor object identity remains conceptual as `vendorSystemId + vendorObjectType + vendorObjectRef`.

Exact database and API representation is deferred.

## 10. ParkingLotIndexCode and AdapterMapping Rules

HCP ParkingLotIndexCode is a vendor-side parking object identity.

Rules:

- ParkingLotIndexCode must map through AdapterMapping.
- ParkingLotIndexCode must not be reused as ExitPass `site_id`.
- ParkingLotIndexCode must not determine Site POS Server routing by itself.
- ParkingLotIndexCode must not become customer-facing Site Group identity.
- Mapping ambiguity must fail closed or route to approved review.
- HCP parking object changes must be treated as configuration changes requiring audit and deployment sign-off.

## 11. Parking Lot Discovery

Confirmed API area:

- `POST /artemis/api/vehicle/v1/parkinglot/list`

Design use:

- Support discovery or confirmation of HCP parking object identities.
- Support AdapterMapping setup and validation.
- Support deployment readiness checks for configured HCP objects.
- Support operational verification that a configured object still exists and is visible to the credential where appropriate.

This profile does not define final request body, response field contract, pagination implementation, or API DTOs.

## 12. Passageway Polling and Projection

Confirmed API area:

- `POST /artemis/api/vehicle/v1/parkinglot/passageway/record`

Design use:

- Poll HCP passageway records for operational projection.
- Normalize passageway facts into Central PMS projection inputs where approved.
- Preserve HCP object identity context and AdapterMapping context.
- Support operational visibility, stale connector alerts, dashboard health, and controlled degraded evidence.

Passageway records are not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority. They must not be used to invent tariffs.

## 13. Projection Freshness and One-Minute Polling Baseline

One-minute HCP passageway polling is the ExitPass v1.3 planning baseline for HikCentral projection.

This baseline is not:

- A final freshness threshold.
- A final scheduler implementation.
- A guarantee that projection is current.
- Approval to use projection for degraded tariff computation.
- Approval to continue payment or exit when projection is stale, ambiguous, or insufficient.

Projection-based views and degraded evaluation inputs must show or carry freshness/staleness context. Exact stale threshold, labels, alert rules, and degraded eligibility rules remain open.

## 14. Live Fee Calculation / Parking Fee Resolve

Confirmed API area:

- `POST /artemis/api/vehicle/v1/parkingfee/calculate`

Design use:

- Request live HCP fee calculation in normal mode where capability and identifier policy are confirmed.
- Normalize HCP fee results for Central PMS.
- Allow Central PMS to record TariffSnapshot as the platform payable-basis record.

The HCP connector is not tariff authority. HCP supplies normal live fee facts; Central PMS records platform state and controls downstream payment, fiscal, and exit workflows.

Ticket-only fee calculation remains unconfirmed until the deployment/vendor confirms the correct identifier.

## 15. Ticket / Card / Plate Identifier Policy

Known identifier posture:

- `cardNum` appears in passageway and fee calculation contexts.
- `plateLicense` appears in fee calculation and passageway contexts.
- Local evidence does not prove that a physical printed ticket number maps to `cardNum`.
- Historical ticket-only validation did not confirm printed ticket numbers as usable `cardNum` values in the tested deployment.
- `plateLicense = Unknown` must be treated as a placeholder, not a real plate identity.
- Plate-based lookup is known as a capability area but remains outside the current ticket-only flow unless later approved.

The connector shall not claim ticket-only support until vendor/deployment validation confirms the correct lookup key and barcode/QR payload behavior.

## 16. cardNum Open Question and Vendor Clarification Required

`cardNum` remains an unresolved HCP vendor/deployment question.

Required clarification:

- Does `cardNum` mean printed ticket number, card identifier, internal credential, or another vendor-side identifier?
- Which field from a physical ticket maps to the active session lookup key accepted by HCP fee calculation?
- Does a barcode or QR payload differ from the visible printed ticket number?
- Can `cardNum` be used safely for ticket-only fee calculation in the target deployment?
- What response codes distinguish not found, already paid, exited, expired, duplicate, amount mismatch, and invalid confirmation?

Until resolved, the connector profile shall treat ticket-only fee calculation as unconfirmed.

## 17. Payment Acknowledgment / Parking Fee Confirmation

Confirmed API area:

- `POST /artemis/api/vehicle/v1/parkingfee/confirm`

Profile posture:

- This is a mutating vendor API area.
- It must remain disabled by default.
- It must not be called in read-only diagnostics.
- It may be enabled only when the deployment requirement, safety behavior, idempotency posture, retry policy, reconciliation handling, and ExitPass design approval are complete.
- It must be invoked only downstream of Central PMS payment finality and required fiscal prerequisites where applicable.
- Its result is a vendor acknowledgment outcome, not ExitPass payment finality.

Open behavior:

- Whether HCP requires confirmation before exit.
- Whether confirmation marks paid, allows exit, both, or another vendor state.
- Whether failure blocks exit, creates retry backlog, or varies by Site/vendor policy.

## 18. Response Success, Error, Pagination, and Date Range Posture

Local/historical evidence indicates:

- `code = 0` is treated as a success posture in local references.
- Historical codes include authentication failure, not found/resource missing, and unknown/internal request categories.
- These historical codes are evidence only and must not be treated as the final error contract.
- Passageway and parking object API areas use paged retrieval concepts, but exact final pagination rules are not approved.
- Maximum page size, maximum date range, retention, request rate, and timezone behavior remain unconfirmed.

The connector shall normalize HCP responses into platform categories such as success, not found, already paid, already exited, fee unavailable, unavailable, timeout, unknown, duplicate, ambiguous, insufficient, authentication/authorization failure, and malformed/unexpected response.

## 19. API Availability, License, and Permission Constraints

HCP connector enablement depends on:

- HCP OpenAPI availability.
- Parking module/license availability.
- Credential permission for each required API area.
- Network route and TLS posture.
- Time synchronization for signed requests.
- Confirmed access to configured HCP parking objects.
- Approved use of mutating confirmation if required.

If a required capability is not licensed, not enabled, not permissioned, or not reachable, the connector shall report unavailable or permission failure. It shall not infer successful session, fee, payment, fiscal, or exit state.

## 20. HikCentral Connector Health and Observability

HCP health and observability shall include concept-level signals for:

- Connector instance health.
- HCP reachability.
- Authentication/permission state.
- Parking object discovery availability.
- Passageway polling availability.
- Last successful poll.
- Projection freshness.
- Poll latency and failure categories.
- Live fee calculation availability.
- Conditional payment acknowledgment availability and backlog where enabled.
- Mapping health and ambiguity.
- Stale, ambiguous, insufficient, unavailable, and unknown conditions.

Operator Console and Management Dashboard may display these signals where authorized with clear source/freshness labels.

## 21. Security, Secrets, and Request Signing Posture

HCP request signing is connector-owned. This profile does not document real credential values or reusable signatures.

Requirements:

- Store HCP secrets only in approved secret channels.
- Redact credentials, signatures, authorization headers, and sensitive payloads from logs and traces.
- Segregate production and non-production credentials.
- Use least-privilege HCP application permissions where supported.
- Keep connector service identity distinct from HCP application credentials.
- Audit credential lifecycle, connector configuration changes, mapping changes, and mutating vendor operations.

Exact signing header construction, canonical string, key rotation, certificate model, vault product, and local clock skew controls are deferred.

## 22. Failure Modes and Safe Defaults

Safe defaults:

- If HCP is unavailable, fail closed or route to approved Continuity evaluation.
- If HCP authentication or permission fails, do not expose secrets and do not treat the operation as successful.
- If projection is stale, ambiguous, or insufficient, do not use it for degraded tariff or exit evaluation unless approved policy explicitly allows controlled use.
- If live fee calculation is unavailable, do not invent tariff from passageway records.
- If `cardNum` is unresolved, do not claim ticket-only fee support.
- If `plateLicense` is `Unknown`, do not treat it as a real plate.
- If `parkingfee/confirm` outcome is unknown, do not retry blindly without later-approved idempotency posture.
- If mapping is ambiguous, do not choose a Site or vendor object by heuristic.

## 23. Vendor Questions and Deployment Sign-Off Checklist

Vendor questions:

- What exactly is `cardNum` in parking fee and passageway contexts?
- What is the correct ticket-only lookup key?
- Does printed ticket barcode/QR payload differ from the visible ticket number?
- Is `parkingfee/confirm` required before exit in this deployment?
- Does `parkingfee/confirm` mark paid, allow exit, both, or another vendor state?
- What exact error codes represent not found, already paid, exited, expired, duplicate, invalid confirmation, and amount mismatch?
- What are maximum `pageSize`, date range, request rate, and retention limits?
- What timezone behavior applies to requests and responses?
- What HCP license/module and application permissions are required?

Deployment sign-off checklist:

- HCP API areas enabled and reachable.
- Credential permissions tested in target environment.
- AdapterMapping reviewed and approved.
- ParkingLotIndexCode values validated as vendor-side identities only.
- Ticket/card/plate identifier policy confirmed.
- Mutating confirmation remains disabled unless explicitly approved.
- Health, projection freshness, and stale warnings visible to authorized operations surfaces.
- No secrets committed or exposed in logs, docs, screenshots, prompts, or support bundles.

## 24. Open Questions and Deferred Decisions

| ID | Open question / deferred decision |
| --- | --- |
| HCP-OQ-001 | Exact `cardNum` meaning and whether it maps to printed ticket number, card identifier, internal credential, or another value. |
| HCP-OQ-002 | Correct ticket-only fee calculation lookup key and physical ticket barcode/QR behavior. |
| HCP-OQ-003 | Whether `parkingfee/confirm` is required before exit and what vendor state it changes. |
| HCP-OQ-004 | Exact HCP error code contract for not found, already paid, exited, expired, duplicate, invalid confirmation, and amount mismatch. |
| HCP-OQ-005 | Maximum page size, date range, retention, request rate, and timezone behavior. |
| HCP-OQ-006 | Exact API license/module enablement and credential permission set. |
| HCP-OQ-007 | Exact projection freshness threshold and stale warning labels. |
| HCP-OQ-008 | Exact push/pull topology and scheduler ownership for HCP projection. |
| HCP-OQ-009 | Exact HCP AK/SK or AppKey/AppSecret signing implementation. |
| HCP-OQ-010 | Exact acknowledgment retry, idempotency, exit-blocking, and reconciliation policy. |

## 25. Requirements Traceability Summary

| Requirement area | Trace source | Profile coverage |
| --- | --- | --- |
| Generic connector subordination | Vendor PMS Connector System Design | Sections 5, 6 |
| HCP API source constraints | HikCentral API discovery input pack | Sections 4, 11, 12, 14, 17, 18 |
| Object identity and ParkingLotIndexCode | ExitPass BRD v1.3, System Design v1.3, input packs 01 and 02 | Sections 9, 10 |
| Projection and one-minute baseline | ExitPass BRD v1.3, input packs 02, 03, 05 | Sections 12, 13, 20 |
| Live fee calculation | ExitPass BRD v1.3, input pack 02 | Section 14 |
| Ticket/card/plate uncertainty | HikCentral API discovery input pack | Sections 15, 16 |
| Conditional vendor acknowledgment | ExitPass BRD v1.3, input packs 02 and 03 | Section 17 |
| Security and credentials | System Design v1.3, input pack 04 | Sections 7, 21 |
| Safe defaults and source gaps | Orchestration plan, input packs 01 through 05 | Sections 22, 24 |

## 26. Appendix A: Confirmed API Areas

| API area | Local source status | Profile use |
| --- | --- | --- |
| `POST /artemis/api/vehicle/v1/parkinglot/list` | Confirmed endpoint area in local guide and input pack | Parking object discovery and mapping support. |
| `POST /artemis/api/vehicle/v1/parkinglot/passageway/record` | Confirmed endpoint area in local guide and input pack | Passageway polling and operational projection. |
| `POST /artemis/api/vehicle/v1/parkingfee/calculate` | Confirmed endpoint area in local guide and input pack | Live fee calculation where identifier policy is confirmed. |
| `POST /artemis/api/vehicle/v1/parkingfee/confirm` | Confirmed endpoint area in local guide and input pack | Conditional mutating vendor acknowledgment; disabled until approved. |

## 27. Appendix B: Glossary

| Term | Definition |
| --- | --- |
| AdapterMapping | Mapping between ExitPass Site and HCP vendor-side parking object. |
| AppKey/AppSecret | HCP application credential style referenced by local sources. |
| AK/SK | Access-key/secret-key style credential terminology used in local references. |
| cardNum | HCP field observed in passageway and fee contexts; exact business meaning remains unconfirmed. |
| ParkingLotIndexCode | HCP vendor-side parking object identity. |
| Passageway record | HCP operational record used as projection input only. |
| VendorSystem | Configured HCP instance. |

## 28. Appendix C: Acronyms

| Acronym | Meaning |
| --- | --- |
| API | Application Programming Interface |
| HCP | HikCentral Professional |
| PMS | Parking Management System |
| POS | Point of Sale |
| UAT | User Acceptance Testing |

## 29. Appendix D: Diagram Index

| Diagram | File |
| --- | --- |
| HCP-D01 HikCentral Object Identity Mapping | [HCP-D01_HikCentral_Object_Identity_Mapping.jpg](diagrams/HCP-D01_HikCentral_Object_Identity_Mapping.jpg) / [PUML](diagrams/HCP-D01_HikCentral_Object_Identity_Mapping.puml) |
| HCP-D02 HikCentral Authentication Boundary | [HCP-D02_HikCentral_Authentication_Boundary.jpg](diagrams/HCP-D02_HikCentral_Authentication_Boundary.jpg) / [PUML](diagrams/HCP-D02_HikCentral_Authentication_Boundary.puml) |
| HCP-D03 Parking Lot / Passageway / Fee API Use Map | [HCP-D03_Parking_Lot_Passageway_Fee_API_Use_Map.jpg](diagrams/HCP-D03_Parking_Lot_Passageway_Fee_API_Use_Map.jpg) / [PUML](diagrams/HCP-D03_Parking_Lot_Passageway_Fee_API_Use_Map.puml) |
| HCP-D04 Passageway Projection Flow | [HCP-D04_Passageway_Projection_Flow.jpg](diagrams/HCP-D04_Passageway_Projection_Flow.jpg) / [PUML](diagrams/HCP-D04_Passageway_Projection_Flow.puml) |
| HCP-D05 Ticket-only Fee Calculation Flow | [HCP-D05_Ticket_Only_Fee_Calculation_Flow.jpg](diagrams/HCP-D05_Ticket_Only_Fee_Calculation_Flow.jpg) / [PUML](diagrams/HCP-D05_Ticket_Only_Fee_Calculation_Flow.puml) |
| HCP-D06 Conditional Vendor Payment Acknowledgment Flow | [HCP-D06_Conditional_Vendor_Payment_Acknowledgment_Flow.jpg](diagrams/HCP-D06_Conditional_Vendor_Payment_Acknowledgment_Flow.jpg) / [PUML](diagrams/HCP-D06_Conditional_Vendor_Payment_Acknowledgment_Flow.puml) |
| HCP-D07 HCP Connector Health and Stale Projection Flow | [HCP-D07_HCP_Connector_Health_and_Stale_Projection_Flow.jpg](diagrams/HCP-D07_HCP_Connector_Health_and_Stale_Projection_Flow.jpg) / [PUML](diagrams/HCP-D07_HCP_Connector_Health_and_Stale_Projection_Flow.puml) |

