# ExitPass System Design v1.3

## 1. Document Control

### Document Title

ExitPass System Design v1.3

### Document Purpose

This document defines the core system design for ExitPass v1.3 at architecture level. It translates the approved v1.3 BRD baseline into controlled system boundaries, authority ownership, trust boundaries, workflow choreography, conceptual state ownership, event posture, failure behavior, deployment posture, observability, continuity posture, and operational runbook posture.

ExitPass System Design v1.3 is a controlled successor to ExitPass System Design v1.2. It preserves the v1.2 top-level section family and authority-separation writing style while adding the approved v1.3 scope for centralized WebPay, Site Group and Site modeling, Vendor PMS/HikCentral connector posture, Site-level POS Server fiscal routing, Assisted Payment Terminal modes, Operator Console governance, Management Dashboard and Reporting, and explicit continuity/degraded operation.

### Scope of This Document

This System Design may describe:

- System components and logical runtime responsibilities.
- Authority boundaries and non-authority constraints.
- Integration and trust boundaries.
- Logical record domains and source-of-truth categories.
- Workflow choreography and state ownership.
- Conceptual event families and outbox-style event posture.
- Failure modes, degraded behavior, observability expectations, deployment posture, and operational posture.
- Open design questions and downstream deferrals.

This System Design does not define final endpoint paths, DTOs, request/response schemas, database tables, database columns, indexes, constraints, SQL routines, migrations, queue names, event payload fields, implementation classes, SDK calls, deployment scripts, test cases, runbook procedures, or BIR accreditation package content.

### Authority of This Document

The approved v1.3 BRD baseline is the source of business authority for this System Design. Where this document identifies a downstream technical decision, the item remains deferred until the appropriate API Contract Pack, Database Design, Engineering Pack, Runbook Pack, Test/UAT Pack, BIR/accreditation pack, or companion technical design is approved.

This document must not be used to override approved BRDs. Any contradiction discovered later must be handled through controlled review, not silent correction.

### Intended Audience

The intended audience includes engineering leads, product owners, operations leads, security reviewers, integration teams, fiscal/POS designers, dashboard/reporting designers, and downstream authors of API, database, engineering, runbook, UAT, and accreditation artifacts.

### Design Baseline

The writing-style and top-level outline baseline is `D:\Docs\ExitPass\v1.2\ExitPass System Design v1.2.docx`.

The approved v1.3 BRD baseline is:

- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/assisted-payment-terminal/ExitPass_Assisted_Payment_Terminal_BRD_v1.0.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`
- `docs/v1.3/pos-invoicing/ExitPass_POS_Invoicing_BRD_v1.0.md`
- `docs/v1.3/ExitPass_v1.3_BRD_Approval_Baseline.md`

The specialist input packs used for integration are:

- `docs/v1.3/system-design/input-packs/01_authority_model_review.md`
- `docs/v1.3/system-design/input-packs/02_traceability_map.md`
- `docs/v1.3/system-design/input-packs/03_workflow_and_state_input.md`
- `docs/v1.3/system-design/input-packs/04_security_trust_and_rbac_input.md`
- `docs/v1.3/system-design/input-packs/05_observability_reporting_and_operations_input.md`
- `docs/v1.3/system-design/input-packs/06_diagram_inventory_and_puml_inputs.md`
- `docs/v1.3/system-design/input-packs/07_scope_guard_and_consistency_review.md`

### Document Status

Draft core System Design for v1.3. Downstream API, database, engineering, runbook, test/UAT, and BIR/accreditation details remain outside this document unless explicitly marked as open or deferred.

## 2. System Overview

### Core System Characteristics

ExitPass v1.3 is a controlled extension of the v1.2 parking payment and exit-control platform. The platform preserves the v1.2 principles of authority separation, explicit trust boundaries, canonical payment finality, immutable financial basis, fail-closed control, adapter containment, and end-to-end traceability.

The v1.3 system adds stronger cross-domain structure around:

- Centralized WebPay with site-specific or payment-scope URLs.
- Site Group as customer lookup/payment scope.
- Site as reporting, contract, Vendor PMS mapping, POS Server, fiscal routing, and operational boundary.
- VendorSystem, AdapterMapping, adapter codebase, and deployed connector instance separation.
- Projection and passageway polling as operational visibility and controlled degraded support.
- Platform-wide POS/Invoicing with Site-level POS Server fiscal authority.
- Fiscal issuance before normal ExitAuthorization.
- Assisted Payment Terminal as a payment-capable terminal family with Cashier-Assisted Terminal and Continuity Terminal modes.
- Operator Console as non-payment governance.
- Management Dashboard and Reporting as visibility/reporting only.
- Explicit continuity/degraded operation with audit, reconciliation, and post-restoration review.

### Primary Technical Components

The v1.3 core design recognizes these components and surfaces:

| Component | System design role |
| --- | --- |
| WebPay | Public customer payment surface bound to configured Site Group/Site/payment scope. |
| Central PMS | Platform control authority for payment-linked state, TariffSnapshot recording, payment finality, fiscal reference recording, degraded resolve decisions under approved policy, and ExitAuthorization. |
| Payment Orchestrator | Provider interaction boundary and verified provider outcome reporter. |
| Vendor PMS / HikCentral Professional | Authority for raw parking session lifecycle and normal tariff computation. |
| Vendor PMS connector instance | Runtime integration boundary for a configured VendorSystem. |
| Site POS Server | Resolved Site-level fiscal issuance authority for Sales Invoice and fiscal records. |
| Assisted Payment Terminal | Payment-capable terminal app family for cashier-assisted and continuity modes. |
| Cashier-Assisted Terminal | Assisted terminal mode for normal cashier-assisted payment and statutory validation input capture. |
| Continuity Terminal | Restricted degraded/BCP assisted terminal mode, disabled by default. |
| Operator Console | Internal governance and operations surface; non-payment and non-fiscal. |
| Management Dashboard and Reporting | Visibility/reporting surface with source-of-truth labels and export controls. |
| Gate/exit execution | Site/gate boundary that consumes Central PMS authorization. |
| Audit/Event capability | Durable audit and outbox-style event posture for traceability, read models, and reconciliation. |

### System Outcome

The designed outcome is a controlled payment-to-exit chain:

1. Vendor PMS/HCP owns normal raw session and tariff facts.
2. Central PMS resolves platform scope, records TariffSnapshot/payable basis, and owns payment-linked control state.
3. Payment Orchestrator interacts with payment providers and reports verified provider outcomes.
4. Central PMS records platform payment finality.
5. Resolved Site POS Server issues the Sales Invoice and returns fiscal status/identity.
6. Central PMS records the fiscal issuance reference.
7. Central PMS issues ExitAuthorization only when eligibility rules are satisfied.
8. Gate/exit execution consumes Central PMS authorization.
9. Events, audit records, reports, and reconciliation preserve end-to-end traceability without transferring authority to consumers.

## 3. System Context

### External Actors and Systems

#### Parker

The parker uses WebPay, an AutoPay or payment channel where present, or an assisted terminal workflow. The parker does not interact directly with Central PMS authority records, POS Server fiscal internals, or gate control APIs.

#### Cashier

The cashier uses Cashier-Assisted Terminal mode to support assisted lookup, payment flow, statutory discount input capture, device/shift/Site context, and status display. The cashier does not approve statutory entitlement independently, mutate payable basis directly, declare payment finality, issue fiscal documents, or authorize exit.

#### Operator

The operator uses Operator Console for session lookup, operational review, evidence governance, continuity governance, fiscal exception review, and manual release governance where policy allows. The Operator Console does not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or directly open gates.

#### Supervisor and Compliance Reviewer

Supervisors and compliance reviewers use governed workflows to review evidence, continuity activation, fiscal exceptions, manual release requests, and post-restoration items. Approval authority remains bounded by later approved policy and must be auditable.

#### Management and Reporting Users

Management and reporting users consume dashboard/report/export views. These views must label source category, freshness, and authority level. They do not become payment, fiscal, tariff, discount, continuity activation, reconciliation closure, or exit authority.

#### Vendor PMS / HikCentral Professional

Vendor PMS/HCP remains the authority for raw parking session lifecycle and normal tariff computation. HCP-specific connector behavior, including final object mapping and connector profile details, is deferred to the HikCentral Connector Profile or Vendor PMS Connector System Design.

#### Payment Providers

Payment providers process external payment rails. Their outcomes are evidence to be verified and reported by the Payment Orchestrator. Provider success is not platform payment finality until Central PMS accepts and records it.

#### Site POS Server

The resolved Site POS Server is the fiscal issuance authority for Sales Invoice output and related fiscal records. It does not declare platform payment finality, issue ExitAuthorization, or open gates.

#### Gate and Site Infrastructure

Gate/exit infrastructure executes exit after consuming Central PMS authorization. It must not bypass Central PMS except under a formally approved manual emergency process that is incident-tagged, audit-tagged, reconciliation-tagged, and subject to review.

### System Boundary

ExitPass v1.3 spans centralized platform services, payment channels, assisted terminal surfaces, governance/reporting surfaces, Vendor PMS connector runtime boundaries, Site POS Server boundaries, gate/exit boundaries, audit/event posture, and reconciliation posture.

The system boundary does not include final database design, final API contracts, fiscal accreditation submissions, BIR package content, terminal hardware SDK mechanics, dashboard BI implementation, deployment scripts, or runbook step procedures.

### Site Group and Site Context

Site Group is the customer lookup/payment scope. The default case is one Site Group to one Site. A special case allows one Site Group to contain multiple Sites. Site is the reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary.

A physical parking lot is not automatically an ExitPass Site. Physical lot or cluster modeling remains an open downstream decision. The resolved Site determines Vendor PMS mapping, Site POS Server routing, financial/fiscal attribution, operational reporting, and relevant dashboard scope.

User-facing terminology for Site Group remains open. The System Design preserves Site Group as the architecture term and allows later UX/product language such as payment scope or lookup scope to be resolved downstream.

### Vendor PMS Connector Context

VendorSystem represents a configured Vendor PMS/HCP instance. AdapterMapping represents the mapping between an ExitPass Site and a vendor-side parking object. Adapter codebase is the reusable connector implementation. Connector instance is the deployed and configured runtime connector for a VendorSystem.

HCP ParkingLotIndexCode is a vendor-side identifier. It must not be treated as ExitPass `site_id`. Runtime vendor object identity remains conceptual in this document and should be understood as a compound vendor object reference, not as a final database or API design.

### Context Diagram Narrative

The logical architecture diagram shows Central PMS as the platform control authority, while preserving Vendor PMS/HCP tariff authority, Payment Orchestrator provider boundary, Site POS Server fiscal authority, and gate/exit execution boundary.

Diagram:

- `docs/v1.3/diagrams/system-design/D-01_ExitPass_v1.3_Logical_Architecture.puml`
- `docs/v1.3/diagrams/system-design/D-01_ExitPass_v1.3_Logical_Architecture.jpg`

### Context Integrity Rules

- Site Group and Site must not be collapsed.
- VendorSystem, AdapterMapping, adapter codebase, and connector instance must remain distinct.
- HCP ParkingLotIndexCode must not be reused as ExitPass `site_id`.
- Projection data must remain operational visibility and controlled degraded support only.
- Payment channels and terminals must remain channels under Central PMS payment authority and resolved Site POS Server fiscal authority.
- Fiscal issuance must precede normal ExitAuthorization unless an approved exception policy applies.

## 4. System Architecture

### Architectural Principles

#### Authority Separation

Each authoritative decision must have a single accountable owner. Authority is not transferred by UI success messages, provider callbacks, dashboard views, projection records, or events.

#### Explicit Trust Boundaries

Public WebPay, field terminals, internal consoles, dashboards, payment providers, Vendor PMS/HCP, POS Server, and gate infrastructure each cross different trust boundaries. Boundary controls must be explicit even when implementation details are deferred.

#### Canonical Payment Finality

Payment provider success is evidence. The Payment Orchestrator verifies and reports provider outcomes. Central PMS declares and records platform payment finality.

#### Fiscal Issuance Before Normal Exit

Normal ExitAuthorization requires Central PMS payment finality, successful Site POS Server fiscal issuance, and Central PMS fiscal reference recording. Fiscal issuance failure or timeout creates a controlled exception and blocks normal ExitAuthorization unless an approved exception/manual-release policy applies.

#### Projection Is Not Financial Truth

Projection and passageway/polling data support operational visibility and controlled degraded decisions only. Projection is not normal tariff authority, payment finality, fiscal truth, settlement truth, discount approval, or exit authority.

#### Fail-Closed Control

Unknown payment outcomes, unsafe degraded resolve, stale projection, missing fiscal issuance, ambiguous session resolution, and untrusted gate/device assertions fail closed unless an approved exception path applies.

#### Adapter Containment

Vendor-specific behavior remains behind adapter/connector boundaries. HCP-specific implementation details are deferred to a connector profile and must not leak into canonical ExitPass identity or authority models.

#### End-to-End Traceability

Channel entry, Site Group, resolved Site, vendor reference, TariffSnapshot, discount validation, payment attempt, provider outcome, payment finality, fiscal reference, ExitAuthorization, gate outcome, continuity tags, manual release tags, export access, and reconciliation status must be reconstructable.

### Canonical Runtime Services and Modules

#### WebPay

WebPay is a centralized customer payment surface using configured site-specific or payment-scope URLs. It initiates or presents payment flows and displays backend status. It does not declare payment finality, issue fiscal documents, or authorize exit. The URL slug registry and exact public URL model remain open.

#### Central PMS

Central PMS is the core platform control authority. It owns payment-linked platform control state, TariffSnapshot recording, payable-basis effect after approved discount validation, PaymentAttempt state, PaymentConfirmation/platform payment finality, fiscal issuance reference recording, degraded resolve decision under approved policy, ExitAuthorization, and control-state audit.

Central PMS does not replace Vendor PMS/HCP normal raw session lifecycle or normal tariff authority, and it does not replace POS Server fiscal issuance authority.

#### Payment Orchestrator

Payment Orchestrator interacts with payment providers, handles provider-facing flow coordination, verifies provider outcomes, and reports verified outcomes to Central PMS. It does not declare platform payment finality, issue fiscal documents, issue ExitAuthorization, or open gates. Callback, retry, idempotency, and provider-specific details are deferred.

#### Vendor PMS Connector

The connector boundary integrates with Vendor PMS/HCP through a configured VendorSystem and AdapterMapping. In normal mode, Vendor PMS/HCP remains raw session lifecycle and tariff authority. Passageway/polling projection, including the one-minute business baseline where approved, supports operational visibility and controlled degraded support. Push/pull topology, connector health model, and HCP-specific details remain deferred.

#### Site POS Server

The Site POS Server is the fiscal issuance authority for the resolved Site. It issues Sales Invoices and owns fiscal treatment, fiscal records, fiscal reports, counters, electronic journal, POSLog, export, retention, and fiscal audit posture at the fiscal-authority level. This System Design does not define POS Server deployment, registration, numbering/counter mechanics, POSLog mapping, BIR/accreditation identity assignment, offline fiscal issuance, or service packaging.

#### Assisted Payment Terminal

Assisted Payment Terminal is a separate payment-capable terminal app family. It supports Cashier-Assisted Terminal mode for normal assisted payment and Continuity Terminal mode for restricted degraded/BCP operation. Android-first hardened terminal posture is the preferred field-terminal reference posture. Final Android shell, WebView, PWA, native bridge, hardware integration, local storage, printer, and key storage details are deferred to Assisted Payment Terminal System Design.

#### Operator Console

Operator Console is an internal non-payment governance module. It supports session lookup, statutory discount evidence review, continuity activation/deactivation review, connector health/projection freshness visibility, fiscal exception review, manual release governance, audit, RBAC, evidence controls, and operational context. It must not collect payment, issue Sales Invoices, declare payment finality, issue ExitAuthorization, or directly open gates.

#### Management Dashboard and Reporting

Management Dashboard and Reporting is visibility/reporting only. It provides operational, financial, fiscal, compliance, reconciliation, executive, exception, statutory discount, coupon, continuity, connector health, projection freshness, and export views. It must label source-of-truth, freshness, and authority level. Reporting store, BI tooling, dashboard implementation, aggregation design, and exact refresh intervals are deferred.

#### Audit and Event Capability

Audit and event capability provides durable event/outbox-style posture and non-repudiation support. Events communicate completed facts and operational state changes. Events do not transfer authority to consumers.

### Architectural Layers

#### Layer A - Public and Channel Interaction Layer

Includes WebPay, customer payment channels, Assisted Payment Terminal surfaces, and future payment-capable channels. These surfaces submit requests and display backend state; they do not own finality, fiscal issuance, discount policy, or exit authorization.

#### Layer B - Trusted Core Domain Layer

Includes Central PMS authority workflows, discount workflow, payment-linked control state, degraded resolve policy decisions, fiscal reference recording, ExitAuthorization issuance, audit classification, and reconciliation coordination.

#### Layer C - Payment and Fiscal Boundary Layer

Includes Payment Orchestrator/provider boundary and Site POS Server fiscal boundary. Payment Orchestrator reports verified provider outcomes. POS Server issues fiscal documents. Neither boundary owns platform finality or exit authorization.

#### Layer D - Integration Boundary Layer

Includes Vendor PMS/HCP connector instances, Site/gate integration, vendor acknowledgment, projection/freshness ingestion, and external dependency monitoring.

#### Layer E - Governance, Reporting, and Operations Layer

Includes Operator Console, Management Dashboard and Reporting, operational visibility, export governance, continuity governance, manual release governance, and post-restoration review visibility.

#### Layer F - Platform Substrate

Includes transactional persistence, durable event/outbox posture, audit storage, observability pipeline, service identity controls, and deployment substrate. This document does not specify final platform tooling or database objects.

### Allowed Service Interaction Paths

#### Public Entry Paths

WebPay and other public channels must enter through approved platform boundaries and scope resolution. Public success screens must reflect backend status and must not create platform finality by themselves.

#### Terminal Paths

Assisted terminals must submit cashier/device/shift/Site context to backend workflows. Terminals may capture statutory discount inputs and display payment/fiscal/exit status, but they must rely on Central PMS, Discount workflow, Payment Orchestrator, and Site POS Server outcomes.

#### Governance Paths

Operator Console may view and govern approved workflows. It may capture review and approval context where policy allows. It must not become a payment, fiscal, or gate execution surface.

#### Reporting Paths

Dashboard/reporting paths consume authorized source records and read models. They must preserve source labeling and must not mutate authority records unless a later approved policy explicitly assigns a limited workflow action.

#### Site Boundary Paths

Central PMS interacts with Vendor PMS connectors, resolved Site POS Server, and gate/exit integration through controlled boundaries. Site identity and resolved Site routing determine Vendor PMS mapping, POS Server routing, fiscal attribution, and operational reporting.

### Canonical Authority Matrix

| Authority area | Owner | Non-authority warning |
| --- | --- | --- |
| Raw parking session lifecycle | Vendor PMS/HCP | Projection and Central PMS control state do not replace raw lifecycle authority in normal mode. |
| Normal tariff computation | Vendor PMS/HCP | Central PMS records TariffSnapshot; it does not become normal tariff authority. |
| Site Group/Site scope resolution | Central PMS using configured scope | Public URL model remains open. |
| Payment provider interaction | Payment Orchestrator | Provider success is not platform finality. |
| Platform payment finality | Central PMS | Payment Orchestrator, WebPay, terminals, and POS Server do not declare finality. |
| Fiscal issuance | Resolved Site POS Server | POS Server does not issue ExitAuthorization. |
| Fiscal reference recording | Central PMS | Recording a reference does not make Central PMS fiscal issuer. |
| ExitAuthorization | Central PMS | Gate, POS Server, WebPay, terminals, console, and dashboard do not issue it. |
| Gate/exit execution | Gate/exit infrastructure consuming Central PMS authorization | Gate must not bypass Central PMS. |
| Statutory discount policy resolution | Central PMS / Discount workflow | Cashiers capture inputs; Operator Console reviews evidence. |
| Continuity activation/governance | Approved continuity/governance workflow | Exact authority remains open; no silent fallback. |
| Reporting visibility | Management Dashboard and Reporting | Reporting is not financial/fiscal/payment/exit authority. |

Diagram:

- `docs/v1.3/diagrams/system-design/D-02_Authority_Boundary_Model.puml`
- `docs/v1.3/diagrams/system-design/D-02_Authority_Boundary_Model.jpg`

### Canonical Domain Objects and Logical Records

This System Design names logical records for ownership discussion only. It does not define database tables, columns, constraints, or schemas.

- Site Group / Site / VendorSystem / AdapterMapping / connector instance.
- Parking session projection and projection freshness.
- TariffSnapshot and payable basis.
- Discount validation and evidence references.
- PaymentAttempt, provider outcome, and PaymentConfirmation.
- Fiscal issuance request/result/exception and fiscal issuance reference.
- POS fiscal records as resolved Site POS Server authority.
- ExitAuthorization and gate outcome.
- Continuity incident/state and Continuity Terminal activity.
- Manual release governance records.
- Audit/event records and reporting/export access records.
- Reconciliation and post-restoration review records.

Diagram:

- `docs/v1.3/diagrams/system-design/D-03_Site_Group_Site_VendorSystem_Connector_POS_Topology.puml`
- `docs/v1.3/diagrams/system-design/D-03_Site_Group_Site_VendorSystem_Connector_POS_Topology.jpg`

## 5. Trust Boundaries

### Trust Boundary Model

The v1.3 trust boundary model extends the v1.2 boundary posture by adding explicit terminal, POS Server, Operator Console, dashboard/reporting, connector projection, and continuity boundaries.

### Public Internet Zone

WebPay operates in the public customer-facing zone. It must bind user interactions to configured payment scope and backend state. Anti-enumeration, customer session policy, QR/token behavior, and exact URL slug registry remain deferred.

### Platform Edge Zone

The platform edge mediates public channels, terminal channels, console sessions, dashboard/reporting access, and service-to-service ingress. Exact endpoint paths, authentication mechanisms, OAuth scopes, certificate model, and DTOs are deferred.

### Trusted Core Platform Zone

Central PMS and related core workflows operate in the trusted core platform zone. This zone owns payment-linked control state, platform finality, fiscal reference recording, degraded decisioning under policy, ExitAuthorization, audit classification, and reconciliation coordination.

### External Payment Provider Zone

Payment providers are external. Payment Orchestrator isolates provider-specific behavior and reports verified outcomes. Unknown outcomes remain pending and fail closed for exit until Central PMS records finality.

### Vendor PMS Integration Zone

Vendor PMS/HCP and connector instances operate across a dedicated integration boundary. Vendor credentials, object mappings, polling/passageway behavior, health signals, and HCP-specific object semantics must remain contained. HCP ParkingLotIndexCode remains a vendor-side identifier.

### Site POS Server Fiscal Zone

The resolved Site POS Server is a fiscal authority boundary. Central PMS sends fiscal issuance work at system level and records returned fiscal identity/status. POS Server trust, registration, deployment, BIR identity assignment, fiscal counters, offline behavior, and recovery mechanics are downstream.

### Assisted Terminal and Device Zone

Assisted Payment Terminal devices require cashier/device/shift/Site context, hardened posture, and device trust. Android-first is the preferred field-terminal reference posture. Terminal certificate, key storage, kiosk lockdown, peripheral integration, and fixed-station compatibility remain deferred.

### Internal Governance Zone

Operator Console is trusted internal governance access with RBAC, device trust, shift/context controls, evidence controls, and audit. It must remain non-payment, non-fiscal, and non-gate.

### Reporting and Export Zone

Management Dashboard and Reporting requires scoped RBAC, source/freshness labels, export controls, evidence/privacy protection, and access audit. Reports may combine operational and financial facts only when source categories remain explicit.

### Site Infrastructure Zone

Gate/exit equipment consumes Central PMS authorization. Site infrastructure failures do not alter payment or fiscal truth. Manual emergency processes remain separately approved, auditable, and reconciled.

### Trust Boundary Enforcement

Boundary enforcement must include authentication, authorization, audit logging, replay safety, idempotency posture, evidence privacy, service identity separation, and fail-closed behavior. This document does not finalize certificate topology, secrets storage, OAuth scopes, device key storage, QR token design, or exact permission matrices.

### Non-Negotiable Boundary Invariants

- WebPay does not declare payment finality.
- Payment Orchestrator does not declare platform payment finality.
- Assisted Payment Terminal does not issue Sales Invoices independently or issue ExitAuthorization.
- Operator Console does not collect payment, issue fiscal documents, mark payments paid, issue ExitAuthorization, or directly open gates.
- Management Dashboard and Reporting does not become workflow authority.
- POS Server does not issue ExitAuthorization.
- Gate/exit execution must consume Central PMS authorization.
- Projection data must not be treated as financial truth.

## 6. Core Workflows

### Resolve Scope and Parking Session

The workflow starts from WebPay, another payment channel, or an assisted terminal. The channel supplies configured payment-scope context. Central PMS resolves Site Group and, where required, the resolved Site. Vendor PMS/HCP remains the source for live raw session and normal tariff facts. Projection may assist visibility and controlled degraded handling but does not replace live authority in normal mode.

### Retrieve Tariff Snapshot and Payable Basis

In normal mode, Central PMS obtains the vendor-authoritative tariff result from Vendor PMS/HCP through the approved connector boundary. Central PMS records TariffSnapshot/payable basis as the immutable platform basis for downstream payment control. If statutory discount validation changes payable basis, Central PMS / Discount workflow owns policy resolution, validation persistence, and payable-basis update.

### Statutory Discount and VAT Privilege Handling

Cashier-Assisted Terminal may capture statutory discount validation inputs, cashier attestation, device/shift context, and evidence references where required. Operator Console may review or govern evidence. Central PMS / Discount workflow owns policy resolution, validation persistence, and payable-basis update.

Senior Citizen and PWD are immediate workflow categories. NAAC and Solo Parent are future-supported categories. Diplomat VAT Privilege / VAT Exemption must be treated as VAT privilege/exemption, not ordinary commercial discount. Exact tax/VAT treatment, evidence, wording, retention, reporting, local ordinance handling, and third-party verification integration remain open.

### Initiate Payment Attempt

Central PMS creates or controls the payment attempt state at platform level. WebPay or Assisted Payment Terminal may initiate the customer/cashier-facing payment flow. Payment Orchestrator interacts with providers and returns verified provider outcomes. Duplicate requests and callbacks must be handled through idempotency/replay-safety posture, with implementation mechanics deferred.

### Finalize Payment

Payment provider outcome is evidence, not platform finality. Payment Orchestrator reports verified provider outcome to Central PMS. Central PMS validates platform eligibility and records PaymentConfirmation/payment finality. Unknown provider outcomes remain pending and fail closed for exit.

### Issue Fiscal Document

After Central PMS records payment finality, Central PMS routes fiscal issuance to the resolved Site POS Server. The Site POS Server issues the Sales Invoice and returns fiscal document identity/status. Central PMS records the fiscal issuance reference. Sales Invoice is the primary parking fiscal output for v1.3.

### Issue ExitAuthorization

Central PMS issues ExitAuthorization only after payment finality, successful fiscal issuance, and fiscal reference recording, unless an approved exception/manual-release policy applies. ExitAuthorization is not issued by WebPay, Payment Orchestrator, POS Server, Assisted Payment Terminal, Operator Console, Management Dashboard, or gate infrastructure.

### Consume ExitAuthorization

Gate/exit infrastructure consumes Central PMS authorization and reports the outcome. Gate failure, vendor acknowledgment failure, or exit device uncertainty must be visible, auditable, and reconciled without mutating payment or fiscal truth.

### Vendor Payment Acknowledgment

Vendor paid-state acknowledgment is downstream of Central PMS payment finality and fiscal handling. Exact synchronous or queued behavior, retry policy, and whether acknowledgment failure blocks exit remain open. The design posture is auditable retry/escalation/reconciliation without transferring authority to Vendor PMS or connector read models.

### Assisted Payment Terminal Cashier-Assisted Flow

Cashier-Assisted Terminal supports assisted lookup, cashier/device/shift/Site context, discount input capture, payment flow surface, backend status display, and receipt/fiscal status display where applicable. It remains a terminal surface under Central PMS payment authority and resolved Site POS Server fiscal authority.

### Continuity Terminal Flow

Continuity Terminal is disabled by default. It may operate only under approved degraded/BCP controls. It must carry activation scope, affected dependency, incident or BCP reference, allowed workflow scope, audit tag, and reconciliation tag. Offline behavior, degraded tariff thresholds, and exact activation authority remain open.

### Manual Release Governance Flow

Manual release is a last resort. Where allowed by policy, it must be supervisor-approved where required, incident-tagged, audit-tagged, reconciliation-tagged, reason-coded, attributable, and subject to post-review. Manual release must not silently become payment finality, fiscal truth, or normal ExitAuthorization.

### Reconciliation and Post-Restoration Review

Reconciliation compares payment/provider facts, POS fiscal facts, fiscal references, vendor acknowledgment facts, gate/manual release facts, continuity-origin facts, and settlement facts. Projection-only records may support investigation but must not close financial or fiscal reconciliation by themselves.

Diagrams:

- `docs/v1.3/diagrams/system-design/D-04_Normal_Payment_to_Exit_Sequence.puml`
- `docs/v1.3/diagrams/system-design/D-04_Normal_Payment_to_Exit_Sequence.jpg`
- `docs/v1.3/diagrams/system-design/D-05_Payment_Finality_Fiscal_Issuance_ExitAuthorization_Sequence.puml`
- `docs/v1.3/diagrams/system-design/D-05_Payment_Finality_Fiscal_Issuance_ExitAuthorization_Sequence.jpg`

### Workflow Consistency Rules

- Fiscal issuance failure or timeout creates a controlled exception and blocks normal ExitAuthorization.
- Payment uncertainty remains pending until verified and accepted by Central PMS.
- Continuity is explicit controlled degraded operation, not silent fallback.
- Operator Console and Management Dashboard must remain governance and visibility surfaces.
- Events and dashboards do not change the authority owner of a workflow step.

## 7. Event Architecture

### Eventing Model

ExitPass v1.3 retains the v1.2 durable event/outbox-style posture. Material authoritative transitions and operational state changes should produce durable events for audit, read-model construction, observability, reporting, and reconciliation.

Events communicate completed facts and operational state changes. Events do not transfer authority to consumers. Each event family should have one authoritative producer. Consumers may build read models but must not become authority owners.

### Conceptual Event Families

The following event families are conceptual. This document does not define event payload fields, queue/topic names, schemas, exchange topology, delivery implementation, or serialization details.

| Event family | Authoritative producer posture | Example consumers |
| --- | --- | --- |
| Session/scope resolution | Central PMS or scope workflow | WebPay status, terminal status, audit, dashboard read models. |
| Tariff/payable-basis snapshot | Central PMS | Payment flow, fiscal flow, audit, reconciliation. |
| Statutory discount validation | Central PMS / Discount workflow | Terminal status, Operator Console, fiscal flow, reporting. |
| Payment attempt | Central PMS / payment workflow | Payment Orchestrator, dashboard, audit. |
| Provider outcome | Payment Orchestrator as verifier/reporter | Central PMS, reconciliation, audit. |
| Payment finality | Central PMS | POS fiscal flow, ExitAuthorization workflow, reporting. |
| Fiscal issuance request/result/exception | Central PMS and POS Server boundary by owned fact | Central PMS, Operator Console, dashboard, reconciliation. |
| Fiscal reference recording | Central PMS | Reporting, reconciliation, audit. |
| ExitAuthorization issue/consume | Central PMS for issue; gate integration for consume/outcome | Gate integration, dashboard, reconciliation. |
| Gate outcome | Gate integration boundary | Central PMS, audit, operations. |
| Connector health/projection freshness | Connector/Central PMS integration boundary | Operator Console, dashboard, continuity workflow. |
| Continuity activation/deactivation | Approved continuity workflow | Terminal, Operator Console, dashboard, audit, reconciliation. |
| Manual release governance | Operator Console/governance workflow | Audit, reconciliation, dashboard. |
| Vendor acknowledgment | Vendor connector/integration workflow | Reconciliation, operations, audit. |
| Dashboard/report/export audit | Reporting/access workflow | Audit, compliance review. |
| Reconciliation lifecycle | Reconciliation workflow | Dashboard, audit, operations review. |

### Producer and Consumer Responsibilities

Producers must emit events only for facts they own. Consumers must treat events as evidence for read models, alerting, reporting, or reconciliation. Consumers must tolerate replay, duplicate delivery, and stale read-models conceptually. External provider, vendor, or POS payloads should be normalized by the responsible boundary owner before entering canonical platform eventing.

### Event Flow Architecture Diagram

Diagram:

- `docs/v1.3/diagrams/system-design/D-11_Audit_Event_Outbox_Conceptual_Flow.puml`
- `docs/v1.3/diagrams/system-design/D-11_Audit_Event_Outbox_Conceptual_Flow.jpg`

### Event Architecture Conformance Rules

- No event payload schema is finalized in this document.
- No queue, exchange, or topic name is finalized in this document.
- Eventing supports traceability and recovery; it does not create hidden authority transfer.
- Reporting and reconciliation read models must preserve source labels.

## 8. State Machines

### General State Machine Principles

Each conceptual state transition must have exactly one owning authority or workflow. State names in this section are conceptual. They are not database enum definitions.

State transitions should be idempotent, auditable, correlated to the relevant Site Group, resolved Site, channel, actor/device where applicable, and durable enough to support reconstruction and reconciliation.

### Vendor Raw Session State

Vendor PMS/HCP owns raw parking session lifecycle in normal mode. Central PMS may project or reference vendor session state for platform workflows, but projection is not raw-session authority.

### Central PMS Payment-Linked Control State

Central PMS owns platform control state spanning scope resolution, TariffSnapshot, PaymentAttempt, PaymentConfirmation, fiscal reference recording, ExitAuthorization, degraded resolve decisions under policy, and exception control.

### Projection and Freshness State

Projection/freshness state reflects connector health, last known vendor data, freshness classification, stale warnings, and degraded-use eligibility. It supports operational visibility and controlled degraded decisions only. Exact freshness thresholds, labels, and health-state implementation remain open.

Diagram:

- `docs/v1.3/diagrams/system-design/D-06_Vendor_PMS_Connector_Projection_Freshness_Flow.puml`
- `docs/v1.3/diagrams/system-design/D-06_Vendor_PMS_Connector_Projection_Freshness_Flow.jpg`

### Tariff and Payable-Basis State

TariffSnapshot/payable-basis state is owned by Central PMS. In normal mode, it records vendor-authoritative tariff results. After approved statutory validation, Central PMS / Discount workflow owns payable-basis refresh. Degraded tariff basis and owner remain open.

### Payment Attempt and Finality State

PaymentAttempt state is controlled by Central PMS. Provider outcomes are verified and reported by Payment Orchestrator. PaymentConfirmation/platform finality is recorded by Central PMS only. Unknown provider outcomes remain pending and fail closed for exit.

### Fiscal Issuance State

Fiscal issuance state crosses Central PMS and Site POS Server authority boundaries. Central PMS requests issuance and records fiscal reference; POS Server owns fiscal issuance. Conceptual states include requested, issued, failed, timed out, exception under review, and recovered/resolved. These are not final database states.

### ExitAuthorization State

Central PMS owns ExitAuthorization issue state. Gate/exit integration consumes authorization and reports outcome. Conceptual states include eligible for issuance, issued, consumed, expired, failed consumption, and exception/manual release related. POS Server and gate infrastructure do not issue ExitAuthorization.

### Vendor Acknowledgment State

Vendor acknowledgment after payment/fiscal progression is an integration/reconciliation state. Exact retry, escalation, and exit-block policy remain open. The state must be auditable and reconciliation-tagged when unresolved.

### Continuity and Degraded Mode State

Conceptual continuity states include normal, degraded-watch, degraded-active, Continuity-terminal-active, restoration-in-progress, post-restoration-review, and closed/reconciled. These states express posture only and do not define database enum values. Continuity must be explicit, scoped, audited, reconciliation-tagged, and time-bound.

Diagram:

- `docs/v1.3/diagrams/system-design/D-07_Degraded_Resolve_and_Continuity_Sequence.puml`
- `docs/v1.3/diagrams/system-design/D-07_Degraded_Resolve_and_Continuity_Sequence.jpg`

### Manual Release Review State

Manual release review state is governed by approved policy. It must preserve request, approval where required, reason, incident, audit tag, reconciliation tag, attribution, execution boundary, and post-review state. It must not become normal ExitAuthorization or payment finality.

### Reconciliation and Post-Restoration State

Reconciliation/post-restoration state tracks open, matched, unmatched, exception, reviewed, escalated, and closed/reconciled concepts at architecture level. Exact SLA, closure authority, and labels remain open.

### Cross-State Machine Consistency Rules

- Payment finality precedes normal fiscal issuance request.
- Fiscal issuance success and fiscal reference recording precede normal ExitAuthorization.
- Projection freshness cannot move payment, fiscal, or exit states to success.
- Manual release does not close payment, fiscal, or reconciliation states by itself.
- Continuity-origin records remain identifiable through reconciliation.

## 9. Data Architecture

### Data Architecture Posture

This section describes logical record domains and source-of-truth boundaries only. It does not define tables, columns, indexes, constraints, SQL routines, functions, triggers, migrations, reporting stores, or data marts.

### System of Record Boundaries

| Record domain | Source-of-truth boundary |
| --- | --- |
| Raw parking session lifecycle | Vendor PMS/HCP in normal mode. |
| Site Group/Site configuration | Central platform configuration domain; exact schema deferred. |
| VendorSystem/AdapterMapping/connector instance | Integration configuration domain; exact database/API shape deferred. |
| Projection/freshness | Operational visibility and degraded support domain. |
| TariffSnapshot/payable basis | Central PMS. |
| PaymentAttempt/provider outcome/PaymentConfirmation | Central PMS for attempt/finality; Payment Orchestrator for verified outcome reporting. |
| Fiscal issuance and Sales Invoice | Resolved Site POS Server. |
| Fiscal issuance reference | Central PMS. |
| ExitAuthorization | Central PMS. |
| Discount validation/evidence references | Central PMS / Discount workflow with evidence controls. |
| Continuity incident/state | Continuity/governance workflow with Central PMS coordination. |
| Manual release governance | Operator Console/governance workflow with audit/reconciliation tags. |
| Audit/event records | Audit/Event capability and owning workflow facts. |
| Reporting/read models | Reporting consumers with source/freshness/authority labels. |
| Reconciliation records | Reconciliation workflow comparing payment, fiscal, vendor, gate, continuity, and manual release facts. |

### Source-of-Truth Classification

Financial and revenue reports must use canonical payment, provider, fiscal, fiscal reference, settlement, and reconciliation records. Projection-only data is excluded from financial truth except as separately labeled operational context.

Fiscal reporting must reconcile POS Server fiscal records with Central PMS fiscal references where applicable. Management summaries may combine categories only with explicit labels.

### Identifier and Mapping Posture

Site Group, Site, VendorSystem, AdapterMapping, connector instance, vendor object references, payment attempts, provider references, fiscal references, ExitAuthorization, gate outcome, and reconciliation identifiers must support end-to-end traceability. This document does not define final identifier formats, schema fields, or API parameters.

### Data Retention and Auditability

Sensitive evidence, discount validation, dashboard exports, fiscal records, manual release records, continuity-origin records, and reconciliation records require privacy-aware retention, access controls, audit trails, and non-repudiation posture. Exact retention periods, evidence redaction rules, and jurisdiction-specific policy remain open.

### Data Architecture Alignment Rules

- Projection is not financial truth.
- POS fiscal records are fiscal-authority records; Central PMS fiscal references are platform linkage records.
- Dashboard/reporting read models must not become source-of-truth records.
- Manual release and continuity records must remain distinguishable from normal payment-to-exit records.
- Final database deltas are deferred to Database Design / Database Delta.

## 10. API Architecture

### API Architecture Posture

This section describes boundary posture and service ownership only. It does not define endpoint paths, HTTP verbs, route hierarchies, DTOs, request/response schemas, status codes, error codes, idempotency key formats, or exact contracts.

### Public WebPay Ingress

Public WebPay ingress supports payment-scope resolution, session lookup, tariff/payable-basis presentation, payment initiation, and status display through approved backend workflows. Exact URL slug registry, anti-enumeration controls, and customer session policy remain open.

### Terminal / Backend Boundary

Assisted Payment Terminal communicates with backend workflows for lookup, cashier/device/shift/Site context, discount input capture, payment initiation/status, fiscal status display, continuity-mode status, and exit status display. Terminal-local actions do not declare finality, fiscal issuance, discount approval, or ExitAuthorization.

### Operator Console / Backend Boundary

Operator Console interacts with backend governance workflows for read-only session context, evidence review, continuity review, fiscal exception review, manual release governance, connector health visibility, audit, and status display. It remains non-payment and non-gate.

### Dashboard / Reporting / Backend Boundary

Dashboard and reporting boundaries provide scope-aware read/report/export access with source-of-truth labels, freshness labels, RBAC, privacy controls, and export audit. They do not mutate authoritative payment, fiscal, discount, continuity, or exit records.

### Central PMS / Vendor Connector Boundary

Central PMS uses connector boundaries for normal vendor session/tariff interactions, projection/freshness ingestion, vendor acknowledgments, and health visibility. HCP-specific operation names, payloads, and connector topology are deferred.

### Central PMS / Payment Orchestrator Boundary

Central PMS initiates or controls payment attempt state and receives verified provider outcomes from Payment Orchestrator. Provider callback/retry/idempotency details and contracts are deferred.

### Central PMS / POS Server Boundary

Central PMS routes fiscal issuance to the resolved Site POS Server after payment finality and records returned fiscal status/identity. Endpoint details, DTOs, error structures, POSLog schema mapping, and POS Server packaging are deferred.

### Central PMS / Gate Boundary

Gate/exit boundary validates and consumes Central PMS authorization and reports outcome. Gate API details, token format, replay controls, and device SDK details are deferred.

### Service-to-Service Authentication and Authorization Posture

Service boundaries require authenticated service identity, authorization by role/scope/action, replay safety, audit correlation, and least privilege. Exact certificate model, mTLS topology, OAuth scopes, secrets implementation, and service mesh/deployment mechanics are deferred.

## 11. Security Architecture

### Security Objectives

The v1.3 security architecture protects payment finality, fiscal issuance separation, ExitAuthorization, statutory discount evidence, continuity activation, manual release governance, dashboard/export access, device trust, service identity, and auditability.

### Public Access and WebPay Scope Binding

WebPay public access must bind customer actions to configured payment scope and backend state. Public URL slug registry, whether slugs resolve to Site Group, Site, or both, anti-enumeration controls, QR/token security, and customer session policy remain open.

### Human Roles

Roles include parker/customer, cashier, operator, supervisor, compliance auditor, finance/revenue assurance user, technical support, administrator, management viewer, Site manager, and read-only client/lessor viewer. Exact permission matrices and role-to-action mappings remain deferred.

### Service Identities and Non-Human Actors

Central PMS, Payment Orchestrator, connector instances, Site POS Server, audit/event capability, reporting services, terminal devices, and gate devices require service or device identity posture. Exact certificate, key storage, rotation, revocation, and break-glass controls remain deferred.

### Device and Terminal Trust

Assisted Payment Terminal requires terminal identity, Site/Site Group assignment, cashier/shift context, hardened posture, and evidence/privacy controls. Android-first hardened terminal posture is the preferred field-terminal reference posture. Terminal key storage, kiosk lockdown, native bridge, scanner/camera/printer/cash drawer integration, and local storage are deferred.

### Operator Console Trusted Internal Access

Operator Console requires trusted internal access, RBAC, device trust, shift/context controls, evidence access controls, and audit. Exact trust mechanism, including mTLS, browser key binding, or another control, remains open.

### POS Server Trust Boundary

Site POS Server must be trusted as fiscal issuance authority for the resolved Site and must be protected from payment-finality or ExitAuthorization leakage. POS Server registration, deployment, BIR identity assignment, counter integrity, and recovery trust model remain downstream.

### Payment Orchestrator and Provider Trust Boundary

Payment Orchestrator isolates provider-specific credentials and verification. Provider callbacks and statuses must be verified before being reported. Central PMS alone records platform finality.

### Vendor PMS / HikCentral Credential Boundary

Vendor PMS/HCP credentials, connector configuration, AdapterMapping, runtime vendor object references, and health/freshness data require isolation. HCP-specific connector profile details remain deferred.

### Gate and Device Trust

Gate devices must consume Central PMS authorization and report outcomes. Gate device failure or local assertion must not alter payment or fiscal truth. Exact token, certificate, SDK, and replay mechanisms remain deferred.

### Evidence and Privacy Controls

Statutory discount evidence, identity evidence, VAT privilege evidence, dashboard exports, audit reports, and compliance views require minimization, redaction/masking where applicable, access audit, retention policy, and elevated permissions. Exact retention, wording, export format, and evidence storage implementation remain open.

### Secrets and Credential Posture

Secrets must be segregated by boundary, least-privilege, rotated, revocable, auditable, and protected against logging/export leakage. Exact secrets storage and operational break-glass implementation are deferred.

### Audit and Non-Repudiation

Audit must correlate actor, device, shift, Site Group, resolved Site, channel, Vendor PMS reference, TariffSnapshot, discount validation, payment attempt, provider outcome, payment finality, fiscal reference, ExitAuthorization, gate outcome, continuity tag, manual release tag, report/export access, and reconciliation status where applicable.

### Security Alignment Rules

- Security controls must reinforce authority separation.
- Field terminal trust must not imply fiscal or finality authority.
- Operator Console trust must not imply payment or gate authority.
- Dashboard export permission must not imply workflow authority.
- Fail-closed posture applies when trust, identity, freshness, or payment/fiscal status is uncertain.

Diagrams:

- `docs/v1.3/diagrams/system-design/D-08_Assisted_Payment_Terminal_Context_and_Modes.puml`
- `docs/v1.3/diagrams/system-design/D-08_Assisted_Payment_Terminal_Context_and_Modes.jpg`
- `docs/v1.3/diagrams/system-design/D-09_Operator_Console_Governance_Boundary.puml`
- `docs/v1.3/diagrams/system-design/D-09_Operator_Console_Governance_Boundary.jpg`

## 12. Failure Mode Architecture

### Failure Design Principles

ExitPass v1.3 preserves the v1.2 failure posture: fail closed by default, isolate external dependency failures, make degraded operation explicit, record audit/reconciliation tags, and avoid silent bypass.

### Failure Breakpoints

| Failure mode | System posture |
| --- | --- |
| No or ambiguous session | Block normal payment/exit until resolved or governed review applies. |
| Vendor PMS unavailable | Use explicit degraded decisioning only where approved; otherwise fail closed. |
| Stale projection | Warn, block degraded use if outside policy, and avoid financial-truth use. |
| Degraded resolve unsafe | Fail closed or route to approved supervisor/governance path. |
| Payment provider uncertainty | Keep pending; do not declare finality or authorize exit. |
| Duplicate provider callback | Treat as idempotency/replay-safety concern; do not duplicate finality. |
| Fiscal issuance failure/timeout | Create controlled exception; block normal ExitAuthorization. |
| POS Server unavailable/recovery risk | Surface fiscal exception and recovery risk; do not make Central PMS fiscal authority. |
| Vendor acknowledgment failure | Retry/escalate/reconcile according to later design; keep auditable. |
| Gate/exit device failure | Preserve payment/fiscal truth; handle site issue through governed path. |
| Continuity activation misuse | Require explicit scope, authority, incident, audit, reconciliation, and post-review. |
| Manual release misuse | Treat as last resort with approval, reason, attribution, tags, and review. |
| Evidence/privacy/export risk | Enforce RBAC, redaction/minimization, retention, and access audit. |
| Dashboard source confusion | Require source/freshness/authority labels. |
| Reconciliation backlog | Surface backlog and preserve open state until formally reviewed. |

### Degraded Resolve and Continuity

Continuity is explicit controlled degraded operation, not silent fallback. Conceptual states include normal, degraded-watch, degraded-active, Continuity-terminal-active, restoration-in-progress, post-restoration-review, and closed/reconciled.

Vendor PMS/HCP degraded resolve may use projection and approved degraded basis only under policy and freshness controls. Exact freshness thresholds, degraded tariff basis, owner, offline behavior, activation authority, and manual release policy remain open.

### Fiscal Exception and Manual Release

Fiscal issuance failure after payment finality must not automatically reverse payment and must not automatically authorize exit. Manual release, if allowed, must remain a separately approved, auditable, reconciliation-tagged exception posture.

### Recovery and Reconciliation

Recovery must not mutate authority records silently. Recovery and reconciliation must compare canonical payment/provider records, POS fiscal records, fiscal references, vendor acknowledgment, gate/manual release records, continuity records, and settlement where applicable.

### Failure Monitoring and Alerting

Alerts should cover connector stale/unavailable, Vendor PMS outage, projection stale/ambiguous, payment uncertainty, fiscal issuance failure, POS Server recovery risk, vendor acknowledgment backlog, gate/exit issue, continuity activation, manual release, export/privacy incidents, and reconciliation backlog. Exact thresholds and monitoring stack are deferred.

## 13. Deployment Architecture

### Deployment Model Overview

The deployment architecture remains system-design level only. It identifies centralized platform services, Site-level fiscal boundary, field/station terminal posture, internal governance/reporting surfaces, integration boundaries, and observability/audit/reconciliation posture.

### Runtime Deployment Posture

| Area | Deployment posture |
| --- | --- |
| Centralized platform services | Central PMS, payment control, audit/event, reconciliation coordination, WebPay backend, governance/reporting boundaries. |
| Centralized WebPay | Public customer channel using configured site-specific or payment-scope URLs. |
| Payment Orchestrator | Provider-facing integration boundary with verified outcome reporting. |
| Vendor PMS connector instances | Runtime connector deployments tied to configured VendorSystem instances and AdapterMapping. |
| Site-level POS Server | Site fiscal boundary; exact packaging and registration remain open. |
| Assisted Payment Terminal | Field/station deployment posture with Android-first hardened terminal preference for field use. |
| Operator Console | Internal web/PWA-oriented governance posture. |
| Management Dashboard/Reporting | Reporting/export posture with source labels and scoped RBAC. |
| Gate/exit integration | Site infrastructure boundary consuming Central PMS authorization. |
| Observability/audit/reconciliation | Cross-boundary telemetry, durable audit, and reconciliation posture. |

### Deployment Non-Decisions

This document does not define infrastructure scripts, environment variables, scaling parameters, physical network topology, Kubernetes/container details, service mesh, monitoring stack, secrets backend, final POS Server packaging, terminal native bridge design, or BI tooling.

### Deployment Alignment Rules

- Deployment packaging must preserve authority separation even if components are co-located.
- POS Server deployment must preserve fiscal authority without becoming payment or exit authority.
- Connector deployment must preserve VendorSystem identity and AdapterMapping.
- Terminal deployments must preserve device trust and Site/Site Group context.
- Reporting deployments must preserve source labels and read-only authority posture.

## 14. Observability

### Observability Objectives

Observability must be control-aware. It must help operators see health, freshness, uncertainty, exception state, continuity state, and reconciliation backlog without treating telemetry or dashboards as authority.

### Observability Domains

Required observability domains include:

- Connector health and projection freshness.
- Vendor PMS/HCP availability.
- Payment Orchestrator and provider uncertainty.
- Site POS Server health and fiscal exception backlog.
- Gate/exit health.
- Continuity and degraded state visibility.
- Assisted Payment Terminal health and device/shift context.
- Operator Console governance visibility.
- Dashboard/reporting source labels, freshness labels, access, and export audit.
- Audit/event correlation.
- Reconciliation and post-restoration backlog.

### Source-of-Truth Labeling

Dashboard and reporting views must label operational visibility, financial truth, fiscal truth, audit/evidence records, and reconciliation records. Operational projection may appear in dashboards with freshness labels, but financial and revenue reporting must use canonical payment, provider, fiscal, fiscal reference, settlement, and reconciliation records.

Diagram:

- `docs/v1.3/diagrams/system-design/D-10_Management_Dashboard_Source_of_Truth_Boundary.puml`
- `docs/v1.3/diagrams/system-design/D-10_Management_Dashboard_Source_of_Truth_Boundary.jpg`

### Alert Categories and Stale Warnings

Alert categories should include connector stale/unavailable, failed polling, high poll latency, Vendor PMS/HCP unavailable, projection stale, payment unknown, duplicate/replayed provider activity, fiscal issuance failure/timeout, POS Server unavailable/recovery risk, gate unavailable, continuity activation/deactivation, manual release, export/privacy incident, and reconciliation backlog.

Exact metric names, log fields, alert thresholds, dashboard wireframes, dashboard refresh intervals, monitoring stack, and BI tooling are deferred.

### Event Audit Correlation

Observability must correlate channel, Site Group, resolved Site, vendor reference, projection freshness where used, TariffSnapshot, discount validation, PaymentAttempt, ProviderOutcome, PaymentConfirmation, fiscal reference, ExitAuthorization, gate outcome, continuity state, manual release governance, export access, and reconciliation status.

### Operational Dashboards

Operational dashboards may show projection and health data with freshness labels. Financial dashboards must use canonical financial/fiscal/reconciliation sources. Compliance dashboards must protect sensitive evidence and preserve access audit. Executive summaries may aggregate domains only with clear labels.

## 15. Business Continuity

### Business Continuity Objectives

Business continuity provides controlled degraded operation when approved dependencies are unavailable or unsafe. It must not redefine authority, silently fall back to alternate payment/exit behavior, or erase reconciliation obligations.

### Continuity Design Principles

- Continuity is explicit.
- Continuity does not redefine Vendor PMS/HCP, Central PMS, POS Server, or gate authority.
- Continuity must be auditable.
- Continuity must be reconciliation-tagged.
- Continuity must be time-bound and post-reviewed.
- Continuity must remain fail-closed when safety or policy conditions are not met.

### Continuity Scenarios

Continuity scenarios include Vendor PMS/HCP unavailability, connector stale/unavailable, projection stale/ambiguous, payment uncertainty, POS Server unavailable or fiscal exception, gate/exit infrastructure issue, continuity terminal activation, and manual emergency release.

### Continuity Activation and Governance

Activation scope, affected dependency, Site/Site Group, incident or BCP reference, allowed workflow scope, approval actor, audit tag, reconciliation tag, and deactivation/post-restoration review must be captured at architecture level. Exact activation authority and workflow remain open.

### Continuity Terminal Architecture

Continuity Terminal is restricted degraded/BCP mode of Assisted Payment Terminal and is disabled by default. It may support limited lookup, degraded payable-basis display, payment collection surface, fiscal routing/status display, and controlled assisted/manual exit handling only where policy allows. It does not declare finality, issue fiscal documents independently, approve unmanaged offline discounts, or issue ExitAuthorization.

### Manual Release and Manual Exit Handling

Manual release is not normal ExitAuthorization. It is last-resort governance under approved policy, with supervisor approval where required, reason, incident tag, audit tag, reconciliation tag, attribution, and post-review.

### Post-Restoration Reconciliation

After restoration or deactivation, continuity-origin activity moves into post-restoration review. Reconciliation should include activations/deactivations, affected dependencies, projection-based resolves, degraded tariff basis, payments, uncertain outcomes, fiscal successes/failures/pending cases, manual releases, vendor acknowledgments, gate events, continuity discount activity, and material customer/operator messages.

### Business Continuity Alignment Rules

- Projection cannot close financial reconciliation.
- Fiscal exception does not automatically authorize exit.
- Payment uncertainty does not become finality.
- Continuity cannot silently become normal operation.
- Manual release remains tagged and reviewable.

## 16. Operational Runbooks

### Runbook Posture

This document identifies future runbook areas at architecture level only. It does not draft step-by-step runbook procedures, escalation scripts, operator instructions, UAT scenarios, or operational playbooks.

### Future Runbook Areas

Future runbook packs should cover:

- Connector stale/unavailable.
- Vendor PMS outage.
- Projection stale/ambiguous.
- Payment uncertainty.
- POS fiscal issuance failure.
- Fiscal recovery/failover.
- Vendor acknowledgment backlog.
- Gate/exit device issue.
- Continuity activation/deactivation.
- Continuity Terminal activation.
- Manual release.
- Reporting/export access.
- Evidence/privacy incident.
- Reconciliation/post-restoration review.

### Runbook Alignment Rules

Runbooks must preserve authority boundaries. They must not instruct operators to mark payments final, issue fiscal documents, authorize exit, activate continuity, or close reconciliation outside approved workflows. Runbooks must preserve audit, RBAC, evidence, incident, and reconciliation tags.

## 17. Appendix

### Appendix A: Glossary

| Term | Definition |
| --- | --- |
| Adapter codebase | Reusable vendor connector implementation. |
| AdapterMapping | Mapping between an ExitPass Site and vendor-side parking object. |
| Assisted Payment Terminal | Payment-capable terminal app family with cashier-assisted and continuity modes. |
| Cashier-Assisted Terminal | Normal assisted terminal mode for cashier-supported payment and discount input capture. |
| Central PMS | Platform control authority for payment-linked state, payment finality, fiscal reference recording, degraded decisions under policy, and ExitAuthorization. |
| Connector instance | Deployed/configured runtime connector for a configured VendorSystem. |
| Continuity Terminal | Restricted degraded/BCP terminal mode, disabled by default. |
| ExitAuthorization | Central PMS-issued authorization consumed by gate/exit execution. |
| Management Dashboard and Reporting | Visibility/reporting module with source labels and export controls. |
| Operator Console | Internal non-payment governance module. |
| Parking Session Projection | Central PMS operational projection of vendor data used for visibility and controlled degraded support. |
| PaymentConfirmation | Central PMS-owned platform payment finality record concept. |
| Payment Orchestrator | Payment provider interaction component that reports verified outcomes. |
| POS Server / Site POS Server | Resolved Site fiscal issuance authority for Sales Invoice and fiscal records. |
| Site | Reporting, contract, Vendor PMS mapping, POS Server routing, fiscal attribution, and operational boundary. |
| Site Group | Customer lookup/payment scope. |
| TariffSnapshot | Central PMS record of payable basis from live vendor calculation or approved degraded computation. |
| VendorSystem | Configured Vendor PMS/HCP instance. |
| WebPay | Public customer payment surface. |

### Appendix B: Acronyms

| Acronym | Meaning |
| --- | --- |
| APT | Assisted Payment Terminal |
| BCP | Business Continuity Plan |
| BIR | Bureau of Internal Revenue |
| DTO | Data Transfer Object |
| EJ | Electronic Journal |
| HCP | HikCentral Professional |
| MDR | Management Dashboard and Reporting |
| NAAC | National Athletes and Coaches |
| PMS | Parking Management System |
| POS | Point of Sale |
| PWD | Persons with Disability |
| RBAC | Role-Based Access Control |
| SI | Sales Invoice |
| SDD | System Design Document |
| UAT | User Acceptance Testing |

### Appendix C: Approved Input Baseline

See the Document Control section for the approved BRD baseline and specialist input packs used for this draft. The approval baseline confirms BRD approval for System Design input purposes only. It does not close downstream API, database, engineering, runbook, tax/accounting, or BIR/accreditation decisions.

### Appendix D: Open Questions and Downstream Deferrals

The following items remain open or deferred and must not be silently closed by this System Design:

- WebPay URL slug registry.
- Whether WebPay slugs resolve to Site Group, Site, or both.
- Site Group user-facing terminology.
- Physical parking lot/cluster modeling.
- HCP connector topology.
- Connector health/freshness model.
- Projection freshness thresholds.
- Degraded tariff basis and owner.
- Continuity/BCP activation authority.
- Manual release policy.
- Vendor acknowledgment retry/exit-block policy.
- POS Server deployment/registration model.
- Whether POS Server is a module or separate service.
- Fiscal numbering, counters, and sequence gaps.
- BIR/accreditation identity assignment.
- Tax/VAT treatment.
- Diplomat VAT evidence, treatment, reporting, and retention.
- Digital Sales Invoice URL security model.
- Terminal final implementation architecture.
- Device trust, certificate, and key storage model.
- Operator Console trust mechanism.
- Dashboard/reporting implementation.
- Export controls and retention.
- Exact API endpoints and DTOs.
- Exact database deltas.
- Exact event payloads.
- Exact engineering implementation.
- Exact Test/UAT coverage.
- Exact runbook procedures.

### Appendix E: Diagram Index

| ID | Diagram | Files |
| --- | --- | --- |
| D-01 | ExitPass v1.3 Logical Architecture | `docs/v1.3/diagrams/system-design/D-01_ExitPass_v1.3_Logical_Architecture.puml`, `.jpg` |
| D-02 | Authority Boundary Model | `docs/v1.3/diagrams/system-design/D-02_Authority_Boundary_Model.puml`, `.jpg` |
| D-03 | Site Group / Site / VendorSystem / Connector Instance / POS Server Topology | `docs/v1.3/diagrams/system-design/D-03_Site_Group_Site_VendorSystem_Connector_POS_Topology.puml`, `.jpg` |
| D-04 | Normal Payment-to-Exit Sequence | `docs/v1.3/diagrams/system-design/D-04_Normal_Payment_to_Exit_Sequence.puml`, `.jpg` |
| D-05 | Payment Finality to Fiscal Issuance to ExitAuthorization Sequence | `docs/v1.3/diagrams/system-design/D-05_Payment_Finality_Fiscal_Issuance_ExitAuthorization_Sequence.puml`, `.jpg` |
| D-06 | Vendor PMS Connector Projection and Freshness Flow | `docs/v1.3/diagrams/system-design/D-06_Vendor_PMS_Connector_Projection_Freshness_Flow.puml`, `.jpg` |
| D-07 | Degraded Resolve and Continuity Sequence | `docs/v1.3/diagrams/system-design/D-07_Degraded_Resolve_and_Continuity_Sequence.puml`, `.jpg` |
| D-08 | Assisted Payment Terminal Context and Modes | `docs/v1.3/diagrams/system-design/D-08_Assisted_Payment_Terminal_Context_and_Modes.puml`, `.jpg` |
| D-09 | Operator Console Governance Boundary | `docs/v1.3/diagrams/system-design/D-09_Operator_Console_Governance_Boundary.puml`, `.jpg` |
| D-10 | Management Dashboard Source-of-Truth Boundary | `docs/v1.3/diagrams/system-design/D-10_Management_Dashboard_Source_of_Truth_Boundary.puml`, `.jpg` |
| D-11 | Audit, Event, and Outbox Conceptual Flow | `docs/v1.3/diagrams/system-design/D-11_Audit_Event_Outbox_Conceptual_Flow.puml`, `.jpg` |

### Appendix F: Requirements Traceability Summary

| Coverage area | Primary source coverage | SDD sections |
| --- | --- | --- |
| Controlled successor posture | Orchestration plan, v1.2 SDD baseline, approval baseline | 1, 2, 17 |
| Authority model | Core BRD, POS/Invoicing BRD, input pack 01 | 2, 3, 4, 5, 6 |
| Site Group/Site model | Core BRD, MDR BRD, input packs 02 and 07 | 3, 4, 9, 14 |
| Vendor PMS/HCP connector | Core BRD, Continuity BRD, input packs 03, 05, 06 | 3, 4, 8, 12, 14 |
| Centralized WebPay | Core BRD, input packs 02 and 04 | 3, 4, 5, 6, 10, 11 |
| Payment orchestration | Core BRD, input packs 01 and 03 | 4, 6, 7, 8, 10, 12 |
| POS/Invoicing and Site POS Server | POS/Invoicing BRD, core BRD, input packs 01 and 02 | 4, 5, 6, 8, 9, 12, 13 |
| Assisted Payment Terminal | APT BRD, input packs 03 and 04 | 3, 4, 5, 6, 11, 15 |
| Operator Console | Operator Console BRD, input packs 04 and 07 | 3, 4, 5, 6, 11, 16 |
| Management Dashboard/Reporting | MDR BRD, input pack 05 | 3, 4, 9, 11, 14 |
| Continuity/degraded operation | Continuity BRD, input packs 03 and 05 | 6, 8, 12, 15, 16 |
| Statutory discount/VAT privilege | Core BRD, APT BRD, POS/Invoicing BRD, Operator Console BRD | 6, 8, 9, 11, 17 |
| Event/outbox posture | v1.2 SDD baseline, input pack 03 | 7, 14, 17 |
| Security/trust/RBAC | Security input pack, approved BRDs | 5, 10, 11, 16 |
| Failure modes | Core BRD, Continuity BRD, POS/Invoicing BRD, input packs 03 and 05 | 12, 15, 16 |
| Deployment posture | v1.2 SDD baseline and v1.3 approved BRDs | 13 |
| Observability and runbook posture | MDR BRD, input pack 05 | 14, 16 |

### Appendix G: Non-Decisions / Deferred Items

This document intentionally does not decide:

- API contract details.
- Database design details.
- Engineering implementation details.
- POS Server internals or BIR/accreditation package content.
- Assisted Payment Terminal implementation stack.
- HikCentral connector profile details.
- Management Dashboard BI/reporting implementation.
- Test/UAT cases.
- Runbook procedures.
- Final tax/accounting treatment.
- Final security mechanism choices where the approved BRDs left the matter open.
