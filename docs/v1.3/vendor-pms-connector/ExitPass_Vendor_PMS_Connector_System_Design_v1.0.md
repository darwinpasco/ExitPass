# ExitPass Vendor PMS Connector System Design v1.0

Status: Draft companion technical design for v1.3

## 1. Document Control

### Version History

| Version | Date | Description |
| --- | --- | --- |
| v1.0 | 2026-07-01 | Initial companion technical design for the reusable Vendor PMS connector boundary, based on the approved ExitPass v1.3 BRD baseline, ExitPass System Design v1.3, connector orchestration plan, and specialist input packs. |

### Document Ownership

| Role | Owner |
| --- | --- |
| Documentation stream | ExitPass v1.3 documentation |
| Lead design owner | Lead Connector Design agent |
| Downstream consumers | ExitPass System Design v1.3 companion technical design stream, API Contract Pack, Database Delta, Engineering Pack, Test/UAT Pack, and Runbook Pack |

### Approval Posture

This document is a companion technical design input for later Database/API/Engineering Pack work. It does not approve database schema, API contracts, implementation classes, deployment scripts, vendor credentials, or operational runbook procedures.

## 2. Executive Summary

The Vendor PMS connector is the ExitPass integration boundary between Central PMS and configured external parking management systems, including HikCentral Professional deployments. The connector design is intentionally reusable across Vendor PMS/HCP systems and separates the generic connector model from vendor-specific connector profiles.

The connector reports vendor facts, health, freshness, availability, and normalized outcomes. It does not create platform payment finality, issue fiscal documents, issue ExitAuthorization, or directly operate gates. In normal mode, Vendor PMS/HCP remains the authority for raw parking session lifecycle and tariff computation. Central PMS remains the authority for platform payment-linked state, TariffSnapshot recording, fiscal issuance reference recording, degraded resolve decisions under approved policy, and ExitAuthorization.

Projection polling supports operational visibility and controlled degraded support only. Projection is not financial truth, fiscal truth, normal tariff truth, payment finality, discount approval, or exit authority.

## 3. Design Purpose and Scope

This design defines the reusable connector architecture posture for Vendor PMS/HCP integrations.

In scope:

- Generic VendorSystem, AdapterMapping, adapter codebase, and connector instance model.
- Normal live resolve and fee calculation responsibilities.
- Projection polling and freshness posture.
- Vendor payment acknowledgment posture.
- Mapping ambiguity, vendor unavailable, timeout, unknown outcome, duplicate, and stale projection handling.
- Security, credential, trust-boundary, observability, audit, and reconciliation posture.
- Authority guardrails for downstream API, database, engineering, test, and runbook work.

Out of scope:

- Final endpoint paths, request/response DTOs, database tables, columns, event payloads, queue names, retry counts, implementation classes, deployment scripts, and runbook steps.
- HikCentral-specific final implementation details, which belong in the HikCentral Connector Profile and later engineering artifacts.
- Any change to approved BRDs, ExitPass System Design v1.3, database schema, API contracts, or source code.

## 4. Approved Baseline Inputs

| Source | Use in this design |
| --- | --- |
| `docs/v1.3/ExitPass_BRD_v1.3.md` | Business authority model, Site Group/Site semantics, projection limits, fiscal-before-exit rule, and open connector questions. |
| `docs/v1.3/ExitPass_System_Design_v1.3.md` | System-level authority, trust boundaries, connector posture, projection/freshness boundaries, and deferred companion design scope. |
| `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md` | Approved BRD baseline status for System Design use. |
| `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md` | Terminal/channel authority boundaries and continuity-mode restrictions. |
| `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md` | Degraded operation, explicit activation, fail-closed posture, projection freshness, manual release, and post-restoration review. |
| `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md` | Operator visibility for connector health and projection freshness, and non-payment governance boundaries. |
| `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md` | Operational visibility versus financial truth and reporting source labels. |
| `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md` | Site POS Server fiscal authority and Sales Invoice before normal ExitAuthorization. |
| `docs/v1.3/ExitPass_v1.3_Documentation_Decision_Log.md` | Approved v1.3 decisions for connector identity, HCP object identity, polling baseline, and authority model. |
| `docs/v1.3/ExitPass_v1.3_Open_Questions.md` | Open connector topology, acknowledgment, health, freshness, and degraded threshold questions. |
| `docs/v1.3/ExitPass_v1.3_Source_Document_Impact_Map.md` | Connector, HikCentral, projection, degraded mode, API, database, and engineering impacts. |
| `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md` | Lead orchestration rules, target document split, source availability posture, and review gates. |
| Specialist input packs `01` through `06` | Authority scope, HikCentral API discovery, workflow/state, security/trust, observability/operations, and diagram planning. |

## 5. Authority Model

| Function | Owner | Connector posture |
| --- | --- | --- |
| Raw parking session lifecycle in normal mode | Vendor PMS / HCP | Queries or receives vendor facts and normalizes them. |
| Normal tariff computation | Vendor PMS / HCP | Requests live vendor fee calculation where capability is confirmed. |
| Site Group and Site resolution | Central PMS | Uses approved platform configuration and mapping context. |
| Session projection and platform control state | Central PMS | Receives connector projection inputs as operational data. |
| TariffSnapshot recording | Central PMS | Records accepted payable basis after vendor fee result or approved degraded basis. |
| Payment provider interaction | Payment Orchestrator or approved payment channel integration | Not owned by connector. |
| Platform payment finality | Central PMS | Connector reports vendor facts only. |
| Sales Invoice issuance | Resolved Site POS Server | Connector has no fiscal issuance authority. |
| Fiscal issuance reference recording | Central PMS | Connector does not own fiscal reference state. |
| Degraded resolve decision | Central PMS under approved Continuity policy | Connector reports unavailability, freshness, and ambiguity. |
| ExitAuthorization | Central PMS | Connector does not issue or simulate authorization. |
| Gate/exit execution | Gate/exit system consuming Central PMS authorization | Connector is not a gate execution authority unless a future approved gate profile defines a controlled boundary. |
| Reconciliation and post-restoration review | Operations / reconciliation workflow | Connector outcomes are inputs, not closure authority. |

## 6. Non-Authority Scope

The Vendor PMS connector shall not:

- Declare platform payment finality.
- Issue fiscal documents or Sales Invoices.
- Record fiscal issuance reference as the platform authority.
- Issue, simulate, or replace ExitAuthorization.
- Directly operate gates unless a later approved gate profile assigns a controlled integration boundary.
- Approve statutory discounts or mutate payable basis.
- Decide degraded resolve eligibility.
- Decide whether stale projection may be used.
- Treat vendor paid state or vendor acknowledgment as proof of ExitPass payment finality.
- Treat vendor-side payment state as proof that Sales Invoice issuance occurred.
- Treat projection or passageway records as financial, fiscal, normal tariff, payment, discount, or exit authority.
- Convert HCP ParkingLotIndexCode or any vendor-side object identity into ExitPass `site_id`.

## 7. Connector Architecture Overview

The generic connector architecture consists of:

- Central PMS, which calls or coordinates with the connector through approved service-to-service trust.
- One or more connector instances, each deployed and configured for a VendorSystem unless a later approved topology allows otherwise.
- Adapter codebases, which implement reusable vendor integration behavior.
- AdapterMapping configuration, which connects an ExitPass Site to a vendor-side parking object.
- Vendor PMS/HCP systems, which remain external authority systems for raw parking session lifecycle and normal tariff calculation.
- Operational consumers, including Operator Console and Management Dashboard, which may display connector health and projection freshness through approved backend flows.
- Audit/event and reconciliation consumers, which use connector facts for traceability and post-restoration review.

The connector boundary normalizes vendor-specific behavior into platform-understandable results without becoming a new authority layer.

## 8. VendorSystem, AdapterMapping, Adapter Codebase, and Connector Instance Model

| Concept | Definition |
| --- | --- |
| VendorSystem | A configured Vendor PMS/HCP instance. It is not an ExitPass Site, adapter codebase, AdapterMapping, or process by itself. |
| AdapterMapping | The mapping between an ExitPass Site and a vendor-side parking object. It is the bridge between platform Site context and vendor object identity. |
| Adapter codebase | A reusable implementation for a vendor family, such as a HikCentral adapter. |
| Connector instance | The deployed and configured runtime connector for a VendorSystem. |

The model shall preserve the distinction between configured external system, platform-to-vendor mapping, reusable adapter implementation, and deployed runtime connector. This design does not define final table names, endpoint names, DTOs, or deployment units.

## 9. Site Group / Site / Vendor Object Identity Model

Site Group is the customer lookup/payment scope. Site is the reporting, contract, Vendor PMS mapping, Site POS Server routing, fiscal attribution, and operational boundary.

The connector design shall preserve these rules:

- A Site Group may resolve to one or more Sites according to Central PMS configuration.
- The resolved Site determines the applicable VendorSystem, AdapterMapping, and Site POS Server routing.
- The vendor object identity is vendor-side context and must be mapped through AdapterMapping.
- Runtime vendor object identity is represented conceptually as `vendorSystemId + vendorObjectType + vendorObjectRef`.
- Exact database and API representation is deferred to Database Delta and API Contract Pack work.

HCP ParkingLotIndexCode is a vendor-side object reference. It must map through AdapterMapping and must not be used as ExitPass `site_id`.

## 10. Normal Mode Responsibilities

In normal mode:

- Central PMS shall resolve the Site Group/payment scope and resolved Site context.
- Central PMS shall choose the configured VendorSystem and connector instance based on approved mapping.
- The connector shall request or receive live vendor session facts where the vendor capability is confirmed.
- The connector shall request live vendor fee calculation where capability is confirmed and required by Central PMS.
- Vendor PMS/HCP remains normal tariff computation authority.
- Central PMS shall record the accepted payable basis as TariffSnapshot according to platform rules.
- Payment shall proceed through the approved payment flow after Central PMS has the payable basis.
- The connector shall not declare payment finality, fiscal success, or exit eligibility.

## 11. Degraded / Continuity Mode Responsibilities

In degraded or continuity mode, the connector remains informational and evidentiary.

The connector may:

- Report vendor unavailable, connector unavailable, timeout, authentication/authorization failure, stale projection, missing mapping, ambiguous mapping, and insufficient projection indicators.
- Continue projection polling where technically possible and approved.
- Provide last-known projection and freshness context for Central PMS / Continuity evaluation.
- Report vendor acknowledgment backlog or unknown acknowledgment outcomes for reconciliation.

The connector shall not:

- Activate continuity.
- Approve degraded resolve.
- Decide degraded tariff basis.
- Decide if stale projection is acceptable.
- Convert manual release into normal ExitAuthorization.

Degraded resolve belongs to Central PMS under approved Continuity policy. Stale, ambiguous, or insufficient projection shall fail closed or route to approved supervisor/manual review.

## 12. Normal Live Resolve Workflow

1. A channel, terminal, or operator-governed backend flow requests parking session context through Central PMS.
2. Central PMS resolves Site Group/payment scope and the resolved Site.
3. Central PMS identifies the configured VendorSystem, AdapterMapping, and connector instance.
4. The connector queries the Vendor PMS/HCP through the appropriate adapter codebase.
5. The connector normalizes the vendor result into live vendor facts or a normalized exception category.
6. Central PMS evaluates the result, updates the ParkingSession projection where appropriate, and controls the next payment step.

Normal live resolve shall not create payment finality, fiscal issuance, or ExitAuthorization.

## 13. Fee Calculation Workflow

1. Central PMS determines that normal payable basis is required for a resolved session.
2. Central PMS requests fee calculation through the configured connector instance.
3. The connector calls the Vendor PMS/HCP fee calculation capability where confirmed.
4. Vendor PMS/HCP returns a fee result or a vendor-side exception.
5. The connector normalizes the vendor response without altering tariff authority.
6. Central PMS records the accepted payable basis as TariffSnapshot.
7. Payment proceeds through Payment Orchestrator or approved channel workflow.

The connector must not invent tariff amounts from projection or passageway records. In degraded mode, tariff basis belongs to Central PMS under approved Continuity policy using approved tariff configuration or approved continuity basis.

## 14. Projection Polling and Freshness Workflow

Projection polling is an operational visibility workflow. For HikCentral, one-minute passageway polling is the v1.3 business planning baseline; the generic connector design remains vendor-neutral.

Conceptual flow:

1. The connector instance collects vendor-side projection facts through polling or a later-approved topology.
2. The connector applies AdapterMapping and vendor object identity context.
3. The connector reports normalized projection facts, mapping status, and freshness inputs.
4. Central PMS stores or uses projection as operational visibility and controlled degraded support only.
5. Operator Console and Management Dashboard may display projection freshness, stale warnings, connector health, and vendor availability where authorized.

Freshness classifications should include fresh, aging, stale, unavailable, ambiguous, and insufficient at concept level. Exact thresholds, labels, alert rules, and persistence fields remain open.

## 15. Vendor Payment Acknowledgment Workflow

Vendor payment acknowledgment is a downstream vendor notification flow. It is not the source of ExitPass payment finality.

Conceptual flow:

1. Central PMS receives verified provider outcome from Payment Orchestrator or approved payment channel integration.
2. Central PMS records platform payment finality.
3. Central PMS requests fiscal issuance from the resolved Site POS Server where required.
4. POS Server issues the Sales Invoice and returns fiscal identity/status.
5. Central PMS records fiscal issuance reference.
6. Central PMS determines whether vendor acknowledgment should be sent now, retried later, or held according to later Site/vendor policy.
7. The connector attempts acknowledgment through the configured connector instance where vendor capability is confirmed and enabled.
8. The connector reports acknowledged, already paid, failed, timeout, unavailable, unknown, duplicate, or conflicting outcome context to Central PMS and reconciliation consumers.

Whether acknowledgment is synchronous, asynchronous, queued, retried, exit-blocking, or Site/vendor-profile dependent remains open.

## 16. Vendor Unavailable / Timeout / Unknown Outcome Workflow

Vendor unavailable, timeout, and unknown states may occur during live resolve, fee calculation, projection polling, or vendor acknowledgment.

The connector shall:

- Normalize the technical failure into a platform-understandable category.
- Preserve affected VendorSystem and vendor object context where known.
- Avoid converting absence of a vendor response into a successful business result.
- Avoid retrying unknown mutating outcomes without later-approved idempotency posture.
- Surface health and freshness impact to Central PMS and authorized operational views.

Central PMS decides whether the user flow remains pending, fails closed, enters approved degraded evaluation, or routes to support/reconciliation.

## 17. Mapping Ambiguity and Vendor Object Resolution

Mapping ambiguity exists when:

- No AdapterMapping exists for the resolved Site and vendor object.
- More than one mapping candidate exists.
- A vendor object reference cannot be safely associated with the resolved Site.
- Vendor response candidates conflict or are insufficient to identify the platform session.

The connector shall report missing or ambiguous mapping and shall not choose a Site, vendor object, fee, or session by heuristic. Mapping ambiguity affects Site routing, VendorSystem selection, POS Server routing, reporting attribution, and reconciliation; therefore it must fail closed or route to approved review.

## 18. Error Normalization and Result Classification

The connector shall classify vendor outcomes at concept level without defining final DTOs or implementation exceptions.

| Category | Meaning |
| --- | --- |
| Success | Vendor interaction returned a usable live fact or acknowledgment outcome for Central PMS evaluation. |
| Not found / missing | Vendor did not locate the requested session or object. |
| Already paid | Vendor indicates a vendor-side paid state; Central PMS decides platform treatment. |
| Already exited | Vendor indicates vendor-side lifecycle exit; Central PMS decides messaging and reconciliation. |
| Fee unavailable | Vendor cannot calculate fee or fee capability is not available. |
| Unavailable | Vendor dependency, connector instance, network path, or required capability is unavailable. |
| Timeout | The operation exceeded allowed waiting posture and outcome is not known. |
| Unknown | The connector cannot safely determine result state. |
| Duplicate / replay | Repeated request or response requires idempotency posture. |
| Ambiguous | Multiple or conflicting mapping/session candidates exist. |
| Insufficient | Projection or vendor response lacks data required for safe evaluation. |
| Authentication / authorization failure | Vendor credential, permission, signature, or service trust issue prevents safe use. |
| Malformed / unexpected response | Vendor response cannot be safely interpreted. |

## 19. Retry / Idempotency / Duplicate Handling Posture

This design defines posture only.

- Live resolve and fee calculation retries must not extend customer wait indefinitely.
- Projection polling retries should preserve last-known success, failure reason, and freshness context without presenting stale data as current.
- Vendor acknowledgment retries must account for Central PMS payment finality, fiscal prerequisites, and vendor-side idempotency behavior where known.
- Repeated Central PMS requests must not produce duplicate vendor-side payment effects.
- Repeated vendor responses must not duplicate Central PMS payment finality, TariffSnapshot, fiscal issuance, or ExitAuthorization.
- Unknown acknowledgment outcomes require later design for safe retry or status confirmation.

Exact retry counts, backoff rules, queue names, idempotency keys, event payloads, and persistence fields are deferred.

## 20. Security, Credentials, and Trust Boundaries

The connector is a security boundary, not only an adapter.

Security posture:

- Central PMS-to-connector trust shall be separate from connector-to-vendor authentication.
- Vendor credentials shall remain inside the connector boundary.
- Vendor credentials shall not appear in repository files, documentation examples, prompts, logs, screenshots, test fixtures, committed configuration, dashboard exports, or support notes.
- Vendor credentials shall not be returned through user-facing APIs or operational dashboards.
- Request/response logging shall redact secrets, signatures, authorization material, sensitive vendor payloads, and sensitive personal data.
- Production, UAT, and test credentials and network routes shall be segregated.
- High-risk actions such as credential provisioning, rotation, revocation, connector disablement, connector reconfiguration, and mapping changes require elevated permission and audit.

Exact mTLS topology, certificate model, vault product, service identity mechanism, signing implementation, secret naming, and rotation process remain open.

## 21. Observability, Projection Freshness, and Operations

The connector design shall expose operational signals without transferring authority.

Observability domains:

- Connector health.
- Vendor PMS/HCP availability.
- Last successful poll.
- Poll outcome and latency.
- Projection freshness and stale warnings.
- Mapping health and ambiguity.
- Sessions projected and sessions not seen in latest poll.
- Live resolve availability.
- Fee calculation availability.
- Vendor acknowledgment backlog and failure context.
- Authentication/authorization failure.
- Continuity/degraded state context where authorized.

Exact metric names, dashboard widgets, alert thresholds, refresh intervals, and monitoring stack are deferred.

## 22. Operator Console and Management Dashboard Visibility

Operator Console may show connector health, last successful poll, projection freshness, stale warnings, vendor availability, and restriction warnings for authorized operational users. It remains non-payment, non-fiscal, and non-exit.

Management Dashboard may show connector status, Vendor PMS/HCP availability, projection freshness, poll latency, failed poll count, sessions projected, sessions stale, sessions not seen in latest poll, mapping ambiguity, vendor acknowledgment backlog, degraded-watch/degraded-active visibility, and reconciliation backlog where authorized.

All dashboard/reporting surfaces must label projection as operational visibility. Financial and revenue reports must use canonical payment, fiscal, settlement, and reconciliation records.

## 23. Audit, Event, and Reconciliation Posture

Connector audit and event posture shall support reconstruction across:

- Site Group lookup and resolved Site.
- VendorSystem, connector instance, AdapterMapping, and vendor object reference.
- Live resolve request and normalized result.
- Fee calculation request and normalized result.
- TariffSnapshot correlation.
- Projection freshness displayed or used.
- Vendor unavailable, timeout, unknown, ambiguous, stale, and insufficient conditions.
- Vendor acknowledgment request and outcome where enabled.
- Continuity activation, degraded use, manual release, incident tags, and reconciliation tags where applicable.
- Dashboard/report access where connector or projection data is viewed or exported.

Audit records must not include real secrets, derived signatures, raw authorization headers, or unredacted sensitive vendor payloads.

## 24. Failure Modes and Fail-Closed Rules

The connector design shall fail closed or route to approved governance when:

- Vendor PMS/HCP is unavailable.
- Live fee calculation is unavailable.
- Projection is stale, ambiguous, insufficient, or unavailable.
- AdapterMapping is missing or ambiguous.
- Vendor response cannot be safely interpreted.
- Vendor acknowledgment outcome is unknown after a mutating call.
- Vendor authentication or permission fails.
- Connector credentials may be compromised.
- Vendor object identity conflicts with resolved Site context.

Fail-closed behavior means the connector does not invent a session, tariff, payment state, fiscal state, discount approval, or exit eligibility. Central PMS and approved Continuity policy determine next steps.

## 25. Deployment Posture

The baseline deployment posture is one connector instance per VendorSystem/HCP instance unless later design approves another topology.

The design must support:

- Environment-segregated VendorSystems and credentials.
- Clear ownership of connector instance configuration.
- Distinct adapter codebase and connector runtime concepts.
- Vendor-specific connector profiles subordinate to this generic design.
- Health and freshness visibility for operations.
- Safe disablement of mutating vendor actions where not approved.

Push/pull topology, runtime packaging, scheduler ownership, high availability, secret injection, and network route details are deferred.

## 26. Open Questions and Deferred Decisions

| ID | Open question / deferred decision |
| --- | --- |
| VPC-OQ-001 | Does each deployment topology use connector push to Central PMS, Central PMS pull from connector, or a mixed model? |
| VPC-OQ-002 | What exact connector health states, freshness labels, stale thresholds, and alert rules are approved? |
| VPC-OQ-003 | What exact degraded tariff freshness threshold applies before projection can support degraded resolve? |
| VPC-OQ-004 | Is vendor payment acknowledgment synchronous, asynchronous, queued/retried, exit-blocking, or Site/vendor-profile dependent? |
| VPC-OQ-005 | How should unknown vendor acknowledgment outcome be confirmed safely without duplicate vendor-side payment effects? |
| VPC-OQ-006 | What exact normalized vendor error categories should be exposed to Central PMS, Operator Console, Dashboard, and reconciliation? |
| VPC-OQ-007 | What mapping governance workflow resolves missing or ambiguous AdapterMapping issues? |
| VPC-OQ-008 | What exact secret store, mTLS/certificate model, service identity, rotation, and break-glass process will be used? |
| VPC-OQ-009 | What exact audit retention period and redaction rules apply to connector credential lifecycle and sensitive vendor payload access? |
| VPC-OQ-010 | What exact post-restoration reconciliation SLA and closure states apply to connector-origin failures? |

## 27. Requirements Traceability Summary

| Requirement area | Trace source | Design coverage |
| --- | --- | --- |
| Authority preservation | ExitPass BRD v1.3, System Design v1.3, input pack 01 | Sections 5, 6, 10, 11, 24 |
| VendorSystem and AdapterMapping | ExitPass BRD v1.3, System Design v1.3, decision log | Sections 8, 9, 17 |
| Normal live resolve and fee calculation | ExitPass BRD v1.3, input pack 03 | Sections 12, 13 |
| Projection and freshness | ExitPass BRD v1.3, Continuity BRD, Dashboard BRD, input packs 03 and 05 | Sections 14, 21, 22, 24 |
| Vendor acknowledgment | ExitPass BRD v1.3, POS/Invoicing BRD, input pack 03 | Section 15 |
| Security and credentials | System Design v1.3, input pack 04 | Section 20 |
| Observability and reporting | Operator Console BRD, Management Dashboard BRD, input pack 05 | Sections 21, 22 |
| Audit and reconciliation | Continuity BRD, Management Dashboard BRD, input packs 03 and 05 | Section 23 |
| HikCentral specialization | HikCentral API discovery pack | Deferred to HikCentral Connector Profile |

## 28. Appendix A: Glossary

| Term | Definition |
| --- | --- |
| Adapter codebase | Reusable vendor integration implementation for a vendor family. |
| AdapterMapping | Mapping between an ExitPass Site and a vendor-side parking object. |
| Connector instance | Deployed/configured runtime connector for a VendorSystem. |
| Degraded resolve | Controlled Central PMS evaluation that may use projection under approved Continuity policy. |
| ExitAuthorization | Central PMS-issued authorization consumed by gate/exit execution. |
| Parking Session Projection | Operational projection of vendor session facts for visibility and controlled degraded support. |
| Site | Reporting, contract, Vendor PMS mapping, Site POS Server routing, fiscal attribution, and operational boundary. |
| Site Group | Customer lookup/payment scope. |
| TariffSnapshot | Central PMS record of accepted payable basis. |
| VendorSystem | Configured Vendor PMS/HCP instance. |

## 29. Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| APM | AutoPay Machine |
| BCP | Business Continuity Plan |
| BRD | Business Requirements Document |
| HCP | HikCentral Professional |
| PMS | Parking Management System |
| POS | Point of Sale |
| UAT | User Acceptance Testing |

## 30. Appendix C: Diagram Index

| Diagram | File |
| --- | --- |
| VPC-D01 Generic Connector Context | [VPC-D01_Generic_Connector_Context.jpg](diagrams/VPC-D01_Generic_Connector_Context.jpg) / [PUML](diagrams/VPC-D01_Generic_Connector_Context.puml) |
| VPC-D02 VendorSystem / AdapterMapping / Connector Instance Model | [VPC-D02_VendorSystem_AdapterMapping_Connector_Instance_Model.jpg](diagrams/VPC-D02_VendorSystem_AdapterMapping_Connector_Instance_Model.jpg) / [PUML](diagrams/VPC-D02_VendorSystem_AdapterMapping_Connector_Instance_Model.puml) |
| VPC-D03 Normal Live Resolve Sequence | [VPC-D03_Normal_Live_Resolve_Sequence.jpg](diagrams/VPC-D03_Normal_Live_Resolve_Sequence.jpg) / [PUML](diagrams/VPC-D03_Normal_Live_Resolve_Sequence.puml) |
| VPC-D04 Fee Calculation Sequence | [VPC-D04_Fee_Calculation_Sequence.jpg](diagrams/VPC-D04_Fee_Calculation_Sequence.jpg) / [PUML](diagrams/VPC-D04_Fee_Calculation_Sequence.puml) |
| VPC-D05 Projection Polling and Freshness Flow | [VPC-D05_Projection_Polling_and_Freshness_Flow.jpg](diagrams/VPC-D05_Projection_Polling_and_Freshness_Flow.jpg) / [PUML](diagrams/VPC-D05_Projection_Polling_and_Freshness_Flow.puml) |
| VPC-D06 Vendor Payment Acknowledgment Flow | [VPC-D06_Vendor_Payment_Acknowledgment_Flow.jpg](diagrams/VPC-D06_Vendor_Payment_Acknowledgment_Flow.jpg) / [PUML](diagrams/VPC-D06_Vendor_Payment_Acknowledgment_Flow.puml) |
| VPC-D07 Degraded Resolve Handoff to Central PMS / Continuity | [VPC-D07_Degraded_Resolve_Handoff_to_Central_PMS_Continuity.jpg](diagrams/VPC-D07_Degraded_Resolve_Handoff_to_Central_PMS_Continuity.jpg) / [PUML](diagrams/VPC-D07_Degraded_Resolve_Handoff_to_Central_PMS_Continuity.puml) |
| VPC-D08 Connector Error Normalization and Health Reporting Flow | [VPC-D08_Connector_Error_Normalization_and_Health_Reporting_Flow.jpg](diagrams/VPC-D08_Connector_Error_Normalization_and_Health_Reporting_Flow.jpg) / [PUML](diagrams/VPC-D08_Connector_Error_Normalization_and_Health_Reporting_Flow.puml) |

